using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace VibeScreen.App.Theme;

/// <summary>
/// Un deslizador: lo andado del color del realce, lo que queda del color del hueco.
/// </summary>
/// <remarks>
/// <para>
/// Propio y no el <c>Slider</c> de Fluent revestido por lo mismo que el interruptor: comprobado en
/// marcha que sus colores no se dejan cambiar desde un estilo de la aplicación, porque los fija el
/// <c>ControlTheme</c> de sus piezas internas.
/// </para>
/// <para>
/// Avisa de cada movimiento <b>y</b> de cuándo se suelta, que no es lo mismo: el brillo se ve al
/// instante pero se le manda al panel cuando la mano para, porque una orden por píxel llenaría la
/// tubería de órdenes que se pisan entre ellas.
/// </para>
/// </remarks>
public sealed class Range : Control
{
    private const double TrackHeight = 6;
    private const double ThumbSize = 18;
    private const double RingWidth = 3;

    private readonly double _minimum;
    private readonly double _maximum;
    private readonly double _step;

    private double _value;
    private bool _dragging;

    /// <summary>Se está moviendo. Salta en cada píxel.</summary>
    public event Action<double>? Moved;

    /// <summary>Se ha soltado. Es el momento de hacer algo caro con el valor.</summary>
    public event Action<double>? Committed;

    public Range(double minimum, double maximum, double step)
    {
        _minimum = minimum;
        _maximum = maximum;
        _step = step > 0 ? step : 1;
        _value = minimum;

        Height = 22;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    /// <summary>Lo que marca.</summary>
    public double Value => _value;

    /// <summary>Lo pone sin avisar: es lo que ya había, no una elección.</summary>
    public void Show(double value)
    {
        double snapped = Snap(value);
        if (Math.Abs(snapped - _value) < 0.0001) return;
        _value = snapped;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _dragging = true;
        e.Pointer.Capture(this);
        MoveTo(e.GetPosition(this).X);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging) MoveTo(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;

        _dragging = false;
        e.Pointer.Capture(null);
        Committed?.Invoke(_value);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        double next = e.Key switch
        {
            Key.Left or Key.Down => _value - _step,
            Key.Right or Key.Up => _value + _step,
            Key.Home => _minimum,
            Key.End => _maximum,
            _ => _value,
        };

        if (Math.Abs(next - _value) < 0.0001) return;

        e.Handled = true;
        _value = Snap(next);
        InvalidateVisual();
        Moved?.Invoke(_value);

        // Con el teclado no hay «soltar»: cada pulsación es un cambio terminado.
        Committed?.Invoke(_value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double width = Bounds.Width;
        if (width <= ThumbSize) return;

        double middle = Bounds.Height / 2;
        double usable = width - ThumbSize;
        double fraction = (_value - _minimum) / (_maximum - _minimum);
        double centre = (ThumbSize / 2) + (usable * fraction);

        var track = new Rect(
            ThumbSize / 2,
            middle - (TrackHeight / 2),
            usable,
            TrackHeight);
        double radius = TrackHeight / 2;

        // El hueco entero primero y lo andado encima: dibujarlos a tope uno al lado del otro deja
        // una costura de un píxel entre los dos cuando el ancho no cae en número redondo.
        context.DrawRectangle(Palette.Surface3Brush, null, new RoundedRect(track, radius));

        double travelled = centre - (ThumbSize / 2);
        if (travelled > 0)
        {
            var done = new Rect(track.X, track.Y, travelled, TrackHeight);
            context.DrawRectangle(Palette.AccentBrush, null, new RoundedRect(done, radius));
        }

        // El pulgar lleva un aro del color del fondo y, por fuera, un filo del realce: sin el aro se
        // funde con lo andado y deja de saberse por dónde va.
        var thumb = new Point(centre, middle);
        context.DrawEllipse(Palette.AccentBrush, null, thumb, ThumbSize / 2, ThumbSize / 2);
        context.DrawEllipse(Palette.ThumbRingBrush, null, thumb, (ThumbSize / 2) - 1, (ThumbSize / 2) - 1);
        context.DrawEllipse(Palette.AccentBrush, null, thumb, (ThumbSize / 2) - RingWidth, (ThumbSize / 2) - RingWidth);
    }

    private void MoveTo(double x)
    {
        double usable = Bounds.Width - ThumbSize;
        if (usable <= 0) return;

        double fraction = Math.Clamp((x - (ThumbSize / 2)) / usable, 0, 1);
        double next = Snap(_minimum + (fraction * (_maximum - _minimum)));
        if (Math.Abs(next - _value) < 0.0001) return;

        _value = next;
        InvalidateVisual();
        Moved?.Invoke(_value);
    }

    private double Snap(double value)
    {
        double clamped = Math.Clamp(value, _minimum, _maximum);
        return Math.Clamp(Math.Round(clamped / _step) * _step, _minimum, _maximum);
    }
}
