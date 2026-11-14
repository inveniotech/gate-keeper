// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InvenioTech.GateKeeper.Engine;

// Positional records expose their lower-case constructor properties alongside
// validated public properties. Explicit converters preserve the public API while
// avoiding camel-case collisions and reapplying validation when reading a snapshot.
internal sealed class LocationJsonConverter : JsonConverter<Location>
{
    public override Location Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var value = document.RootElement;
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("path", out var path) || path.ValueKind != JsonValueKind.String
            || !value.TryGetProperty("line", out var line) || line.ValueKind != JsonValueKind.Number || !line.TryGetInt32(out var lineNumber)
            || !value.TryGetProperty("column", out var column) || column.ValueKind != JsonValueKind.Number || !column.TryGetInt32(out var columnNumber))
            throw new JsonException("A location snapshot requires a path, line and column.");
        try { return new Location(path.GetString()!, lineNumber, columnNumber); }
        catch (ArgumentException) { throw new JsonException("The location snapshot has invalid coordinates."); }
    }

    public override void Write(Utf8JsonWriter writer, Location value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("path", value.Path);
        writer.WriteNumber("line", value.Line);
        writer.WriteNumber("column", value.Column);
        writer.WriteEndObject();
    }
}

internal sealed class FingerprintJsonConverter : JsonConverter<Fingerprint>
{
    public override Fingerprint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var value = document.RootElement;
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("value", out var fingerprint) || fingerprint.ValueKind != JsonValueKind.String)
            throw new JsonException("A fingerprint snapshot requires a hexadecimal value.");
        try { return new Fingerprint(fingerprint.GetString()!); }
        catch (ArgumentException) { throw new JsonException("The fingerprint snapshot has an invalid hexadecimal value."); }
    }

    public override void Write(Utf8JsonWriter writer, Fingerprint value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("value", value.Value);
        writer.WriteEndObject();
    }
}
