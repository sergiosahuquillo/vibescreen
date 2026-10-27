using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using VibeScreen.App.Localization;
using VibeScreen.App.Theme;
using VibeScreen.Contracts;
using VibeScreen.Core;
using VibeScreen.Core.Drivers;
using VibeScreen.Core.Layout;
using VibeScreen.Core.Rendering;
using VibeScreen.Core.Sensors;
using VibeScreen.Protocol.Rendering;
using static VibeScreen.Protocol.Display.OrientationExtensions;
using DisplayCapabilities = VibeScreen.Protocol.Display.DisplayCapabilities;
using HardwareRevision = VibeScreen.Protocol.Display.HardwareRevision;
using PanelOrientation = VibeScreen.Protocol.Display.Orientation;
using PixelFormat = VibeScreen.Protocol.Display.PixelFormat;
using PixelRect = VibeScreen.Protocol.Display.PixelRect;

namespace VibeScreen.App.Views;

/// <summary>
/// El editor de temas: la vista previa a la izquierda, la lista de widgets y sus propiedades.
/// </summary>
/// <remarks>
/// El documento en memoria son los mismos <c>record</c> que se serializan al JSON, y se modifican en
/// sitio. No hay modelo de vista intermedio: con un formato que ya está pensado para que lo edite una
/// interfaz, duplicarlo solo añadiría un sitio donde desincronizarse.
/// </remarks>
public sealed class EditorView : UserControl, IDisposable
{
    private readonly ServiceLink _service;
    private readonly RemoteSensorProvider _sensors;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private readonly PanelCanvas _canvas = new();

    /// <summary>
    /// La lista de widgets. Su relleno va por dentro de lo que se desplaza, como el de <c>.wlist</c>, y
    /// su alto máximo lo pone <see cref="FitWidgetList"/> según la ventana.
    /// </summary>
    private readonly ListBox _list = new()
    {
        Name = "wlist",
        Background = Brushes.Transparent,
        Padding = new Thickness(8, 4, 8, 10),
        MaxHeight = 160,
    };

    private readonly ContentControl _properties = new();
    private readonly ContentControl _display = new();

