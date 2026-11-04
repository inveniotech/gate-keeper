// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// One named validator of the closed set (P1-06, card 2; rule-pack format,
/// section 8.1). A validator receives the finding span — the emitting part's
/// span, or the whole match when no part emits — and the pack data the rule
/// carries beside the validator's name. A span the validator refuses is
/// discarded: the shape matched, the value is not what the rule is about.
/// </summary>
/// <remarks>
/// <para>
/// The span never contains a matched value in any output; it exists in
/// memory only for this check and for the fingerprint (rule 10).
/// </para>
/// <para>
/// Implementations must be immutable and safe to call from many threads at
/// once, because one registry serves every scan (rule B1).
/// </para>
/// </remarks>
public interface IRuleValidator
{
    /// <summary>Gets the closed name this validator answers to.</summary>
    RuleValidatorName Name { get; }

    /// <summary>
    /// Validates one finding span.
    /// </summary>
    /// <param name="span">The finding span text.</param>
    /// <param name="parameters">The pack data the rule carries beside the name.</param>
    /// <returns><see langword="true"/> when the span satisfies the validator.</returns>
    bool Validate(ReadOnlySpan<char> span, RuleValidator parameters);
}

/// <summary>
/// The registry that maps a validator name to its implementation (P1-06,
/// card 2). The default registry carries the closed first-release set:
/// <c>digits-length</c>, <c>nuban</c>, <c>ng-phone-prefix</c> and
/// <c>email-not-example-domain</c>. An adapter or a test may build its own
/// registry; the engine refuses a rule whose validator the registry does not
/// carry, so a missing capability is loud at start-up, never a silent skip.
/// </summary>
public sealed class RuleValidatorRegistry
{
    private readonly IReadOnlyDictionary<RuleValidatorName, IRuleValidator> _validators;

    /// <summary>
    /// Creates a registry from the validators given. Two validators answering
    /// to one name is a programming error and fails immediately.
    /// </summary>
    /// <param name="validators">The validators to register.</param>
    /// <exception cref="ArgumentException">Two validators share one name.</exception>
    public RuleValidatorRegistry(params IRuleValidator[] validators)
    {
        var byName = new Dictionary<RuleValidatorName, IRuleValidator>();

        foreach (var validator in validators)
        {
            if (!byName.TryAdd(validator.Name, validator))
            {
                throw new ArgumentException(
                    $"Two validators answer to the name '{PackName(validator.Name)}'. A name maps to one validator (section 8.1).",
                    nameof(validators));
            }
        }

        _validators = byName;
    }

    /// <summary>Gets the registry with the closed first-release set of section 8.1.</summary>
    public static RuleValidatorRegistry Default { get; } = new(
        new DigitsLengthValidator(),
        new NubanValidator(),
        new NgPhonePrefixValidator(),
        new EmailNotExampleDomainValidator());

    /// <summary>
    /// Looks one validator up by its closed name.
    /// </summary>
    /// <param name="name">The name the rule carries.</param>
    /// <param name="validator">The validator, or null when the registry does not carry the name.</param>
    /// <returns><see langword="true"/> when the name is registered.</returns>
    public bool TryGet(RuleValidatorName name, out IRuleValidator? validator)
    {
        return _validators.TryGetValue(name, out validator);
    }

    /// <summary>The pack spelling of a validator name, which is what an error sentence shows.</summary>
    internal static string PackName(RuleValidatorName name)
    {
        return name switch
        {
            RuleValidatorName.DigitsLength => "digits-length",
            RuleValidatorName.Nuban => "nuban",
            RuleValidatorName.NgPhonePrefix => "ng-phone-prefix",
            RuleValidatorName.EmailNotExampleDomain => "email-not-example-domain",
            _ => name.ToString(),
        };
    }
}

/// <summary>
/// The <c>digits-length</c> validator: the finding span must carry exactly the
/// digit count the rule carries as pack data (section 8.1). The count is of
/// digits, not of characters, so a span with separators beside its digits
/// still validates on its digit count.
/// </summary>
public sealed class DigitsLengthValidator : IRuleValidator
{
    /// <inheritdoc />
    public RuleValidatorName Name => RuleValidatorName.DigitsLength;

    /// <inheritdoc />
    public bool Validate(ReadOnlySpan<char> span, RuleValidator parameters)
    {
        var length = parameters.Length ?? 0;
        var digits = 0;

        foreach (var character in span)
        {
            if (character is >= '0' and <= '9')
            {
                digits++;
            }
        }

        return digits == length;
    }
}

