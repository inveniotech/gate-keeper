// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using InvenioTech.GateKeeper.Engine;

try
{
    if (args.Length != 1) throw new ArgumentException("Supply one checkout root.");
    var root = Path.GetFullPath(args[0]);
    long totalBytes = 0;
    var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
    foreach (var path in Directory.EnumerateFiles(root, "*", options))
    {
        totalBytes = checked(totalBytes + new FileInfo(path).Length);
        if (totalBytes > 268_435_456) throw new InvalidOperationException("The checkout exceeds the corpus size bound.");
    }
    // Reuse the product's exact eligibility, encoding, binary, size and ignore rules.
    var enumeration = await new FileEnumerator().EnumerateAsync(new FileEnumerationOptions { Root = root }).ConfigureAwait(false);
    using var writer = new Utf8JsonWriter(Console.OpenStandardOutput());
    writer.WriteStartObject();
    writer.WriteNumber("eligibleFiles", enumeration.Files.Count);
    writer.WriteNumber("totalBytes", totalBytes);
    writer.WriteEndObject();
    writer.Flush();
    return 0;
}
catch (Exception)
{
    Console.Error.WriteLine("Checkout inspection failed. Check its file access, size and local Git availability.");
    return 2;
}
