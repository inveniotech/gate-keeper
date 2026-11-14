// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Middleware;
using InvenioTech.GateKeeper.Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog;

namespace InvenioTech.GateKeeper.Middleware.Tests;

public sealed class GateKeeperSerilogTests
{
    [Fact]
    public void Enricher_Should_ReplaceScalarStrings_When_TheEventCarriesAMarkedValue()
    {
        var sink = new RecordingSink();
        using var logger = new LoggerConfiguration()
            .Enrich.With(new GateKeeperEnricher(TestRuntimes.Matcher()))
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("Customer record {Record} score {Score}", "BVN 32826314843", 42);

        var entry = sink.Events.Should().ContainSingle().Subject;
        entry.Properties["Record"].Should().BeOfType<ScalarValue>().Which.Value.Should().Be("BVN [REDACTED:pii.bvn]");
        entry.Properties["Score"].Should().BeOfType<ScalarValue>().Which.Value.Should().Be(42);
        entry.RenderMessage().Should().Be("Customer record \"BVN [REDACTED:pii.bvn]\" score 42");
    }

    [Fact]
    public void Enricher_Should_LeaveNonStringScalarsAlone_When_TheEventCarriesNumbersAndNulls()
    {
        var sink = new RecordingSink();
        using var logger = new LoggerConfiguration()
            .Enrich.With(new GateKeeperEnricher(TestRuntimes.Matcher()))
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("Count {Count} note {Note}", 3, null);

        var entry = sink.Events.Should().ContainSingle().Subject;
        entry.Properties["Count"].Should().BeOfType<ScalarValue>().Which.Value.Should().Be(3);
        entry.Properties["Note"].Should().Be(ScalarValue.Null);
    }

    [Fact]
    public void Enricher_Should_ThrowTheLimitError_When_APropertyExceedsTheCap()
    {
        var enricher = new GateKeeperEnricher(TestRuntimes.Matcher());
        var entry = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Information, null, MessageTemplate.Empty,
            [new LogEventProperty("Huge", new ScalarValue(new string('a', 65_537)))]);

        var enrich = () => enricher.Enrich(entry, new TestPropertyFactory());

        enrich.Should().Throw<InputLimitException>();
    }

    [Fact]
    public void DestructuringPolicy_Should_RedactStringMembers_When_ADictionaryIsDestructured()
    {
        var policy = new GateKeeperDestructuringPolicy(TestRuntimes.Matcher());
        var payload = new Dictionary<string, object?> { ["record"] = "BVN 32826314843", ["count"] = 2 };

        var destructured = policy.TryDestructure(payload, new TestFactory(policy), out var result);

        destructured.Should().BeTrue();
        var structure = result.Should().BeOfType<DictionaryValue>().Subject;
        structure.Elements[new ScalarValue("record")].Should().BeOfType<ScalarValue>().Which.Value.Should().Be("BVN [REDACTED:pii.bvn]");
        structure.Elements[new ScalarValue("count")].Should().BeOfType<ScalarValue>().Which.Value.Should().Be(2);
    }

    [Fact]
    public void DestructuringPolicy_Should_RedactStringMembers_When_AStringListIsDestructured()
    {
        var policy = new GateKeeperDestructuringPolicy(TestRuntimes.Matcher());
        var tags = new[] { "BVN 32826314843", "ordinary" };

        var destructured = policy.TryDestructure(tags, new TestFactory(policy), out var result);

        destructured.Should().BeTrue();
        var sequence = result.Should().BeOfType<SequenceValue>().Subject;
        sequence.Elements[0].Should().BeOfType<ScalarValue>().Which.Value.Should().Be("BVN [REDACTED:pii.bvn]");
        sequence.Elements[1].Should().BeOfType<ScalarValue>().Which.Value.Should().Be("ordinary");
    }

    [Fact]
    public void DestructuringPolicy_Should_RedactNestedMembers_When_ADictionaryInsideADictionaryIsDestructured()
    {
        var policy = new GateKeeperDestructuringPolicy(TestRuntimes.Matcher());
        var payload = new Dictionary<string, object?> { ["nested"] = new Dictionary<string, object?> { ["record"] = "BVN 32826314843" } };

        var destructured = policy.TryDestructure(payload, new TestFactory(policy), out var result);

        destructured.Should().BeTrue();
        var outer = result.Should().BeOfType<DictionaryValue>().Subject;
        var inner = outer.Elements[new ScalarValue("nested")].Should().BeOfType<DictionaryValue>().Subject;
        inner.Elements[new ScalarValue("record")].Should().BeOfType<ScalarValue>().Which.Value.Should().Be("BVN [REDACTED:pii.bvn]");
    }

    [Fact]
    public void DestructuringPolicy_Should_ReturnFalse_When_TheValueIsNotAStructuredCollection()
    {
        var policy = new GateKeeperDestructuringPolicy(TestRuntimes.Matcher());
        var factory = new TestFactory(policy);

        policy.TryDestructure(new object(), factory, out _).Should().BeFalse();
        policy.TryDestructure("BVN 32826314843", factory, out _).Should().BeFalse();
    }

    [Fact]
    public void DestructuringPolicy_Should_ThrowTheLimitError_When_AMemberExceedsTheCap()
    {
        var policy = new GateKeeperDestructuringPolicy(TestRuntimes.Matcher());
        var payload = new Dictionary<string, object?> { ["huge"] = new string('a', 65_537) };

        var destructure = () => policy.TryDestructure(payload, new TestFactory(policy), out _);

        destructure.Should().Throw<InputLimitException>();
    }

    [Fact]
    public void Logger_Should_RedactBothLoggingPaths_When_TheSampleConfigurationRuns()
    {
        var sink = new RecordingSink();
        using var logger = new LoggerConfiguration()
            .Destructure.With(new GateKeeperDestructuringPolicy(TestRuntimes.Matcher()))
            .Enrich.With(new GateKeeperEnricher(TestRuntimes.Matcher()))
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("Customer {Record} with {@Payload}", "BVN 32826314843",
            new Dictionary<string, object?> { ["record"] = "BVN 32826314843" });

        sink.Events.Should().HaveCount(1);
        var entry = sink.Events[0];
        entry.RenderMessage().Should().Contain("[REDACTED:pii.bvn]");
        entry.RenderMessage().Should().NotContain("32826314843");
        entry.Properties["Payload"].Should().BeOfType<DictionaryValue>()
            .Which.Elements[new ScalarValue("record")].Should().BeOfType<ScalarValue>()
            .Which.Value.Should().Be("BVN [REDACTED:pii.bvn]");
    }

    private sealed class RecordingSink : ILogEventSink
    {
        internal List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private sealed class TestPropertyFactory : ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false) =>
            new(name, new ScalarValue(value));
    }

    /// <summary>Recurses through the policy so nested dictionaries reach the same redaction.</summary>
    private sealed class TestFactory(GateKeeperDestructuringPolicy policy) : ILogEventPropertyValueFactory
    {
        public LogEventPropertyValue CreatePropertyValue(object? value, bool destructureObjects)
        {
            if (value is null)
            {
                return ScalarValue.Null;
            }

            if (policy.TryDestructure(value, this, out var result))
            {
                return result;
            }

            return new ScalarValue(value);
        }
    }
}
