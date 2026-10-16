using SkiaSharp;
using VibeScreen.Protocol.Display;

namespace VibeScreen.Core.Rendering;

/// <summary>
/// Lienzo del panel: un mapa de bits en memoria sobre el que dibuja todo el mundo.
/// </summary>
/// <remarks>
/// Es la pieza que hace que la vista previa del editor sea idéntica al panel por construcción.
/// El servicio dibuja aquí y extrae rectángulos en RGB565 para el cable; el editor dibuja con
/// este mismo código y muestra el mapa de bits en pantalla. Un solo camino de render, sin
/// posibilidad de que la vista previa y el panel se separen.
/// </remarks>
public sealed class PanelSurface : IDisposable
{
    private readonly SKBitmap _bitmap;

    public PanelSurface(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        Width = width;
        Height = height;
        // Bgra8888 es el formato nativo de Skia en máquinas little-endian: así la conversión
        // a RGB565 lee memoria contigua sin ningún paso intermedio.
        _bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        Canvas = new SKCanvas(_bitmap);
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Lienzo sobre el que dibujan los widgets.</summary>
    public SKCanvas Canvas { get; }

    public SKBitmap Bitmap => _bitmap;
    public PixelRect Bounds => new(0, 0, Width, Height);

    /// <summary>
    /// Extrae un rectángulo ya codificado en RGB565 little-endian, listo para
    /// <see cref="IDisplayDriver.BlitAsync"/>.
    /// </summary>
    public byte[] ExtractRgb565(in PixelRect rect)
    {
        var clamped = rect.ClampTo(Width, Height);
        if (clamped.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(rect), rect, "El rectángulo queda fuera del lienzo.");

        var destination = new byte[Rgb565.ByteCount(clamped)];
        ExtractRgb565(clamped, destination);
        return destination;
    }

    /// <summary>Variante sin reservar memoria: escribe en un búfer que proporciona quien llama.</summary>
    public void ExtractRgb565(in PixelRect rect, Span<byte> destination)
    {
        var clamped = rect.ClampTo(Width, Height);
        if (clamped.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(rect), rect, "El rectángulo queda fuera del lienzo.");

        int rowBytes = _bitmap.RowBytes;
        ReadOnlySpan<byte> all = _bitmap.GetPixelSpan();

        // Una sola llamada, no una por fila: FromBgra8888 ya sabe de paso de fila, así que se le pasa
        // el del mapa de bits y convierte el rectángulo entero. Fila a fila eran 480 pasadas de
        // validación de argumentos y casi mil Slice por fotograma completo para hacer lo mismo.
        //
        // El trozo va desde el primer píxel hasta el final de la última fila: lo que sobra a la
        // derecha de cada fila lo salta el paso, y detrás de la última no hace falta nada.
        int start = (clamped.Y * rowBytes) + (clamped.X * 4);
        int length = ((clamped.Height - 1) * rowBytes) + (clamped.Width * 4);

        Rgb565.FromBgra8888(all.Slice(start, length), rowBytes, clamped.Width, clamped.Height, destination);
    }

    public void Clear(SKColor color) => Canvas.Clear(color);

    public void Dispose()
    {
        Canvas.Dispose();
        _bitmap.Dispose();
    }
}
