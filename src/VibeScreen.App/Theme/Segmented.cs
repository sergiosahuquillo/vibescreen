using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VibeScreen.App.Localization;

namespace VibeScreen.App.Theme;

/// <summary>Una opción de un <see cref="Segmented{T}"/>: lo que vale, cómo se lee y con qué icono.</summary>
public sealed record SegmentOption<T>(T Value, Text Label, StreamGeometry? Icon = null);

/// <summary>
/// Un grupo de dos o tres opciones excluyentes, todas a la vista.
/// </summary>
/// <remarks>
/// <para>
/// No es un desplegable ni un grupo de radios. Un desplegable esconde las opciones detrás de un
/// clic y no dice cuántas hay; unos radios ocupan tres renglones para tres palabras. Con dos o tres
/// opciones cortas que se comparan entre sí —oscuro, claro, el del sistema— enseñarlas todas y
/// marcar la puesta es más rápido de leer que cualquiera de los dos.
/// </para>
/// <para>
/// Se construye a mano en vez de revestir un <c>RadioButton</c>: el aspecto no se parece en nada al
/// de un radio, y —comprobado— los colores de algunos controles de Fluent no se dejan cambiar desde
/// un estilo de la aplicación. Aquí no hay plantilla ajena con la que pelearse.
/// </para>
/// </remarks>
public sealed class Segmented<T> : Border
    where T : notnull
{
    private readonly List<(T Value, Border Pill, TextBlock Text, Viewbox? Icon)> _segments = [];
    private readonly Action<T> _choose;
    private T _current;

    /// <param name="options">Las opciones, en el orden en que se leen.</param>
    /// <param name="current">La que está puesta al construirlo.</param>
    /// <param name="choose">Se llama al elegir otra. No se llama al marcarla desde fuera.</param>
    public Segmented(IReadOnlyList<SegmentOption<T>> options, T current, Action<T> choose)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(choose);

        _choose = choose;
        _current = current;

        Background = Palette.Surface2Brush;
        BorderBrush = Palette.LineStrongBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(3);
        HorizontalAlignment = HorizontalAlignment.Left;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        foreach (var option in options) row.Children.Add(Build(option));
        Child = row;

        Mark();
    }

    /// <summary>Marca otra opción sin avisar a quien escucha: es lo que ya había, no una elección.</summary>
    public void Show(T value)
    {
        _current = value;
        Mark();
    }

    private Button Build(SegmentOption<T> option)
    {
        var text = Loc.Set(new TextBlock { VerticalAlignment = VerticalAlignment.Center }, TextBlock.TextProperty, option.Label);

        var icon = option.Icon is { } geometry ? Icons.Make(geometry, Palette.MutedBrush, 16) : null;

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (icon is not null) content.Children.Add(icon);
        content.Children.Add(text);

        // La pastilla de la opción puesta es un borde propio dentro del botón, y no el fondo del
        // botón: el fondo lo pinta el tema del botón en cuanto el ratón pasa por encima, y la
        // elegida perdería el realce justo al apuntarla. Así el paso del ratón queda por detrás,
        // que es lo que hace el diseño.
        var pill = new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12, 0),
            MinHeight = 30,
            Child = content,
        };

        // Del color del propio grupo y no transparente. A la vista es lo mismo —el borde de fuera ya
        // es de este color—, pero el fondo del botón se anima al pasar el ratón, y desde transparente
        // eso destella en la paleta oscura: mírese ControlThemes.Hover().
        var button = new Button
        {
            Content = pill,
            Background = Palette.Surface2Brush,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(0),
            MinHeight = 30,
            FontSize = 14,
        };
        button.Click += (_, _) =>
        {
            if (EqualityComparer<T>.Default.Equals(_current, option.Value)) return;
            _current = option.Value;
            Mark();
            _choose(option.Value);
        };

        Loc.Set(button, AutomationProperties.NameProperty, option.Label);
        _segments.Add((option.Value, pill, text, icon));
        return button;
    }

    private void Mark()
    {
        foreach ((var value, var pill, var text, var icon) in _segments)
        {
            bool on = EqualityComparer<T>.Default.Equals(value, _current);
            pill.Background = on ? Palette.AccentMediumBrush : Brushes.Transparent;
            text.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            text.Foreground = on ? Palette.TextBrush : Palette.MutedBrush;
            if (icon?.Child is Avalonia.Controls.Shapes.Path path)
                path.Stroke = on ? Palette.TextBrush : Palette.MutedBrush;
        }
    }
}
