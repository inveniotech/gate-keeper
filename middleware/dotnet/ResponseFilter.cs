// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text.Json.Nodes;

namespace InvenioTech.GateKeeper.Middleware;

/// <summary>A protected response failed; the host must not send a partial body.</summary>
public sealed class ResponseFilterException : Exception
{
    /// <summary>Whether the framework must abort an already committed response.</summary>
    public bool AbortRequired { get; }
    /// <summary>Creates a content-free response error.</summary>
    /// <param name="abortRequired">True if the headers are already committed.</param>
    public ResponseFilterException(bool abortRequired) : base("The protected response could not be filtered safely. Do not send its body.") => AbortRequired = abortRequired;
}

/// <summary>Atomic JSON-tree filtering with the owner's explicit array wildcard extension.</summary>
public sealed class ResponseFilter
{
    private readonly Func<string, string> redact;
    private readonly Segment[][] paths;
    private sealed record Segment(string Key, bool Wildcard);

    /// <summary>Compiles JSON Pointers; complete * segments select every array element.</summary>
    /// <param name="runtime">The prepared runtime redactor.</param>
    /// <param name="paths">Object paths; ~2 names a literal star, alongside ~0 and ~1.</param>
    public ResponseFilter(RuntimeMatcher runtime, IReadOnlyList<string> paths)
        : this((runtime ?? throw new ArgumentNullException(nameof(runtime))).Redact, paths) { }

    // Fault injection exercises matcher failures without timing-dependent regex probes.
    internal ResponseFilter(Func<string, string> redact, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        this.redact = redact;
        this.paths = paths.Select(Parse).ToArray();
    }

    /// <summary>Filters a private copy. Framework hooks must bound parsing before calling this core.</summary>
    /// <param name="body">The already parsed, bounded JSON body.</param>
    /// <param name="lengthKnown">False means streaming and must fail when protected.</param>
    /// <param name="headersCommitted">True requires the host to abort on failure.</param>
    /// <param name="contentType">Only application/json or application/*+json is supported.</param>
    public JsonNode? Transform(JsonNode? body, bool lengthKnown = true, bool headersCommitted = false, string? contentType = null)
    {
        if (paths.Length == 0) return body?.DeepClone();
        var media = (contentType ?? "").Split(';')[0].Trim();
        if (!lengthKnown || headersCommitted || !(media.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || media.StartsWith("application/", StringComparison.OrdinalIgnoreCase) && media.EndsWith("+json", StringComparison.OrdinalIgnoreCase)))
            throw new ResponseFilterException(headersCommitted);
        var selected = new HashSet<(JsonNode Parent, string Key)>();
        try
        {
            var holder = new JsonObject { ["body"] = body?.DeepClone() };
            foreach (var path in paths) Select(holder, "body", path, 0);
            foreach (var slot in selected)
            {
                var node = Get(slot.Parent, slot.Key);
                if (node is not JsonValue value || !value.TryGetValue<string>(out var text)) throw new ResponseFilterException(headersCommitted);
                Set(slot.Parent, slot.Key, JsonValue.Create(redact(text)));
            }
            var result = holder["body"];
            holder.Remove("body");
            return result;
        }
        catch (Exception)
        { throw new ResponseFilterException(headersCommitted); }

        void Select(JsonNode parent, string key, Segment[] path, int depth)
        {
            if (depth == path.Length) { selected.Add((parent, key)); return; }
            var node = Get(parent, key); var segment = path[depth];
            if (segment.Wildcard)
            {
                if (node is not JsonArray array) throw new ResponseFilterException(headersCommitted);
                for (var index = 0; index < array.Count; index++) Select(array, index.ToString(CultureInfo.InvariantCulture), path, depth + 1);
            }
            else if (node is JsonObject obj)
            {
                if (obj.ContainsKey(segment.Key)) Select(obj, segment.Key, path, depth + 1);
            }
            else if (node is JsonArray array && int.TryParse(segment.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                && index >= 0 && index < array.Count && (segment.Key == "0" || !segment.Key.StartsWith('0')))
                Select(array, segment.Key, path, depth + 1);
            else throw new ResponseFilterException(headersCommitted);
        }
    }

    private static Segment[] Parse(string pointer)
    {
        if (pointer is null || pointer.Length > 0 && !pointer.StartsWith('/')) throw new ArgumentException("Use a JSON Pointer beginning with '/'.");
        if (pointer.Length == 0) return [];
        return pointer[1..].Split('/').Select(raw =>
        {
            var key = new System.Text.StringBuilder();
            for (var at = 0; at < raw.Length; at++)
                if (raw[at] != '~') key.Append(raw[at]);
                else if (++at < raw.Length && raw[at] is '0' or '1' or '2') key.Append(raw[at] == '0' ? '~' : raw[at] == '1' ? '/' : '*');
                else throw new ArgumentException("Use only ~0, ~1 or ~2 in protected JSON paths.");
            return new Segment(key.ToString(), raw == "*");
        }).ToArray();
    }
    private static JsonNode? Get(JsonNode parent, string key) => parent is JsonArray array ? array[int.Parse(key, CultureInfo.InvariantCulture)] : parent[key];
    private static void Set(JsonNode parent, string key, JsonNode? value)
    {
        if (parent is JsonArray array) array[int.Parse(key, CultureInfo.InvariantCulture)] = value; else parent[key] = value;
    }
}
