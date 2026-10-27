namespace VibeScreen.Protocol.Display;

/// <summary>Cómo viaja un píxel por el cable. Lo declara cada panel en sus capacidades.</summary>
/// <remarks>
/// Es lo único del formato que tiene que saber el resto del programa: el lienzo dibuja siempre en
/// BGRA y se convierte una sola vez, al extraer el rectángulo, al formato que diga el panel. Así un
/// panel que pida los bytes al revés no obliga a tocar ni el render ni el planificador, que cuentan
/// los bytes con <see cref="PixelFormats.ByteCount"/> en lugar de dar por hecho que son dos por píxel.
/// </remarks>
public enum PixelFormat
{
    /// <summary>RGB565 con el byte bajo delante. El de la revisión A.</summary>
    Rgb565LittleEndian = 0,

    /// <summary>RGB565 con el byte alto delante. El de la implementación de referencia para la revisión B.</summary>
    Rgb565BigEndian,
}

public static class PixelFormats
{
    public static int BytesPerPixel(this PixelFormat format) => format switch
    {
        PixelFormat.Rgb565LittleEndian or PixelFormat.Rgb565BigEndian => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Formato de píxel desconocido."),
    };

    /// <summary>Bytes que ocupa un rectángulo en el cable con este formato.</summary>
    /// <remarks>
    /// Con <c>checked</c>, por lo mismo que <see cref="Rgb565.ByteCount"/>: si alguna vez llega un tamaño
    /// que no cabe en un <c>int</c>, se entera en el sitio y no aguas abajo con un tamaño negativo.
    /// </remarks>
    /// <exception cref="OverflowException">El rectángulo ocupa más de <see cref="int.MaxValue"/> bytes.</exception>
    public static int ByteCount(this PixelFormat format, in PixelRect rect) =>
        checked((int)(rect.Area * format.BytesPerPixel()));

    /// <summary>Convierte un bloque BGRA de 8 bits por canal, el del lienzo, a este formato.</summary>
    /// <param name="source">Píxeles de origen, 4 bytes por píxel.</param>
    /// <param name="sourceStride">Bytes por fila en el origen, que puede ser mayor que <paramref name="width"/> * 4.</param>
    /// <param name="destination">Destino, sin relleno entre filas.</param>
    public static void FromBgra8888(
        this PixelFormat format, ReadOnlySpan<byte> source, int sourceStride, int width, int height,
        Span<byte> destination)
    {
        switch (format)
        {
            case PixelFormat.Rgb565LittleEndian:
                Rgb565.FromBgra8888(source, sourceStride, width, height, destination);
                break;
            case PixelFormat.Rgb565BigEndian:
                Rgb565.FromBgra8888(source, sourceStride, width, height, destination, bigEndian: true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Formato de píxel desconocido.");
        }
    }
}
