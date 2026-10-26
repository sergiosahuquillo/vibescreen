using SkiaSharp;
using VibeScreen.Core.Layout;

namespace VibeScreen.Core.Rendering;

/// <summary>Lo que se sabe de una imagen sin abrirla entera: su tamaño y cómo se anima.</summary>
/// <param name="ShortestMs">
/// Lo que dura el fotograma más corto, en milisegundos. Es el ritmo al que la animación pide
/// cambiar, y por tanto lo más a menudo que merece la pena mirarla. En una fija, 0.
/// </param>
public readonly record struct ImageProbe(int Width, int Height, int Frames, int ShortestMs)
{
    public bool Animated => Frames > 1;

    /// <summary>Fotogramas por segundo en el tramo más rápido de la animación. Una fija, 0.</summary>
    public double FramesPerSecond => Animated && ShortestMs > 0 ? 1000.0 / ShortestMs : 0;
}

/// <summary>Los fotogramas de una imagen, ya compuestos uno sobre otro y listos para dibujar.</summary>
internal sealed class ImageFrames(SKBitmap[] bitmaps, int[] durations) : IDisposable
{
    public IReadOnlyList<SKBitmap> Bitmaps { get; } = bitmaps;

    /// <summary>Lo que dura cada fotograma, en milisegundos. Una fija lleva un 0.</summary>
    public IReadOnlyList<int> Durations { get; } = durations;

    public long Bytes { get; } = bitmaps.Sum(b => (long)b.ByteCount);

    public void Dispose()
    {
        foreach (var bitmap in Bitmaps) bitmap.Dispose();
    }
}

/// <summary>
/// Las imágenes de los widgets, decodificadas y guardadas para no volver a hacerlo en cada
/// reconstrucción del renderizador.
/// </summary>
/// <remarks>
/// <para>
/// Es <see cref="BackgroundImages"/> con más de una entrada: un tema puede llevar varias imágenes,
/// y un GIF son varios fotogramas que decodificar, que con el editor rehaciendo el renderizador con
/// cada tecla se notaría. Cada entrada se descarta si el fichero cambia de fecha, y las que llevan
/// más tiempo sin usarse salen cuando lo guardado pasa de <see cref="CacheBytes"/>.
/// </para>
/// <para>
/// Hay topes antes de decodificar nada porque quien pinta el panel es un servicio que corre como
/// SYSTEM y la ruta sale de un JSON: un GIF de diez mil fotogramas a pantalla completa son gigabytes.
/// Lo que pasa de ahí no se pinta, y de avisar se encarga <see cref="LayoutInspector"/>.
/// </para>
/// </remarks>
public static class WidgetImages
{
    /// <summary>Fotogramas como mucho. Muchos más que los que caben en el enlace a un ritmo razonable.</summary>
    public const int MaxFrames = 256;

    /// <summary>Lo que puede ocupar una imagen abierta, con todos sus fotogramas, en megabytes.</summary>
    public const int MaxMegabytes = 64;

    public const long MaxBytes = MaxMegabytes * 1024L * 1024;

    /// <summary>
    /// Por debajo de esto un GIF no dice lo que dura de verdad. Los navegadores toman 10 ms o menos
    /// —y el 0, que es lo más habitual— como 100 ms, y así es como se ha visto siempre esa animación.
    /// </summary>
    private const int SlowestUnsetMs = 10;

    private const int UnsetDurationMs = 100;

    /// <summary>Lo que se guarda entre todas las entradas antes de empezar a soltar las viejas.</summary>
    private const long CacheBytes = 2 * MaxBytes;

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
    private static long _uses;

    private sealed class Entry(DateTime stamp)
    {
        public DateTime Stamp { get; } = stamp;
        public ImageProbe? Probe { get; set; }
        public bool Probed { get; set; }
        public ImageFrames? Frames { get; set; }
        public bool Decoded { get; set; }
        public long LastUse { get; set; }
    }

    /// <summary>
    /// El tamaño y los fotogramas de una imagen, leyendo solo su cabecera. Null si no está, no se
    /// puede leer o pasa de los topes.
    /// </summary>
    public static ImageProbe? Probe(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Stamp(path) is not { } stamp) return null;

