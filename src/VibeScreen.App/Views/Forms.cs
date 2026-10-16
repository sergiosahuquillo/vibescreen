using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using VibeScreen.App.Localization;
using VibeScreen.App.Theme;

namespace VibeScreen.App.Views;

/// <summary>Las piezas que se repiten en toda la ventana. Sin esto, la interfaz es pura fontanería.</summary>
public static class Forms
{
    /// <summary>Una tarjeta con título.</summary>
    /// <remarks>
    /// El título va en mayúsculas, que es lo que separa una tarjeta de otra cuando toda la ventana
    /// lleva la misma monoespaciada y la negrita apenas se distingue. Se pasa en minúsculas y se sube
    /// aquí, para no tener que escribirlo gritando en cada sitio.
    /// </remarks>
    public static Border Card(Text title, params Control[] children) =>
        Theme.Blocks.Card(title, null, children);

    /// <summary>
    /// Una pregunta de sí o no, en una ventana modal propia.
    /// </summary>
    /// <remarks>
    /// Avalonia no trae diálogo de mensaje, así que hay que montarlo. Existe porque «Nuevo» y
    /// «Abrir…» se llevaban por delante el tema sin guardar sin decir nada: un clic y veinte minutos
    /// de trabajo fuera, sin aviso ni deshacer. Devuelve false si no hay ventana donde enseñarla,
    /// que es lo prudente cuando la pregunta es «¿seguro que lo tiro?».
    /// </remarks>
    public static async Task<bool> ConfirmAsync(Visual? owner, Text title, Text question, Text accept)
    {
        if (TopLevel.GetTopLevel(owner) is not Window parent) return false;

        var dialog = new Window
        {
            Title = title.ToString(),
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Palette.BackgroundBrush,
            FontFamily = parent.FontFamily,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        buttons.Children.Add(Action(accept, () => dialog.Close(true)));
        buttons.Children.Add(Action(Loc.L("common.cancel"), () => dialog.Close(false)));

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            MaxWidth = 420,
            Children =
            {
                new TextBlock { Text = title.Upper().ToString(), Classes = { "h2" } },
                new TextBlock { Text = question.ToString(), TextWrapping = TextWrapping.Wrap, Foreground = Palette.TextBrush },
                buttons,
            },
        };

        // Escapar es no: cerrar la ventana con la cruz o con Esc no puede significar «adelante».
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape) dialog.Close(false);
        };

        return await dialog.ShowDialog<bool>(parent).ConfigureAwait(true);
    }

    /// <summary>
    /// Pide un nombre, comprobándolo mientras se escribe. Devuelve null si el usuario se echa atrás.
    /// </summary>
    /// <remarks>
    /// El aviso sale debajo según se teclea y el botón no se deja pulsar hasta que el nombre vale:
    /// enterarse de que «CPU: carga» no sirve <b>después</b> de aceptar, con el diálogo ya cerrado,
    /// es la peor forma de contarlo.
    /// </remarks>
    public static async Task<string?> AskNameAsync(
        Visual? owner, Text title, Text question, string initial, Func<string, Text?> problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(initial);

        if (TopLevel.GetTopLevel(owner) is not Window parent) return null;

        var dialog = new Window
        {
            Title = title.ToString(),
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Palette.BackgroundBrush,
            FontFamily = parent.FontFamily,
        };

        var box = new TextBox { Text = initial, PlaceholderText = Loc.S("dialog.layout-name.placeholder") };
        var trouble = new TextBlock { Foreground = Palette.BadBrush, TextWrapping = TextWrapping.Wrap };
        var accept = Action(Loc.L("common.save"), () => dialog.Close(box.Text?.Trim()));

        void Recheck()
        {
            var bad = problem(box.Text?.Trim() ?? "");
            trouble.Text = bad?.ToString() ?? "";
            accept.IsEnabled = bad is null;
        }

        box.TextChanged += (_, _) => Recheck();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter && accept.IsEnabled) dialog.Close(box.Text?.Trim());
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = title.Upper().ToString(), Classes = { "h2" } },
                new TextBlock { Text = question.ToString(), TextWrapping = TextWrapping.Wrap, Foreground = Palette.TextBrush },
                box,
                trouble,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { accept, Action(Loc.L("common.cancel"), () => dialog.Close(null)) },
                },
            },
        };

        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape) dialog.Close(null);
        };

        dialog.Opened += (_, _) =>
        {
            Recheck();
            box.SelectAll();
            box.Focus();
        };

        return await dialog.ShowDialog<string?>(parent).ConfigureAwait(true);
    }

    public static TextBlock Label(Text text) => Loc.Set(new TextBlock
    {
        FontFamily = AppFonts.Ui,
        FontSize = 13,
        Foreground = Palette.MutedBrush,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
    }, TextBlock.TextProperty, text);

    public static TextBlock Value(string text = "") => AppFonts.Mono(new TextBlock
    {
        Text = text,
        FontSize = 14,
        Foreground = Palette.TextBrush,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
    });

    /// <summary>
    /// Un texto de un solo renglón, con puntos suspensivos si no cabe. Para lo que cambia solo: si
    /// pudiera pasar a dos líneas, movería lo que tenga al lado cada vez que cambiara.
    /// </summary>
    public static TextBlock OneLine(Text text = default) => Loc.Set(new TextBlock
    {
        FontFamily = AppFonts.Ui,
        FontSize = 13,
        Foreground = Palette.MutedBrush,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
    }, TextBlock.TextProperty, text);

    public static Button Action(Text text, Action onClick)
    {
        ArgumentNullException.ThrowIfNull(onClick);
        var button = Loc.Set(new Button { Padding = new Thickness(14, 6) }, ContentControl.ContentProperty, text);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>
    /// Un contador numérico, con los separadores del idioma.
    /// </summary>
    /// <remarks>
    /// El formato sigue al idioma en caliente: en español se escribe «12,5» y en inglés «12.5». Lee
    /// sin separador de miles a propósito, para que un «1.5» tecleado en español no se convierta en
    /// quince sin decir nada: se rechaza y el contador vuelve a lo que tenía.
    /// </remarks>
    public static NumericUpDown Number(double minimum, double maximum, double increment = 1, int decimals = 0)
    {
        var spinner = new NumericUpDown
        {
            Minimum = (decimal)minimum,
            Maximum = (decimal)maximum,
            Increment = (decimal)increment,
            FormatString = decimals > 0 ? "F" + decimals.ToString(CultureInfo.InvariantCulture) : "F0",
            ParsingNumberStyle = NumberStyles.Float,
            ClipValueToMinMax = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Loc.Follow(spinner, NumericUpDown.NumberFormatProperty, () => Loc.Culture.NumberFormat);
        return spinner;
    }

    /// <summary>
    /// Un contador estrecho, el <c>.input-num</c> del diseño: 66 px, centrado, con 6 de hueco a cada
    /// lado y en la letra de los datos.
    /// </summary>
    /// <remarks>
    /// Sin los botones del contador: en 66 px no caben, y aquí el número se escribe. Es lo que hace que
    /// las cifras de varias filas se lean como una columna. Lo usan las filas de las propiedades —la
    /// posición y el tamaño de la caja, que además se arrastra en el lienzo— y el paso de la rejilla.
    /// </remarks>
    public static NumericUpDown Narrow(NumericUpDown spinner)
    {
        ArgumentNullException.ThrowIfNull(spinner);

        spinner.ShowButtonSpinner = false;
        spinner.Width = 66;
        spinner.Padding = new Thickness(6, 0);
        spinner.HorizontalAlignment = HorizontalAlignment.Left;
        spinner.HorizontalContentAlignment = HorizontalAlignment.Center;
        return AppFonts.Mono(spinner);
    }

    /// <summary>
    /// Un desplegable con los valores de un enumerado. La etiqueta va en el idioma de la ventana y el
    /// valor sigue siendo el del JSON: el usuario lee «Apaisada» y el fichero guarda «landscape».
    /// </summary>
    public static ComboBox Enum<T>(T current, Func<T, Text> label, Action<T> set) where T : struct, System.Enum
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(set);

        var box = Labeled(new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch });
        foreach (var value in System.Enum.GetValues<T>()) box.Items.Add(new EnumOption<T>(value, label(value)));
        box.SelectedItem = new EnumOption<T>(current, label(current));
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is EnumOption<T> option) set(option.Value);
        };
        return box;
    }

    /// <summary>
    /// Deja un desplegable pintando sus opciones con nombre en el idioma de ahora.
    /// </summary>
    /// <remarks>
    /// Sin esto, el desplegable pinta cada opción con su <c>ToString()</c> una vez y para siempre: al
    /// cambiar de idioma, la lista y lo elegido se quedarían en el de antes. Con la plantilla, cada
    /// opción es un texto enlazado como cualquier otro. Lo que no tenga nombre —una ruta, el nombre
    /// de un tema— se sigue pintando como siempre.
    /// </remarks>
    public static T Labeled<T>(T box) where T : ItemsControl
    {
        ArgumentNullException.ThrowIfNull(box);
        box.ItemTemplate = LabelTemplate;
        return box;
    }

    private static readonly FuncDataTemplate<ILabeled> LabelTemplate =
        new((option, _) => Loc.Set(new TextBlock(), TextBlock.TextProperty, option?.Label ?? default), supportsRecycling: false);
}

