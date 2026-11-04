// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// One accepted match from the matcher engine (P1-06). The matched text is
/// not a property: outputs name the rule and the location only (rule 10).
/// <see cref="SpanStart"/> and <see cref="SpanLength"/> are indexes into the
/// text the caller passed, so the fingerprint step can read the span without
/// this record carrying it. Nothing that writes a result may print that span.
/// </summary>
/// <param name="RuleId">The rule identifier that produced the match.</param>
/// <param name="Severity">The severity the rule carries. Overrides are the orchestrator's.</param>
/// <param name="Category">The rule's category.</param>
/// <param name="Path">The repository-relative path, forward slashes, that the caller supplied.</param>
/// <param name="Line">The 1-based line of the finding span, counted in scalar values.</param>
/// <param name="Column">The 1-based column of the finding span, counted in scalar values.</param>
/// <param name="LocationKind">The rule's location kind, or <see cref="LocationKind.Source"/> when the rule names none.</param>
/// <param name="Citation">The citation of the rule.</param>
/// <param name="SuppressionHints">The rule's help text, copied through and never interpreted. Empty when the rule carries none.</param>
/// <param name="SpanStart">The UTF-16 index of the finding span in the text the caller passed.</param>
/// <param name="SpanLength">The UTF-16 length of the finding span.</param>
public sealed record RuleMatch(
    string RuleId,
    Severity Severity,
    string Category,
    string Path,
    int Line,
    int Column,
    LocationKind LocationKind,
    Citation Citation,
    IReadOnlyList<string> SuppressionHints,
    int SpanStart,
    int SpanLength);
