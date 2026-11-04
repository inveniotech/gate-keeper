// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// One scanned file: the forward-slash path relative to the repository root,
/// in Unicode Normalisation Form C, and the decoded text the matching stage
/// reads. The raw bytes never travel further; encoding, binary and size
/// decisions were already taken by the enumerator (section 6.4 of the
/// configuration specification, which also declines to carry any matched
/// value anywhere it does not need to).
/// </summary>
/// <param name="Path">The forward-slash path relative to the repository root.</param>
/// <param name="Text">The decoded text of the file.</param>
/// <exception cref="ArgumentException">The path is empty or whitespace.</exception>
public sealed record EnumeratedFile(string Path, string Text)
{
    /// <summary>Gets the forward-slash path relative to the repository root.</summary>
    public string Path { get; } = string.IsNullOrWhiteSpace(Path)
        ? throw new ArgumentException(
            "An enumerated file needs its repository-relative path.",
            nameof(Path))
        : Path;
}
