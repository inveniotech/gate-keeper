// SPDX-License-Identifier: Apache-2.0

using InvenioTech.GateKeeper.Engine;

namespace InvenioTech.GateKeeper.Engine.Tests;

/// <summary>
/// The closed validator set (P1-06, card 2; rule-pack format, section 8.1).
/// The NUBAN vectors are the two worked examples the Central Bank of Nigeria
/// published, which the corpus pins to bank code 011.
/// </summary>
public sealed class RuleValidatorTests
{
    private static readonly RuleValidator Digits = new(RuleValidatorName.DigitsLength, Length: 11, Prefixes: null);

    private static readonly RuleValidator Nuban = new(RuleValidatorName.Nuban, Length: null, Prefixes: null);

    private static readonly RuleValidator Phone = new(RuleValidatorName.NgPhonePrefix, Length: null, Prefixes: ["080", "081", "090"]);

    private static readonly RuleValidator Email = new(RuleValidatorName.EmailNotExampleDomain, Length: null, Prefixes: null);

    [Fact]
    public void Registry_Should_ResolveTheClosedSet_When_TheDefaultRegistryIsUsed()
    {
        RuleValidatorRegistry.Default.TryGet(RuleValidatorName.DigitsLength, out var digits).Should().BeTrue();
        RuleValidatorRegistry.Default.TryGet(RuleValidatorName.Nuban, out var nuban).Should().BeTrue();
        RuleValidatorRegistry.Default.TryGet(RuleValidatorName.NgPhonePrefix, out var phone).Should().BeTrue();
        RuleValidatorRegistry.Default.TryGet(RuleValidatorName.EmailNotExampleDomain, out var email).Should().BeTrue();
        digits!.Name.Should().Be(RuleValidatorName.DigitsLength);
        nuban!.Name.Should().Be(RuleValidatorName.Nuban);
        phone!.Name.Should().Be(RuleValidatorName.NgPhonePrefix);
        email!.Name.Should().Be(RuleValidatorName.EmailNotExampleDomain);
    }

    [Fact]
    public void Registry_Should_RefuseARepeatedName_When_TwoValidatorsAnswerToIt()
    {
        var act = () => new RuleValidatorRegistry(new DigitsLengthValidator(), new DigitsLengthValidator());

        act.Should().Throw<ArgumentException>().WithMessage("*digits-length*");
    }

    [Theory]
    [InlineData("32826314843", true)]
    [InlineData("3282-6314-843", true)]
    [InlineData("3282631484", false)]
    [InlineData("", false)]
    public void DigitsLength_Should_CountDigits_When_TheSpanCarriesSeparators(string span, bool accepted)
    {
        new DigitsLengthValidator().Validate(span, Digits).Should().Be(accepted);
    }

    [Theory]
    [InlineData("0000014579", true)]
    [InlineData("0000000220", true)]
    [InlineData("0000-0145-79", true)]
    [InlineData("0000014570", false)]
    [InlineData("000001457", false)]
    [InlineData("00000145790", false)]
    [InlineData("not-an-account", false)]
    public void Nuban_Should_AcceptThePublishedCheckDigit_When_TheSerialWeightsMatch(string span, bool accepted)
    {
        new NubanValidator().Validate(span, Nuban).Should().Be(accepted);
    }

    [Theory]
    [InlineData("08079012062", true)]
    [InlineData("08100000000", true)]
    [InlineData("06079012062", false)]
    public void NgPhonePrefix_Should_RequireAPackPrefix_When_TheSpanOpens(string span, bool accepted)
    {
        new NgPhonePrefixValidator().Validate(span, Phone).Should().Be(accepted);
    }

    [Fact]
    public void NgPhonePrefix_Should_RejectTheSpan_When_TheRuleCarriesNoPrefixes()
    {
        var parameters = new RuleValidator(RuleValidatorName.NgPhonePrefix, Length: null, Prefixes: null);

        new NgPhonePrefixValidator().Validate("08079012062", parameters).Should().BeFalse();
    }

    [Theory]
    [InlineData("ada@ledgerops.ng", true)]
    [InlineData("ada@ledger-ops.example", true)]
    [InlineData("ada@notexample.com", true)]
    [InlineData("Ada@Example.COM", false)]
    [InlineData("ada@mail.example.com", false)]
    [InlineData("ada@example.net", false)]
    [InlineData("ada@example.org", false)]
    [InlineData("ada@foo.test", false)]
    [InlineData("ada@foo.invalid", false)]
    [InlineData("ada@localhost", false)]
    [InlineData("ada@files.localhost", false)]
    [InlineData("ada@", false)]
    [InlineData("@ledger-ops.ng", false)]
    [InlineData("not-an-address", false)]
    public void Email_Should_RefuseADocumentationDomain_When_TheAddressUsesOne(string span, bool accepted)
    {
        new EmailNotExampleDomainValidator().Validate(span, Email).Should().Be(accepted);
    }
}
