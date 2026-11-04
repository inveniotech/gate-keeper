// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The <c>nuban</c> validator: the finding span must satisfy the check-digit
/// algorithm the Central Bank of Nigeria published in its 2010 proposals on
/// the Nigeria Uniform Bank Account Number (NUBAN) scheme, version 0.4
/// (section 8.1 of the rule-pack format; the check digit is engine logic,
/// not pack data).
/// </summary>
/// <remarks>
/// <para>
/// The published algorithm weights the twelve digits of the 3-digit bank code
/// and the 9-digit account serial with <c>3,7,3,3,7,3,3,7,3,3,7,3</c>, takes
/// the sum modulo 10, subtracts the remainder from 10, and writes a remainder
/// of zero as check digit zero. The customer-facing account number is the
/// serial followed by that check digit: ten digits.
/// </para>
/// <para>
/// A rule pack carries no bank code — section 8.1 fixes <c>nuban</c> as a
/// validator with no pack data — so the engine weights the span's own nine
/// serial digits with the serial portion of the published sequence
/// (<c>3,7,3,3,7,3,3,7,3</c>, the twelve-weight sequence from the fourth
/// weight). That equals the published twelve-digit algorithm exactly when the
/// bank code's own weighted sum is a multiple of ten, which First Bank of
/// Nigeria's code <c>011</c> is (<c>0×3 + 1×7 + 1×3 = 10</c>), and the
/// golden corpus pins bank code <c>011</c> for every NUBAN it embeds
/// (P1-02). The two worked examples of the published proposal — serial
/// <c>000001457</c> with check digit <c>9</c>, and serial <c>000000022</c>
/// with check digit <c>0</c> — are asserted in the tests so the arithmetic
/// cannot drift.
/// </para>
/// <para>
/// The span may carry non-digit characters beside its digits; the validator
/// reads the digits in order and requires exactly ten of them, the last being
/// the check digit. A span with any other digit count fails.
/// </para>
/// </remarks>
public sealed class NubanValidator : IRuleValidator
{
    /// <summary>
    /// The serial weights: the published twelve-weight sequence from the
    /// fourth weight, covering the nine serial digits of a ten-digit account
    /// number.
    /// </summary>
    private static readonly int[] SerialWeights = [3, 7, 3, 3, 7, 3, 3, 7, 3];

    /// <inheritdoc />
    public RuleValidatorName Name => RuleValidatorName.Nuban;

    /// <inheritdoc />
    public bool Validate(ReadOnlySpan<char> span, RuleValidator parameters)
    {
        var digitCount = 0;
        var weightedSum = 0;
        var checkDigit = -1;

        foreach (var character in span)
        {
            if (character is < '0' or > '9')
            {
                continue;
            }

            if (digitCount == 9)
            {
                checkDigit = character - '0';
            }
            else if (digitCount < 9)
            {
                weightedSum += (character - '0') * SerialWeights[digitCount];
            }
            else
            {
                // More than ten digits: not one account number.
                return false;
            }

            digitCount++;
        }

        if (digitCount != 10 || checkDigit < 0)
        {
            return false;
        }

        var remainder = weightedSum % 10;
        var expectedCheckDigit = remainder == 0 ? 0 : 10 - remainder;

        return expectedCheckDigit == checkDigit;
    }
}
