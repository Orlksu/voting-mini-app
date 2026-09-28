using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backend.Api.Services.Common;

public class MskDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            var parsed = DateTimeParserHelper.ParseMskDateTimeToUtc(text);
            if (parsed.HasValue)
                return parsed.Value;

            if (DateTimeOffset.TryParse(text, out var dto))
                return dto.ToUniversalTime();
        }

        throw new JsonException($"Не удалось распарсить дату и время '{reader.GetString()}' в формате MSK/ISO 8601.");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
    }
}

public class MskNullableDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var parsed = DateTimeParserHelper.ParseMskDateTimeToUtc(text);
            if (parsed.HasValue)
                return parsed.Value;

            if (DateTimeOffset.TryParse(text, out var dto))
                return dto.ToUniversalTime();
        }

        throw new JsonException($"Не удалось распарсить дату и время '{reader.GetString()}' в формате MSK/ISO 8601.");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(value.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
        else
            writer.WriteNullValue();
    }
}
