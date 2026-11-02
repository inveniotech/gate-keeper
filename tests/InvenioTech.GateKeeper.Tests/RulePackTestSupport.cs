// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace InvenioTech.GateKeeper.Tests;

/// <summary>
/// Shared helpers for the rule-pack schema and consistency tests. This is test-only code; it does not implement the
/// production loader (P1-03) or the matcher (P1-06).
/// </summary>
internal static class RulePackTestSupport
{
    // Built once and shared, because building the same schema twice against the global registry would overwrite its $id.
    private static readonly Lazy<JsonSchema> Schema = new(BuildSchema);

    /// <summary>Builds the rule-pack v1 schema from its repository file.</summary>
    public static JsonSchema LoadSchema() => Schema.Value;

    /// <summary>Reads a file copied into the test output directory.</summary>
    public static string ReadOutputFile(string relativePath)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(fullPath);
    }

    /// <summary>
    /// Evaluates an instance document against the schema and returns the result plus the flattened errors. The evaluation
    /// is fully materialised while the parsed document is still alive.
    /// </summary>
    public static ValidationSummary Validate(string instanceText)
    {
        using var document = JsonDocument.Parse(instanceText);
        var results = Schema.Value.Evaluate(
            document.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });
        var errors = CollectErrors(results);
        return new ValidationSummary(results.IsValid, errors);
    }

    /// <summary>Flattens a list-format result into one error string per failing keyword, each with its instance location.</summary>
    private static IReadOnlyList<string> CollectErrors(EvaluationResults results)
    {
        var errors = new List<string>();
        Collect(results, errors);
        return errors;

        static void Collect(EvaluationResults result, List<string> errors)
        {
            if (result.Errors is { Count: > 0 })
            {
                foreach (var message in result.Errors.Values)
                {
                    errors.Add($"{result.InstanceLocation}: {message}");
                }
            }

            if (result.Details is { Count: > 0 })
            {
                foreach (var child in result.Details)
                {
                    Collect(child, errors);
                }
            }
        }
    }

    /// <summary>
    /// Applies the civil-date and base-version consistency checks that the production loader (P1-03) performs on the
    /// signed content. Returns the violations; an empty list means the document is consistent.
    /// </summary>
    public static IReadOnlyList<string> CheckPackLineConsistency(string instanceText)
    {
        var root = JsonNode.Parse(instanceText)!.AsObject()!;
        var violations = new List<string>();

        var coverage = root["coverageDate"]?.GetValue<string>();
        var issued = root["contentIssuedAt"]?.GetValue<string>();

        if (coverage is not null && issued is not null)
        {
            var coverageDate = DateOnly.Parse(coverage, System.Globalization.CultureInfo.InvariantCulture);
            var issuedDate = DateOnly.Parse(issued, System.Globalization.CultureInfo.InvariantCulture);
            if (coverageDate > issuedDate)
            {
                violations.Add($"coverageDate {coverage} is after contentIssuedAt {issued}.");
            }
        }

        var version = root["version"]?.GetValue<string>();
        var baseVersion = root["baseVersion"]?.GetValue<string>();
        if (version is not null && baseVersion is not null)
        {
            var versionTriple = ParseCalendarVersion(version);
            var baseTriple = ParseCalendarVersion(baseVersion);
            if (versionTriple.Year != baseTriple.Year || versionTriple.Month != baseTriple.Month)
            {
                violations.Add($"baseVersion {baseVersion} does not share the year and month of version {version}.");
            }

            if (baseTriple.Patch != 0)
            {
                violations.Add($"baseVersion {baseVersion} does not end in patch 0.");
            }
        }

        return violations;
    }

    private static JsonSchema BuildSchema()
    {
        var schemaText = ReadOutputFile("schemas/rulepack.schema.json");
        // Kept alive (not disposed) because the built schema may retain the parsed element.
        var document = JsonDocument.Parse(schemaText);
        return JsonSchema.Build(document.RootElement);
    }

    private static (int Year, int Month, int Patch) ParseCalendarVersion(string version)
    {
        var parts = version.Split('.');
        return (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
    }
}

/// <summary>The materialised outcome of validating one document against the schema.</summary>
internal sealed record ValidationSummary(bool IsValid, IReadOnlyList<string> Errors);
