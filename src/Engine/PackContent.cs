// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The content a pack loader (P1-03) can use once it was read: the typed pack
/// plus the parsed version the merge compares by.
/// </summary>
/// <param name="Pack">The typed content document.</param>
/// <param name="PackVersion">The parsed version: an Invenio pack's calendar triple, a local pack's positive integer.</param>
internal sealed record PackContentLoaded(RulePack Pack, PackVersionValue PackVersion);

/// <summary>One parsed pack version: a calendar triple or a local positive integer (IQ-36).</summary>
/// <param name="Calendar">The calendar triple for an Invenio pack, or null.</param>
/// <param name="LocalNumber">The local positive integer, or null.</param>
internal readonly record struct PackVersionValue(CalendarVersionValue? Calendar, int? LocalNumber)
{
    /// <summary>Builds the value for a calendar version.</summary>
    public static PackVersionValue ForCalendar(int year, int month, int patch)
    {
        return new PackVersionValue(new CalendarVersionValue(year, month, patch), null);
    }

    /// <summary>Builds the value for a local pack version.</summary>
    public static PackVersionValue ForLocal(int number)
    {
        return new PackVersionValue(null, number);
    }
}

/// <summary>
/// The numeric triple the calendar pack line format <c>YYYY.MM.P</c> expands
/// into (IQ-36): patched as integers, never as a string sort.
/// </summary>
/// <param name="Year">The line's year.</param>
/// <param name="Month">The line's month.</param>
/// <param name="Patch">The patch number: 0 opens the line, above 0 is a correction.</param>
internal readonly record struct CalendarVersionValue(int Year, int Month, int Patch);