/// <summary>Una opción de un desplegable que se pinta con un nombre traducido.</summary>
public interface ILabeled
{
    Text Label { get; }
}

/// <summary>Un valor de enumerado con el nombre que ve el usuario.</summary>
public sealed record EnumOption<T>(T Value, Text Label) : ILabeled where T : struct, System.Enum
{
    public override string ToString() => Label.ToString();
}

/// <summary>Un formato de reloj con nombre, para no tener que aprenderse las marcas.</summary>
public sealed record ClockPreset(Text Label, Text Format) : ILabeled
{
    public override string ToString() => Label.ToString();
}

/// <summary>Qué clase de fondo tiene el tema. Es cosa de la interfaz; el layout guarda el tipo en el JSON.</summary>
public enum BackgroundKind
{
    Color,
    Image,
}

/// <summary>Los nombres para leer de lo que el layout guarda con su nombre de JSON.</summary>
public static class Labels
{
    /// <summary>
    /// En qué punto está el servicio, dicho para leer.
    /// </summary>
    /// <remarks>
    /// Vive aquí porque lo enseñan dos sitios —la barra de estado y la pestaña Panel— y en uno de
    /// ellos salía el nombre inglés del enumerado tal cual.
    /// </remarks>
    public static Text ServiceState(VibeScreen.Contracts.ServiceState value) => value switch
    {
        VibeScreen.Contracts.ServiceState.Painting => Loc.L("service.state.painting"),
        VibeScreen.Contracts.ServiceState.Connected => Loc.L("service.state.connected"),
        VibeScreen.Contracts.ServiceState.Searching => Loc.L("service.state.searching"),
        VibeScreen.Contracts.ServiceState.Starting => Loc.L("service.state.starting"),
        _ => Loc.L("service.state.stopping"),
    };

