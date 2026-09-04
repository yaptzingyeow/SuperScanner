using System.Globalization;
using System.Text;
using System.Text.Json;
using SuperScanner.Application.Abstractions;

namespace SuperScanner.Infrastructure.Auditing;

public static class CanonicalAuditPayload
{
    public static byte[] Serialize(
        AuditWriteRequest request,
        long sequence,
        string signingKeyId)
    {
        using var region = JsonDocument.Parse(request.RegionJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("sequence", sequence);
            writer.WriteString("actorUid", request.ActorUid);
            writer.WriteString("action", request.Action);
            writer.WriteString("targetType", request.TargetType);
            writer.WriteString("targetId", request.TargetId.ToString("N"));
            writer.WritePropertyName("region");
            WriteCanonicalElement(writer, region.RootElement);
            writer.WriteString(
                "occurredAt",
                request.OccurredAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("signingKeyId", signingKeyId);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static string CanonicalizeRegionJson(string regionJson)
    {
        using var document = JsonDocument.Parse(regionJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonicalElement(writer, document.RootElement);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonicalElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalElement(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalElement(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidDataException("Unsupported audit region JSON value.");
        }
    }
}
