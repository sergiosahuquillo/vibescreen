using System.Text.Json;
using System.Text.Json.Serialization;
using VibeScreen.Contracts;
using VibeScreen.Protocol.Display;

namespace VibeScreen.Core.Layout;

/// <summary>
/// Serializador generado en compilación: sin reflexión, para que el build recortado de Linux no pierda
/// tipos. Es estricto a propósito, porque el layout se escribe a mano: una propiedad mal escrita tiene
/// que dar error y no quedarse ignorada en silencio.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    AllowOutOfOrderMetadataProperties = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    // Las propiedades sin valor no se escriben. Lo que sí se escribe es todo lo demás, incluso si
    // coincide con el valor por defecto: la mitad de los defectos del layout no son el del tipo
    // —«showUnit» es true y «size» es 16—, así que omitirlos cambiaría el tema al releerlo.
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(LayoutDocument))]
// Un widget suelto, para poder copiarlo por una ida y vuelta. Como raíz propia sale con su
// discriminador «type», que dentro del documento lo pone la lista de widgets.
[JsonSerializable(typeof(LayoutWidget))]
internal sealed partial class LayoutJsonContext : JsonSerializerContext
{
}

/// <summary>Rectángulo como <c>[x, y, ancho, alto]</c>.</summary>
internal sealed class PixelRectJsonConverter : JsonConverter<PixelRect>
{
    private const string Shape = "La caja va como [x, y, ancho, alto], con cuatro enteros.";

    public override PixelRect Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException(Shape);

        var values = new int[4];
        int count = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (count == values.Length || reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out values[count]))
                throw new JsonException(Shape);
            count++;
        }

        if (count != values.Length) throw new JsonException(Shape);
        return new PixelRect(values[0], values[1], values[2], values[3]);
    }

    public override void Write(Utf8JsonWriter writer, PixelRect value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteNumberValue(value.Width);
        writer.WriteNumberValue(value.Height);
        writer.WriteEndArray();
    }
}

/// <summary>
/// Un fallo de lectura del JSON que es nuestro y no de System.Text.Json: lleva su aviso, para que
/// quien lo enseñe lo pueda decir en su idioma.
/// </summary>
/// <remarks>
/// System.Text.Json vuelve a lanzar la misma excepción con la ruta y la línea puestas, así que el
/// aviso llega intacto hasta <see cref="LayoutLoader.Parse"/>.
/// </remarks>
internal sealed class NoticeJsonException(Notice notice) : JsonException(notice.ToString()), INoticed
{
    public Notice Notice { get; } = notice;
}

/// <summary>Enumerado por su nombre y nada más, con la misma regla que la línea de comandos.</summary>
internal sealed class StrictEnumJsonConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && StrictEnum.TryParse(reader.GetString(), out TEnum value))
            return value;

        throw new NoticeJsonException(Notice.Of("layout.json.enum", StrictEnum.Names<TEnum>()));
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString().ToLowerInvariant());
}
