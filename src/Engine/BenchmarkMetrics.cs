// SPDX-License-Identifier: Apache-2.0
#if GATEKEEPER_BENCHMARK
using System.Text.Json;

namespace InvenioTech.GateKeeper.Engine;

/// <summary>One rule's search calls and UTF-16 input suffix bytes, available only in instrumented builds.</summary>
/// <param name="RuleId">The rule identifier, never a source path.</param>
/// <param name="Calls">The number of regular-expression searches, including unsuccessful searches.</param>
/// <param name="InputBytes">The sum of searchable UTF-16 suffix lengths in bytes.</param>
public sealed record BenchmarkRegexMetric(string RuleId, long Calls, long InputBytes);

/// <summary>Per-file counters owned by one matching worker in an instrumented build.</summary>
public sealed class BenchmarkRegexCounters
{
    private readonly Dictionary<string, (long Calls, long Bytes)> values = new(StringComparer.Ordinal);

    /// <summary>Returns an immutable, ordinally sorted snapshot after matching.</summary>
    public IReadOnlyList<BenchmarkRegexMetric> Values => values.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => new BenchmarkRegexMetric(v.Key, v.Value.Calls, v.Value.Bytes)).ToArray();

    internal void Record(string ruleId, long bytes)
    {
        values.TryGetValue(ruleId, out var previous);
        values[ruleId] = (previous.Calls + 1, previous.Bytes + bytes);
    }
}

/// <summary>An anonymous matching-stage measurement; no source path, content or span is retained.</summary>
/// <param name="Milliseconds">Elapsed matching-stage time from a monotonic timer.</param>
/// <param name="AllocatedBytes">Current-thread allocation bytes while matching synchronously.</param>
/// <param name="Regex">The rule-level regular-expression counters.</param>
public sealed record BenchmarkFileMetric(double Milliseconds, long AllocatedBytes, IReadOnlyList<BenchmarkRegexMetric> Regex);

/// <summary>Writes anonymous repository benchmark artefacts; absent from release builds.</summary>
public static class BenchmarkMetrics
{
    /// <summary>Serialises per-file times and aggregate counters without reflection or source data.</summary>
    public static byte[] Write(IReadOnlyList<BenchmarkFileMetric> files)
    {
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            writer.WriteStartObject(); writer.WriteNumber("schemaVersion", 1); writer.WriteNumber("fileCount", files.Count);
            writer.WriteStartArray("fileMilliseconds"); foreach (var file in files) writer.WriteNumberValue(file.Milliseconds); writer.WriteEndArray();
            writer.WriteNumber("allocatedBytesPerFile", files.Count == 0 ? 0 : files.Sum(f => (double)f.AllocatedBytes) / files.Count);
            writer.WriteStartArray("regex");
            var groups = files.SelectMany(f => f.Regex).GroupBy(r => r.RuleId, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal);
            foreach (var group in groups)
            {
                writer.WriteStartObject(); writer.WriteString("ruleId", group.Key); writer.WriteNumber("calls", group.Sum(r => r.Calls));
                writer.WriteNumber("inputBytes", group.Sum(r => r.InputBytes)); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        bytes.WriteByte((byte)'\n');
        return bytes.ToArray();
    }
}
#endif
