// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

/// <summary>
/// The cross-stack matcher vectors (P1-06, the lines P3-00 will lift). The
/// file is the contract; this test only checks that the C# engine agrees.
/// </summary>
public sealed class MatcherConformanceVectorTests
{
    private static readonly Citation Citation = new("Synthetic citation for the matcher vectors.", null, null);

    public static IEnumerable<object[]> Cases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(VectorPath()));

        foreach (var vectorCase in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            yield return [vectorCase.GetProperty("name").GetString()!];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatcherEngine_Should_AgreeWithTheVector_When_TheCaseRuns(string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(VectorPath()));
        var vectorCase = document.RootElement.GetProperty("cases").EnumerateArray()
            .Single(candidate => candidate.GetProperty("name").GetString() == name);
        var ruleElement = vectorCase.GetProperty("rule");
        var rule = ReadRule(ruleElement);
        var matches = new MatcherEngine([rule]).Match(
            vectorCase.GetProperty("path").GetString()!,
            vectorCase.GetProperty("text").GetString()!);

        var expected = vectorCase.GetProperty("expected");
        matches.Should().HaveCount(expected.GetArrayLength());

        var index = 0;

        foreach (var entry in expected.EnumerateArray())
        {
            var match = matches[index];
            match.RuleId.Should().Be(entry.GetProperty("ruleId").GetString());
            match.Line.Should().Be(entry.GetProperty("line").GetInt32());
            match.Column.Should().Be(entry.GetProperty("column").GetInt32());

            if (entry.TryGetProperty("locationKind", out var kind))
            {
                match.LocationKind.Should().Be(ReadKind(kind.GetString()!));
            }

            if (entry.TryGetProperty("hints", out var hints))
            {
                match.SuppressionHints.Should().Equal(hints.EnumerateArray().Select(hint => hint.GetString()!).ToArray());
            }

            index++;
        }
    }

    private static Rule ReadRule(JsonElement rule)
    {
        var id = rule.GetProperty("id").GetString()!;
        IReadOnlyList<string>? hints = rule.TryGetProperty("hints", out var hintElement)
            ? hintElement.EnumerateArray().Select(hint => hint.GetString()!).ToArray()
            : null;

        if (rule.TryGetProperty("packages", out _) || rule.TryGetProperty("hostnames", out _))
        {
            var packages = ReadStrings(rule, "packages");
            var hostnames = ReadStrings(rule, "hostnames");
            return new Rule(
                id,
                Severity.Note,
                "synthetic",
                Citation,
                "1",
                null,
                null,
                hints,
                null,
                new RuleMatcher(null, new RuleListMatcher(packages, hostnames)));
        }

        var shape = new List<RuleShapePart>();

        foreach (var part in rule.GetProperty("shape").EnumerateArray())
        {
            shape.Add(ReadPart(part));
        }

        var boundary = rule.GetProperty("boundary").GetString() switch
        {
            "none" => MatchBoundary.None,
            "digit" => MatchBoundary.Digit,
            "alnum" => MatchBoundary.Alnum,
            _ => throw new InvalidOperationException("A vector boundary is none, digit or alnum."),
        };
        RuleValidator? validator = null;

        if (rule.TryGetProperty("validator", out var validatorElement))
        {
            validator = validatorElement.GetString() switch
            {
                "nuban" => new RuleValidator(RuleValidatorName.Nuban, null, null),
                "ng-phone-prefix" => new RuleValidator(RuleValidatorName.NgPhonePrefix, null, ReadStrings(rule, "prefixes")),
                "email-not-example-domain" => new RuleValidator(RuleValidatorName.EmailNotExampleDomain, null, null),
                "digits-length" => new RuleValidator(RuleValidatorName.DigitsLength, rule.GetProperty("length").GetInt32(), null),
                _ => throw new InvalidOperationException("A vector names a validator outside the closed set."),
            };
        }

        IReadOnlyList<string>? keywords = ReadStrings(rule, "keywords");
        int? window = rule.TryGetProperty("window", out var windowElement) ? windowElement.GetInt32() : null;
        RuleLocation? location = null;

        if (rule.TryGetProperty("kind", out var kindElement))
        {
            location = new RuleLocation(ReadKind(kindElement.GetString()!), ReadStrings(rule, "paths"));
        }

        var pattern = new RulePatternMatcher(shape, boundary, validator, keywords, window, location, null, null, null);
        return new Rule(id, Severity.Error, "synthetic", Citation, "1", null, null, hints, null, new RuleMatcher(pattern, null));
    }

    private static RuleShapePart ReadPart(JsonElement part)
    {
        var min = part.GetProperty("min").GetInt32();
        var max = part.GetProperty("max").GetInt32();
        var emit = part.TryGetProperty("emit", out var emitElement) && emitElement.GetBoolean();
        string? literal = part.TryGetProperty("literal", out var literalElement) ? literalElement.GetString() : null;
        ShapeClass? shapeClass = part.TryGetProperty("class", out var classElement)
            ? classElement.GetString() switch
            {
                "digit" => ShapeClass.Digit,
                "alnum" => ShapeClass.Alnum,
                _ => throw new InvalidOperationException("A vector class is digit or alnum."),
            }
            : null;
        return new RuleShapePart(min, max, literal, shapeClass, null, null, emit);
    }

    private static LocationKind ReadKind(string kind)
    {
        return kind switch
        {
            "log" => LocationKind.Log,
            "migration" => LocationKind.Migration,
            "api-model" => LocationKind.ApiModel,
            "configuration" => LocationKind.Configuration,
            "fixture" => LocationKind.Fixture,
            "source" => LocationKind.Source,
            _ => throw new InvalidOperationException("A vector location kind is outside the closed set."),
        };
    }

    private static string[]? ReadStrings(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.EnumerateArray().Select(item => item.GetString()!).ToArray();
    }

    private static string VectorPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "vectors", "matcher", "vectors.json");
    }
}
