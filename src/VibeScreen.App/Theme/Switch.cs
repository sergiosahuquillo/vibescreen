using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using VibeScreen.App.Localization;

namespace VibeScreen.App.Theme;

/// <summary>
/// Un interruptor de encendido y apagado, con lo que significa escrito al lado.
/// </summary>
/// <remarks>
/// Propio y no el <c>ToggleSwitch</c> de Fluent revestido, porque **comprobado en marcha que aquel
/// no se deja**: sus colores salen de su propio <c>ControlTheme</c>, que se aplica con prioridad de
/// plantilla y gana a cualquier estilo de la aplicación. Y aunque se dejara, la pista del diseño es
/// más ancha y el pulgar más grande, y la plantilla de Fluent calcula el recorrido del pulgar a
/// partir de anchos fijos suyos: tocarlos descuadra la animación.
/// </remarks>
public sealed class Switch : Border
{
    private const double TrackWidth = 46;
    private const double TrackHeight = 26;
    private const double ThumbSize = 20;

    private readonly Border _track;
    private readonly Ellipse _thumb;
    private readonly TextBlock _text;
    private readonly Text _on;
    private readonly Text _off;

    private bool _checked;

    /// <summary>Alguien lo ha pulsado. No salta al ponerlo desde fuera con <see cref="Show"/>.</summary>
    public event Action<bool>? Toggled;

    /// <param name="on">Lo que se lee cuando está encendido.</param>
    /// <param name="off">Y cuando está apagado.</param>
    public Switch(Text on, Text off)
    {
        _on = on;
        _off = off;

        _thumb = new Ellipse
        {
            Width = ThumbSize,
            Height = ThumbSize,
            Fill = Palette.ThumbOffBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = new TranslateTransform(0, 0),
            Transitions =
            [
                new TransformOperationsTransition
                {
                    Property = RenderTransformProperty,
                    Duration = Palette.Quick,
                    Easing = Palette.Ease,
                },
            ],
        };

        _track = new Border
        {
            Width = TrackWidth,
            Height = TrackHeight,
            CornerRadius = Palette.PillRadius,
            Background = Palette.Surface3Brush,
            BorderBrush = Palette.LineStrongBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _thumb,
            Transitions =
            [
                new BrushTransition { Property = BackgroundProperty, Duration = Palette.Quick, Easing = Palette.Ease },
                new BrushTransition { Property = BorderBrushProperty, Duration = Palette.Quick, Easing = Palette.Ease },
            ],
        };

        _text = Loc.Set(new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeight.Medium,
            Foreground = Palette.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        }, TextBlock.TextProperty, off);

        Background = Brushes.Transparent;
        Padding = new Thickness(4);
        CornerRadius = Palette.ControlRadius;
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = true;
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { _track, _text },
        };

        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Flip();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Space or Key.Enter)) return;
            e.Handled = true;
            Flip();
        };

        Paint();
    }

    /// <summary>Si está encendido.</summary>
    public bool IsChecked => _checked;

    /// <summary>Lo pone como esté, sin avisar a nadie: es lo que ya había, no una elección.</summary>
    public void Show(bool on)
    {
        if (_checked == on) return;
        _checked = on;
        Paint();
    }

    private void Flip()
    {
        _checked = !_checked;
        Paint();
        Toggled?.Invoke(_checked);
    }

    private void Paint()
    {
        _track.Background = _checked ? Palette.SelectionBrush : Palette.Surface3Brush;
        _track.BorderBrush = _checked ? Palette.AccentBrush : Palette.LineStrongBrush;
        _thumb.Fill = _checked ? Palette.AccentBrush : Palette.ThumbOffBrush;

        // El recorrido es el hueco que queda: la pista menos el pulgar, menos los bordes y el
        // relleno de los dos lados. Sacarlo de aquí y no de un número escrito a mano es lo que hace
        // que cambiar el tamaño de la pista no deje el pulgar a medio salir.
        double travel = TrackWidth - ThumbSize - (2 * 2) - (2 * 1);
        _thumb.RenderTransform = new TranslateTransform(_checked ? travel : 0, 0);

        Loc.Set(_text, TextBlock.TextProperty, _checked ? _on : _off);
    }
}
