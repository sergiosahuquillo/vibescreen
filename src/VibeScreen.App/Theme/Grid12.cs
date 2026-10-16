using Avalonia;
using Avalonia.Controls;

namespace VibeScreen.App.Theme;

/// <summary>
/// La rejilla de doce columnas del diseño, con sus puntos de ruptura.
/// </summary>
/// <remarks>
/// <para>
/// Cada hijo dice cuántas columnas ocupa y la rejilla los va colocando de izquierda a derecha,
/// saltando de fila cuando lo que queda no le llega al siguiente. Las filas se alinean arriba: dos
/// tarjetas de alturas distintas no tienen por qué estirarse hasta igualarse, que es lo que hace un
/// <see cref="Grid"/> normal y lo que deja tarjetas medio vacías.
/// </para>
/// <para>
/// <b>Los puntos de ruptura se miden contra el ancho que le queda a la rejilla, no contra el de la
/// ventana.</b> Avalonia no tiene consultas de medios, así que esto se decide al medir; y medir la
/// ventana sería mentir, porque el rail se lleva 232 px fijos y los márgenes otros 64. Los números
/// de aquí son los del diseño menos esa resta: sus 1280 px de ventana son 984 de rejilla.
/// </para>
/// </remarks>
public sealed class Grid12 : Panel
{
    private const int Columns = 12;

    /// <summary>Por debajo de esto, lo ancho pasa a la fila entera y lo demás a media.</summary>
    private const double Narrow = 984;

    /// <summary>Y por debajo de esto no cabe nada al lado de nada.</summary>
    private const double Cramped = 560;

    /// <summary>Cuántas columnas ocupa un hijo. Doce —la fila entera— si no dice otra cosa.</summary>
    public static readonly AttachedProperty<int> SpanProperty =
        AvaloniaProperty.RegisterAttached<Grid12, Control, int>("Span", Columns);

    public static int GetSpan(Control control) => control.GetValue(SpanProperty);

    public static void SetSpan(Control control, int span) => control.SetValue(SpanProperty, span);

    /// <summary>El hueco entre columnas y entre filas.</summary>
    public double Gap { get; set; } = 20;

    /// <summary>Coloca un control y dice de paso cuánto ocupa, para no tener dos líneas por hijo.</summary>
    public static T Span<T>(T control, int span)
        where T : Control
    {
        ArgumentNullException.ThrowIfNull(control);
        SetSpan(control, span);
        return control;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? Narrow : availableSize.Width;
        double column = (width - (Gap * (Columns - 1))) / Columns;
        if (column <= 0) return default;

        double bottom = 0;
        foreach ((var child, int span, double left, double top) in Layout(width, column))
            bottom = Math.Max(bottom, top + child.DesiredSize.Height);

        return new Size(width, bottom);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double column = (finalSize.Width - (Gap * (Columns - 1))) / Columns;
        if (column <= 0) return finalSize;

        foreach ((var child, int span, double left, double top) in Layout(finalSize.Width, column))
            child.Arrange(new Rect(left, top, SpanWidth(span, column), child.DesiredSize.Height));

        return finalSize;
    }

    /// <summary>
    /// Dónde va cada hijo: cuántas columnas ocupa de verdad y en qué punto empieza.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lo calculan igual el medir y el colocar, así que se hace una vez y en un sitio: que los dos
    /// se salgan de cuentas por separado es la clase de fallo que sale como una fila descuadrada y
    /// se busca durante media tarde.
    /// </para>
    /// <para>
    /// Con la regla que el diseño no llegó a escribir: <b>un hijo que se queda solo en su fila la
    /// ocupa entera.</b> Sus repartos están pensados para una rejilla con cinco tarjetas, donde
    /// siempre hay otra al lado; en una de dos, la segunda caía a media anchura con la otra media
    /// en blanco. Al ancho de siempre no cambia nada, porque ahí no se queda nadie solo.
    /// </para>
    /// </remarks>
    private List<(Control Child, int Span, double Left, double Top)> Layout(double width, double column)
    {
        // Primero, en qué fila cae cada uno.
        var rows = new List<List<(Control Child, int Span)>>();
        var row = new List<(Control Child, int Span)>();
        int filled = 0;

        foreach (var child in Children)
        {
            int span = Effective(GetSpan(child), width);
            if (filled > 0 && filled + span > Columns)
            {
                rows.Add(row);
                row = [];
                filled = 0;
            }

            row.Add((child, span));
            filled += span;
        }

        if (row.Count > 0) rows.Add(row);

        // Y ahora el que va solo se lleva la fila.
        var placed = new List<(Control, int, double, double)>(Children.Count);
        double top = 0;

        foreach (var line in rows)
        {
            double left = 0;
            double height = 0;

            foreach ((var child, int span) in line)
            {
                int columns = line.Count == 1 ? Columns : span;

                // Se mide aquí, con el ancho definitivo, y no antes de repartir: la regla de arriba
                // puede ensanchar a un hijo hasta la fila entera, y un texto medido a media anchura
                // pide el doble de alto del que va a necesitar. Volver a medir con lo mismo en el
                // arranque no cuesta: Avalonia guarda el resultado.
                child.Measure(new Size(SpanWidth(columns, column), double.PositiveInfinity));

                placed.Add((child, columns, left, top));
                left += (columns * column) + (columns * Gap);
                height = Math.Max(height, child.DesiredSize.Height);
            }

            top += height + Gap;
        }

        return placed;
    }

    /// <summary>Lo que miden unas cuantas columnas, contando los huecos que llevan dentro.</summary>
    private double SpanWidth(int span, double column) => (span * column) + ((span - 1) * Gap);

    /// <summary>
    /// Cuántas columnas ocupa de verdad un hijo con el ancho que hay.
    /// </summary>
    /// <remarks>
    /// Los repartos son los del diseño: lo que ocupaba siete pasa a la fila entera, lo de cinco y lo
    /// de cuatro se ponen a media, y lo de tres —que era una columna estrecha al lado de otras dos—
    /// se lleva la fila. Apretando más, todo a la fila entera: dos tarjetas de 280 px no se leen.
    /// </remarks>
    private static int Effective(int span, double width)
    {
        if (width >= Narrow) return span;
        if (width < Cramped) return Columns;

        return span switch
        {
            7 or 3 => Columns,
            5 or 4 => 6,
            _ => span,
        };
    }
}
