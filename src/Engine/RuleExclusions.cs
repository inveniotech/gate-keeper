// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The closed exclusions of section 8.2. A match any one of them applies to
/// is discarded. Digit and letter boundaries stay the boundary field; they
/// are not exclusions.
/// </summary>
internal static class RuleExclusions
{
    /// <summary>
    /// Reports whether the finding span is void under the rule's exclusions,
    /// <c>excludePrefixes</c> or <c>excludeValues</c>.
    /// </summary>
    public static bool Voids(string text, int start, int length, RulePatternMatcher pattern)
    {
        var span = text.AsSpan(start, length);

        if (pattern.ExcludePrefixes is not null)
        {
            foreach (var prefix in pattern.ExcludePrefixes)
            {
                if (span.StartsWith(prefix.AsSpan(), StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        if (pattern.ExcludeValues is not null)
        {
            foreach (var value in pattern.ExcludeValues)
            {
                if (span.Equals(value.AsSpan(), StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        if (pattern.Exclusions is null)
        {
            return false;
        }

        foreach (var exclusion in pattern.Exclusions)
        {
            if (Applies(exclusion, text, start, length))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Applies(RuleExclusion exclusion, string text, int start, int length)
    {
        var span = text.AsSpan(start, length);

        return exclusion switch
        {
            RuleExclusion.RepeatedDigits => IsRepeatedDigits(span),
            RuleExclusion.SequentialDigits => IsSequentialDigits(span),
            RuleExclusion.HexFragment => IsHexFragment(span),
            RuleExclusion.UuidFragment => OverlapsUuid(text, start, length),
            RuleExclusion.VersionString => IsVersionComponent(text, start, length),
            RuleExclusion.DateOrTimestamp => OverlapsDate(text, start, length),
            _ => false,
        };
    }

    /// <summary>Every scalar value in the span is the same digit.</summary>
    private static bool IsRepeatedDigits(ReadOnlySpan<char> span)
    {
        if (span.Length < 2 || span[0] is < '0' or > '9')
        {
            return false;
        }

        foreach (var character in span)
        {
            if (character != span[0])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The span is digits and each step is +1 or each step is −1, modulo 10,
    /// so the documented ascending example that wraps from 9 to 0 is sequential.
    /// </summary>
    private static bool IsSequentialDigits(ReadOnlySpan<char> span)
    {
        if (span.Length < 2)
        {
            return false;
        }

        foreach (var character in span)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        var direction = 0;

        for (var index = 1; index < span.Length; index++)
        {
            var step = (span[index] - span[index - 1] + 10) % 10;

            if (step is not (1 or 9))
            {
                return false;
            }

            if (direction == 0)
            {
                direction = step;
            }
            else if (direction != step)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The span is hexadecimal and contains a letter, so a decimal run is not
    /// a hex fragment.
    /// </summary>
    private static bool IsHexFragment(ReadOnlySpan<char> span)
    {
        if (span.Length < 2)
        {
            return false;
        }

        var letter = false;

        foreach (var character in span)
        {
            if (character is >= '0' and <= '9')
            {
                continue;
            }

            if (character is (>= 'A' and <= 'F') or (>= 'a' and <= 'f'))
            {
                letter = true;
                continue;
            }

            return false;
        }

        return letter;
    }

    /// <summary>The span overlaps a hyphenated UUID of the 8-4-4-4-12 form.</summary>
    private static bool OverlapsUuid(string text, int start, int length)
    {
        const int UuidLength = 36;
        var from = Math.Max(0, start - UuidLength);
        var to = Math.Min(text.Length, start + length + UuidLength);

        for (var index = from; index + UuidLength <= to; index++)
        {
            if (!IsUuidAt(text, index))
            {
                continue;
            }

            if (start < index + UuidLength && index < start + length)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUuidAt(string text, int index)
    {
        return HexRun(text, index, 8)
            && text[index + 8] == '-'
            && HexRun(text, index + 9, 4)
            && text[index + 13] == '-'
            && HexRun(text, index + 14, 4)
            && text[index + 18] == '-'
            && HexRun(text, index + 19, 4)
            && text[index + 23] == '-'
            && HexRun(text, index + 24, 12);
    }

    private static bool HexRun(string text, int index, int length)
    {
        for (var offset = 0; offset < length; offset++)
        {
            var character = text[index + offset];

            if (character is not ((>= '0' and <= '9') or (>= 'A' and <= 'F') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The span sits inside a dotted numeric version, optionally led by one
    /// <c>v</c>. A bare digit run with no dot is not a version.
    /// </summary>
    private static bool IsVersionComponent(string text, int start, int length)
    {
        var left = start;

        while (left > 0 && text[left - 1] is '.' or (>= '0' and <= '9'))
        {
            left--;
        }

        if (left > 0 && (text[left - 1] is 'v' or 'V') && (left == 1 || !IsAsciiLetterOrDigit(text[left - 2])))
        {
            left--;
        }

        var right = start + length;

        while (right < text.Length && text[right] is '.' or (>= '0' and <= '9'))
        {
            right++;
        }

        return IsVersion(text.AsSpan(left, right - left));
    }

    private static bool IsVersion(ReadOnlySpan<char> slice)
    {
        var index = 0;

        if (slice.Length > 0 && slice[0] is 'v' or 'V')
        {
            index++;
        }

        if (!TakeDigits(slice, ref index))
        {
            return false;
        }

        var groups = 0;

        while (index < slice.Length && slice[index] == '.')
        {
            index++;

            if (!TakeDigits(slice, ref index))
            {
                return false;
            }

            groups++;
        }

        return groups >= 1 && index == slice.Length;
    }

    /// <summary>
    /// The span overlaps a delimited date or timestamp, or the span itself is
    /// an 8-digit <c>YYYYMMDD</c> date. An 8-digit window inside a longer run
    /// is not a date: that reading would void an identifier that happens to
    /// contain eight plausible digits.
    /// </summary>
    private static bool OverlapsDate(string text, int start, int length)
    {
        var span = text.AsSpan(start, length);

        if (span.Length == 8 && IsCompactDate(span))
        {
            return true;
        }

        var from = Math.Max(0, start - 32);
        var to = Math.Min(text.Length, start + length + 32);

        for (var index = from; index < to; index++)
        {
            if (DelimitedDateLength(text, index) is not int dateLength)
            {
                continue;
            }

            if (start < index + dateLength && index < start + length)
            {
                return true;
            }
        }

        return false;
    }

    private static int? DelimitedDateLength(string text, int index)
    {
        if (TryDate(text, index, '-', out var yearMonthDay) && yearMonthDay)
        {
            return HasTime(text, index + 10) ? 19 : 10;
        }

        if (TryDate(text, index, '/', out yearMonthDay) && yearMonthDay)
        {
            return 10;
        }

        if (index + 10 <= text.Length
            && text[index + 2] == '-'
            && text[index + 5] == '-'
            && IsYear(text.AsSpan(index + 6, 4))
            && IsMonthDay(text.AsSpan(index + 3, 2), text.AsSpan(index, 2)))
        {
            return 10;
        }

        return null;
    }

    private static bool TryDate(string text, int index, char separator, out bool yearFirst)
    {
        yearFirst = false;

        if (index + 10 > text.Length || text[index + 4] != separator || text[index + 7] != separator)
        {
            return false;
        }

        if (!IsYear(text.AsSpan(index, 4)) || !IsMonthDay(text.AsSpan(index + 5, 2), text.AsSpan(index + 8, 2)))
        {
            return false;
        }

        yearFirst = true;
        return true;
    }

    private static bool HasTime(string text, int index)
    {
        return index + 9 <= text.Length
            && text[index] == 'T'
            && IsDigits(text.AsSpan(index + 1, 2))
            && text[index + 3] == ':'
            && IsDigits(text.AsSpan(index + 4, 2))
            && text[index + 6] == ':'
            && IsDigits(text.AsSpan(index + 7, 2));
    }

    private static bool IsCompactDate(ReadOnlySpan<char> span)
    {
        return IsYear(span[..4]) && IsMonthDay(span.Slice(4, 2), span.Slice(6, 2));
    }

    private static bool IsYear(ReadOnlySpan<char> year)
    {
        if (!TryInvariant(year, out var value))
        {
            return false;
        }

        return value is >= 1900 and <= 2100;
    }

    private static bool IsMonthDay(ReadOnlySpan<char> month, ReadOnlySpan<char> day)
    {
        if (!TryInvariant(month, out var monthValue) || !TryInvariant(day, out var dayValue))
        {
            return false;
        }

        var maxDay = monthValue switch
        {
            1 or 3 or 5 or 7 or 8 or 10 or 12 => 31,
            4 or 6 or 9 or 11 => 30,
            2 => 29,
            _ => 0,
        };

        return dayValue >= 1 && dayValue <= maxDay;
    }

    private static bool TakeDigits(ReadOnlySpan<char> slice, ref int index)
    {
        var start = index;

        while (index < slice.Length && slice[index] is >= '0' and <= '9')
        {
            index++;
        }

        return index > start;
    }

    private static bool TryInvariant(ReadOnlySpan<char> text, out int value)
    {
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsDigits(ReadOnlySpan<char> slice)
    {
        if (slice.Length == 0)
        {
            return false;
        }

        foreach (var character in slice)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetterOrDigit(char character)
    {
        return character is (>= '0' and <= '9') or (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');
    }
}
