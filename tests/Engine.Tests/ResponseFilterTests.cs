// SPDX-License-Identifier: Apache-2.0
using System.Text.Json.Nodes;
using InvenioTech.GateKeeper.Engine;
using InvenioTech.GateKeeper.Middleware;

namespace InvenioTech.GateKeeper.Engine.Tests;

public sealed class ResponseFilterTests
{
    [Fact]
    public void Response_Should_ConvertEngineFailuresWithoutLeakingContent_When_RedactionThrows()
    {
        var body = JsonNode.Parse("""{"phone":"marker"}""");
        var filter = new ResponseFilter(_ => throw new EngineToolException("synthetic-sensitive-marker"), ["/phone"]);
        Action transform = () => filter.Transform(body);
        var error = transform.Should().ThrowExactly<ResponseFilterException>().Which;
        error.AbortRequired.Should().BeFalse(); error.InnerException.Should().BeNull();
        error.Message.Should().NotContain("synthetic-sensitive-marker");
        body!["phone"]!.GetValue<string>().Should().Be("marker");
    }

    [Fact]
    public void Response_Should_RejectNullPathsWithAnArgumentError_When_Configured()
    {
        Action create = () => new ResponseFilter(Runtime(), null!);
        create.Should().ThrowExactly<ArgumentNullException>().Which.ParamName.Should().Be("paths");
    }

    [Fact]
    public void Response_Should_FailClosed_When_TheContentTypeIsMissing()
    {
        var body = JsonNode.Parse("""{"phone":"marker"}""");
        Action transform = () => new ResponseFilter(Runtime(), ["/phone"]).Transform(body, contentType: null);
        transform.Should().ThrowExactly<ResponseFilterException>().Which.AbortRequired.Should().BeFalse();
        body!["phone"]!.GetValue<string>().Should().Be("marker");
    }

    [Fact]
    public void Response_Should_FilterEveryWildcardElementAndLeaveInputUnchanged_When_ProtectedPathsSelectArrays()
    {
        var body = JsonNode.Parse("""{"customers":[{"phone":"marker"},{"phone":"marker"}],"other":"unchanged"}""")!;
        var filter = new ResponseFilter(Runtime(), ["/customers/*/phone"]);
        var result = filter.Transform(body)!;
        result["customers"]![0]!["phone"]!.GetValue<string>().Should().Be("[REDACTED:local.marker]");
        result["customers"]![1]!["phone"]!.GetValue<string>().Should().Be("[REDACTED:local.marker]");
        result["other"]!.GetValue<string>().Should().Be("unchanged");
        body["customers"]![0]!["phone"]!.GetValue<string>().Should().Be("marker");
    }

    [Fact]
    public void Response_Should_FailAtomically_When_ALaterSelectedElementHasTheWrongType()
    {
        var body = JsonNode.Parse("""{"customers":[{"phone":"marker"},{"phone":123}]}""")!;
        var before = body.ToJsonString(); var filter = new ResponseFilter(Runtime(), ["/customers/*/phone"]);
        Action transform = () => filter.Transform(body);
        transform.Should().ThrowExactly<ResponseFilterException>(); body.ToJsonString().Should().Be(before);
    }

    [Fact]
    public void Response_Should_RequestAbort_When_HeadersAreAlreadyCommitted()
    {
        var filter = new ResponseFilter(Runtime(), ["/phone"]);
        Action transform = () => filter.Transform(JsonNode.Parse("""{"phone":"marker"}""")!, headersCommitted: true);
        transform.Should().ThrowExactly<ResponseFilterException>().Which.AbortRequired.Should().BeTrue();
    }

    [Fact]
    public void Response_Should_UseTheProtectedFailureTypeAndAbortSignal_When_TheRootIsJsonNull()
    {
        var filter = new ResponseFilter(Runtime(), [""]);
        Action transform = () => filter.Transform(null, headersCommitted: true);
        transform.Should().ThrowExactly<ResponseFilterException>().Which.AbortRequired.Should().BeTrue();
    }

    [Fact]
    public void Response_Should_SupportNestedWildcardsAndLiteralEscapes_When_PathsAreExplicit()
    {
        var body = JsonNode.Parse("""{"a/b":{"~":{"*":"marker"}},"groups":[[{"value":"marker"}]]}""")!;
        var filter = new ResponseFilter(Runtime(), ["/a~1b/~0/~2", "/groups/*/*/value"]);
        var result = filter.Transform(body)!;
        result["a/b"]!["~"]!["*"]!.GetValue<string>().Should().Be("[REDACTED:local.marker]");
        result["groups"]![0]![0]!["value"]!.GetValue<string>().Should().Be("[REDACTED:local.marker]");
    }

    [Fact]
    public void Response_Should_FailWithoutChangingEarlierElements_When_ASelectedStringCannotBeRedacted()
    {
        var body = new JsonObject { ["customers"] = new JsonArray(new JsonObject { ["phone"] = "marker" }, new JsonObject { ["phone"] = new string('x', 65_537) }) };
        var filter = new ResponseFilter(Runtime(), ["/customers/*/phone"]);
        Action transform = () => filter.Transform(body);
        transform.Should().ThrowExactly<ResponseFilterException>();
        body["customers"]![0]!["phone"]!.GetValue<string>().Should().Be("marker");
        body["customers"]![1]!["phone"] = "\ud800";
        transform.Should().ThrowExactly<ResponseFilterException>();
        body["customers"]![0]!["phone"]!.GetValue<string>().Should().Be("marker");
    }

    private static RuntimeMatcher Runtime()
    {
        var pack = new LoadedPack("synthetic.runtime", "2026.11.0", PackProvenance.Bundled, new string('a', 64));
        var rule = new Rule("local.marker", Severity.Note, "synthetic", new Citation("Synthetic response test.", null, null),
            "2026.11.0", null, null, null, null, new RuleMatcher(null, new RuleListMatcher(["marker"], null)));
        return new RuntimeMatcher(new MergedRules([pack], [new LoadedRule(rule, pack)]));
    }
}
