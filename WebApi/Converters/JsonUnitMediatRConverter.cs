using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;

namespace Cable.Converters;

public class JsonUnitMediatRConverter : JsonConverter<Unit>
{
    public override Unit Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => Unit.Value;
    public override void Write(Utf8JsonWriter writer, Unit value, JsonSerializerOptions options)
    {

    }
}
