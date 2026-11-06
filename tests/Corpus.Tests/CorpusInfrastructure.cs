// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace InvenioTech.GateKeeper.Corpus.Tests;

/// <summary>
/// The paths the harness needs and the structural checks every case must
/// satisfy before anything else runs. The checks live once so each test
/// covers one behaviour and stays readable.
/// </summary>
public static class CorpusPaths
{
    /// <summary>
    /// Gets the absolute path of <c>tests/corpus/</c>, located by walking up
    /// from the test assembly's output folder until it finds the folder.
    /// </summary>
    public static string Root { get; } = LocateCorpusRoot();

    private static string LocateCorpusRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "tests", "corpus");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "The corpus harness could not locate tests/corpus under the test output directory.");
    }
}

/// <summary>
/// The structural rules every case obeys, in one place:
/// <list type="bullet">
/// <item>the expected file is UTF-8 without a byte-order mark and carries LF line endings with a final newline (rule 22);</item>
/// <item>every expected entry names a dotted lowercase rule identifier, a repository-relative path in forward slashes without escapes, a 1-based line and column, and, when the triage label is present, one of the closed labels;</item>
/// <item>every expected path resolves to a file under the case's <c>repo/</c>;</item>
/// <item>no two expected entries name the same rule, path, line and column.</item>
/// </list>
/// </summary>
public static class CorpusCaseValidator
{
    /// <summary>Gets the triage labels the policy closes the set on (the quality-budgets specification, section 3.2).</summary>
    public static readonly string[] TriageLabels = [CorpusTriage.TruePositive, CorpusTriage.FalsePositive, CorpusTriage.Unresolved];

    /// <summary>
    /// Validates one case and returns every problem found, in the order the
    /// checks fixed them. An empty list means the case is well formed.
    /// </summary>
    /// <param name="item">The case to validate.</param>
    /// <returns>The problems; empty when the case is well formed.</returns>
    public static IReadOnlyList<string> Validate(CorpusCase item)
    {
        var problems = new List<string>();
        var expectedPath = Path.Combine(
            Path.GetDirectoryName(item.RepositoryRoot)!,
            "expected.json");
        var bytes = File.ReadAllBytes(expectedPath);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            problems.Add("expected.json carries a UTF-8 byte-order mark (rule 22).");
        }

        var json = Encoding.UTF8.GetString(bytes);

        if (json.Contains('\r'))
        {
            problems.Add("expected.json carries a carriage return, but the format writes LF line endings (rule 22).");
        }

        if (json.Length == 0 || json[^1] != '\n')
        {
            problems.Insert(0, "expected.json does not end with a final newline (rule 22).");
        }

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < item.Expected.Count; index++)
        {
            var entry = item.Expected[index];
            var where = $"entry {index + 1}";

            if (string.IsNullOrWhiteSpace(entry.RuleId) || !IsDottedLowercase(entry.RuleId))
            {
                problems.Add($"{where}: the rule identifier '{entry.RuleId}' is not dotted lowercase.");
            }

            if (string.IsNullOrWhiteSpace(entry.Path))
            {
                problems.Add($"{where}: the path is required.");
            }
            else
            {
                if (entry.Path.Contains('\\'))
                {
                    problems.Add($"{where}: the path '{entry.Path}' uses a backslash (rule 22).");
                }

                if (Path.IsPathRooted(entry.Path))
                {
                    problems.Add($"{where}: the path '{entry.Path}' is absolute (rule 22).");
                }

                var segments = entry.Path.Split('/');
                if (segments.Contains("..") || segments.Contains("."))
                {
                    problems.Add($"{where}: the path '{entry.Path}' escapes the repository (rule 22).");
                }

                var onDisk = Path.Combine(item.RepositoryRoot, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(onDisk))
                {
                    problems.Add($"{where}: the path '{entry.Path}' does not exist under the case's repo folder.");
                }
            }

            if (entry.Line < 1)
            {
                problems.Add($"{where}: the line is {entry.Line}, but lines are 1-based.");
            }

            if (entry.Column < 1)
            {
                problems.Add($"{where}: the column is {entry.Column}, but columns are 1-based.");
            }

            if (entry.Triage is not null && !TriageLabels.Contains(entry.Triage, StringComparer.Ordinal))
            {
                problems.Add($"{where}: the triage label '{entry.Triage}' is not one of the closed labels.");
            }

            var key = $"{entry.RuleId}|{entry.Path}|{entry.Line}|{entry.Column}";

            if (!seenKeys.Add(key))
            {
                problems.Add($"{where}: a duplicate expected entry names the same rule, path, line and column.");
            }
        }

        return problems;
    }

    private static bool IsDottedLowercase(string ruleId)
    {
        var segments = ruleId.Split('.');

        return segments.Length >= 2
            && segments.All(segment =>
                segment.Length > 0
                && char.IsAsciiLetter(segment[0])
                && segment[1..].All(sign => char.IsAsciiLetterOrDigit(sign) || sign == '-'));
    }
}

/// <summary>
/// The xUnit data source that turns every case folder into a theory case, so
/// the suite grows with the corpus and adding a case needs no code change.
/// </summary>
public static class CorpusCaseSource
{
    /// <summary>Gets every loaded case, one per theory member.</summary>
    public static IEnumerable<object[]> Cases()
    {
        foreach (var item in CorpusCase.LoadAll(CorpusPaths.Root))
        {
            yield return [item];
        }
    }
}
