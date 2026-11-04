// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The options one enumeration run carries: the directory to scan, the size
/// limit, the extra exclusion globs, the submodule policy, the
/// <c>--changed-since</c> reference and whether history scanning is in scope
/// (sections 6 to 8 of the configuration specification). The configuration
/// loader (P1-08) fills them from <c>ndpa-check.toml</c>; until then callers
/// set every field explicitly.
/// </summary>
/// <remarks>
/// Every setter validates its value in place, so an invalid option is a tool
/// error before any file is read (rule 28).
/// </remarks>
public sealed class FileEnumerationOptions
{
    private long _maxFileBytes = 1_048_576;

    /// <summary>
    /// Gets or sets the directory the scan runs on. Inside a git work tree
    /// this MUST be the repository root; outside git it is the directory the
    /// scan walks. An empty value is repaired by
    /// <see cref="FileEnumerator.EnumerateAsync"/>, which refuses the scan.
    /// </summary>
    public string Root
    {
        get;
        set;
    } = string.Empty;

    /// <summary>
    /// Gets or sets the largest file the scan reads, in bytes, compared
    /// against the raw byte length before decoding. The default is one
    /// mebibyte, the effective <c>scan.maxFileBytes</c> value of the
    /// <c>default</c> profile.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The limit is below one byte.</exception>
    public long MaxFileBytes
    {
        get => _maxFileBytes;
        set => _maxFileBytes = value >= 1
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(MaxFileBytes),
                value,
                "The per-file size limit must be at least one byte.");
    }

    /// <summary>
    /// Gets or sets the extra exclusion globs, matching the
    /// <c>scan.exclude</c> configuration key. Each entry is validated against
    /// the subsection 6.3 glob subset; the built-in exclusions cannot be
    /// removed, whatever the globs name.
    /// </summary>
    /// <exception cref="EngineToolException">An entry is outside the glob subset.</exception>
    public IReadOnlyList<string>? ExcludeGlobs
    {
        get;
        set
        {
            // Validation runs here, so a glob outside the subset is a tool
            // error before any file is read (rule 28).
            if (value is not null)
            {
                foreach (var entry in value)
                {
                    PathGlob.Compile(entry);
                }
            }

            field = value;
        }
    }

    /// <summary>
    /// Gets or sets whether checked-out submodules are enumerated with their
    /// paths prefixed by the submodule path. The default is <see langword="false"/>,
    /// the <c>--include-submodules</c> default (IQ-21).
    /// </summary>
    public bool IncludeSubmodules { get; set; }

    /// <summary>
    /// Gets or sets the <c>--changed-since</c> reference. A non-empty value
    /// limits the work-tree scan to the tracked paths whose content differs
    /// from the reference. Empty and whitespace-only values mean the whole
    /// work tree.
    /// </summary>
    public string? ChangedSince { get; set; }

    /// <summary>
    /// Gets or sets whether history scanning runs. The default is
    /// <see langword="false"/>: the <c>default</c> profile keeps history off
    /// and NFR-012 unchanged (IQ-83).
    /// </summary>
    public bool ScanHistory { get; set; }
}



