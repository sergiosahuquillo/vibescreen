using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using VibeScreen.App.Localization;

namespace VibeScreen.App.Theme;

/// <summary>De qué color va una pastilla de estado.</summary>
public enum Tone
{
    Neutral,
    Good,
    Warn,
    Bad,
}

/// <summary>Las piezas que el diseño repite en las cuatro secciones.</summary>
/// <remarks>
/// <para>
/// Esto es lo que <see cref="Views.Forms"/> era antes del rediseño, con las piezas que el diseño
/// añade: la tarjeta con su pastilla de estado, la cifra con su etiqueta encima, el campo con su
/// ayuda debajo, el estado vacío. Vive aparte de <c>Forms</c> porque aquello son controles de
/// formulario —cosas que se tocan— y esto es presentación: cosas que se leen.
/// </para>
/// <para>
/// Todo va en la tipografía de interfaz. Lo que es <b>dato</b> —una cifra, una unidad, un
/// identificador de sensor, una ruta— lo pide quien lo escribe con <see cref="Data"/>, y esa es la
/// única monoespaciada que queda: la que se elige en Ajustes. Antes lo era la ventana entera.
/// </para>
/// </remarks>
public static class Blocks
{
    /// <summary>La cabecera de una sección: el nombre y qué se hace ahí.</summary>
    public static Control ViewHeader(Text title, Text subtitle, Control? trailing = null)
    {
        var text = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                Loc.Set(new TextBlock
                {
                    FontFamily = AppFonts.Display,
                    FontSize = 26,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Palette.TextBrush,
                }, TextBlock.TextProperty, title),
                Loc.Set(new TextBlock
                {
                    FontSize = 15,
                    MaxWidth = 620,
                    // Pegado a la izquierda a mano: un texto con ancho máximo dentro de algo más
                    // ancho se queda centrado por su cuenta, y el subtítulo se iba al medio de la
                    // ventana mientras el título seguía en el canto.
                    HorizontalAlignment = HorizontalAlignment.Left,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Palette.MutedBrush,
                }, TextBlock.TextProperty, subtitle),
            },
        };

        if (trailing is null) return text;

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        trailing.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(trailing, 1);
        row.Children.Add(text);
        row.Children.Add(trailing);
        return row;
    }

    /// <summary>
    /// Una tarjeta: el título en versalitas del realce y, a la derecha, cómo va eso.
    /// </summary>
    /// <remarks>
    /// El título se pasa en minúsculas y se sube aquí, para no tener que escribirlo gritando en cada
    /// sitio. La pastilla de la derecha es opcional porque no todas las tarjetas tienen un estado
    /// que contar: «Apariencia» no está ni bien ni mal.
    /// </remarks>
    public static Border Card(Text title, Control? flag, params Control[] children)
    {
        ArgumentNullException.ThrowIfNull(children);

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        head.Children.Add(SectionTitle(title));
        if (flag is not null)
        {
            Grid.SetColumn(flag, 1);
            head.Children.Add(flag);
        }

        var stack = new StackPanel { Spacing = 18 };
        stack.Children.Add(head);
        foreach (var child in children) stack.Children.Add(child);

        return new Border
        {
            Background = Palette.SurfaceBrush,
            BorderBrush = Palette.LineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = Palette.CardRadius,
            BoxShadow = Palette.CardShadow,
            Padding = new Thickness(20, 18, 20, 20),
            Child = stack,
        };
    }

    /// <summary>
    /// El título de una tarjeta o de una caja de la columna del editor: versalitas en el realce.
    /// </summary>
    /// <remarks>
    /// Se pasa en minúsculas y se sube aquí, para no tener que escribirlo gritando en cada sitio. Es
    /// el mismo en la tarjeta y en la caja porque el diseño escribe los dos igual.
    /// </remarks>
    public static TextBlock SectionTitle(Text title) =>
        Loc.Set(new TextBlock
        {
            FontFamily = AppFonts.Display,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = 1.2,
            Foreground = Palette.AccentBrush,
            VerticalAlignment = VerticalAlignment.Center,
        }, TextBlock.TextProperty, title.Upper());

    /// <summary>Un campo: su nombre, el control, y debajo lo que haya que explicar.</summary>
    public static Control Field(Text label, Control control, Text? help = null)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(FieldLabel(label));
        stack.Children.Add(control);
        if (help is { } note) stack.Children.Add(Help(note));
        return stack;
    }

    /// <summary>
    /// El nombre de un campo. Encima del control dentro de <see cref="Field"/>, o a su lado en una
    /// fila de herramientas, como el «Zoom» de la barra del editor.
    /// </summary>
    public static TextBlock FieldLabel(Text label) => Loc.Set(new TextBlock
    {
        FontSize = 14,
        FontWeight = FontWeight.Medium,
        Foreground = Palette.MutedBrush,
        VerticalAlignment = VerticalAlignment.Center,
    }, TextBlock.TextProperty, label);

    /// <summary>Lo que se cuenta debajo de un control cuando hace falta contarlo.</summary>
    public static TextBlock Help(Text text) => Loc.Set(new TextBlock
    {
        FontSize = 14,
        LineHeight = 21,
        MaxWidth = 620,
        HorizontalAlignment = HorizontalAlignment.Left,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Palette.FaintBrush,
    }, TextBlock.TextProperty, text);

    /// <summary>
    /// Una cifra con su etiqueta encima, de las que cambian solas.
    /// </summary>
    /// <remarks>
    /// Devuelve los textos además del bloque, para poder cambiar el valor sin rehacer la fila. Sin
    /// esto, cada refresco reconstruiría media tarjeta y el foco se perdería a cada segundo.
    /// </remarks>
    public static (Control Control, TextBlock Value, TextBlock Note) LiveStat(Text label, bool data = true)
    {
        var value = Font(new TextBlock
        {
            FontSize = 17,
            FontWeight = FontWeight.Medium,
            Foreground = Palette.TextBrush,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        }, data);

        var note = new TextBlock
        {
            FontSize = 14,
            Foreground = Palette.MutedBrush,
            IsVisible = false,
        };

        var stack = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Loc.Set(new TextBlock
                {
                    FontSize = 11,
                    LetterSpacing = 1,
                    Foreground = Palette.FaintBrush,
                }, TextBlock.TextProperty, label.Upper()),
                value,
                note,
            },
        };

        return (stack, value, note);
    }

    /// <summary>Una cifra con su etiqueta encima. Lo que se lee de un vistazo sin buscar.</summary>
    public static Control Stat(Text label, Text value, Text? note = null, bool data = true)
    {
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(Loc.Set(new TextBlock
        {
            FontSize = 11,
            LetterSpacing = 1,
            Foreground = Palette.FaintBrush,
        }, TextBlock.TextProperty, label.Upper()));
        stack.Children.Add(Font(Loc.Set(new TextBlock
        {
            FontSize = 17,
            FontWeight = FontWeight.Medium,
            Foreground = Palette.TextBrush,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        }, TextBlock.TextProperty, value), data));
        if (note is { } said)
            stack.Children.Add(Loc.Set(new TextBlock
            {
                FontSize = 14,
                Foreground = Palette.MutedBrush,
            }, TextBlock.TextProperty, said));
        return stack;
    }

    /// <summary>Una pastilla de estado, con su punto o su icono delante.</summary>
    public static Border Flag(Text text, Tone tone = Tone.Neutral, StreamGeometry? icon = null)
    {
        var (foreground, background, edge) = tone switch
        {
            Tone.Good => (Palette.GoodBrush, Palette.GoodSoftBrush, Palette.GoodEdgeBrush),
            Tone.Warn => (Palette.WarnBrush, Palette.WarnSoftBrush, Palette.WarnEdgeBrush),
            Tone.Bad => (Palette.BadBrush, Palette.BadSoftBrush, Palette.BadEdgeBrush),
            _ => (Palette.MutedBrush, (IBrush)Palette.Surface2Brush, (IBrush)Palette.LineStrongBrush),
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (icon is not null) row.Children.Add(Icons.Make(icon, foreground, 13));
        else if (tone != Tone.Neutral)
            row.Children.Add(new Avalonia.Controls.Shapes.Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = foreground,
                VerticalAlignment = VerticalAlignment.Center,
            });

        row.Children.Add(Loc.Set(new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeight.Medium,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
        }, TextBlock.TextProperty, text));

        return new Border
        {
            Background = background,
            BorderBrush = edge,
            BorderThickness = new Thickness(1),
            CornerRadius = Palette.PillRadius,
            Padding = new Thickness(9, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = row,
        };
    }

    /// <summary>
    /// La cifra grande de una tarjeta, con su unidad pequeña detrás.
    /// </summary>
    /// <remarks>
    /// La unidad va aparte y más chica a propósito: pegada y del mismo tamaño se lee como parte del
    /// número, y «13.284 B/s» pasa a parecer una cifra de cinco dígitos con letras. Devuelve también
    /// los dos textos para poder cambiar el valor sin rehacer la fila.
    /// </remarks>
    public static (Control Control, TextBlock Value, TextBlock Unit) NumXl(string value, string unit)
    {
        var number = AppFonts.Mono(new TextBlock
        {
            Text = value,
            FontSize = 34,
            FontWeight = FontWeight.Medium,
            Foreground = Palette.TextBrush,
            VerticalAlignment = VerticalAlignment.Bottom,
        });

        var suffix = AppFonts.Mono(new TextBlock
        {
            Text = unit,
            FontSize = 15,
            Foreground = Palette.MutedBrush,
            Margin = new Thickness(6, 0, 0, 5),
            VerticalAlignment = VerticalAlignment.Bottom,
        });

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { number, suffix },
        };

        return (row, number, suffix);
    }

    /// <summary>Una pastilla con un punto que late, para lo que está pasando ahora mismo.</summary>
    public static (Control Control, TextBlock Text, Avalonia.Controls.Shapes.Ellipse Dot) Chip(Text text)
    {
        var dot = new Avalonia.Controls.Shapes.Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = Palette.GoodBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var label = Loc.Set(new TextBlock
        {
            FontSize = 13,
            Foreground = Palette.MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
        }, TextBlock.TextProperty, text);

        var chip = new Border
        {
            Background = Palette.SurfaceBrush,
            BorderBrush = Palette.LineStrongBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = Palette.PillRadius,
            Padding = new Thickness(12, 6),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { dot, label },
            },
        };

        return (chip, label, dot);
    }

    /// <summary>Un texto que es un dato: cifras, unidades, identificadores y rutas.</summary>
    public static TextBlock Data(string text = "", double size = 14, IBrush? brush = null) => AppFonts.Mono(new TextBlock
    {
        Text = text,
        FontSize = size,
        Foreground = brush ?? Palette.TextBrush,
        VerticalAlignment = VerticalAlignment.Center,
    });

    /// <summary>La letra de una cifra que puede ser un dato o una palabra, como el modelo del panel.</summary>
    private static TextBlock Font(TextBlock text, bool data)
    {
        if (data) return AppFonts.Mono(text);
        text.FontFamily = AppFonts.Ui;
        return text;
    }

    /// <summary>Lo que se enseña cuando no hay nada que enseñar.</summary>
    /// <remarks>
    /// Con icono, una frase corta que dice que está vacío a propósito, y otra que dice qué llegaría
    /// a aparecer ahí. Sin la segunda, un hueco vacío parece una avería.
    /// </remarks>
    public static Control Empty(StreamGeometry icon, Text title, Text note, Control? action = null) =>
        EmptyState(icon, title, note, action, table: false);

    /// <summary>
    /// Lo mismo, en el sitio de las filas de una tabla vacía: el <c>.empty-table</c> del diseño.
    /// </summary>
    /// <remarks>
    /// Centrado y con aire por arriba —56 px arriba y abajo, 24 a los lados y 10 entre las piezas—,
    /// al contrario que el de una tarjeta. Pegado a la izquierda, en una tabla se leía como una fila
    /// más; así se lee como lo que es, la tabla sin nada.
    /// </remarks>
    public static Control EmptyTable(StreamGeometry icon, Text title, Text note, Control? action = null) =>
        EmptyState(icon, title, note, action, table: true);

    private static StackPanel EmptyState(StreamGeometry icon, Text title, Text note, Control? action, bool table)
    {
        var side = table ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        var text = table ? TextAlignment.Center : TextAlignment.Left;

        var stack = new StackPanel
        {
            Spacing = table ? 10 : 8,
            HorizontalAlignment = side,
            Margin = table ? new Thickness(24, 56) : default,
        };
        stack.Children.Add(new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(9),
            Background = Palette.Surface2Brush,
            BorderBrush = Palette.LineBrush,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = side,
            Child = Icons.Make(icon, Palette.FaintBrush, 18),
        });
        stack.Children.Add(Loc.Set(new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = Palette.TextBrush,
            HorizontalAlignment = side,
            TextAlignment = text,
        }, TextBlock.TextProperty, title));
        stack.Children.Add(Loc.Set(new TextBlock
        {
            FontSize = 14,
            MaxWidth = 420,
            HorizontalAlignment = side,
            TextAlignment = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.FaintBrush,
        }, TextBlock.TextProperty, note));
        if (action is not null)
        {
            action.HorizontalAlignment = side;

            // En una tarjeta, el botón algo más separado del texto; en la tabla, todo a la misma
            // distancia, que es lo que dice su gap.
            action.Margin = table ? default : new Thickness(0, 6, 0, 0);
            stack.Children.Add(action);
        }

        return stack;
    }

    /// <summary>Un botón con su icono delante.</summary>
    public static Button Action(Text text, StreamGeometry? icon, Action click, params string[] classes)
    {
        ArgumentNullException.ThrowIfNull(click);
        ArgumentNullException.ThrowIfNull(classes);

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (icon is not null) content.Children.Add(Icons.Make(icon, Palette.TextBrush, 17));
        content.Children.Add(Loc.Set(new TextBlock { VerticalAlignment = VerticalAlignment.Center }, TextBlock.TextProperty, text));

        var button = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (string name in classes) button.Classes.Add(name);

        // El icono no hereda el color del botón: en el principal la letra va sobre el realce y en el
        // de borrar va en rojo, y un icono gris al lado de una letra roja se ve mal pegado.
        if (content.Children.Count > 1 && content.Children[0] is Viewbox box
            && box.Child is Avalonia.Controls.Shapes.Path path)
        {
            path.Stroke = classes.Contains("primary") ? Palette.OnAccentBrush
                : classes.Contains("danger") ? Palette.BadBrush
                : Palette.TextBrush;
        }

        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Una fila de controles que se ponen unos al lado de otros y saltan si no caben.</summary>
    public static Control Cluster(params Control[] children)
    {
        ArgumentNullException.ThrowIfNull(children);
        var panel = new WrapPanel { ItemSpacing = 10, LineSpacing = 10 };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    /// <summary>Pone la tipografía de interfaz en una vista entera.</summary>
    /// <remarks>
    /// Corta la herencia desde la ventana, así que lo que sea dato no puede contar con ella: lo pide
    /// con <see cref="Data"/> o con <see cref="AppFonts.Mono"/>, que es lo que lo deja siguiendo a la
    /// letra elegida en Ajustes.
    /// </remarks>
    public static T Ui<T>(T control)
        where T : Control
    {
        ArgumentNullException.ThrowIfNull(control);
        TextElement.SetFontFamily(control, AppFonts.Ui);
        return control;
    }
}
