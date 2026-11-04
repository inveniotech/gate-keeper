// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Json.Schema;

namespace InvenioTech.GateKeeper.Tests;

/// <summary>
/// Shared helpers for the results-format schema tests. This is test-only code; it does not implement the writers (P1-11).
/// </summary>
internal static class ResultsTestSupport
{
    private static readonly Lazy<JsonSchema> EvidenceSchema = new(() => Build("schemas/gk-results.schema.json"));
    private static readonly Lazy<JsonSchema> SarifSchema = new(() => Build("schemas/sarif-profile.schema.json"));

    /// <summary>Validates an evidence document against <c>schemas/gk-results.schema.json</c>.</summary>
    public static ValidationSummary ValidateEvidence(string instanceText) => Validate(EvidenceSchema.Value, instanceText);

    /// <summary>Validates a SARIF log against the Gate-Keeper SARIF profile schema.</summary>
    public static ValidationSummary ValidateSarif(string instanceText) => Validate(SarifSchema.Value, instanceText);

    /// <summary>Validates emitted bytes against the offline official SARIF schema.</summary>
    public static ValidationSummary ValidateOfficialSarif(string text)
    {
        var schemaText = ReadOutputFile("schemas/sarif-2.1.0.schema");
        // The pinned draft-04 schema uses the common draft-06 validation subset.
        // Refuse dialect-specific bounds so fixture updates cannot silently change semantics.
        using var document = JsonDocument.Parse(schemaText);
        CheckSubset(document.RootElement);
        var registry = new DialectRegistry();
        registry.Register(Dialect.Draft06.With([], id: new Uri("http://json-schema.org/draft-04/schema#")));
        var schema = JsonSchema.Build(document.RootElement, new BuildOptions { DialectRegistry = registry });
        return Validate(schema, text);
    }

    private static void CheckSubset(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CheckSubset(item);
        if (element.ValueKind != JsonValueKind.Object) return;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name is "exclusiveMinimum" or "exclusiveMaximum")
                throw new InvalidOperationException("The official fixture now requires draft-04 numeric-bound semantics.");
            CheckSubset(property.Value);
        }
    }

    /// <summary>Reads a file copied into the test output directory.</summary>
    public static string ReadOutputFile(string relativePath)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(fullPath);
    }

    private static ValidationSummary Validate(JsonSchema schema, string instanceText)
    {
        using var document = JsonDocument.Parse(instanceText);
        var results = schema.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        var errors = new List<string>();
        Collect(results, errors);
        return new ValidationSummary(results.IsValid, errors);
    }

    private static void Collect(EvaluationResults result, List<string> errors)
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

    private static JsonSchema Build(string relativePath)
    {
        var schemaText = ReadOutputFile(relativePath);
        var document = JsonDocument.Parse(schemaText);
        return JsonSchema.Build(document.RootElement);
    }
}
