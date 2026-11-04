// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The byte-level decisions the scan takes on every candidate file and every
/// history blob, in the order section 6.4 of the configuration
/// specification fixes: the encoding byte-order marks, then the binary
/// check, then the UTF-8 decode. Also the Git LFS pointer test and the git
/// blob object identifier of the bytes the scan actually read.
/// </summary>
internal static class ContentRules
{
    private const string GitLfsPointerBanner = "version https://git-lfs.github.com/spec/v1";
    private const int BinaryProbeLength = 8_192;

    private static readonly UTF8Encoding s_strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// The encoding the classification named.
    /// </summary>
    internal enum ContentEncoding
    {
        /// <summary>UTF-16 little endian, read through a byte-order mark.</summary>
        Utf16Le,

        /// <summary>UTF-16 big endian, read through a byte-order mark.</summary>
        Utf16Be,

        /// <summary>UTF-8, with or without a byte-order mark.</summary>
        Utf8,

        /// <summary>Binary content with a NUL byte in the first 8,192 bytes.</summary>
        Binary,
    }

    /// <summary>
    /// Tests whether the bytes begin with the Git LFS pointer banner. A
    /// pointer names an object the scan must not fetch (IQ-21).
    /// </summary>
    /// <param name="bytes">The bytes of the file or blob.</param>
    /// <returns><see langword="true"/> when the bytes are a Git LFS pointer.</returns>
    internal static bool IsGitLfsPointer(ReadOnlySpan<byte> bytes)
    {
        var banner = GitLfsPointerBanner;

        if (bytes.Length < banner.Length)
        {
            return false;
        }

        for (var index = 0; index < banner.Length; index++)
        {
            if (bytes[index] != (byte)banner[index])
            {
                return false;
            }
        }

        // The banner ends the first line: a newline or a carriage return must
        // follow, exactly as the pointer format writes it.
        return bytes.Length > banner.Length
            && (bytes[banner.Length] == (byte)'\n' || bytes[banner.Length] == (byte)'\r');
    }

    /// <summary>
    /// Classifies the bytes in the specification's order: the UTF-16 and
    /// UTF-8 byte-order marks first, then the NUL-byte binary probe, then
    /// plain UTF-8.
    /// </summary>
    /// <param name="bytes">The bytes of the file or blob.</param>
    /// <param name="encoding">The encoding the classification named.</param>
    /// <param name="markLength">The byte length of the leading byte-order mark, when present.</param>
    /// <returns><see langword="true"/> when the bytes carry a scan-able encoding.</returns>
    internal static bool TryClassify(ReadOnlySpan<byte> bytes, out ContentEncoding encoding, out int markLength)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = ContentEncoding.Utf16Le;
            markLength = 2;
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = ContentEncoding.Utf16Be;
            markLength = 2;
            return true;
        }

        var probeLength = Math.Min(BinaryProbeLength, bytes.Length);

        for (var index = 0; index < probeLength; index++)
        {
            if (bytes[index] == 0)
            {
                encoding = ContentEncoding.Binary;
                markLength = 0;
                return false;
            }
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = ContentEncoding.Utf8;
            markLength = 3;
            return true;
        }

        encoding = ContentEncoding.Utf8;
        markLength = 0;
        return true;
    }

    /// <summary>
    /// Decodes the text of the bytes, honouring the byte-order mark the
    /// classification named. The UTF-16 decoders read the mark's endian
    /// order; UTF-8 decodes strictly, so invalid sequences skip the file with
    /// a note rather than a silent replacement (section 6.4).
    /// </summary>
    /// <param name="bytes">The bytes of the file or blob.</param>
    /// <param name="encoding">The encoding the classification named.</param>
    /// <param name="markLength">The byte length of the leading byte-order mark, when present.</param>
    /// <returns>The decoded text.</returns>
    internal static string Decode(ReadOnlySpan<byte> bytes, ContentEncoding encoding, int markLength)
    {
        return encoding switch
        {
            ContentEncoding.Utf16Le => Encoding.Unicode.GetString(bytes[markLength..]),
            ContentEncoding.Utf16Be => Encoding.BigEndianUnicode.GetString(bytes[markLength..]),
            _ => s_strictUtf8.GetString(bytes[markLength..]),
        };
    }

    /// <summary>
    /// Runs the whole byte-level pipeline the specification fixes for one
    /// candidate: the Git LFS pointer test, the classification, then the
    /// decode. A decision that skips the candidate writes its note and
    /// returns <see langword="null"/>.
    /// </summary>
    /// <param name="bytes">The bytes of the file or blob.</param>
    /// <param name="relativePath">The forward-slash path, for the note.</param>
    /// <param name="notes">The notes list the skips reach.</param>
    /// <returns>The decoded text, or <see langword="null"/> when the candidate was skipped.</returns>
    internal static string? TryDecode(ReadOnlySpan<byte> bytes, string relativePath, List<string> notes)
    {
        if (IsGitLfsPointer(bytes))
        {
            notes.Add(FormattableString.Invariant(
                $"Skipped '{relativePath}': the file is a Git LFS pointer. The scan does not fetch Git LFS objects."));
            return null;
        }

        if (!TryClassify(bytes, out var encoding, out var markLength))
        {
            notes.Add(FormattableString.Invariant($"Skipped '{relativePath}': the file is binary."));
            return null;
        }

        try
        {
            return Decode(bytes, encoding, markLength);
        }
        catch (DecoderFallbackException)
        {
            notes.Add(FormattableString.Invariant(
                $"Skipped '{relativePath}': the bytes are not valid UTF-8 and the file carries no byte-order mark."));
            return null;
        }
    }

    /// <summary>
    /// Computes the git blob object identifier of the bytes the scan read, so
    /// history is skipped for the blobs a work-tree file already covers
    /// (section 7 of the configuration specification).
    /// </summary>
    /// <param name="bytes">The bytes that were read.</param>
    /// <returns>The 40-character lowercase hexadecimal blob identifier.</returns>
    internal static string ComputeBlobIdentifier(ReadOnlySpan<byte> bytes)
    {
        // The blob header 'blob <length>\0' precedes exactly the way git
        // computes it, so a work-tree file whose bytes equal a historical
        // blob's bytes share one identifier.
        Span<byte> digest = stackalloc byte[20];
        using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        incremental.AppendData(Encoding.ASCII.GetBytes(FormattableString.Invariant($"blob {bytes.Length}\0")));
        incremental.AppendData(bytes);
        incremental.GetHashAndReset(digest);

        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
