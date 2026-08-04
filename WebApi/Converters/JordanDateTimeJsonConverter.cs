using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebApi.Converters;

/// <summary>
/// Temporary converter: auto-converts UTC DateTime to Jordan time (UTC+3) in API responses.
/// Remove when mobile app handles UTC → local conversion.
/// </summary>
public class JordanDateTimeJsonConverter : JsonConverter<DateTime>
{
    private static readonly TimeZoneInfo JordanZone = GetJordanTimeZone();

    private static TimeZoneInfo GetJordanTimeZone()
    {
        try
        {
            // Windows
            return TimeZoneInfo.FindSystemTimeZoneById("Jordan Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            // Linux / macOS
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Amman");
        }
    }

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.GetDateTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        // EF Core reads DateTime from SQL Server with Kind = Unspecified (not Utc).
        // Since all dates in DB are stored as UTC, treat Unspecified as UTC too.
        // Only skip conversion if Kind is explicitly Local (already converted).
        var utcValue = value.Kind == DateTimeKind.Local ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        var jordanTime = utcValue.Kind == DateTimeKind.Utc
            ? TimeZoneInfo.ConvertTimeFromUtc(utcValue, JordanZone)
            : utcValue;

        writer.WriteStringValue(jordanTime.ToString("yyyy-MM-ddTHH:mm:ss.fff"));
    }
}

/// <summary>
/// Nullable version of JordanDateTimeJsonConverter.
/// Remove when mobile app handles UTC → local conversion.
/// </summary>
public class JordanNullableDateTimeJsonConverter : JsonConverter<DateTime?>
{
    private static readonly JordanDateTimeJsonConverter InnerConverter = new();

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        return reader.GetDateTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        InnerConverter.Write(writer, value.Value, options);
    }
}
