using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cable.Converters;

public class JsonTimeOnlyConverter : JsonConverter<TimeOnly>
{

    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string input = reader.GetString();

        if (TimeOnly.TryParse(input, out TimeOnly output))
        {
            return output;
        }

        throw new JsonException("Invalid Format for TimeOnly fields");
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("HH:mm"));

}