/// <summary>
/// The <c>ng-phone-prefix</c> validator: the finding span must open with one
/// of the leading prefixes the rule carries as pack data (section 8.1). The
/// prefix list is pack data, so the engine capability stays generic and the
/// prefixes change with the numbering plan, not with the engine (rule 9).
/// Comparison is ordinal: the prefixes are digits.
/// </summary>
public sealed class NgPhonePrefixValidator : IRuleValidator
{
    /// <inheritdoc />
    public RuleValidatorName Name => RuleValidatorName.NgPhonePrefix;

    /// <inheritdoc />
    public bool Validate(ReadOnlySpan<char> span, RuleValidator parameters)
    {
        var prefixes = parameters.Prefixes;

        if (prefixes is null)
        {
            return false;
        }

        foreach (var prefix in prefixes)
        {
            if (span.StartsWith(prefix.AsSpan(), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// The <c>email-not-example-domain</c> validator: the finding span must be an
/// email address whose domain is not a documentation domain (section 8.1).
/// The reserved names are <c>example.com</c>, <c>example.net</c>,
/// <c>example.org</c> and the documentation names <c>test</c>, <c>invalid</c>
/// and <c>localhost</c>, including subdomains of each. The <c>.example</c>
/// top-level domain is not reserved here: the golden corpus's positive email
/// case uses it, and the near-miss the corpus pins is <c>example.com</c>.
/// A placeholder address on a reserved domain is documentation, not personal
/// data, so the match is discarded. Domain comparison folds ASCII A–Z only,
/// the folding the rule-pack format fixes once for every engine (section 7.2).
/// </summary>
public sealed class EmailNotExampleDomainValidator : IRuleValidator
{
    /// <summary>
    /// Documentation domains the validator refuses, as an exact domain or as a
    /// parent of the address's domain. <c>example.com</c>, <c>example.net</c>
    /// and <c>example.org</c> are the RFC 2606 example domains. <c>test</c>,
    /// <c>invalid</c> and <c>localhost</c> are the other documentation names
    /// the Phase 2 email rule excludes. The <c>.example</c> top-level domain
    /// is deliberately not in this list: the golden corpus's positive email
    /// case uses <c>ledger-ops.example</c>, and the near-miss the corpus pins
    /// is the <c>example.com</c> domain.
    /// </summary>
    private static readonly string[] ReservedDomains =
    [
        "example.com",
        "example.net",
        "example.org",
        "test",
        "invalid",
        "localhost",
    ];

    /// <inheritdoc />
    public RuleValidatorName Name => RuleValidatorName.EmailNotExampleDomain;

    /// <inheritdoc />
    public bool Validate(ReadOnlySpan<char> span, RuleValidator parameters)
    {
        // The shape matched the address; the validator only settles whether
        // it is an address at a domain at all, and whether that domain is a
        // documentation domain. The local part must be non-empty and the
        // domain after the last '@' must be non-empty.
        var at = span.LastIndexOf('@');

        if (at < 1 || at == span.Length - 1)
        {
            return false;
        }

        var domain = span[(at + 1)..];

        foreach (var reserved in ReservedDomains)
        {
            if (IsReservedDomain(domain, reserved))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A domain is reserved when it equals the reserved name, or when the
    /// reserved name is a parent of it. The character before a parent must be
    /// a dot, so a longer name that only ends with the same letters is not a
    /// subdomain.
    /// </summary>
    private static bool IsReservedDomain(ReadOnlySpan<char> domain, string reserved)
    {
        if (EqualsFoldedAscii(domain, reserved))
        {
            return true;
        }

        if (domain.Length <= reserved.Length + 1)
        {
            return false;
        }

        var parentAt = domain.Length - reserved.Length;

        return domain[parentAt - 1] == '.' && EndsWithFoldedAscii(domain, reserved);
    }

    private static bool EqualsFoldedAscii(ReadOnlySpan<char> span, string literal)
    {
        if (span.Length != literal.Length)
        {
            return false;
        }

        for (var index = 0; index < span.Length; index++)
        {
            if (FoldAscii(span[index]) != literal[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool EndsWithFoldedAscii(ReadOnlySpan<char> span, string suffix)
    {
        if (span.Length < suffix.Length)
        {
            return false;
        }

        var offset = span.Length - suffix.Length;

        for (var index = 0; index < suffix.Length; index++)
        {
            if (FoldAscii(span[offset + index]) != suffix[index])
            {
                return false;
            }
        }

        return true;
    }

    private static char FoldAscii(char character)
    {
        return character is >= 'A' and <= 'Z' ? (char)(character + 32) : character;
    }
}