    public static Text BackgroundKind(Views.BackgroundKind value) =>
        value == Views.BackgroundKind.Image ? Loc.L("enum.background.image") : Loc.L("enum.background.color");

    public static Text ImageFit(VibeScreen.Core.Layout.ImageFit value) => value switch
    {
        VibeScreen.Core.Layout.ImageFit.Contain => Loc.L("enum.image-fit.contain"),
        VibeScreen.Core.Layout.ImageFit.Stretch => Loc.L("enum.image-fit.stretch"),
        VibeScreen.Core.Layout.ImageFit.Center => Loc.L("enum.image-fit.center"),
        _ => Loc.L("enum.image-fit.cover"),
    };

    public static Text Orientation(VibeScreen.Protocol.Display.Orientation value) => value switch
    {
        VibeScreen.Protocol.Display.Orientation.Portrait => Loc.L("enum.orientation.portrait"),
        VibeScreen.Protocol.Display.Orientation.ReversePortrait => Loc.L("enum.orientation.reverse-portrait"),
        VibeScreen.Protocol.Display.Orientation.Landscape => Loc.L("enum.orientation.landscape"),
        _ => Loc.L("enum.orientation.reverse-landscape"),
    };

    public static Text UnitPlacement(VibeScreen.Core.Layout.UnitPlacement value) =>
        value == VibeScreen.Core.Layout.UnitPlacement.Prefix
            ? Loc.L("enum.unit-placement.prefix")
            : Loc.L("enum.unit-placement.suffix");

