using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using VibeScreen.App.Localization;

namespace VibeScreen.App.Theme;

/// <summary>
/// Lo que se está viendo en el panel, dentro de un marco.
/// </summary>
/// <remarks>
/// <para>
/// No se parece al panel: <b>es</b> el panel. El mapa de bits sale del mismo <c>LayoutRenderer</c>
/// que usa el servicio, con los mismos píxeles, igual que la vista previa del editor.
/// </para>
/// <para>
/// A diferencia de la del editor, esta no se toca: no hay rejilla, ni tiradores, ni arrastre. Es
/// para mirar, y por eso puede escalarse a lo que quepa en vez de ir a aumentos enteros.
/// </para>
/// </remarks>
public sealed class PanelPreview : Control, IDisposable
{
    private readonly double _width;
    private readonly double _height;

    private WriteableBitmap? _front;
    private WriteableBitmap? _back;
    private PixelSize _frameSize;
    private bool _released;

    /// <summary>De quién copia lo que enseña, si no pinta lo suyo.</summary>
    private PanelPreview? _mirrors;

    /// <param name="width">El ancho al que se dibuja. El alto sale de la proporción del panel.</param>
    public PanelPreview(double width, double panelWidth = 480, double panelHeight = 320)
    {
        _width = width;
        _height = Math.Round(width * panelHeight / panelWidth);
        Width = _width;
        Height = _height;

        // Sin suavizar: un píxel del panel tiene que verse como un píxel, no como una mancha.
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
    }

    /// <summary>
    /// Hace que esta previa enseñe lo mismo que otra, sin volver a copiar el mapa de bits.
    /// </summary>
    /// <remarks>
    /// La miniatura de la tarjeta del tema y la grande de la del servicio enseñan exactamente lo
    /// mismo. Copiarles el fotograma a cada una serían dos copias de 600 KB por segundo para pintar
    /// dos veces la misma imagen; así el mapa de bits es uno y la miniatura solo lo dibuja más
    /// pequeño.
    /// </remarks>
    public void Mirror(PanelPreview source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _mirrors = source;
    }

    /// <summary>Si la pantalla está apagada, se pinta en gris y se dice.</summary>
    public bool ScreenOff { get; set; }

    /// <summary>Lo que se lee cuando no hay nada que enseñar.</summary>
    public Text EmptyText { get; set; } = Loc.L("preview.no-panel");

    /// <summary>Copia el fotograma recién pintado.</summary>
    /// <remarks>
    /// Dos mapas de bits que se turnan, como en el lienzo del editor: escribir en el que se está
    /// enseñando se ve como un parpadeo, porque el hilo de composición puede estar leyéndolo.
    /// </remarks>
    public void SetFrame(SKBitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var size = new PixelSize(source.Width, source.Height);
        if (_front is null || _back is null || _frameSize != size)
        {
            _front?.Dispose();
            _back?.Dispose();
            _front = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            _back = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            _frameSize = size;
        }

        using (var buffer = _back.Lock())
        using (var pixels = source.PeekPixels())
        {
            var info = new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            pixels.ReadPixels(info, buffer.Address, buffer.RowBytes);
        }

        (_front, _back) = (_back, _front);
        InvalidateVisual();
        Mirrored?.Invoke();
    }

    /// <summary>Hay fotograma nuevo. Lo escuchan las previas que copian de esta.</summary>
    public event Action? Mirrored;

    /// <summary>Se ha quedado sin nada que enseñar: sin servicio, sin panel o sin tema.</summary>
    public void Clear()
    {
        Mirrored?.Invoke();
        _front?.Dispose();
        _back?.Dispose();
        _front = null;
        _back = null;
        _frameSize = default;

        // Se repinta siempre, aunque ya estuviera vacio: lo que se lee en el hueco puede haber
        // cambiado -de «sin servicio» a «sin tema»- y sin esto se quedaria el de antes.
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var box = new Rect(0, 0, Bounds.Width, Bounds.Height);
        var rounded = new RoundedRect(box, 5);
        context.DrawRectangle(Palette.Bezel2Brush, null, rounded);

        var frame = _mirrors?._front ?? _front;
        var frameSize = _mirrors?._frameSize ?? _frameSize;

        if (frame is null)
        {
            var text = new FormattedText(
                EmptyText.ToString(),
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(AppFonts.DataCurrent),
                11,
                Palette.FaintBrush);
            context.DrawText(text, new Point(
                (Bounds.Width - text.Width) / 2,
                (Bounds.Height - text.Height) / 2));
            return;
        }

        using (context.PushClip(rounded))
        {
            context.DrawImage(frame, new Rect(0, 0, frameSize.Width, frameSize.Height), box);

            // Apagada no es negra: el tema sigue cargado y lo que cambia es que no se ve. Un velo
            // encima lo dice sin tener que borrar lo que hay debajo.
            if (ScreenOff)
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(200, 10, 12, 16)), null, rounded);
                var text = new FormattedText(
                    Loc.S("preview.screen-off"),
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(AppFonts.DataCurrent),
                    11,
                    Palette.FaintBrush);
                context.DrawText(text, new Point(
                    (Bounds.Width - text.Width) / 2,
                    (Bounds.Height - text.Height) / 2));
            }
        }
    }

    public void Dispose()
    {
        if (_released) return;
        _released = true;
        _front?.Dispose();
        _back?.Dispose();
        _front = null;
        _back = null;
    }

    /// <summary>El marco con bisel que rodea la pantalla, como en el diseño.</summary>
    public static Border Framed(PanelPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        return new Border
        {
            Padding = new Thickness(7),
            CornerRadius = new CornerRadius(10),
            BorderBrush = Palette.LineStrongBrush,
            BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Palette.Bezel1Brush.Color, 0),
                    new GradientStop(Palette.Bezel2Brush.Color, 1),
                },
            },
            Child = preview,
        };
    }
}
