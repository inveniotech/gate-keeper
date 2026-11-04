// SPDX-License-Identifier: Apache-2.0

namespace InvenioTech.GateKeeper.Engine;

/// <summary>
/// The one read pipeline the work-tree enumeration and the directory walk
/// share: size check, full read, then the byte-level rules of
/// <see cref="ContentRules"/>. It also records the git blob identifier of the
/// bytes it read, so history skips blobs a work-tree file covers (section 7).
/// </summary>
internal static class EnumerationFileReader
{
    /// <summary>
    /// Reads and decodes one candidate file, appending the result or the
    /// skip note.
    /// </summary>
    /// <param name="fullPath">The absolute path of the file.</param>
    /// <param name="relativePath">The forward-slash path relative to the repository root.</param>
    /// <param name="maxFileBytes">The effective <c>scan.maxFileBytes</c>.</param>
    /// <param name="files">The files the scan keeps.</param>
    /// <param name="notes">The notes the skips reach.</param>
    /// <param name="coveredBlobs">The blob identifiers the work tree read; <see langword="null"/> for the outside-git walk.</param>
    /// <returns><see langword="true"/> when the file entered the scan.</returns>
    internal static async Task<bool> ReadFileAsync(
        string fullPath,
        string relativePath,
        long maxFileBytes,
        List<EnumeratedFile> files,
        List<string> notes,
        HashSet<string>? coveredBlobs)
    {
        var length = new FileInfo(fullPath).Length;

        if (length > maxFileBytes)
        {
            notes.Add(FormattableString.Invariant(
                $"Skipped '{relativePath}': the file is {length} bytes, and the limit is {maxFileBytes} bytes."));
            return false;
        }

        byte[] bytes;

        try
        {
            bytes = await File.ReadAllBytesAsync(fullPath).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            notes.Add(FormattableString.Invariant($"Skipped '{relativePath}': the file could not be read."));
            return false;
        }

        coveredBlobs?.Add(ContentRules.ComputeBlobIdentifier(bytes));

        var content = ContentRules.TryDecode(bytes, relativePath, notes);

        if (content is null)
        {
            return false;
        }

        files.Add(new EnumeratedFile(relativePath, content));
        return true;
    }
}