        lock (Gate)
        {
            var entry = Fresh(path, stamp);
            if (!entry.Probed)
            {
                entry.Probe = ReadProbe(path);
                entry.Probed = true;
            }

            return entry.Probe;
        }
    }

    /// <summary>
    /// Usa los fotogramas de esa imagen, si se pueden abrir, sin soltarlos fuera del candado.
    /// </summary>
    /// <remarks>Sin soltarlos por lo mismo que <see cref="BackgroundImages.Use"/>.</remarks>
    internal static void Use(string path, Action<ImageFrames> draw)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(draw);
        if (Stamp(path) is not { } stamp) return;

        lock (Gate)
        {
            var entry = Fresh(path, stamp);
            if (!entry.Decoded)
            {
                entry.Frames = Decode(path);
                entry.Decoded = true;
                Trim(keep: entry);
            }

            if (entry.Frames is { } frames) draw(frames);
        }
    }

    /// <summary>
    /// La duración de un fotograma tal como se ve: lo que diga el fichero, salvo que no diga nada útil.
    /// </summary>
    public static int Duration(int declaredMs) => declaredMs <= SlowestUnsetMs ? UnsetDurationMs : declaredMs;

    private static DateTime? Stamp(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? file.LastWriteTimeUtc : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>La entrada de esa ruta, vacía si el fichero ha cambiado desde que se guardó.</summary>
    private static Entry Fresh(string path, DateTime stamp)
    {
        if (!Entries.TryGetValue(path, out var entry) || entry.Stamp != stamp)
        {
            entry?.Frames?.Dispose();
            entry = new Entry(stamp);
            Entries[path] = entry;
        }

        entry.LastUse = ++_uses;
        return entry;
    }

    /// <summary>Suelta las imágenes que llevan más tiempo sin usarse hasta caber. La de ahora, nunca.</summary>
    private static void Trim(Entry keep)
    {
        long total = Entries.Values.Sum(e => e.Frames?.Bytes ?? 0);
        while (total > CacheBytes)
        {
            var oldest = Entries.Values
                .Where(e => e != keep && e.Frames is not null)
                .MinBy(e => e.LastUse);
            if (oldest is null) return;

            total -= oldest.Frames!.Bytes;
            oldest.Frames.Dispose();
            oldest.Frames = null;
            oldest.Decoded = false;
        }
    }

    private static ImageProbe? ReadProbe(string path)
    {
        try
        {
            using var codec = SKCodec.Create(path);
            if (codec is null) return null;

            var probe = Measure(codec);
            return Fits(probe) ? probe : null;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Ancho a propósito, como en el fondo: un fichero roto no puede tumbar a quien lo mira.
            return null;
        }
    }

    private static ImageProbe Measure(SKCodec codec)
    {
        int frames = Math.Max(codec.FrameCount, 1);
        int shortest = 0;
        if (frames > 1)
            shortest = codec.FrameInfo.Select(f => Duration(f.Duration)).DefaultIfEmpty(UnsetDurationMs).Min();

        return new ImageProbe(codec.Info.Width, codec.Info.Height, frames, shortest);
    }

    private static bool Fits(ImageProbe probe) =>
        probe.Width > 0 && probe.Height > 0 && probe.Frames <= MaxFrames &&
        (long)probe.Width * probe.Height * 4 * probe.Frames <= MaxBytes;

    /// <summary>
    /// Decodifica todos los fotogramas. Cada uno se compone sobre el que pide el fichero, que en un
    /// GIF suele traer solo el trozo que cambia.
    /// </summary>
    private static ImageFrames? Decode(string path)
    {
        var bitmaps = new List<SKBitmap>();
        try
        {
            using var codec = SKCodec.Create(path);
            if (codec is null) return null;

            var probe = Measure(codec);
            if (!Fits(probe)) return null;

            var info = new SKImageInfo(probe.Width, probe.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var frameInfo = codec.FrameInfo;
            var durations = new int[probe.Frames];

            for (int i = 0; i < probe.Frames; i++)
            {
                var bitmap = new SKBitmap(info);
                bitmaps.Add(bitmap);

                int prior = probe.Animated && i < frameInfo.Length ? frameInfo[i].RequiredFrame : -1;
                using (var canvas = new SKCanvas(bitmap))
                {
                    canvas.Clear(SKColors.Transparent);
                    if (prior >= 0 && prior < i) canvas.DrawBitmap(bitmaps[prior], 0, 0);
                }

                var result = codec.GetPixels(info, bitmap.GetPixels(), new SKCodecOptions(i, prior < i ? prior : -1));
                // Un fichero cortado al final da lo que tiene: es mejor que no pintar nada.
                if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                {
                    foreach (var made in bitmaps) made.Dispose();
                    return null;
                }

                durations[i] = probe.Animated && i < frameInfo.Length ? Duration(frameInfo[i].Duration) : 0;
            }

            return new ImageFrames([.. bitmaps], durations);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            foreach (var made in bitmaps) made.Dispose();
            return null;
        }
    }
}
