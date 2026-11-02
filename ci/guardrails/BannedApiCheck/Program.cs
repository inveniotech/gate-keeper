using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace GateKeeper.Guardrails.BannedApiCheck;

internal static class Program
{
    private static int Main(string[] args)
    {
        var paths = new List<string>();
        var excludes = new List<string>();
        string? allowedAssemblyName = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--allowed-assembly" when i + 1 < args.Length:
                    allowedAssemblyName = args[++i];
                    break;
                case "--exclude" when i + 1 < args.Length:
                    excludes.Add(args[++i]);
                    break;
                case "--help" or "-h":
                    Console.WriteLine("Usage: BannedApiCheck [--allowed-assembly <name>] [--exclude <wildcard>] <path>...");
                    return 0;
                default:
                    paths.Add(args[i]);
                    break;
            }
        }

        if (paths.Count == 0)
        {
            Console.Error.WriteLine("Error: at least one path is required.");
            return 2;
        }

        var findings = new List<string>();
        var examined = 0;

        foreach (var path in paths)
        {
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                Console.Error.WriteLine($"Error: path '{path}' does not exist.");
                return 2;
            }

            var files = Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*.dll", SearchOption.AllDirectories)
                : [path];

            foreach (var file in files)
            {
                if (SkipAssembly(file, allowedAssemblyName) || IsExcluded(file, excludes))
                {
                    continue;
                }

                var fileFindings = Examine(file);
                examined++;
                findings.AddRange(fileFindings);
            }
        }

        foreach (var finding in findings.OrderBy(f => f, StringComparer.Ordinal))
        {
            Console.WriteLine(finding);
        }

        if (findings.Count > 0)
        {
            Console.Error.WriteLine($"Banned-API check failed: {findings.Count} reference(s) to System.Net types.");
            return 1;
        }

        Console.WriteLine($"Banned-API check passed: {examined} assembly/ies examined, no System.Net references.");
        return 0;
    }

    private static bool SkipAssembly(string filePath, string? allowedAssemblyName)
    {
        if (string.IsNullOrWhiteSpace(allowedAssemblyName))
        {
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(filePath);
        return string.Equals(name, allowedAssemblyName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExcluded(string filePath, List<string> excludes)
    {
        var fileName = Path.GetFileName(filePath);
        foreach (var pattern in excludes)
        {
            if (MatchesWildcard(fileName, pattern))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesWildcard(string value, string pattern)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(value, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static List<string> Examine(string filePath)
    {
        var results = new List<string>();
        using var stream = File.OpenRead(filePath);
        using var peReader = new PEReader(stream);

        if (!peReader.HasMetadata)
        {
            return results;
        }

        var block = peReader.GetMetadata();
        unsafe
        {
            var reader = new MetadataReader(block.Pointer, block.Length);
            foreach (var handle in reader.TypeReferences)
            {
                var typeRef = reader.GetTypeReference(handle);
                var ns = reader.GetString(typeRef.Namespace);
                if (ns is not null && (ns == "System.Net" || ns.StartsWith("System.Net.", StringComparison.Ordinal)))
                {
                    var name = reader.GetString(typeRef.Name);
                    var relative = filePath.Replace(Path.DirectorySeparatorChar, '/');
                    results.Add($"{relative}: banned type reference {ns}.{name}");
                }
            }
        }

        return results;
    }
}