    // Las cajas de la columna que se tocan después de construirlas: Añadir se abre y se cierra con la
    // selección, y las dos fijas cambian la nota de la derecha. Pantalla no: nadie la toca una vez puesta.
    private readonly InspectorPanel _add;
    private readonly InspectorPanel _widgetsPanel;
    private readonly InspectorPanel _propsPanel;
    private readonly AutoCompleteBox _picker = new()
    {
        FilterMode = AutoCompleteFilterMode.Contains,
        MinimumPrefixLength = 0,
    };
    // Estos dos cambian de contenido con el tema y con las lecturas. Van a un renglón fijo y con
    // puntos suspensivos: si pudieran pasar a dos líneas, el lienzo daría un salto cada segundo.
    private readonly TextBlock _pickerHint = Forms.OneLine();
    private readonly TextBlock _bandwidth = Forms.OneLine();
    private readonly ProgressBar _bandwidthBar = new() { Minimum = 0, Maximum = 100, Height = 6, CornerRadius = new CornerRadius(3) };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Foreground = Palette.WarnBrush };
    /// <summary>Si lo que hay en pantalla está guardado. En el sitio donde estaba el nombre.</summary>
    private readonly TextBlock _file = Forms.Label(Loc.Empty);
    private readonly Control _saveState;
    private readonly Viewbox _saveIcon;

    /// <summary>El tamaño del panel, debajo del lienzo. Es la medida de lo que se está dibujando.</summary>
    private readonly TextBlock _ruler = AppFonts.Mono(new TextBlock
    {
        FontSize = 12,
        Foreground = Palette.FaintBrush,
        HorizontalAlignment = HorizontalAlignment.Center,
    });

    /// <summary>Se guarda aparte porque se apaga con un tema que no se ha guardado nunca.</summary>
    private readonly Button _delete;

    /// <summary>
    /// Los layouts guardados. Elegir uno lo abre: no hay diálogo de fichero ni rutas a la vista.
    /// </summary>
    private readonly ComboBox _chooser = new()
    {
        MaxDropDownHeight = 320,

        // Fluent lo deja a la izquierda y del ancho de lo elegido; aquí llena su campo, que es el que
        // decide cuánto mide.
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private LayoutDocument _layout = NewLayout(480, 320, PanelOrientation.Landscape);
    private List<LayoutWidget> _widgets = [];
    private LayoutRenderer? _renderer;
    private WidgetProperties? _editing;
    /// <summary>El nombre del layout abierto, o null si es nuevo y todavía no se ha guardado.</summary>
    private string? _name;

    /// <summary>El nombre del layout que está pintando el panel, para saber si borrar le afecta.</summary>
    private string? _painting;

    /// <summary>
    /// El widget copiado, ya copiado.
    /// </summary>
    /// <remarks>
    /// Vive en el editor y no en el portapapeles del sistema: no puede fallar, no depende de nada de
    /// fuera y sirve igual para copiar de un tema y pegar en otro, que es el caso que importa. Lo que
    /// no hace es sobrevivir a cerrar la ventana.
    /// </remarks>
    private LayoutWidget? _clipboard;

    private bool _rebuild = true;
    private bool _suspend;
    private bool _offered;
    private bool _dirty;

    /// <summary>El mensaje que se ve ahora lo puso una acción del usuario, no el inspector del tema.</summary>
    private bool _ownMessage;

    /// <summary>Lo que dicen las filas de la lista ahora mismo, para no rehacerla cuando no cambia.</summary>
    private List<string> _shownLabels = [];

    /// <summary>
    /// Si un fichero estaba ahí, y cuándo se miró.
    /// </summary>
    /// <remarks>
    /// El inspector comprueba que la imagen de fondo exista, y eso es disco. Como los avisos se
    /// recalculan con cada tecla del panel de propiedades, una imagen en una unidad de red caída
    /// congelaba la ventana letra a letra esperando el timeout. Se pregunta una vez por segundo.
    /// </remarks>
    private readonly Dictionary<string, (bool There, TimeSpan At)> _fileMemo = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (ImageProbe? Probe, TimeSpan At)> _probeMemo = new(StringComparer.OrdinalIgnoreCase);
    private int _throughput = RevADisplayFamily.MeasuredThroughputBytesPerSecond;

    /// <summary>Cómo cuenta los bytes el panel conectado. Hasta saberlo, como la revisión A.</summary>
    private PixelFormat _pixelFormat = PixelFormat.Rgb565LittleEndian;

    /// <summary>La forma del panel conectado, o null mientras no se sepa. Es la de un tema nuevo.</summary>
    private (int Width, int Height, PanelOrientation Orientation)? _panelShape;

    public EditorView(ServiceLink service, RemoteSensorProvider sensors)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(sensors);
        _service = service;
        _sensors = sensors;

        _delete = Blocks.Action(Loc.L("editor.delete"), Icons.Trash, () => Run(DeleteThemeAsync), "danger");
        Loc.Set(_picker, AutoCompleteBox.PlaceholderTextProperty, Loc.L("editor.picker.placeholder"));

        _saveIcon = Icons.Make(Icons.Check, Palette.GoodBrush, 14);
        _saveState = new StackPanel
        {
            Name = "save-state",
            Orientation = Orientation.Horizontal,
            Spacing = 6,

            // Del alto de un botón, para quedar a su altura en una fila que alinea por abajo.
            MinHeight = 36,
            Children = { _saveIcon, _file },
        };

        _add = new InspectorPanel(Loc.L("editor.panel.add"), AddPanel(), InspectorPanel.BodyPadding, collapsible: true, open: true);

        // La lista va pegada a la caja, sin el cuerpo de las demás: su relleno es el suyo.
        _widgetsPanel = new InspectorPanel(Loc.L("editor.panel.widgets"), _list, default);
        _propsPanel = new InspectorPanel(Loc.L("editor.panel.properties"), _properties, InspectorPanel.BodyPadding) { Name = "props-panel" };

        _widgets = [.. _layout.Widgets];
        _canvas.Layout = _layout;
        _canvas.SelectionChanged += Select;
        _canvas.Edited += () =>
        {
            _editing?.SyncRect();
            ShowBandwidth();
            Touch();
            Repaint();
        };
        _canvas.DeleteRequested += Remove;
        _canvas.CopyRequested += Copy;
        _canvas.PasteRequested += Paste;
        _canvas.DuplicateRequested += Duplicate;
        _canvas.ContextMenu = WidgetMenu();

        _list.SelectionChanged += (_, _) =>
        {
            if (_suspend) return;
            Select(_list.SelectedItem as LayoutWidget);
        };
        // Su propia instancia: un ContextMenu no se comparte entre dos controles.
        _list.ContextMenu = WidgetMenu();

        // El botón derecho sobre una fila no la selecciona por su cuenta, así que el menú actuaría
        // sobre el widget que estuviera seleccionado y no sobre el que se ha señalado. Se selecciona
        // a mano, antes de que el menú se abra: por eso va en la bajada y no en el burbujeo.
        _list.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(_list).Properties.IsRightButtonPressed) return;
            if (RowOf(e.Source as Visual) is { } row) _list.SelectedItem = row;
        }, RoutingStrategies.Tunnel);

        // Una pila y no la virtualizada de serie: el diseño separa las filas 2 px, y un tema no tiene
        // widgets suficientes para que virtualizar se note.
        _list.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Spacing = 2 });

        // El tipo en una columna fija y el identificador en monoespaciada: con quince widgets, los
        // tipos alineados se leen como una columna y el ojo encuentra «el arco» sin releerlo todo. El
        // color de los dos lo ponen sus clases, que cambian con la fila: mírese ControlThemes.
        _list.ItemTemplate = new FuncDataTemplate<LayoutWidget>((widget, _) =>
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("56,*"), ColumnSpacing = 10 };
            var id = AppFonts.Mono(new TextBlock
            {
                Text = widget.Id,
                Classes = { "witem-id" },
                FontSize = 13,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(id, 1);
            row.Children.Add(Loc.Set(new TextBlock
            {
                Classes = { "witem-kind" },
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            }, TextBlock.TextProperty, Describe(widget)));
            row.Children.Add(id);
            return row;
        }, supportsRecycling: true);

        _picker.SelectionChanged += (_, _) => ShowPickerHint();
        _picker.TextChanged += (_, _) => ShowPickerHint();

        _chooser.SelectionChanged += (_, _) =>
        {
            if (_suspend || _chooser.SelectedItem is not string chosen || chosen == _name) return;
            Run(() => ConfirmAndOpenAsync(chosen));
        };

        Content = Build();
        RefreshList();
        RefreshChooser();
        ShowFile();
        ShowDisplayProperties();

        // Desde el primer fotograma: el pie dice lo que el tema le pide al panel, y antes se quedaba
        // en blanco hasta que algo cambiaba. Con un tema recién abierto y sin tocar, eso es nunca.
        ShowBandwidth();
        Select(null);
        Render();
    }

    /// <summary>Los sensores que la vista previa necesita refrescados.</summary>
    public IReadOnlyList<string> SensorIds =>
        _widgets.Select(w => w.SensorId).OfType<string>().Distinct(StringComparer.Ordinal).ToList();

    public void OnStatus(ServiceStatus? status)
    {
        bool bandwidth = false;
        if (status?.ThroughputBytesPerSecond is > 0 and var throughput && throughput != _throughput)
        {
            _throughput = throughput;
            bandwidth = true;
        }

        if (StrictEnum.TryParse(status?.Features?.PixelFormat, out PixelFormat format) && format != _pixelFormat)
        {
            _pixelFormat = format;
            bandwidth = true;
        }

        if (bandwidth) ShowBandwidth();

        if (status is { Width: > 0, Height: > 0 } && StrictEnum.TryParse(status.Orientation, out PanelOrientation orientation))
            _panelShape = (status.Width, status.Height, orientation);

        // Antes de cualquier salida: hace falta saber siempre qué hay en el panel, no solo la primera
        // vez, para que borrar un tema sepa si le está quitando al panel lo que está pintando.
        _painting = LayoutPaths.NameOf(status?.LayoutPath);

        // La primera vez que se sabe qué está pintando el panel, se abre eso: quien entra al editor
        // casi siempre viene a retocar el layout que tiene delante, no a empezar uno en blanco.
        if (!_offered && _name is null && _widgets.Count == 0 && _painting is { } painting)
        {
            _offered = true;
            Open(painting);
            return;
        }

        FitBlankToPanel();
    }

    /// <summary>
    /// Le da a un tema en blanco y sin tocar la forma del panel conectado.
    /// </summary>
    /// <remarks>
    /// El editor arrancaba siempre en 480x320 apaisado, que es la Turing 3.5" de quien lo escribió: con
    /// otro panel, el primer tema salía de un tamaño que el servicio luego no quería pintar. Solo con
    /// uno sin tocar, porque cambiarle el tamaño a lo que alguien está haciendo no es ayudar.
    /// </remarks>
    private void FitBlankToPanel()
    {
        if (_panelShape is not { } shape || _name is not null || _widgets.Count > 0 || _dirty) return;

        var display = _layout.Display;
        if (display.Width == shape.Width && display.Height == shape.Height && display.Orientation == shape.Orientation)
            return;

        display.Width = shape.Width;
        display.Height = shape.Height;
        display.Orientation = shape.Orientation;
        _rebuild = true;
        ShowDisplayProperties();
        ShowBandwidth();
        Render();
    }

    /// <summary>Deja un sensor preparado en el buscador, desde el explorador.</summary>
    public void UseSensor(string id)
    {
        _picker.Text = id;
        ShowPickerHint();
    }

    /// <summary>
    /// Un latido de la ventana. La vista previa se mueve aunque no haya sensores que mirar: un tema
    /// de reloj y texto fijo se quedaría congelado si solo se repintara cuando llegan lecturas.
    /// </summary>
    public void Tick() => Render();

    /// <summary>
    /// Ha cambiado el idioma.
    /// </summary>
    /// <remarks>
    /// Los textos cambian solos. Lo que no: la vista previa, que lleva el reloj con los nombres de
    /// días y meses del idioma de antes, y la línea del buscador, que escribe la lectura con sus
    /// separadores.
    /// </remarks>
    public void LanguageChanged()
    {
        _rebuild = true;
        Render();
        ShowPickerHint();
    }

    /// <summary>Han llegado lecturas nuevas: la vista previa enseña lo que enseñaría el panel.</summary>
    public void SensorsChanged()
    {
        Render();
        ShowPickerHint();
        RefreshSensorList();
        ShowThemeWarnings();
    }

    private Control Build()
    {
        // ── la barra, en dos filas ─────────────────────────────────────────────
        //
        // La primera es lo que se le hace al tema entero -abrir, guardar, aplicar- y la segunda lo
        // que se le hace a la vista -zoom y rejilla-. Separadas porque no se mezclan nunca: nadie
        // cambia el zoom mientras decide si guardar.
        var bar = new Border
        {
            Name = "editor-bar",
            Background = Palette.SurfaceThinBrush,
            BorderBrush = Palette.LineBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(20, 14, 20, 12),
            Child = new StackPanel { Spacing = 12, Children = { ThemeRow(), ViewRow() } },
        };

        // ── el banco de trabajo ────────────────────────────────────────────────
        var stage = new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _canvas, _ruler },
        };

        var workbench = new Border
        {
            Background = Palette.WashBrush,
            Padding = new Thickness(28),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = stage,
            },
        };

        // ── la columna de inspección ───────────────────────────────────────────
        //
        // De arriba abajo, al lado de todo lo demás. Pantalla, Añadir y Widgets miden lo que piden y
        // Propiedades se queda con lo que sobra: su fila es elástica y va dentro de un ScrollViewer,
        // que coloca lo que lleva dentro con el mayor de los dos altos, el suyo y el que se ve. Así
        // llena el hueco cuando sobra sitio y crece cuando no, y entonces se desplaza la columna
        // entera. Con un solo desplegable abierto a la vez, casi siempre cabe.
        var column = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            RowSpacing = 10,

            // El relleno del diseño va por dentro de lo que se desplaza, como en el CSS.
            Margin = new Thickness(12),
        };
        Row(column, new InspectorPanel(Loc.L("editor.panel.display"), _display, InspectorPanel.BodyPadding, collapsible: true), 0);
        Row(column, _add, 1);
        Row(column, _widgetsPanel, 2);
        Row(column, _propsPanel, 3);

        var wired = Accordion.Only(column);
        var inspectorScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        // La superficie a medias y no maciza, que es lo que el diseño pide para esta columna: con el
        // fondo de la ventana asomando por detrás, las cajas de dentro -que sí van macizas- se leen
        // como encajadas en ella y no como parches pegados encima.
        var inspector = new Border
        {
            Name = "inspector",
            Width = 372,
            Background = Palette.SurfaceSoftBrush,
            BorderBrush = Palette.LineBrush,
            BorderThickness = new Thickness(1, 0, 0, 0),
            ClipToBounds = true,
        };

        // ── el pie: lo que el tema le va a pedir al panel ──────────────────────
        var cost = new Grid { ColumnDefinitions = new ColumnDefinitions("*,220"), ColumnSpacing = 16 };
        _bandwidthBar.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_bandwidthBar, 1);
        cost.Children.Add(_bandwidth);
        cost.Children.Add(_bandwidthBar);

        var foot = new Border
        {
            Name = "editor-foot",
            Background = Palette.SurfaceThinBrush,
            BorderBrush = Palette.LineBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 10),
            Child = new StackPanel { Spacing = 6, Children = { cost, _message } },
        };

        // ── cómo se reparte ────────────────────────────────────────────────────
        //
        // Con sitio, una rejilla de dos columnas: la barra, el banco y el pie a la izquierda, y la de
        // inspección a la derecha, de arriba abajo. Estrecho, una sola columna en el orden del diseño
        // -barra, banco, inspección y pie- que se desplaza entera: ahí la de inspección no tiene alto
        // propio, y metida en su ScrollViewer quedaría un desplazamiento dentro de otro.
        Grid.SetRow(workbench, 1);
        Grid.SetRow(foot, 2);
        Grid.SetColumn(inspector, 1);
        Grid.SetRowSpan(inspector, 3);

        var wide = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
        };
        var single = new StackPanel();
        var singleScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = single,
        };
        var host = new Border();

        bool? alone = null;
        bool slim = false;

        void Place(bool narrow)
        {
            if (alone == narrow) return;
            alone = narrow;

            // Fuera de donde estuvieran antes de entrar en el otro sitio: un control no puede tener
            // dos padres, y Avalonia lo dice con una excepción.
            wide.Children.Clear();
            single.Children.Clear();
            inspectorScroll.Content = null;
            inspector.Child = null;

            if (narrow)
            {
                workbench.Padding = new Thickness(20);
                inspector.Width = double.NaN;
                inspector.BorderThickness = new Thickness(0, 1, 0, 0);
                inspector.Child = wired;
                single.Children.AddRange([bar, workbench, inspector, foot]);
                host.Child = singleScroll;
            }
            else
            {
                workbench.Padding = new Thickness(28);
                inspector.Width = slim ? 330 : 372;
                inspector.BorderThickness = new Thickness(1, 0, 0, 0);
                inspectorScroll.Content = wired;
                inspector.Child = inspectorScroll;
                wide.Children.AddRange([bar, workbench, foot, inspector]);
                host.Child = wide;
            }
        }

        void Narrower(bool value)
        {
            slim = value;
            if (alone == false) inspector.Width = slim ? 330 : 372;
        }

        // Los puntos de ruptura del diseño son de ventana, y aquí se mide el editor, como en Grid12:
        // sus 1280 y 1024 px son 1048 y 792 de editor, la ventana menos los 232 del rail. Por debajo
        // del primero la columna de inspección adelgaza; por debajo del segundo, todo a una columna.
        Place(false);
        Reflow.OnWidth(host, 1048, Narrower);
        Reflow.OnWidth(host, 792, Place);

        // La lista de widgets crece con la ventana, cada vez que el editor cambia de tamaño, que es
        // cuando ha podido cambiar ella.
        host.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.BoundsProperty) FitWidgetList();
        };

        return Blocks.Ui(host);
    }

    /// <summary>
    /// La fila del tema: cuál es, su fichero, si está guardado, y mandarlo al panel o tirarlo.
    /// </summary>
    /// <remarks>
    /// En grupos que saltan enteros, porque con la columna de inspección a todo el alto esta barra
    /// tiene el ancho justo: el campo del tema crece hasta 240 si sobra sitio y encoge hasta 120 si
    /// falta, y lo demás baja de línea sin partirse.
    /// </remarks>
    private Control ThemeRow()
    {
        var theme = Blocks.Field(Loc.L("editor.theme"), _chooser);
        theme.Name = "bar-theme";
        theme.MinWidth = 120;
        theme.MaxWidth = 240;
        BarRow.SetBasis(theme, 160);

        var file = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                Tool(Icons.Plus, Loc.L("editor.tool.new"), () => Run(NewThemeAsync)),
                Tool(Icons.Save, Loc.L("editor.tool.save"), () => Run(SaveAsync)),
                Tool(Icons.Import, Loc.L("editor.tool.import"), () => Run(ImportAsync)),
            },
        };
        Loc.Set(file, AutomationProperties.NameProperty, Loc.L("editor.tool.group"));

        // Al otro canto a propósito: «Eliminar» es lo único que no se puede deshacer, y no tiene que
        // quedar pegado a «Guardar». Detrás de «Aplicar al panel» y de una raya.
        var right = new StackPanel
        {
            Name = "bar-right",
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                Blocks.Action(Loc.L("editor.apply"), Icons.Send, () => Run(ApplyAsync), "primary"),
                Separator(),
                _delete,
            },
        };
        BarRow.SetPush(right, true);

        return new BarRow { Gap = 12, RowGap = 10, Children = { theme, file, _saveState, right } };
    }

    /// <summary>La fila de la vista: con cuánto aumento se mira el lienzo, y su rejilla.</summary>
    private Control ViewRow()
    {
        var zoom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { Blocks.FieldLabel(Loc.L("editor.zoom")), Zoom() },
        };

        // El gap de 16 de .editor-bar-2 va detrás del de la fila de arriba y pisa también su row-gap
        // de 10: el atajo pone los dos huecos a la vez.
        return new BarRow
        {
            Gap = 16,
            RowGap = 16,
            Align = VerticalAlignment.Center,
            Children = { zoom, Separator(), GridGroup(), Separator(), Snap() },
        };
    }

    /// <summary>Lo de dar de alta un widget: el buscador de sensor y los seis tipos.</summary>
    private Control AddPanel()
    {
        var withSensor = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        withSensor.Children.Add(Add(Loc.L("widget.kind.text"), w => Text(w)));
        withSensor.Children.Add(Add(Loc.L("widget.kind.bar"), w => Bar(w)));
        withSensor.Children.Add(Add(Loc.L("widget.kind.gauge"), w => Gauge(w)));
        withSensor.Children.Add(Add(Loc.L("widget.kind.chart"), w => Chart(w)));

        var without = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        without.Children.Add(Small(Loc.L("widget.kind.fixed-text"), () => Insert(FixedText())));
        without.Children.Add(Small(Loc.L("editor.add.clock"), () => Insert(Clock())));
        without.Children.Add(Small(Loc.L("editor.add.image"), () => Run(AddImageAsync)));

        // Como el cuerpo de una caja del diseño: el buscador y su ayuda, a 8, son un campo; y de un
        // grupo al siguiente, el hueco del cuerpo.
        return new StackPanel
        {
            Spacing = InspectorPanel.BodySpacing,
            Children =
            {
                new StackPanel { Spacing = 8, Children = { _picker, _pickerHint } },
                withSensor,
                Blocks.Help(Loc.L("editor.add.without-sensor")),
                without,
            },
        };
    }

    private static Button Small(Text text, Action click) => Blocks.Action(text, null, click, "sm");

    /// <summary>
    /// Un botón de la barra con solo el icono, y lo que hace en la etiqueta flotante y para el lector
    /// de pantalla, que sin texto a la vista son los dos únicos sitios donde se dice.
    /// </summary>
    private static Button Tool(StreamGeometry icon, Text name, Action click)
    {
        var button = new Button
        {
            Classes = { "icon" },
            Content = Icons.Make(icon, Palette.TextBrush, 17),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Loc.Set(button, ToolTip.TipProperty, name);
        Loc.Set(button, AutomationProperties.NameProperty, name);
        return button.WithClick(click);
    }

    private static Control Separator() => new Border
    {
        Width = 1,
        Height = 20,
        Background = Palette.LineStrongBrush,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static void Row(Grid grid, Control control, int row)
    {
        Grid.SetRow(control, row);
        grid.Children.Add(control);
    }

    private Button Add(Text text, Func<string, LayoutWidget> build) => Small(text, () =>
    {
        if (SelectedSensor() is not { } sensor)
        {
            ShowMessage(Loc.L("editor.pick-sensor-first"));
            return;
        }
        Insert(build(sensor));
    });

    /// <summary>
    /// Los aumentos de la vista previa: el tamaño real, uno intermedio y el doble.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Antes se llegaba hasta ocho y sobraban: pasado el doble, un panel apaisado ya no cabe en la
    /// ventana y editar se convierte en arrastrar la vista previa de un lado a otro para ver dónde
    /// se está poniendo el widget.
    /// </para>
    /// <para>
    /// El pequeño del diseño y de ancho fijo: un desplegable de la web mide lo que su opción más
    /// ancha, y uno de Avalonia lo que la elegida, así que sin ancho la barra se movería al elegir.
    /// </para>
    /// </remarks>
    private Control Zoom()
    {
        double[] zooms = [1, 1.5, 2];

        var box = new ComboBox { Name = "zoom", Width = 70, Classes = { "sm" }, VerticalAlignment = VerticalAlignment.Center };
        foreach (double zoom in zooms)
            box.Items.Add(zoom.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x");

        // Se marca el que ya tiene el lienzo, para que la casilla y el dibujo no digan cosas distintas.
        box.SelectedIndex = Math.Max(Array.IndexOf(zooms, _canvas.Zoom), 0);
        box.SelectionChanged += (_, _) =>
        {
            _canvas.Zoom = zooms[box.SelectedIndex];
            _canvas.InvalidateMeasure();
            _canvas.InvalidateVisual();
        };
        return box;
    }

    /// <summary>La rejilla en un solo grupo: encenderla, y cada cuántos píxeles.</summary>
    private Control GridGroup()
    {
        var box = Loc.Set(new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center }, ContentControl.ContentProperty, Loc.L("editor.grid"));
        box.IsCheckedChanged += (_, _) =>
        {
            _canvas.ShowGrid = box.IsChecked == true;
            _canvas.InvalidateVisual();
        };

        // Con sus flechas y del ancho justo para ellas, como el resto de contadores de la ventana. El
        // prototipo lo dibuja como los estrechos de la caja —66 px y sin flechas—, y así se puso al
        // principio, pero al lado de los demás era el único al que le faltaban: se aparta del diseño
        // a propósito.
        var step = Forms.Number(1, 64);
        step.Value = 8;
        step.Width = 120;
        step.VerticalAlignment = VerticalAlignment.Center;
        step.ValueChanged += (_, _) =>
        {
            _canvas.GridStep = (int)(step.Value ?? 8);
            _canvas.InvalidateVisual();
        };

        var unit = Blocks.Help(Loc.Raw("px"));
        unit.VerticalAlignment = VerticalAlignment.Center;

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { box, Blocks.FieldLabel(Loc.L("editor.grid.every")), step, unit },
        };
    }

    private Control Snap()
    {
        var box = Loc.Set(new CheckBox
        {
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
        }, ContentControl.ContentProperty, Loc.L("editor.snap"));
        box.IsCheckedChanged += (_, _) => _canvas.Snap = box.IsChecked == true;
        return box;
    }

    /// <summary>
    /// Le pone a la lista de widgets el alto máximo del diseño: <c>clamp(160px, 24vh, 320px)</c>.
    /// </summary>
    /// <remarks>
    /// El vh es la ventana entera —en el prototipo, <c>.win</c> mide <c>100dvh</c>— y no el editor,
    /// así que se le pregunta a ella. Con 900 px de alto salen 216, relleno incluido: unas cinco filas
    /// y media, donde antes cabían tres.
    /// </remarks>
    private void FitWidgetList()
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        _list.MaxHeight = Math.Clamp(top.ClientSize.Height * 0.24, 160, 320);
    }

    // ---- documento ----

    private static LayoutDocument NewLayout(int width, int height, PanelOrientation orientation) => new()
    {
        Schema = LayoutLoader.CurrentSchema,
        Meta = new LayoutMeta { Name = Loc.S("editor.new-layout-name") },
        Display = new LayoutDisplay { Width = width, Height = height, Orientation = orientation },
        Background = new ColorBackground { Color = "#0C0E14" },
        Widgets = [],
    };

    /// <summary>
    /// Lanza una acción del editor sin dejar que un fallo se lleve la ventana.
    /// </summary>
    /// <remarks>
    /// Los manejadores de la barra son «async void» por fuerza: nadie espera a un clic. Lo que se
    /// escape de ahí es una excepción sin observar en el hilo de interfaz, y eso cierra la
    /// aplicación —que vive en la bandeja— sin una sola palabra de explicación.
    /// </remarks>
    private async void Run(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception e)
        {
            ShowMessage(Loc.L("editor.failed", Loc.Notice(Notice.From(e))));
            _message.Foreground = Palette.BadBrush;
        }
    }

    /// <summary>Si se puede tirar lo que hay en el editor, preguntándolo cuando hay algo que perder.</summary>
    private async Task<bool> CanDiscardAsync(Text what) =>
        !_dirty || await Forms.ConfirmAsync(this, what,
            _name is null
                ? Loc.L("editor.discard.never-saved")
                : Loc.L("editor.discard.unsaved", _name),
            Loc.L("editor.discard.accept")).ConfigureAwait(true);

    private async Task NewThemeAsync()
    {
        if (!await CanDiscardAsync(Loc.L("editor.new.title")).ConfigureAwait(true)) return;

        Blank();
        ShowMessage(Loc.Empty);
    }

    /// <summary>
    /// Deja el editor con un tema vacío del tamaño del panel conectado o, sin panel, del que había. No
    /// pregunta nada.
    /// </summary>
    private void Blank()
    {
        var (width, height, orientation) = _panelShape
            ?? (_layout.Display.Width, _layout.Display.Height, _layout.Display.Orientation);
        _layout = NewLayout(width, height, orientation);
        _widgets = [];
        _layout.Widgets = _widgets;
        _name = null;
        _canvas.Layout = _layout;
        _canvas.Selected = null;
        _properties.Content = NothingSelected();
        _editing = null;
        AfterStructureChange();

        // AfterStructureChange pasa por Touch: un tema recién vaciado no tiene cambios que perder.
        _dirty = false;
        ShowFile();
        RefreshChooser();
    }

    /// <summary>
    /// Abre uno de los guardados, preguntando antes si hay algo que perder.
    /// </summary>
    /// <remarks>
    /// No es la versión asíncrona de <see cref="Open"/>, que es síncrono y sigue haciendo el trabajo:
    /// lo que esto añade es la pregunta. De ahí el nombre, que antes era <c>OpenAsync</c> y hacía
    /// creer lo contrario.
    /// </remarks>
    private async Task ConfirmAndOpenAsync(string name)
    {
        if (!await CanDiscardAsync(Loc.L("editor.open.title", name)).ConfigureAwait(true))
        {
            RefreshChooser(); // el desplegable se había movido; vuelve a decir lo que hay abierto
            return;
        }

        Open(name);
    }

    private void Open(string name)
    {
        try
        {
            _layout = LayoutLoader.Load(LayoutPaths.For(name));
        }
        catch (LayoutException e)
        {
            ShowProblems(Loc.L("editor.open.failed", name), e);
            RefreshChooser();
            return;
        }

        _widgets = [.. _layout.Widgets];
        _layout.Widgets = _widgets;
        _name = name;
        _canvas.Layout = _layout;
        _canvas.Selected = null;
        _properties.Content = NothingSelected();
        _editing = null;
        AfterStructureChange();

        // AfterStructureChange pasa por Touch, que marca el layout como tocado: acabar de abrirlo no
        // cuenta, y la marca de «sin guardar» dejaría de significar nada si saliera siempre.
        _dirty = false;
        ShowFile();
        RefreshChooser();
        ShowMessage(Loc.Empty);
    }

    /// <summary>
    /// Borra el tema abierto: su carpeta entera, con la imagen dentro.
    /// </summary>
    /// <remarks>
    /// No se pregunta por los cambios sin guardar. Borrar el tema es tirarlo, y sus cambios van
    /// dentro: preguntar dos veces por lo mismo solo enseña a no leer los diálogos.
    /// </remarks>
    private async Task DeleteThemeAsync()
    {
        if (_name is not { } name) return;

        bool onPanel = string.Equals(name, _painting, StringComparison.Ordinal);
        bool last = LayoutPaths.All().Count <= 1;

        var question = last
            ? Loc.L("editor.delete.last")
            : onPanel
                ? Loc.L("editor.delete.on-panel")
                : Loc.L("editor.delete.folder");

        if (!await Forms.ConfirmAsync(this, Loc.L("editor.delete.title", name),
                Loc.L("editor.delete.confirm", question),
                Loc.L("editor.delete.accept")).ConfigureAwait(true))
            return;

        if (!LayoutPaths.Delete(name))
        {
            // Alguien lo ha quitado por detrás entre mirar y borrar. No es una avería: el resultado
            // es el que se pedía, así que se sigue como si lo hubiéramos borrado nosotros.
            ShowMessage(Loc.L("editor.delete.gone", name));
        }

        if (LayoutPaths.All().FirstOrDefault() is { } next)
        {
            Open(next);
            if (!onPanel)
            {
                ShowMessage(Loc.L("editor.delete.done", name));
                _message.Foreground = Palette.GoodBrush;
                return;
            }

            // Recuperar no es elegir: con «chosen: false» el sorteo se queda como estaba, igual que
            // al aplicar desde el editor.
            var error = await _service.SetLayoutAsync(LayoutPaths.For(next), chosen: false).ConfigureAwait(true);
            ShowMessage(error ?? Loc.L("editor.delete.done-moved", name, next));
            _message.Foreground = error is null ? Palette.GoodBrush : Palette.BadBrush;
            return;
        }

        // No queda ninguno. El editor se va a uno nuevo sin preguntar, porque el que había ya no
        // existe y no hay nada que perder.
        Blank();
        ShowMessage(onPanel
            ? Loc.L("editor.delete.done-none-on-panel", name)
            : Loc.L("editor.delete.done-none", name));
        _message.Foreground = Palette.WarnBrush;
    }

    /// <summary>
    /// Trae un layout de fuera: el único sitio donde queda un diálogo de fichero.
    /// </summary>
    /// <remarks>
    /// Para lo que alguien te pase o lo que tengas de antes. Se carga, se le pide nombre y se guarda
    /// como uno más —con su imagen de fondo copiada dentro—, así que a partir de ahí es indistinguible
    /// de los que se hicieron aquí. Elegir de dónde <b>leer</b> no es elegir dónde guardar.
    /// </remarks>
    private async Task ImportAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.S("editor.import.title"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(Loc.S("editor.import.file-type")) { Patterns = ["*.json"] }],
        });

        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;

        LayoutDocument imported;
        try
        {
            imported = LayoutLoader.Load(path);
        }
        catch (LayoutException e)
        {
            ShowProblems(Loc.L("editor.import.failed-file", Path.GetFileName(path)), e);
            return;
        }

        string suggested = imported.Meta?.Name ?? Path.GetFileNameWithoutExtension(path);
        if (await AskLayoutNameAsync(Loc.L("editor.import.title"), suggested).ConfigureAwait(true) is not { } name) return;

        try
        {
            LayoutLoader.Save(imported, LayoutPaths.For(name), name);
        }
        catch (LayoutException e)
        {
            ShowProblems(Loc.L("editor.import.failed"), e);
            return;
        }

        Open(name);
        ShowMessage(Loc.L("editor.import.done", name));
        _message.Foreground = Palette.GoodBrush;
    }

    /// <summary>Pide un nombre libre. Solo se llega aquí con un layout nuevo, así que cualquiera que
    /// exista ya está cogido: guardar no puede llevarse por delante el de otro.</summary>
    private Task<string?> AskLayoutNameAsync(Text title, string initial) =>
        Forms.AskNameAsync(this, title, Loc.L("editor.name.question"), initial,
            candidate => LayoutPaths.NameProblem(candidate) is { } problem ? Loc.Notice(problem)
                : LayoutPaths.Exists(candidate) ? Loc.L("editor.name.taken", candidate)
                : (Text?)null);

    /// <summary>Vuelve a leer la lista de layouts y deja marcado el que está abierto.</summary>
    private void RefreshChooser()
    {
        _suspend = true;
        try
        {
            _chooser.ItemsSource = LayoutPaths.All();
            _chooser.SelectedItem = _name;
            Loc.Set(_chooser, ComboBox.PlaceholderTextProperty, _name is null ? Loc.L("editor.chooser.unsaved") : Loc.Empty);
        }
        finally
        {
            _suspend = false;
        }
    }

    /// <summary>
    /// Guarda el layout: si es nuevo pregunta el nombre, y si ya existe lo actualiza y ya está.
    /// </summary>
    /// <remarks>
    /// Un solo botón, sin «guardar como». El usuario pone un nombre la primera vez y a partir de ahí
    /// guardar es guardar; dónde va la carpeta, cómo se llama el fichero y dónde acaba la imagen de
    /// fondo lo decide el programa. Dejarle elegir la ubicación era una fuente de layouts sueltos por
    /// el disco, con fondos que se pierden en cuanto se mueve la foto original.
    /// </remarks>
    private async Task<bool> SaveAsync()
    {
        string? name = _name;
        if (name is null)
        {
            string initial = _layout.Meta?.Name ?? Loc.S("editor.default-name");
            if (await AskLayoutNameAsync(Loc.L("editor.save.title"), initial).ConfigureAwait(true) is not { } chosen)
                return false;

            name = chosen;
        }

        try
        {
            LayoutLoader.Save(_layout, LayoutPaths.For(name), name);
        }
        catch (LayoutException e)
        {
            ShowProblems(Loc.L("editor.save.failed"), e);
            return false;
        }

        bool wasNew = _name is null;
        _name = name;
        _dirty = false;
        ShowFile();
        RefreshChooser();

        if (wasNew)
        {
            ShowMessage(Loc.L("editor.save.done", name));
            _message.Foreground = Palette.GoodBrush;
        }
        else
        {
            ShowMessage(Loc.Empty);
        }

        return true;
    }

    private async Task ApplyAsync()
    {
        if (!await SaveAsync().ConfigureAwait(true)) return;

        // Si el servicio ya está pintando este mismo fichero, basta con que lo relea.
        // Aplicar es enseñar en qué estás trabajando, no elegir el layout de cada día: si el panel
        // está en aleatorio, sigue estándolo y este tema se queda hasta el próximo sorteo.
        var error = await _service.SetLayoutAsync(LayoutPaths.For(_name!), chosen: false).ConfigureAwait(true);
        ShowMessage(error ?? Loc.L("editor.apply.done", _name));
        _message.Foreground = error is null ? Palette.GoodBrush : Palette.BadBrush;
    }

    // ---- widgets ----

    private string? SelectedSensor()
    {
        string? id = (_picker.SelectedItem as string) ?? _picker.Text;
        return string.IsNullOrWhiteSpace(id) || _sensors.Read(id) is null ? null : id;
    }

    private SensorKind KindOf(string? sensor) => sensor is null ? SensorKind.Factor : _sensors.Read(sensor)?.Kind ?? SensorKind.Factor;

    private void Insert(LayoutWidget widget)
    {
        _widgets.Add(widget);
        AfterStructureChange();
        Select(widget);
    }

    private void Remove(LayoutWidget widget)
    {
        _widgets.Remove(widget);
        // Deseleccionar de verdad, y no solo en el lienzo: si el formulario se quedara con el widget
        // borrado, tocar una casilla cambiaría un objeto que ya no forma parte del tema.
        Select(null);
        // AfterStructureChange ya rehace el formulario de pantalla: llamarlo otra vez construía un
        // ColorPicker entero para tirarlo acto seguido.
        AfterStructureChange();
    }

    /// <summary>Se lo queda el portapapeles del editor, copiado ya.</summary>
    /// <remarks>
    /// Se copia <b>al copiar</b> y no al pegar: si se guardara el widget de verdad, seguir
    /// retocándolo cambiaría lo que hay copiado sin que nadie lo haya pedido.
    /// </remarks>
    private void Copy(LayoutWidget widget)
    {
        _clipboard = LayoutLoader.Copy(widget);
        ShowMessage(Loc.L("editor.copied", widget.Id));
        _message.Foreground = Palette.GoodBrush;
    }

    private void Paste()
    {
        if (_clipboard is not { } copied)
        {
            ShowMessage(Loc.L("editor.clipboard-empty"));
            return;
        }

        Insert(Clone(copied));
    }

    /// <summary>Copiar y pegar de una vez, sin pasar por el portapapeles para no pisar lo que hubiera.</summary>
    private void Duplicate(LayoutWidget widget) => Insert(Clone(widget));

    /// <summary>
    /// Una copia lista para entrar en el tema: identificador libre y apartada del original.
    /// </summary>
    /// <remarks>
    /// Se copia otra vez aunque venga del portapapeles, que ya era una copia: si no, pegar dos veces
    /// metería el mismo objeto dos veces y mover uno movería el otro.
    /// </remarks>
    private LayoutWidget Clone(LayoutWidget widget)
    {
        var copy = LayoutLoader.Copy(widget);
        copy.Id = UniqueId(copy.Id);

        // Desplazada una rejilla, para verla al lado del original y poder arrastrarla.
        var rect = copy.Rect;
        int step = Math.Max(1, _canvas.GridStep);
        var beside = new PixelRect(rect.X + step, rect.Y + step, rect.Width, rect.Height);

        if (beside.X + beside.Width <= _layout.Display.Width && beside.Y + beside.Height <= _layout.Display.Height)
        {
            copy.Rect = beside;
        }
        else
        {
            // Desplazada no cabe entera: al primer hueco libre, que es donde van los widgets nuevos.
            var (x, y) = FreeSpot(rect.Width, rect.Height);
            copy.Rect = new PixelRect(x, y, rect.Width, rect.Height);
        }

        return copy;
    }

    /// <summary>El widget de la fila que hay bajo ese control, subiendo hasta encontrarla.</summary>
    private static LayoutWidget? RowOf(Visual? from)
    {
        for (var at = from; at is not null; at = at.GetVisualParent())
            if (at is ListBoxItem { DataContext: LayoutWidget widget }) return widget;

        return null;
    }

    /// <summary>
    /// El menú del botón derecho sobre un widget. Uno por control: no se comparte entre dos.
    /// </summary>
    /// <remarks>
    /// Lo que se puede hacer se decide al abrirlo y no al construirlo, porque depende de si hay algo
    /// seleccionado y de si hay algo copiado, y las dos cosas cambian a cada rato.
    /// </remarks>
    private ContextMenu WidgetMenu()
    {
        var duplicate = new MenuItem();
        var copy = new MenuItem();
        var paste = new MenuItem();
        var remove = new MenuItem();

        duplicate.Click += (_, _) => { if (_canvas.Selected is { } w) Duplicate(w); };
        copy.Click += (_, _) => { if (_canvas.Selected is { } w) Copy(w); };
        paste.Click += (_, _) => Paste();
        remove.Click += (_, _) => { if (_canvas.Selected is { } w) Remove(w); };

        var menu = new ContextMenu
        {
            ItemsSource = new Control[] { duplicate, copy, paste, new Separator(), remove },
        };

        menu.Opening += (_, _) =>
        {
            // Los nombres se ponen al abrirlo y no enlazados: cerrado, el menú no está en ningún
            // árbol y no se entera de un cambio de idioma, y se quedaba con el de cuando se hizo.
            duplicate.Header = Loc.S("editor.menu.duplicate");
            copy.Header = Loc.S("editor.menu.copy");
            paste.Header = Loc.S("editor.menu.paste");
            remove.Header = Loc.S("editor.delete");

            bool something = _canvas.Selected is not null;
            duplicate.IsEnabled = something;
            copy.IsEnabled = something;
            remove.IsEnabled = something;
            paste.IsEnabled = _clipboard is not null;
        };

        return menu;
    }

    /// <summary>
    /// El primer hueco donde cabe lo nuevo sin pisar nada. Si el tema está lleno, arriba a la
    /// izquierda: encima de otro widget se ve mal, pero se ve, y de ahí se arrastra a donde toque.
    /// </summary>
    private (int X, int Y) FreeSpot(int width, int height)
    {
        const int Step = 8;
        for (int y = Step; y + height < _layout.Display.Height; y += Step)
        {
            for (int x = Step; x + width < _layout.Display.Width; x += Step)
            {
                var candidate = new PixelRect(x, y, width, height);
                if (!_widgets.Any(w => w.Rect.IntersectsWith(candidate))) return (x, y);
            }
        }
        return (Step, Step);
    }

    private TextWidget Text(string sensor)
    {
        var kind = KindOf(sensor);
        var (x, y) = FreeSpot(150, 30);
        return new TextWidget
        {
            Id = UniqueId(sensor),
            Rect = new PixelRect(x, y, 150, 30),
            Sensor = sensor,
            Decimals = Decimals(kind),
            // La unidad se escribe resuelta y no como «auto»: así, si el tema acaba en una máquina
            // sin ese sensor, el hueco sigue enseñando «-- °C» en vez de un «--» pelado.
            Unit = kind.NativeUnit(),
            Size = 22,
        };
    }

    private TextWidget FixedText()
    {
        var (x, y) = FreeSpot(120, 26);
        return new TextWidget
        {
            Id = UniqueId(Loc.S("editor.id.text")),
            Rect = new PixelRect(x, y, 120, 26),
            Text = Loc.S("editor.fixed-text.default"),
            Size = 18,
            Color = "#8AB4F8",
        };
    }

    /// <summary>Un reloj nuevo. Va con la hora a secas, que es lo que se pone el 90 % de las veces.</summary>
    private ClockWidget Clock()
    {
        var (x, y) = FreeSpot(120, 34);
        return new ClockWidget
        {
            Id = UniqueId(Loc.S("editor.id.clock")),
            Rect = new PixelRect(x, y, 120, 34),
            Size = 26,
        };
    }

    private BarWidget Bar(string sensor)
    {
        var kind = KindOf(sensor);
        var (x, y) = FreeSpot(180, 14);
        return new BarWidget
        {
            Id = UniqueId(sensor + "-" + Loc.S("editor.id.bar")),
            Rect = new PixelRect(x, y, 180, 14),
            Sensor = sensor,
            Unit = kind.NativeUnit(),
            Max = kind.Ceiling(),
            Background = "#1B2030",
            Radius = 7,
        };
    }

    private GaugeWidget Gauge(string sensor)
    {
        var kind = KindOf(sensor);
        var (x, y) = FreeSpot(90, 90);
        return new GaugeWidget
        {
            Id = UniqueId(sensor + "-" + Loc.S("editor.id.gauge")),
            Rect = new PixelRect(x, y, 90, 90),
            Sensor = sensor,
            Unit = kind.NativeUnit(),
            Max = kind.Ceiling(),
            Background = "#1B2030",
        };
    }

    private ChartWidget Chart(string sensor)
    {
        var kind = KindOf(sensor);
        var (x, y) = FreeSpot(200, 60);
        return new ChartWidget
        {
            Id = UniqueId(sensor + "-" + Loc.S("editor.id.chart")),
            Rect = new PixelRect(x, y, 200, 60),
            Sensor = sensor,
            Unit = kind.NativeUnit(),
            Max = kind.Ceiling(),
            Fill = "#16223A",
        };
    }

    /// <summary>
    /// Una imagen nueva. El fichero se elige al pulsar, como el del fondo, y sin fichero no se añade
    /// nada: un widget de imagen vacío solo sería un recuadro que no se puede guardar.
    /// </summary>
    private async Task AddImageAsync()
    {
        if (await Forms.PickImageAsync(this, Loc.L("editor.image.pick")).ConfigureAwait(true) is not { } path) return;

        if (WidgetImages.Probe(path) is not { } probe)
        {
            ShowMessage(Loc.L("editor.image.unreadable", Path.GetFileName(path), WidgetImages.MaxFrames, WidgetImages.MaxMegabytes));
            _message.Foreground = Palette.BadBrush;
            return;
        }

        Insert(Image(path, probe));
    }

    /// <summary>
    /// La imagen a su tamaño, o reducida hasta caber si es más grande que el panel. Si es animada, se
    /// mira al ritmo de su fotograma más corto, que es lo que hace falta para no saltarse ninguno.
    /// </summary>
    private ImageWidget Image(string path, ImageProbe probe)
    {
        const int Margin = 16;
        double scale = Math.Min(1, Math.Min(
            (double)(_layout.Display.Width - Margin) / probe.Width,
            (double)(_layout.Display.Height - Margin) / probe.Height));
        int width = Math.Max(1, (int)Math.Round(probe.Width * scale));
        int height = Math.Max(1, (int)Math.Round(probe.Height * scale));
        var (x, y) = FreeSpot(width, height);

        string name = CanonicalSensorIds.Slug(Path.GetFileNameWithoutExtension(path));
        return new ImageWidget
        {
            Id = UniqueId(name.Length > 0 ? name : Loc.S("editor.id.image")),
            Rect = new PixelRect(x, y, width, height),
            Path = path,
            RefreshMs = probe.Animated ? Math.Max(LayoutLoader.MinRefreshMs, probe.ShortestMs) : 1000,
        };
    }

    /// <summary>Un identificador legible y libre, a partir del sensor.</summary>
    private string UniqueId(string seed)
    {
        string root = CanonicalSensorIds.Slug(seed.Replace('/', '-'));
        if (_widgets.All(w => w.Id != root)) return root;

        for (int n = 2; ; n++)
        {
            string candidate = $"{root}-{n}";
            if (_widgets.All(w => w.Id != candidate)) return candidate;
        }
    }

    private static int Decimals(SensorKind kind) => kind switch
    {
        SensorKind.Temperature or SensorKind.Data => 1,
        SensorKind.Voltage => 3,
        SensorKind.Load or SensorKind.Control or SensorKind.Level or SensorKind.Clock or SensorKind.Fan => 0,
        _ => 1,
    };

    private static Text Describe(LayoutWidget widget) => widget switch
    {
        TextWidget { Sensor: null } => Loc.L("widget.kind.fixed-text"),
        TextWidget => Loc.L("widget.kind.text"),
        ClockWidget => Loc.L("widget.kind.clock"),
        BarWidget => Loc.L("widget.kind.bar"),
        GaugeWidget => Loc.L("widget.kind.gauge"),
        ChartWidget => Loc.L("widget.kind.chart"),
        ImageWidget => Loc.L("widget.kind.image"),
        _ => Loc.L("widget.kind.generic"),
    };

    // ---- selección y propiedades ----

    private void Select(LayoutWidget? widget)
    {
        _suspend = true;
        try
        {
            _list.SelectedItem = widget;
            _canvas.Selected = widget;
            _canvas.InvalidateVisual();
        }
        finally
        {
            _suspend = false;
        }

        // Sin nada seleccionado se viene a añadir, y con algo seleccionado a tocarlo: el desplegable
        // de añadir ocupa doscientos píxeles que las propiedades necesitan más que él.
        _add.IsExpanded = widget is null;
        ShowNotes();

        if (widget is null)
        {
            _editing = null;
            _properties.Content = NothingSelected();
            return;
        }

        _editing = new WidgetProperties(widget, _sensors, Changed, () => Remove(widget), IdProblem, _layout.Resolve);
        _properties.Content = _editing;
    }

    /// <summary>
    /// Lo que hace que un identificador de widget no sirva, o null si sirve.
    /// </summary>
    /// <remarks>
    /// Se comprueba al escribirlo y no al guardar. El cargador rechaza los repetidos, pero enterarse
    /// al pulsar «Guardar» —y encima entre los problemas de todo el tema— es la peor forma de
    /// contarlo. Es la misma idea que la comprobación del nombre del layout.
    /// </remarks>
    private Text? IdProblem(LayoutWidget widget, string candidate) =>
        _widgets.Any(other => !ReferenceEquals(other, widget) && other.Id == candidate)
            ? Loc.L("editor.widget-id.taken", candidate)
            : (Text?)null;

    private void ShowDisplayProperties()
    {
        // La medida de debajo del lienzo se pone aquí porque aquí es donde puede cambiar: es la única
        // pantalla donde se tocan el ancho, el alto y la orientación.
        _ruler.Text = $"{_layout.Display.Width} × {_layout.Display.Height} px";

        var form = new FormGrid();

        var name = new TextBox { Text = _layout.Meta?.Name ?? "" };
        name.TextChanged += (_, _) =>
        {
            _layout.Meta ??= new LayoutMeta();
            _layout.Meta.Name = string.IsNullOrWhiteSpace(name.Text) ? null : name.Text;
        };
        form.Add(Loc.L("editor.display.name"), name);

        var width = Forms.Number(16, 2048);
        var height = Forms.Number(16, 2048);
        width.Value = _layout.Display.Width;
        height.Value = _layout.Display.Height;

        var orientation = Forms.Enum(_layout.Display.Orientation, Labels.Orientation, chosen =>
        {
            if (chosen == _layout.Display.Orientation) return;

            // Al pasar de vertical a apaisado el panel es el mismo: lo que cambia es de qué lado se
            // mira, así que el tema cambia de forma con él.
            if (chosen.IsRotated() != _layout.Display.Orientation.IsRotated())
            {
                (_layout.Display.Width, _layout.Display.Height) = (_layout.Display.Height, _layout.Display.Width);
                width.Value = _layout.Display.Width;
                height.Value = _layout.Display.Height;
            }

            _layout.Display.Orientation = chosen;
            AfterDisplayResize();
        });

        width.ValueChanged += (_, _) =>
        {
            _layout.Display.Width = (int)(width.Value ?? 480);
            AfterDisplayResize();
        };
        height.ValueChanged += (_, _) =>
        {
            _layout.Display.Height = (int)(height.Value ?? 320);
            AfterDisplayResize();
        };

        form.Add(Loc.L("editor.display.orientation"), orientation);
        form.Add(Loc.L("editor.display.width"), width);
        form.Add(Loc.L("editor.display.height"), height);
        form.AddWide(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 4),
            Children = { Forms.Action(Loc.L("editor.display.fit"), FitWidgetsNow) },
        });

        form.Add(Loc.L("editor.display.background"), Forms.Enum(
            _layout.Background is ImageBackground ? BackgroundKind.Image : BackgroundKind.Color,
            Labels.BackgroundKind,
            SetBackgroundKind));

        switch (_layout.Background)
        {
            case ColorBackground background:
                form.Add(Loc.L("editor.display.color"), WidgetProperties.ColorField(background.Color, color =>
                {
                    background.Color = color ?? "#000000";
                    Changed();
                }, allowNone: false));
                break;

            case ImageBackground image:
                form.Add(Loc.L("editor.display.image"), new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        Forms.Action(Loc.L("editor.display.image.choose"), PickBackgroundImage),
                        Loc.Set(new TextBlock
                        {
                            Foreground = image.Path.Length == 0 ? Palette.WarnBrush : Palette.MutedBrush,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        }, TextBlock.TextProperty, image.Path.Length == 0 ? Loc.L("editor.display.image.none") : Loc.Raw(Path.GetFileName(image.Path))),
                    },
                });

                form.Add(Loc.L("editor.display.fit-mode"), Forms.Enum(image.Fit, Labels.ImageFit, fit =>
                {
                    image.Fit = fit;
                    Changed();
                }));

                form.Add(Loc.L("editor.display.fallback"), WidgetProperties.ColorField(image.Color, color =>
                {
                    image.Color = color ?? "#000000";
                    Changed();
                }, allowNone: false));
                break;
        }

        _display.Content = form;
    }

    /// <summary>Cambia entre fondo de color y fondo de imagen conservando el color que hubiera.</summary>
    private void SetBackgroundKind(BackgroundKind kind)
    {
        bool isImage = _layout.Background is ImageBackground;
        if (isImage == (kind == BackgroundKind.Image)) return;

        string color = _layout.Background switch
        {
            ColorBackground existing => existing.Color,
            ImageBackground existing => existing.Color,
            _ => "#000000",
        };

        _layout.Background = kind == BackgroundKind.Image
            ? new ImageBackground { Path = "", Color = color }
            : new ColorBackground { Color = color };

        Changed();
        ShowDisplayProperties();

        // Elegir «una imagen» y que no pase nada sería raro: se abre el diálogo en el momento.
        if (kind == BackgroundKind.Image) PickBackgroundImage();
    }

    private void PickBackgroundImage() => Run(PickBackgroundImageAsync);

    private async Task PickBackgroundImageAsync()
    {
        if (_layout.Background is not ImageBackground image) return;
        if (await Forms.PickImageAsync(this, Loc.L("editor.display.image.pick")).ConfigureAwait(true) is not { } path) return;

        image.Path = path;
        Changed();
        ShowDisplayProperties();
    }

    /// <summary>
    /// El panel ha cambiado de forma.
    /// </summary>
    /// <remarks>
    /// Aquí <b>no</b> se toca ninguna caja. Recolocarlas solas parecía buena idea hasta que se ve
    /// escribir: las casillas de tamaño aplican también lo que se teclea a medias, así que poner
    /// «320» pasa antes por un panel de 16 px y dejaría los quince widgets aplastados sin vuelta
    /// atrás. Lo que se sale se avisa —lo dice <c>LayoutInspector</c>— y encajarlo es un botón que
    /// pulsa quien quiera.
    /// </remarks>
    private void AfterDisplayResize()
    {
        Changed();
        _editing?.SyncRect();
    }

    /// <summary>Mete todas las cajas dentro del panel, a petición. Devuelve cuántas ha movido.</summary>
    private int FitWidgets()
    {
        int panelWidth = _layout.Display.Width;
        int panelHeight = _layout.Display.Height;
        int moved = 0;

        foreach (var widget in _widgets)
        {
            var rect = widget.Rect;
            int w = Math.Clamp(rect.Width, 1, panelWidth);
            int h = Math.Clamp(rect.Height, 1, panelHeight);
            var fitted = new PixelRect(
                Math.Clamp(rect.X, 0, panelWidth - w),
                Math.Clamp(rect.Y, 0, panelHeight - h),
                w, h);

            if (fitted == rect) continue;
            widget.Rect = fitted;
            moved++;
        }

        return moved;
    }

    private void FitWidgetsNow()
    {
        int moved = FitWidgets();
        Changed();
        _editing?.SyncRect();

        ShowMessage(moved == 0
            ? Loc.L("editor.fit.none")
            : Loc.Plural("editor.fit.moved", moved));
    }

    /// <summary>Algo del tema ha cambiado y hay que rehacer el renderizador.</summary>
    private void Changed()
    {
        _rebuild = true;
        Touch();
        Render();
        RefreshList();
        ShowBandwidth();
    }

    private void AfterStructureChange()
    {
        _layout.Widgets = _widgets;
        _rebuild = true;
        Touch();
        RefreshList();
        ShowDisplayProperties();
        ShowBandwidth();
        Select(null);
        Render();
    }

    /// <summary>
    /// La lista de widgets, rehecha solo si lo que dice ha cambiado.
    /// </summary>
    /// <remarks>
    /// Se llama desde cada cambio de propiedad, o sea con cada tecla y cada movimiento del selector
    /// de color, pero el texto de las filas solo depende del tipo y del id. Rehacerla siempre tiraba
    /// el desplazamiento: con veinte widgets, arrastrar un color mandaba la lista arriba del todo.
    /// </remarks>
    private void RefreshList()
    {
        var labels = _widgets.Select(w => $"{w.GetType().Name}  ·  {(w is TextWidget { Sensor: null } ? "fixed" : "")}  ·  {w.Id}").ToList();
        if (labels.SequenceEqual(_shownLabels, StringComparer.Ordinal)) return;
        _shownLabels = labels;

        // Las notas cambian con lo mismo que las filas: cuántos hay, y el identificador del elegido,
        // que se renombra desde sus propiedades.
        ShowNotes();

        _suspend = true;
        try
        {
            var selected = _list.SelectedItem;
            _list.ItemsSource = null;
            _list.ItemsSource = _widgets.ToList();
            _list.SelectedItem = selected;
        }
        finally
        {
            _suspend = false;
        }
    }

    /// <summary>
    /// Lo que se lee a la derecha del título de las dos cajas fijas: cuántos widgets tiene el tema y
    /// cuál se está tocando, o una raya si ninguno.
    /// </summary>
    private void ShowNotes()
    {
        _widgetsPanel.Note = _widgets.Count.ToString(Loc.Culture);
        _propsPanel.Note = _canvas.Selected?.Id ?? "—";
    }

    /// <summary>
    /// La lista del buscador, rehecha solo cuando cambia el conjunto de sensores.
    /// </summary>
    /// <remarks>
    /// Se compara el contenido y no solo cuántos hay: un sensor que aparece y otro que desaparece en
    /// el mismo sondeo dejan la cuenta igual, y el buscador se quedaba ofreciendo uno que ya no está.
    /// </remarks>
    private void RefreshSensorList()
    {
        var ids = _sensors.ReadAll().Select(s => s.Id).ToList();
        if (_picker.ItemsSource is IEnumerable<string> current && current.SequenceEqual(ids, StringComparer.Ordinal)) return;
        _picker.ItemsSource = ids;
    }

    private void ShowPickerHint()
    {
        if (SelectedSensor() is not { } id)
        {
            Loc.Set(_pickerHint, TextBlock.TextProperty, _sensors.ReadAll().Count == 0
                ? Loc.L("editor.picker.no-service")
                : Loc.L("editor.picker.hint"));
            return;
        }

        var reading = _sensors.Read(id)!;
        string value = reading.Value is { } v
            ? v.ToString("0.###", Loc.Culture) + " " + reading.Unit
            : TextComposer.MissingValue;
        Loc.Set(_pickerHint, TextBlock.TextProperty, Loc.Raw($"{reading.Native.Name} · {value} · {reading.Native.HardwareName}"));
    }

    // ---- vista previa ----

    /// <summary>
    /// Actualiza la vista previa. Solo se sube un fotograma nuevo cuando hay algo distinto que
    /// enseñar: repintar la ventana una vez por segundo para dejarla igual se nota.
    /// </summary>
    private void Render()
    {
        if (_rebuild || _renderer is null)
        {
            _renderer?.Dispose();
            _renderer = null;
            try
            {
                _renderer = new LayoutRenderer(_layout, language: Loc.Current);
            }
            catch (Exception e) when (e is NotSupportedException or FormatException or ArgumentException)
            {
                ShowMessage(Loc.L("editor.render-failed", e.Message));
                return;
            }

            _rebuild = false;
            _renderer.RenderAll(_sensors, _clock.Elapsed);
            _canvas.SetFrame(_renderer.Surface.Bitmap);
            ShowThemeWarnings();
            return;
        }

        // El mismo mecanismo que usa el panel: se refrescan los widgets a los que les toca y, si
        // ninguno cambia de aspecto, no hay nada que subir.
        if (_renderer.Update(_sensors, _clock.Elapsed).Count == 0) return;
        _canvas.SetFrame(_renderer.Surface.Bitmap);
    }

    /// <summary>Repinta la vista previa entera, la haya cambiado quien la haya cambiado.</summary>
    private void Repaint()
    {
        if (_renderer is null || _rebuild)
        {
            Render();
            return;
        }

        _renderer.RenderAll(_sensors, _clock.Elapsed);
        _canvas.SetFrame(_renderer.Surface.Bitmap);
    }

    /// <summary>Lo que el tema pide y esta máquina no puede dar: se ve al lado de la vista previa.</summary>
    private void ShowThemeWarnings()
    {
        // Lo que el usuario acaba de provocar manda sobre los avisos del tema. Antes no: «Elige antes
        // un sensor de la lista» lo borraba esto en menos de un segundo, y parecía que el botón
        // estuviera roto.
        if (_ownMessage) return;

        var warnings = LayoutInspector.Warnings(_layout, _sensors, FileThere, ImageThere);
        if (warnings.Count == 0)
        {
            if (_message.Foreground == Palette.WarnBrush) Loc.Set(_message, TextBlock.TextProperty, Loc.Empty);
            return;
        }

        Loc.Set(_message, TextBlock.TextProperty, Loc.Notices(warnings));
        _message.Foreground = Palette.WarnBrush;
    }

    /// <summary>Si el fichero está, preguntándoselo al disco como mucho una vez por segundo.</summary>
    private bool FileThere(string path)
    {
        var now = _clock.Elapsed;
        if (_fileMemo.TryGetValue(path, out var seen) && now - seen.At < TimeSpan.FromSeconds(1))
            return seen.There;

        bool there = File.Exists(path);
        _fileMemo[path] = (there, now);
        return there;
    }

    /// <summary>
    /// Cómo es una imagen de widget, preguntándoselo al disco como mucho una vez por segundo: por
    /// lo mismo que <see cref="FileThere"/>, que esto también se recalcula con cada tecla.
    /// </summary>
    private ImageProbe? ImageThere(string path)
    {
        var now = _clock.Elapsed;
        if (_probeMemo.TryGetValue(path, out var seen) && now - seen.At < TimeSpan.FromSeconds(1))
            return seen.Probe;

        var probe = WidgetImages.Probe(path);
        _probeMemo[path] = (probe, now);
        return probe;
    }

    /// <summary>Lo que se lee en la columna cuando no hay ningún widget elegido.</summary>
    /// <remarks>
    /// Antes se quedaba en blanco, y una columna vacía debajo de una lista llena parece que algo se
    /// ha roto. Decir qué hacer cuesta una línea.
    /// </remarks>
    private static Control NothingSelected() =>
        Blocks.Help(Loc.L("editor.nothing-selected"));

    private void ShowBandwidth()
    {
        var capabilities = new DisplayCapabilities
        {
            Model = "panel",
            Revision = HardwareRevision.Unknown,
            NativeWidth = _layout.Display.Width,
            NativeHeight = _layout.Display.Height,
            ThroughputBytesPerSecond = _throughput,
            PixelFormat = _pixelFormat,
        };

        var report = BandwidthBudget.Analyze(LayoutInspector.Consumers(_layout, ImageThere), capabilities);
        var worst = report.Consumers.Count > 0
            ? Loc.L("editor.bandwidth.worst", report.Consumers[0].WidgetId, report.Consumers[0].BytesPerSecond)
            : Loc.Empty;

        Loc.Set(_bandwidth, TextBlock.TextProperty, Loc.L("editor.bandwidth",
            report.RequiredBytesPerSecond, report.BudgetBytesPerSecond, report.Utilization, worst));
        Loc.Set(_bandwidth, ToolTip.TipProperty,
            Loc.L("editor.bandwidth.tip", report.PanelBytesPerSecond, ByteBudgetScheduler.DefaultUtilization));
        _bandwidth.Foreground = report.Oversubscribed ? Palette.BadBrush
            : report.NearLimit ? Palette.WarnBrush
            : Palette.MutedBrush;

        _bandwidthBar.Value = Math.Clamp(report.Utilization * 100, 0, 100);
        _bandwidthBar.Foreground = report.Oversubscribed ? Palette.BadBrush
            : report.NearLimit ? Palette.WarnBrush
            : Palette.AccentBrush;
    }

    private void Touch()
    {
        _dirty = true;
        ShowFile();
    }

    // El punto delante es la marca de «sin guardar». Discreta, pero suficiente para que «Nuevo» no
    // sorprenda a nadie, que es de lo que se trata.
    private void ShowFile()
    {
        Loc.Set(_file, TextBlock.TextProperty, _name is null
            ? (_dirty ? Loc.L("editor.file.new-unsaved") : Loc.L("editor.file.new"))
            : (_dirty ? Loc.L("editor.file.unsaved") : Loc.L("editor.file.saved")));

        // El punto de antes delante del nombre lo decía sin decirlo. Ahora lo dice el icono y el
        // color: un aviso ámbar mientras haya cambios, una marca verde cuando no queda ninguno.
        _file.Foreground = _dirty ? Palette.WarnBrush : Palette.GoodBrush;
        if (_saveIcon.Child is Avalonia.Controls.Shapes.Path glyph)
        {
            glyph.Data = _dirty ? Icons.Alert : Icons.Check;
            glyph.Stroke = _dirty ? Palette.WarnBrush : Palette.GoodBrush;
        }

        // Un tema que no se ha guardado nunca no está en ningún sitio: no hay nada que borrar.
        _delete.IsEnabled = _name is not null;
    }

    private void ShowMessage(Text text)
    {
        _ownMessage = !text.IsEmpty;
        Loc.Set(_message, TextBlock.TextProperty, text);
        _message.Foreground = Palette.WarnBrush;
    }

    private void ShowProblems(Text title, LayoutException error)
    {
        var problems = Loc.Notices(error.Problems);
        Loc.Set(_message, TextBlock.TextProperty, Loc.Of(() => title + ":" + Environment.NewLine + problems));
        _message.Foreground = Palette.BadBrush;
    }

    public void Dispose()
    {
        _renderer?.Dispose();
        _renderer = null;

        // El lienzo guarda dos mapas de bits del tamaño del panel y no los suelta solo.
        _canvas.Dispose();
    }
}

internal static class ButtonExtensions
{
    public static Button WithClick(this Button button, Action onClick)
    {
        button.Click += (_, _) => onClick();
        return button;
    }
}
