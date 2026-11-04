// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace InvenioTech.GateKeeper.Corpus.Tests;

/// <summary>
/// One golden-corpus case: a case folder under <c>tests/corpus/&lt;case-name&gt;/</c> that
/// holds a small synthetic repository under <c>repo/</c> and an <c>expected.json</c>
/// file that lists, by rule, file, line and column, every finding the engine must
/// produce (the quality-budgets specification, section 4.1).
/// </summary>
/// <param name="Name">The case folder's name.</param>
/// <param name="RepositoryRoot">The absolute path of the case's <c>repo/</c> directory.</param>
/// <param name="Expected">The expected findings, in the order the file lists them.</param>
public sealed record CorpusCase(
    string Name,
    string RepositoryRoot,
    IReadOnlyList<CorpusExpectedEntry> Expected)
{
    /// <summary>
    /// The folder names inside <c>tests/corpus/</c> that are not cases: the
    /// documentation and tooling the corpus carries.
    /// </summary>
    public static readonly string[] NonCaseNames = ["validation"];

    /// <summary>
    /// Loads every case folder under the corpus root, in ordinal case-name
    /// order (rule 6). A folder is a case exactly when it carries both
    /// <c>repo/</c> and <c>expected.json</c>.
    /// </summary>
    /// <param name="corpusRoot">The absolute path of <c>tests/corpus/</c>.</param>
    /// <returns>The cases, sorted by case name ordinal.</returns>
    public static IReadOnlyList<CorpusCase> LoadAll(string corpusRoot)
    {
        var cases = new List<CorpusCase>();

        foreach (var directory in Directory.EnumerateDirectories(corpusRoot).OrderBy(name => name, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(directory);

            if (NonCaseNames.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            var repositoryRoot = Path.Combine(directory, "repo");
            var expectedPath = Path.Combine(directory, "expected.json");

            if (!Directory.Exists(repositoryRoot) || !File.Exists(expectedPath))
            {
                continue;
            }

            cases.Add(new CorpusCase(name, repositoryRoot, LoadExpected(expectedPath)));
        }

        return cases;
    }

    private static IReadOnlyList<CorpusExpectedEntry> LoadExpected(string path)
    {
        var json = File.ReadAllText(path);
        var entries = JsonSerializer.Deserialize(json, CorpusJsonContext.Default.CorpusExpectedEntryArray);

        return entries is null ? [] : entries.ToArray();
    }
}

/// <summary>
/// One expected finding of a case's <c>expected.json</c>: the rule identifier,
/// the repository-relative path with forward slashes, the 1-based line and
/// column, and, where relevant, the triage label (the quality-budgets
/// specification, section 4.2). The matched value never travels in this shape.
/// </summary>
public sealed record CorpusExpectedEntry
{
    [JsonPropertyName("ruleId"), JsonPropertyOrder(0)]
    public string RuleId { get; set; } = string.Empty;

    [JsonPropertyName("path"), JsonPropertyOrder(1)]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("line"), JsonPropertyOrder(2)]
    public int Line { get; set; }

    [JsonPropertyName("column"), JsonPropertyOrder(3)]
    public int Column { get; set; }

    [JsonPropertyName("triage"), JsonPropertyOrder(4)]
    public string? Triage { get; set; }
}