    public static Text TextAlignment(VibeScreen.Core.Layout.TextAlignment value) => value switch
    {
        VibeScreen.Core.Layout.TextAlignment.Center => Loc.L("enum.text-alignment.center"),
        VibeScreen.Core.Layout.TextAlignment.Right => Loc.L("enum.text-alignment.right"),
        _ => Loc.L("enum.text-alignment.left"),
    };

    public static Text BarDirection(VibeScreen.Core.Layout.BarDirection value) => value switch
    {
        VibeScreen.Core.Layout.BarDirection.Left => Loc.L("enum.bar-direction.left"),
        VibeScreen.Core.Layout.BarDirection.Up => Loc.L("enum.bar-direction.up"),
        VibeScreen.Core.Layout.BarDirection.Down => Loc.L("enum.bar-direction.down"),
        _ => Loc.L("enum.bar-direction.right"),
    };

    /// <summary>
    /// Un tramo de la regla de color, para leer: «menos de 40 °C», «40 a 80 °C», «80 °C o más».
    /// </summary>
    /// <remarks>
    /// El medio lleva la unidad solo al final: «40 a 80 °C» se lee mejor que «40 °C a 80 °C». Las
    /// cifras van sin ceros de sobra y con los separadores del idioma.
    /// </remarks>
    /// <param name="levels">La regla.</param>
    /// <param name="band">El tramo, de 0 a 2.</param>
    /// <param name="symbol">Símbolo de la unidad del widget, o vacío si no tiene.</param>
    public static Text Range(VibeScreen.Core.Layout.ColorLevels levels, int band, string symbol = "")
    {
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(symbol);

        string unit = symbol.Length == 0 ? "" : " " + symbol;
        return band switch
        {
            <= 0 => Loc.L("levels.below", levels.MediumFrom, unit),
            1 => Loc.L("levels.between", levels.MediumFrom, levels.HighFrom, unit),
            _ => Loc.L("levels.above", levels.HighFrom, unit),
        };
    }
}

/// <summary>Una rejilla de dos columnas —etiqueta y control— que va creciendo según se le añaden filas.</summary>
public sealed class FormGrid : Grid
{
    public FormGrid(double labelWidth = 130)
    {
        ColumnDefinitions = new ColumnDefinitions($"{labelWidth.ToString(CultureInfo.InvariantCulture)},*");
        RowSpacing = 10;
        ColumnSpacing = 10;
    }

    public T Add<T>(Text label, T control) where T : Control
    {
        int row = RowDefinitions.Count;
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var text = Forms.Label(label);
        SetRow(text, row);
        SetColumn(text, 0);
        Children.Add(text);

        SetRow(control, row);
        SetColumn(control, 1);
        Children.Add(control);
        return control;
    }

    /// <summary>Una fila que ocupa las dos columnas, para casillas y botones.</summary>
    public T AddWide<T>(T control) where T : Control
    {
        int row = RowDefinitions.Count;
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        SetRow(control, row);
        SetColumn(control, 0);
        SetColumnSpan(control, 2);
        Children.Add(control);
        return control;
    }

    /// <summary>
    /// Una fila con una sola etiqueta y los campos estrechos que hagan falta.
    /// </summary>
    /// <remarks>
    /// La posición de un widget no son dos propiedades, son dos cifras de la misma: el diseño las
    /// pone en una fila que pone «Posición» y no en dos que ponen «X» e «Y». Lo mismo el tamaño.
    /// Cada campo va de 66 px, centrado y en monoespaciada, que es lo que hace que las cifras de
    /// varias filas se lean como una columna.
    /// </remarks>
    public void AddFields(Text label, params Control[] controls)
    {
        ArgumentNullException.ThrowIfNull(controls);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        foreach (var control in controls)
            row.Children.Add(control is NumericUpDown spinner ? Forms.Narrow(spinner) : control);

        Add(label, row);
    }

