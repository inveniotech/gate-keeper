// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>The scan boundary decision. Tool errors retain a diagnostic and never retain partial results.</summary>
/// <param name="Outcome">Pass, violations or tool error, mapping exactly to process codes 0, 1 and 2.</param>
/// <param name="Result">The completed policy result, absent on a tool error.</param>
/// <param name="Error">The user-facing diagnostic, present only on a tool error.</param>
public sealed record ScanDecision(ExitOutcome Outcome, PolicyResult? Result, string? Error);

/// <summary>The one failure-threshold decision after overrides, exceptions and baseline application (P1-10, IQ-22).</summary>
public static class SeverityPolicy
{
    /// <summary>Returns pass or violations for a completed policy result. Baselined work-tree findings never fail; history findings can fail. Invalid severities are tool errors.</summary>
    public static ExitOutcome Evaluate(PolicyResult result, Severity threshold)
    {
        ValidateSeverity(threshold);
        var fails = false;
        foreach (var finding in result.Findings)
        {
            ValidateSeverity(finding.Severity);
            if (!Enum.IsDefined(finding.BaselineState)) throw new EngineToolException("A finding has an invalid baseline state. Re-run the scan with a supported engine.");
            if (finding.BaselineState == BaselineState.New && Meets(finding.Severity, threshold)) fails = true;
        }
        foreach (var finding in result.History)
        {
            ValidateSeverity(finding.Match.Severity);
            if (Meets(finding.Match.Severity, threshold)) fails = true;
        }
        return fails ? ExitOutcome.Violations : ExitOutcome.Pass;
    }

    /// <summary>Executes the scan operation and maps known tool and file-access failures to code 2 with a diagnostic. Cancellation propagates. Findings alone never return code 2.</summary>
    public static async Task<ScanDecision> RunAsync(Func<CancellationToken, Task<PolicyResult>> scan, Severity threshold,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            ValidateSeverity(threshold);
            var result = await scan(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new ScanDecision(Evaluate(result, threshold), result, null);
        }
        catch (EngineToolException exception)
        {
            return new ScanDecision(ExitOutcome.ToolError, null, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ScanDecision(ExitOutcome.ToolError, null,
                "The repository could not be read, so the scan could not finish. Check the repository path and file access permissions, then run the scan again.");
        }
    }

    /// <summary>Validates override and disabled-rule identifiers against loaded packs before matching. Unknown rules or severity words are tool errors naming the setting.</summary>
    public static void ValidateSettings(ScanSettings settings, IReadOnlyList<Rule> rules)
    {
        ValidateSeverity(settings.FailOn);
        var known = rules.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in settings.SeverityOverrides)
        {
            ValidateSeverity(entry.Value);
            if (!known.Contains(entry.Key)) Unknown(entry.Key);
        }
        foreach (var entry in settings.DisabledRules)
            if (!known.Contains(entry.Id)) Unknown(entry.Id);
        static void Unknown(string id) => throw new EngineToolException($"gatekeeper.toml names unknown rule '{id}'. Load its pack or remove the rule setting.");
    }

    private static bool Meets(Severity severity, Severity threshold) => threshold switch
    {
        Severity.Error => severity == Severity.Error,
        Severity.Warning => severity is Severity.Error or Severity.Warning,
        Severity.Note => true,
        _ => false,
    };
    private static void ValidateSeverity(Severity severity)
    {
        if (!Enum.IsDefined(severity)) throw new EngineToolException("The severity or failure threshold is invalid. Use error, warning or note, then run the scan again.");
    }
}
