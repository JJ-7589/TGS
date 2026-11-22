using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Caninos.Api.Validation;

public sealed class StrictUtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("La fecha debe enviarse como cadena de texto en formato ISO-8601 (ej. '2026-11-25T10:00:00Z').");
        }

        var raw = reader.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new JsonException("La fecha no puede estar vacía.");
        }

        if (!DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dto))
        {
            throw new JsonException("Formato de fecha inválido. Usa ISO-8601 (ej. '2026-11-25T10:00:00Z').");
        }

        return dto.UtcDateTime;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        writer.WriteStringValue(utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
    }
}