    /// <summary>
    /// Dos casillas estrechas en una sola fila, cada una con su etiqueta.
    /// </summary>
    /// <remarks>
    /// X e Y, o ancho y alto, se leen juntos y se escriben juntos; en filas separadas ocupan el doble
    /// de alto sin decir más. Solo vale para controles que no necesitan la fila entera.
    /// </remarks>
    public void AddPair(Text first, Control firstControl, Text second, Control secondControl)
    {
        ArgumentNullException.ThrowIfNull(firstControl);
        ArgumentNullException.ThrowIfNull(secondControl);

        // Ocupa las dos columnas y las etiquetas van pegadas a lo suyo, en vez de dejar la primera en
        // la columna fija: con la columna de etiquetas, a las dos casillas les quedaría un palmo y el
        // número no cabría entre los botones del contador.
        // Sin los botones del contador: dos de ellos en una columna de 400 px dejan al número unos
        // treinta píxeles y no se lee. El valor se escribe, y la caja además se arrastra en el lienzo.
        foreach (var control in new[] { firstControl, secondControl })
            if (control is NumericUpDown spinner) spinner.ShowButtonSpinner = false;

        var firstLabel = Forms.Label(first);
        var secondLabel = Forms.Label(second);
        secondLabel.Margin = new Thickness(10, 0, 0, 0);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*"), ColumnSpacing = 8 };
        SetColumn(firstLabel, 0);
        SetColumn(firstControl, 1);
        SetColumn(secondLabel, 2);
        SetColumn(secondControl, 3);
        row.Children.Add(firstLabel);
        row.Children.Add(firstControl);
        row.Children.Add(secondLabel);
        row.Children.Add(secondControl);

        AddWide(row);
    }
}

/// <summary>
/// Una sección plegable de un formulario.
/// </summary>
/// <remarks>
/// Un arco con valor dentro tiene treinta propiedades y en la columna caben once. Repartidas en
/// secciones, solo está abierta la que se está tocando; las demás resumen sus valores a la derecha,
/// así que se ven sin abrirlas y no hace falta desplazarse para comprobar una caja o un sensor.
/// </remarks>
public sealed class FormSection : Expander
{
    private readonly TextBlock _summary = Forms.OneLine();

    /// <summary>
    /// De dónde saca el estilo. Sin esto, Avalonia busca una plantilla para <c>FormSection</c>, no la
    /// encuentra y la sección sale sin cabecera y sin poder plegarse.
    /// </summary>
    protected override Type StyleKeyOverride => typeof(Expander);

    public FormSection(Text title, bool open = false)
    {
        // Los tres números son los del diseño: la columna de etiquetas mide 96 y el cuerpo lleva
        // 12 por los cuatro lados. El resto de la caja —fondo, borde, redondeo y el alto de la
        // cabecera— lo pone el estilo del Expander, que es donde vive la forma de los controles.
        Fields = new FormGrid(96);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        IsExpanded = open;
        Padding = new Thickness(12);
        Content = Fields;

        var name = Loc.Set(new TextBlock
        {
            FontFamily = AppFonts.Ui,
            FontWeight = FontWeight.SemiBold,
            Foreground = open ? Palette.TextBrush : Palette.MutedBrush,
            FontSize = 12,
            LetterSpacing = 1.1,
            VerticalAlignment = VerticalAlignment.Center,
        }, TextBlock.TextProperty, title.Upper());

        // El resumen es un dato -«40,76 · 74x28 · 1000 ms»- y va en monoespaciada, que es lo que
        // hace que las cifras de dos secciones cerradas se lean como una columna y no como prosa.
        AppFonts.Mono(_summary);
        _summary.FontSize = 12;
        _summary.Foreground = Palette.FaintBrush;

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(name, 0);
        Grid.SetColumn(_summary, 1);
        _summary.Margin = new Thickness(12, 0, 0, 0);
        _summary.TextAlignment = TextAlignment.Right;
        _summary.IsVisible = !open;
        header.Children.Add(name);
        header.Children.Add(_summary);
        Header = header;

        // El resumen es lo que se sabe de la sección sin abrirla; abierta estorba y se repite. Y el
        // título sube de tono al abrirla, que es lo que marca cuál de las seis se está tocando
        // cuando todas llevan la misma letra en versalitas.
        PropertyChanged += (_, e) =>
        {
            if (e.Property != IsExpandedProperty) return;

            _summary.IsVisible = !IsExpanded;
            name.Foreground = IsExpanded ? Palette.TextBrush : Palette.MutedBrush;
        };
    }

    /// <summary>Donde van las filas de la sección.</summary>
    public FormGrid Fields { get; }

    /// <summary>Lo que se lee a la derecha del título cuando está cerrada.</summary>
    public Text Summary
    {
        set => Loc.Set(_summary, TextBlock.TextProperty, value);
    }
}
