// SPDX-License-Identifier: Apache-2.0
using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

public sealed class PackAssemblyTests
{
    [Fact]
    public void AssembledPack_Should_LoadAndMatch_When_DevelopmentSignatureVerifies()
    {
        var input = PackTestSupport.SignedPack("assembly");
        var loaded = PackLoader.Load([input], [], PackTestSupport.Catalogue, BundledPacksOnlyPolicy.Instance);
        loaded.Packs.Should().ContainSingle().Which.Id.Should().Be("invenio.synthetic.assembly");
        loaded.Rules.Select(rule => rule.Rule.Id).Should().Equal("pii.synthetic.alpha", "pii.synthetic.zeta");
        var matches = new MatcherEngine(loaded.Rules.Select(rule => rule.Rule).ToArray()).Match("sample.txt", "gatekeeper-assembly-probe");
        matches.Select(match => match.RuleId).Should().Equal("pii.synthetic.alpha", "pii.synthetic.zeta");
        var changed = input.ContentBytes.ToArray();
        changed[0] ^= 1;
        Action load = () => PackLoader.Load([input with { ContentBytes = changed }], [], PackTestSupport.Catalogue, BundledPacksOnlyPolicy.Instance);
        load.Should().Throw<EngineToolException>();
    }
}
