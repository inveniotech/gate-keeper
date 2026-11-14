// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// Where a finding was located: the repository-relative path, a 1-based line
/// and a 1-based column. The path uses forward slashes and is relative to the
/// repository root (rule 22); the line and the column are never zero.
/// </summary>
/// <param name="path">The repository-relative path with forward-slash separators.</param>
/// <param name="line">The 1-based line.</param>
/// <param name="column">The 1-based column.</param>
/// <exception cref="ArgumentException">The path is empty or whitespace.</exception>
/// <exception cref="ArgumentOutOfRangeException">The line or the column is below 1.</exception>
[JsonConverter(typeof(LocationJsonConverter))]
public readonly record struct Location(string path, int line, int column)
{
    /// <summary>Gets the repository-relative path with forward-slash separators.</summary>
    public string Path { get; } = string.IsNullOrWhiteSpace(path)
        ? throw new ArgumentException("A location's path is required and cannot be only whitespace.", nameof(path))
        : path;

    /// <summary>Gets the 1-based line.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The line is below 1.</exception>
    public int Line { get; } = line >= 1
        ? line
        : throw new ArgumentOutOfRangeException(nameof(line), line, "A location's line is 1-based, so it is at least 1.");

    /// <summary>Gets the 1-based column.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The column is below 1.</exception>
    public int Column { get; } = column >= 1
        ? column
        : throw new ArgumentOutOfRangeException(nameof(column), column, "A location's column is 1-based, so it is at least 1.");
}
