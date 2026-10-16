using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VibeScreen.App.Theme;

/// <summary>
/// La línea del último minuto: por dónde ha ido el caudal, no cuánto marca ahora.
/// </summary>
/// <remarks>
/// <para>
/// Una cifra dice lo que hay; esto dice si eso es lo de siempre. Un tema que va a tirones y otro
/// que va liso pueden marcar el mismo número a la vez, y en la cifra sola no se distinguen.
/// </para>
/// <para>
/// La escala es la de las muestras que hay, no de cero al máximo del panel: contra el caudal
/// entero, la variación de un tema normal es una raya plana y no se vería nada. A cambio, la altura
/// no significa nada por sí sola, y por eso debajo va escrito entre qué y qué se mueve.
/// </para>
/// </remarks>
public sealed class Sparkline : Control
{
    private readonly double[] _samples;
    private int _count;
    private int _next;

    public Sparkline(int samples = 40)
    {
        _samples = new double[Math.Max(2, samples)];
        Height = 48;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
    }

    /// <summary>Entre qué y qué se ha movido, o null si todavía no hay dos muestras.</summary>
    public (double Low, double High)? Extent
    {
        get
        {
            if (_count < 2) return null;

            double low = double.MaxValue;
            double high = double.MinValue;
            for (int i = 0; i < _count; i++)
            {
                low = Math.Min(low, _samples[i]);
                high = Math.Max(high, _samples[i]);
            }

            return (low, high);
        }
    }

    /// <summary>Apunta una muestra. Las viejas se van cayendo por el otro lado.</summary>
    public void Add(double value)
    {
        _samples[_next] = value;
        _next = (_next + 1) % _samples.Length;
        if (_count < _samples.Length) _count++;
        InvalidateVisual();
    }

    /// <summary>Borra lo apuntado. Al perder el panel, lo de antes ya no cuenta nada.</summary>
    public void Clear()
    {
        _count = 0;
        _next = 0;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_count < 2 || Bounds.Width <= 1) return;

        double low = double.MaxValue;
        double high = double.MinValue;
        for (int i = 0; i < _count; i++)
        {
            low = Math.Min(low, _samples[i]);
            high = Math.Max(high, _samples[i]);
        }

        // Un tema quieto da todas las muestras iguales, y entonces el recorrido es cero: sin este
        // mínimo, la división de abajo saldría infinita y la línea se iría de la caja.
        double span = Math.Max(1, high - low);
        double top = 4;
        double usable = Bounds.Height - top - 2;
        double step = Bounds.Width / (_count - 1);

        var line = new StreamGeometry();
        using (var draw = line.Open())
        {
            for (int i = 0; i < _count; i++)
            {
                // El anillo se lee desde la muestra más vieja, que es la que viene después de la
                // última escrita cuando ya ha dado la vuelta.
                int index = _count == _samples.Length ? (_next + i) % _samples.Length : i;
                var point = new Point(i * step, top + (usable * (1 - ((_samples[index] - low) / span))));
                if (i == 0) draw.BeginFigure(point, isFilled: false);
                else draw.LineTo(point);
            }

            draw.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, new Pen(Palette.AccentBrush, 1.6), line);
    }
}
