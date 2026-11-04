// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Corpus.Tests;

/// <summary>
/// The structural checks every case obeys, one case per theory member, so a
/// malformed case names itself rather than failing an aggregate run.
/// </summary>
public sealed class CorpusCaseWellFormedTests
{
    [Theory]
    [MemberData(nameof(CorpusCaseSource.Cases), MemberType = typeof(CorpusCaseSource))]
    public void Corpus_Case_Should_BeWellFormed_When_TheHarnessLoadsIt(CorpusCase item)
    {
        var problems = CorpusCaseValidator.Validate(item);

        const string expectedEmpty = "(well-formed corpus case)";
        problems.Should().BeEmpty(expectedEmpty);
    }

    [Fact]
    public void Corpus_Should_KeepItsCaseFloor_When_TheCasesAreCounted()
    {
        var cases = CorpusCase.LoadAll(CorpusPaths.Root);

        cases.Should().HaveCountGreaterThanOrEqualTo(30, "the card fixes at least 30 opening cases");
    }

    [Fact]
    public void Corpus_ReadMe_Should_NameEveryCaseInItsTable_When_TheCorpusChanges()
    {
        var readme = File.ReadAllText(Path.Combine(CorpusPaths.Root, "README.md"));
        var cases = CorpusCase.LoadAll(CorpusPaths.Root);

        foreach (var item in cases)
        {
            readme.Should().Contain($"`{item.Name}`", $"the corpus documentation must name case {item.Name}");
        }
    }
}
