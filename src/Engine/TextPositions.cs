// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// Scalar-value positions in a .NET string (rule-pack format, section 7.5).
/// A match never splits a Unicode scalar value, and line and column numbers
/// count scalar values so the four engines agree. A CRLF pair is one line
/// break.
/// </summary>
internal static class TextPositions
{
    /// <summary>Writes the 1-based line and column of a UTF-16 index.</summary>
    public static void LineAndColumn(string text, int charIndex, out int line, out int column)
    {
        line = 1;
        var lineStart = 0;
        var index = 0;

        while (index < charIndex && index < text.Length)
        {
            if (text[index] == '\r')
            {
                line++;
                index++;
                if (index < charIndex && index < text.Length && text[index] == '\n')
                {
                    index++;
                }

                lineStart = index;
                continue;
            }

            if (text[index] == '\n')
            {
                line++;
                index++;
                lineStart = index;
                continue;
            }

            index++;
        }

        column = CountScalars(text.AsSpan(lineStart, Math.Max(0, charIndex - lineStart))) + 1;
    }

    /// <summary>Advances one scalar value, or stays at the end of the text.</summary>
    public static int NextScalar(string text, int charIndex)
    {
        if (charIndex >= text.Length)
        {
            return text.Length;
        }

        if (char.IsHighSurrogate(text[charIndex])
            && charIndex + 1 < text.Length
            && char.IsLowSurrogate(text[charIndex + 1]))
        {
            return charIndex + 2;
        }

        return charIndex + 1;
    }

    /// <summary>Counts the scalar values in a slice.</summary>
    public static int CountScalars(ReadOnlySpan<char> text)
    {
        var count = 0;
        var index = 0;

        while (index < text.Length)
        {
            count++;
            index += char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
                ? 2
                : 1;
        }

        return count;
    }
}
