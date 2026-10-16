using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace VibeScreen.App.Theme;

/// <summary>
/// Una fila de grupos que salta de línea por grupos enteros: la <c>.bar-row</c> del diseño.
/// </summary>
/// <remarks>
/// <para>
/// En el CSS es un flex con salto de línea, y un <see cref="WrapPanel"/> se le parece hasta que se
/// mira qué hace con lo que sobra. El diseño se lo da a dos sitios: el campo del tema crece con la
/// línea —<c>flex: 1 1 160px</c>, entre 120 y 240— y el grupo de aplicar se va al canto derecho
/// —<c>margin-left: auto</c>—. Un WrapPanel deja ese sobrante al final, sin dárselo a nadie, y con la
/// columna de inspección a todo el alto la barra ya no tiene ancho para perder.
/// </para>
/// <para>
/// Hace lo que el navegador y en su orden: reparte en líneas con el ancho de partida de cada hijo —su
/// base, o lo que pide—, y en cada línea el sobrante va primero al que crece, hasta su ancho máximo, y
/// lo que quede empuja a la derecha al hijo marcado y a los de detrás. Si falta sitio, el que crece es
/// también el único que encoge, hasta su mínimo; los demás son <c>flex: none</c> y saltan enteros.
/// </para>
/// </remarks>
public sealed class BarRow : Panel
{
    /// <summary>
    /// El ancho de partida de un hijo que crece y encoge, acotado por su propio
    /// <see cref="Layoutable.MinWidth"/> y <see cref="Layoutable.MaxWidth"/>. Sin él —el NaN de
    /// fábrica— el hijo mide lo que pide y no se estira.
    /// </summary>
    public static readonly AttachedProperty<double> BasisProperty =
        AvaloniaProperty.RegisterAttached<BarRow, Control, double>("Basis", double.NaN);

    /// <summary>Si el hijo se va al canto derecho de su línea, con los que tenga detrás.</summary>
    public static readonly AttachedProperty<bool> PushProperty =
        AvaloniaProperty.RegisterAttached<BarRow, Control, bool>("Push");

    public static double GetBasis(Control control) => control.GetValue(BasisProperty);

    public static void SetBasis(Control control, double basis) => control.SetValue(BasisProperty, basis);

    public static bool GetPush(Control control) => control.GetValue(PushProperty);

    public static void SetPush(Control control, bool push) => control.SetValue(PushProperty, push);

    /// <summary>El hueco entre dos hijos de la misma línea.</summary>
    public double Gap { get; set; } = 12;

    /// <summary>El hueco entre una línea y la siguiente.</summary>
    public double RowGap { get; set; } = 10;

    /// <summary>
    /// Dónde se queda cada hijo en el alto de su línea, sin estirarse: abajo es el <c>flex-end</c> del
    /// diseño, que deja los botones a la altura del desplegable y no de su etiqueta.
    /// </summary>
    public VerticalAlignment Align { get; set; } = VerticalAlignment.Bottom;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(double.IsNaN(GetBasis(child))
                ? Size.Infinity
                : new Size(Start(child), double.PositiveInfinity));
        }

        var lines = Lines(availableSize.Width);
        double widest = 0;
        double height = RowGap * Math.Max(0, lines.Count - 1);

        foreach (var line in lines)
        {
            widest = Math.Max(widest, line.Sum(Start) + (Gap * (line.Count - 1)));
            height += line.Max(child => child.DesiredSize.Height);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? widest : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double top = 0;

        foreach (var line in Lines(finalSize.Width))
        {
            var widths = line.Select(Start).ToList();
            double free = finalSize.Width - widths.Sum() - (Gap * (line.Count - 1));

            // Primero el que crece: hasta su tope si sobra, hasta su mínimo si falta.
            int grow = line.FindIndex(child => !double.IsNaN(GetBasis(child)));
            if (grow >= 0)
            {
                double wanted = Math.Clamp(widths[grow] + free, line[grow].MinWidth, line[grow].MaxWidth);
                free -= wanted - widths[grow];
                widths[grow] = wanted;
            }

            double height = line.Max(child => child.DesiredSize.Height);
            double left = 0;

            for (int i = 0; i < line.Count; i++)
            {
                // Y lo que quede, delante del empujado: de él en adelante, todo va a la derecha.
                if (free > 0 && GetPush(line[i]))
                {
                    left += free;
                    free = 0;
                }

                double tall = line[i].DesiredSize.Height;
                double drop = Align switch
                {
                    VerticalAlignment.Top => 0,
                    VerticalAlignment.Center => (height - tall) / 2,
                    _ => height - tall,
                };

                line[i].Arrange(new Rect(left, top + drop, widths[i], tall));
                left += widths[i] + Gap;
            }

            top += height + RowGap;
        }

        return finalSize;
    }

    /// <summary>El ancho de partida de un hijo: su base acotada, o lo que pide si no la tiene.</summary>
    private static double Start(Control child)
    {
        double basis = GetBasis(child);
        return double.IsNaN(basis) ? child.DesiredSize.Width : Math.Clamp(basis, child.MinWidth, child.MaxWidth);
    }

    /// <summary>
    /// Los hijos repartidos en líneas, con su ancho de partida y no con el que acaben teniendo.
    /// </summary>
    /// <remarks>
    /// Lo calculan igual el medir y el colocar, como en <see cref="Grid12"/>: si cada uno partiera las
    /// líneas por su cuenta, un hijo podría medirse en una y colocarse en otra. Los ocultos no cuentan:
    /// ni ocupan ni dejan su hueco.
    /// </remarks>
    private List<List<Control>> Lines(double width)
    {
        var lines = new List<List<Control>>();
        var line = new List<Control>();
        double used = 0;

        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;

            double start = Start(child);
            if (line.Count > 0 && used + Gap + start > width)
            {
                lines.Add(line);
                line = [];
                used = 0;
            }

            used += (line.Count > 0 ? Gap : 0) + start;
            line.Add(child);
        }

        if (line.Count > 0) lines.Add(line);
        return lines;
    }
}
