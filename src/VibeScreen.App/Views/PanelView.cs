using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VibeScreen.App.Localization;
using VibeScreen.App.Theme;
using VibeScreen.Contracts;
using VibeScreen.Core.Layout;
using VibeScreen.Core.Rendering;
using VibeScreen.Protocol.Rendering;

// Los dos chocan con tipos del sistema que ya estan en el alcance: System.Diagnostics.Switch, que
// entra con Stopwatch, y System.Range. Los nombres buenos son estos, asi que se desempatan aqui.
using Range = VibeScreen.App.Theme.Range;
using Switch = VibeScreen.App.Theme.Switch;

namespace VibeScreen.App.Views;

/// <summary>Lo que está pasando en el panel y los mandos que no son del tema: brillo, encendido y qué tema pintar.</summary>
public sealed class PanelView : UserControl, IDisposable
{
    private readonly ServiceLink _service;
    private readonly RemoteSensorProvider _sensors;

    /// <summary>El tema que el panel está pintando, dibujado aquí con el mismo renderizador.</summary>
    private LayoutRenderer? _renderer;

    /// <summary>De dónde salió, para no releerlo en cada sondeo.</summary>
    private string? _renderedPath;
    private DateTime _renderedStamp;
    private TimeSpan? _stampChecked;

    /// <summary>El reloj de los relojes del tema. El mismo papel que el del editor.</summary>
    private readonly Stopwatch _frameClock = Stopwatch.StartNew();

    // ── cabecera ──────────────────────────────────────────────────────────────
    private readonly Control _uptimeChip;
    private readonly TextBlock _uptimeChipText;
    private readonly Avalonia.Controls.Shapes.Ellipse _uptimeDot;

    // ── servicio ──────────────────────────────────────────────────────────────
    private readonly PanelPreview _previewSurface = new(258);
    private readonly Control _preview;
    private readonly TextBlock _previewCaption = Blocks.Data("", 13, Palette.FaintBrush);
    private readonly Border _serviceFlag = Blocks.Flag(Loc.L("panel.no-service"));
    private readonly Control _modelBlock;
    private readonly TextBlock _modelValue;
    private readonly TextBlock _modelNote;
    private readonly Control _port;
    private readonly TextBlock _portValue;
    private readonly Control _size;
    private readonly TextBlock _sizeValue;
    private readonly Control _uptime;
    private readonly TextBlock _uptimeValue;
    private readonly Control _reconnects;
    private readonly TextBlock _reconnectsValue;

    // ── pantalla ──────────────────────────────────────────────────────────────
    private readonly Border _screenFlag = Blocks.Flag(Loc.L("panel.screen.off"));
    private readonly Switch _screen = new(Loc.L("panel.screen.on"), Loc.L("panel.screen.off"));
    private readonly Range _brightness = new(0, 100, 5);
    private readonly Control _brightnessNumber;
    private readonly TextBlock _brightnessValue;

    // ── tema ──────────────────────────────────────────────────────────────────
    private readonly Border _themeFlag = Blocks.Flag(Loc.L("panel.theme.none"));
    private readonly PanelPreview _thumbnailSurface = new(132);
    private readonly Control _thumbnail;

    private readonly TextBlock _themeName = new()
    {
        FontSize = 16,
        FontWeight = FontWeight.SemiBold,
        Foreground = Palette.TextBrush,
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private readonly TextBlock _themeNote = Blocks.Help(Text.Empty);

    /// <summary>Los layouts guardados, más el azar. Elegir se lo manda al servicio: aquí tampoco hay rutas.</summary>
    private readonly ComboBox _layouts = Forms.Labeled(new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 320 });

    /// <summary>Sortear otro ahora. Solo está cuando hay un sorteo que repetir.</summary>
    private readonly Button _reroll;

    /// <summary>Los nombres que hay puestos en el desplegable, para no rehacerlo en cada sondeo.</summary>
    private IReadOnlyList<string> _shownLayouts = [];

    /// <summary>
    /// Los layouts guardados, y cuándo se le preguntó al disco.
    /// </summary>
    /// <remarks>
    /// <see cref="ShowLayouts"/> corre en cada sondeo, o sea una vez por segundo mientras la ventana
    /// se ve, y <c>LayoutPaths.All()</c> es enumerar un directorio más un <c>File.Exists</c> por tema.
    /// Con la carpeta compartida en un disco lento o redirigido, eso es un tirón por segundo en el
    /// hilo de la interfaz.
    /// </remarks>
    private static readonly TimeSpan LayoutsMemo = TimeSpan.FromSeconds(2);

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private IReadOnlyList<string> _savedLayouts = [];
    private TimeSpan? _savedRead;

    // ── enlace ────────────────────────────────────────────────────────────────
    private readonly Control _rateBlock;
    private readonly TextBlock _rateValue;
    private readonly Sparkline _spark = new();
    private readonly Control _blits;
    private readonly TextBlock _blitsValue;
    private readonly Control _bytes;
    private readonly TextBlock _bytesValue;
    private readonly ProgressBar _budget = new() { Minimum = 0, Maximum = 100, Height = 8 };
    private readonly TextBlock _budgetValue = Blocks.Data("—", 14, Palette.TextBrush);
    private readonly TextBlock _budgetNote = Blocks.Help(Text.Empty);

    // ── avisos ────────────────────────────────────────────────────────────────
    private readonly Border _warningsFlag = Blocks.Flag(Loc.L("panel.warnings.none"), Tone.Good);
    private readonly StackPanel _warnings = new() { Spacing = 10 };

    /// <summary>
    /// Lo que ya se ha contado, con la hora a la que apareció.
    /// </summary>
    /// <remarks>
    /// El servicio manda una lista de avisos sin más: no dice cuándo empezó cada uno, y en cada
    /// sondeo llegan los mismos. La hora se sella aquí, la primera vez que se ve uno, y por eso es
    /// «desde cuándo lo sabe la ventana» y no «cuándo pasó»: es lo único honrado que se puede decir.
    /// La clave es el aviso y no su texto: cambiar de idioma no puede volver a poner todos los relojes
    /// a cero.
    /// </remarks>
    private readonly Dictionary<Notice, DateTime> _warningSince = [];

    private readonly TextBlock _message = new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontSize = 14,
        Foreground = Palette.BadBrush,
    };

    private readonly DispatcherTimer _brightnessDelay;
    private bool _suspend;

    /// <summary>Hay una orden de encendido en vuelo: el sondeo no puede deshacer lo que se acaba de pulsar.</summary>
    private bool _switching;

    /// <summary>
    /// Órdenes de brillo en vuelo: el sondeo tampoco puede deshacer lo que se acaba de soltar.
    /// </summary>
    /// <remarks>
    /// Un contador y no un sí o no: se puede soltar otra vez antes de que conteste la primera, y la
    /// segunda espera su turno detrás. Con un sí o no, el final de la primera abría la guarda con la
    /// segunda todavía sin atender.
    /// </remarks>
    private int _brightnessSending;

    private bool _released;

    /// <summary>El documento del tema que se está dibujando, para saber qué sensores pide.</summary>
    private LayoutDocument? _layout;

    public PanelView(ServiceLink service, RemoteSensorProvider sensors)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(sensors);
        _service = service;
        _sensors = sensors;

        // La miniatura del tema y la previa grande enseñan lo mismo: un solo mapa de bits, dibujado
        // a dos tamaños. Dos copias por segundo para pintar dos veces la misma imagen no.
        _thumbnailSurface.Mirror(_previewSurface);
        _previewSurface.Mirrored += _thumbnailSurface.InvalidateVisual;

        (_uptimeChip, _uptimeChipText, _uptimeDot) = Blocks.Chip(Loc.L("panel.no-service"));
        _preview = PanelPreview.Framed(_previewSurface);
        _thumbnail = PanelPreview.Framed(_thumbnailSurface);

        (_modelBlock, _modelValue, _modelNote) = Blocks.LiveStat(Loc.L("panel.model"), data: false);
        _modelValue.FontFamily = AppFonts.Display;
        _modelValue.FontSize = 22;
        _modelValue.FontWeight = FontWeight.SemiBold;

        (_port, _portValue, _) = Blocks.LiveStat(Loc.L("panel.port"));
        (_size, _sizeValue, _) = Blocks.LiveStat(Loc.L("panel.resolution"));
        (_uptime, _uptimeValue, _) = Blocks.LiveStat(Loc.L("panel.uptime"));
        (_reconnects, _reconnectsValue, _) = Blocks.LiveStat(Loc.L("panel.reconnects"));

        (_brightnessNumber, _brightnessValue, _) = Blocks.NumXl("—", "%");
        _brightnessValue.FontSize = 30;

        (_rateBlock, _rateValue, _) = Blocks.NumXl("—", "B/s");

        (_blits, _blitsValue, _) = Blocks.LiveStat(Loc.L("panel.blits"));
        (_bytes, _bytesValue, _) = Blocks.LiveStat(Loc.L("panel.sent"));

        // El brillo se manda cuando el usuario deja de mover: una orden por píxel del deslizador
        // llenaría la tubería de órdenes que se pisan entre ellas.
        _brightnessDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _brightnessDelay.Tick += (_, _) =>
        {
            _brightnessDelay.Stop();
            int percent = (int)_brightness.Value;
            _brightnessSending++;
            Run(async () =>
            {
                try
                {
                    Report(await _service.SetBrightnessAsync(percent).ConfigureAwait(true));
                }
                finally
                {
                    _brightnessSending--;
                }
            });
        };

        _brightness.Moved += value =>
        {
            _brightnessValue.Text = ((int)value).ToString(Loc.Culture);
            if (_suspend) return;
            _brightnessDelay.Stop();
            _brightnessDelay.Start();
        };

        _layouts.SelectionChanged += (_, _) =>
        {
            if (_suspend) return;

            // Se coge ahora, no dentro de la tarea: para cuando esta conteste, el sondeo puede haber
            // dejado seleccionada otra cosa.
            object? chosen = _layouts.SelectedItem;
            Run(async () =>
            {
                if (chosen is RandomLayoutEntry)
                    Report(await _service.SetRandomLayoutAsync(true).ConfigureAwait(true));
                else if (chosen is string name)
                    Report(await _service.SetLayoutAsync(LayoutPaths.For(name)).ConfigureAwait(true));
            });
        };

        _reroll = Blocks.Action(Loc.L("panel.reroll"), Icons.Shuffle, () =>
            Run(async () => Report(await _service.SetRandomLayoutAsync(true).ConfigureAwait(true))));
        _reroll.IsVisible = false;

        _screen.Toggled += on =>
        {
            if (_suspend) return;
            _switching = true;
            Run(async () =>
            {
                try
                {
                    Report(await _service.SetScreenPowerAsync(on).ConfigureAwait(true));
                }
                finally
                {
                    _switching = false;
                }
            });
        };

        ShowWarnings([]);
        Content = Build();
    }

    private Control Build()
    {
        var header = Blocks.ViewHeader(
            Loc.L("panel.title"),
            Loc.L("panel.subtitle"),
            _uptimeChip);

        var grid = new Grid12();
        grid.Children.Add(Grid12.Span(ServiceCard(), 7));
        grid.Children.Add(Grid12.Span(ScreenCard(), 5));
        grid.Children.Add(Grid12.Span(ThemeCard(), 5));
        grid.Children.Add(Grid12.Span(LinkCard(), 4));
        grid.Children.Add(Grid12.Span(WarningsCard(), 3));

        var page = new StackPanel
        {
            Spacing = 22,
            Children = { header, grid, _message },
        };

        return Blocks.Ui(new ScrollViewer
        {
            Padding = new Thickness(32, 26, 32, 32),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = page,
        });
    }

    /// <summary>Lo que el servicio tiene delante: qué panel, por dónde y desde cuándo.</summary>
    private Control ServiceCard()
    {
        var facts = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 16,
            RowSpacing = 16,
        };
        Add(facts, _port, 0, 0);
        Add(facts, _size, 0, 1);
        Add(facts, _uptime, 1, 0);
        Add(facts, _reconnects, 1, 1);

        var right = new StackPanel
        {
            Spacing = 20,
            Children = { _modelBlock, facts },
        };

        var hero = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 24 };
        Grid.SetColumn(right, 1);
        hero.Children.Add(_preview);
        hero.Children.Add(right);

        return Blocks.Card(Loc.L("panel.service"), _serviceFlag, hero);
    }

    /// <summary>El encendido y el brillo. Son del panel, así que salen por la tubería.</summary>
    private Control ScreenCard()
    {
        var toggleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
        var labels = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Loc.Set(new TextBlock
                {
                    FontSize = 14,
                    FontWeight = FontWeight.Medium,
                    Foreground = Palette.MutedBrush,
                }, TextBlock.TextProperty, Loc.L("panel.screen")),
                Blocks.Help(Loc.L("panel.screen.help")),
            },
        };
        Grid.SetColumn(_screen, 1);
        toggleRow.Children.Add(labels);
        toggleRow.Children.Add(_screen);

        var brightHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var brightLabel = Loc.Set(new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeight.Medium,
            Foreground = Palette.MutedBrush,
            VerticalAlignment = VerticalAlignment.Bottom,
        }, TextBlock.TextProperty, Loc.L("panel.brightness"));
        Grid.SetColumn(_brightnessNumber, 1);
        brightHead.Children.Add(brightLabel);
        brightHead.Children.Add(_brightnessNumber);

        var scale = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var zero = Scale("0");
        var fifty = Scale("50");
        var hundred = Scale("100");
        fifty.HorizontalAlignment = HorizontalAlignment.Center;
        hundred.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(fifty, 1);
        Grid.SetColumn(hundred, 2);
        scale.Children.Add(zero);
        scale.Children.Add(fifty);
        scale.Children.Add(hundred);

        var bright = new StackPanel
        {
            Spacing = 6,
            Children = { brightHead, _brightness, scale },
        };

        return Blocks.Card(Loc.L("panel.screen"), _screenFlag, toggleRow, bright);
    }

    /// <summary>
    /// Qué tema pinta el panel, y quién lo elige.
    /// </summary>
    /// <remarks>
    /// El desplegable es el mando principal de esta pestaña y lleva dentro la entrada del azar, que
    /// no es un tema sino una forma de elegirlo. El diseño no lo traía —enseñaba solo el modo de
    /// arranque—, pero sin él no habría manera de decirle al panel qué pintar sin pasar por el
    /// editor, que es otra cosa.
    /// </remarks>
    private Control ThemeCard()
    {
        var current = new StackPanel
        {
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Loc.Set(new TextBlock
                {
                    FontSize = 11,
                    LetterSpacing = 1,
                    Foreground = Palette.FaintBrush,
                }, TextBlock.TextProperty, Loc.L("panel.theme.painting").Upper()),
                _themeName,
                _themeNote,
            },
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 16 };
        Grid.SetColumn(current, 1);
        row.Children.Add(_thumbnail);
        row.Children.Add(current);

        return Blocks.Card(Loc.L("panel.theme"), _themeFlag,
            row,
            Blocks.Field(Loc.L("panel.layout"), _layouts),
            Blocks.Cluster(
                Blocks.Action(Loc.L("panel.reload"), Icons.Refresh, () =>
                    Run(async () => Report(await _service.ReloadLayoutAsync().ConfigureAwait(true)))),
                _reroll));
    }

    /// <summary>Cuánto está costando pintar, contra lo que el panel aguanta.</summary>
    private Control LinkCard()
    {
        var now = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Loc.Set(new TextBlock
                {
                    FontSize = 11,
                    LetterSpacing = 1,
                    Foreground = Palette.FaintBrush,
                }, TextBlock.TextProperty, Loc.L("panel.link.now").Upper()),
                _rateBlock,
            },
        };

        var counts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16 };
        Add(counts, _blits, 0, 0);
        Add(counts, _bytes, 0, 1);

        var meterHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var meterLabel = Loc.Set(new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeight.Medium,
            Foreground = Palette.MutedBrush,
        }, TextBlock.TextProperty, Loc.L("panel.link.budget"));
        Grid.SetColumn(_budgetValue, 1);
        meterHead.Children.Add(meterLabel);
        meterHead.Children.Add(_budgetValue);

        var meter = new StackPanel
        {
            Spacing = 7,
            Children = { meterHead, _budget, _budgetNote },
        };

        return Blocks.Card(Loc.L("panel.link"), Blocks.Flag(Loc.L("panel.link.serial"), Tone.Neutral, Icons.Link),
            now, _spark, counts, meter);
    }

    /// <summary>Lo que el servicio ha tenido que contar, con la hora a la que lo contó.</summary>
    private Control WarningsCard() => Blocks.Card(Loc.L("panel.warnings"), _warningsFlag, _warnings);

    private static TextBlock Scale(string text) => AppFonts.Mono(new TextBlock
    {
        Text = text,
        FontSize = 12,
        Foreground = Palette.FaintBrush,
    });

    private static void Add(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    /// <summary>
    /// Los sensores que la previa necesita refrescados.
    /// </summary>
    /// <remarks>
    /// Son los del tema que el panel <b>está pintando</b>, y eso es lo que hace que esta previa no
    /// cueste lecturas: el servicio no vuelve a muestrear un sensor que el panel acaba de leer para
    /// pintarlo, sirve lo que leyó. Con el tema aplicado, todos los que se piden aquí están en ese
    /// caso. La del editor no puede decir lo mismo, porque el tema abierto puede mirar sensores que
    /// el panel no pinta.
    /// </remarks>
    public IReadOnlyList<string> SensorIds =>
        _renderer is null
            ? []
            : _layout?.Widgets.Select(w => w.SensorId).OfType<string>().Distinct(StringComparer.Ordinal).ToList()
              ?? [];

    /// <summary>Un latido: la previa se mueve aunque no lleguen lecturas, por los relojes.</summary>
    public void Tick()
    {
        if (_renderer is null) return;
        if (_renderer.Update(_sensors, _frameClock.Elapsed).Count == 0) return;
        _previewSurface.SetFrame(_renderer.Surface.Bitmap);
    }

    /// <summary>Han llegado lecturas nuevas.</summary>
    public void SensorsChanged() => Tick();

    /// <summary>
    /// Ha cambiado el idioma.
    /// </summary>
    /// <remarks>
    /// Los textos cambian solos, y el sondeo reescribe el resto cada segundo. Lo que no: las dos
    /// previas, que pintan su leyenda a mano y solo se repintan cuando llega un fotograma, y el
    /// tema dibujado, que lleva el reloj con los nombres de días y meses del idioma de antes.
    /// </remarks>
    public void LanguageChanged()
    {
        _renderedPath = null;
        _previewSurface.InvalidateVisual();
        _thumbnailSurface.InvalidateVisual();
    }

    public void OnStatus(ServiceStatus? status)
    {
        _suspend = true;
        try
        {
            if (status is null)
            {
                NoService();
                return;
            }

            bool painting = status.State == ServiceState.Painting;
            _serviceFlag.Child = FlagContent(Labels.ServiceState(status.State), painting ? Tone.Good : Tone.Neutral);
            Recolour(_serviceFlag, painting ? Tone.Good : Tone.Neutral);

            Loc.Set(_modelValue, TextBlock.TextProperty, status.Model is { } model ? Text.Raw(model) : Loc.L("panel.model.none"));
            Loc.Set(_modelNote, TextBlock.TextProperty, OrientationName(status.Orientation));
            _modelNote.IsVisible = !string.IsNullOrEmpty(status.Orientation);

            _portValue.Text = status.PortName ?? "—";
            _sizeValue.Text = status.Width > 0 ? $"{status.Width}×{status.Height}" : "—";
            _uptimeValue.Text = Duration(status.UptimeSeconds);
            _reconnectsValue.Text = status.Reconnects.ToString(Loc.Culture);

            Loc.Set(_uptimeChipText, TextBlock.TextProperty,
                painting ? Loc.L("panel.uptime.chip", Duration(status.UptimeSeconds)) : Labels.ServiceState(status.State));
            _uptimeDot.Fill = painting ? Palette.GoodBrush : Palette.WarnBrush;

            ShowScreen(status);
            ShowLayouts(status);
            ShowPreview(status);
            ShowLink(status);
            ShowWarnings(status.Warnings);
        }
        finally
        {
            _suspend = false;
        }
    }

    /// <summary>
    /// Carga el tema que el panel pinta y lo dibuja.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se relee cuando cambia la ruta y cuando cambia la fecha del fichero, que es lo que pasa al
    /// pulsar «Recargar actual» o al aplicar desde el editor: el panel pasa a pintar otra cosa sin
    /// que el nombre cambie, y sin mirar la fecha la previa se quedaría enseñando lo de antes.
    /// </para>
    /// <para>
    /// La fecha se mira como mucho una vez cada dos segundos, por lo mismo que la lista de layouts:
    /// esto corre una vez por segundo y la carpeta puede estar en un disco lento.
    /// </para>
    /// </remarks>
    private void ShowPreview(ServiceStatus status)
    {
        if (status.LayoutPath is not { } path)
        {
            Drop(Loc.L("preview.no-theme"));
            return;
        }

        DateTime stamp = _renderedStamp;
        var now = _frameClock.Elapsed;
        if (_stampChecked is not { } last || now - last >= LayoutsMemo)
        {
            _stampChecked = now;
            try
            {
                stamp = File.GetLastWriteTimeUtc(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                stamp = _renderedStamp;
            }
        }

        if (_renderer is not null && path == _renderedPath && stamp == _renderedStamp) return;

        try
        {
            _layout = LayoutLoader.Load(path);
            _renderer?.Dispose();
            _renderer = new LayoutRenderer(_layout, language: Loc.Current);
            _renderer.RenderAll(_sensors, _frameClock.Elapsed);
            _previewSurface.SetFrame(_renderer.Surface.Bitmap);
            _renderedPath = path;
            _renderedStamp = stamp;
            _previewSurface.EmptyText = Text.Empty;
        }
        catch (Exception e) when (e is IOException or LayoutException or NotSupportedException
                                       or FormatException or ArgumentException or UnauthorizedAccessException)
        {
            // Que la previa no sepa dibujarlo no es motivo para molestar: el panel lo está pintando
            // igual, y quien tenga que arreglarlo lo verá en el editor con el problema entero.
            Drop(Loc.L("preview.cannot-draw"));
        }
    }

    /// <summary>Suelta el tema dibujado y dice por qué no hay nada que enseñar.</summary>
    private void Drop(Text reason)
    {
        _renderer?.Dispose();
        _renderer = null;
        _layout = null;
        _renderedPath = null;
        _renderedStamp = default;
        _previewSurface.EmptyText = reason;
        _previewSurface.Clear();
    }

    /// <summary>Lo que se puede decir cuando no hay nadie al otro lado de la tubería.</summary>
    private void NoService()
    {
        Recolour(_serviceFlag, Tone.Bad);
        _serviceFlag.Child = FlagContent(Loc.L("panel.no-service"), Tone.Bad);
        Loc.Set(_modelValue, TextBlock.TextProperty, Loc.L("panel.service.not-running"));
        _modelNote.IsVisible = false;
        _portValue.Text = "—";
        _sizeValue.Text = "—";
        _uptimeValue.Text = "—";
        _reconnectsValue.Text = "—";
        Loc.Set(_uptimeChipText, TextBlock.TextProperty, Loc.L("panel.no-service"));
        _uptimeDot.Fill = Palette.FaintBrush;

        Drop(Loc.L("preview.no-service"));
        _previewCaption.Text = "";

        Loc.Set(_themeName, TextBlock.TextProperty, Text.Raw("—"));
        Loc.Set(_themeNote, TextBlock.TextProperty, Text.Empty);
        _rateValue.Text = "—";
        _blitsValue.Text = "—";
        _bytesValue.Text = "—";
        _budget.Value = 0;
        _budgetValue.Text = "—";
        Loc.Set(_budgetNote, TextBlock.TextProperty, Text.Empty);
        _spark.Clear();
        _spark.IsVisible = false;
    }

    private void ShowScreen(ServiceStatus status)
    {
        // Sin tocar el interruptor mientras su orden va de camino: el estado que llega todavía es el
        // de antes, así que volvía solo a su sitio durante un segundo y el usuario lo pulsaba otra
        // vez, mandando la orden contraria.
        if (!_switching) _screen.Show(status.ScreenOn);

        Recolour(_screenFlag, status.ScreenOn ? Tone.Good : Tone.Neutral);
        _screenFlag.Child = FlagContent(status.ScreenOn ? Loc.L("panel.screen.on") : Loc.L("panel.screen.off"), status.ScreenOn ? Tone.Good : Tone.Neutral);
        _previewSurface.ScreenOff = !status.ScreenOn;
        _thumbnailSurface.ScreenOff = !status.ScreenOn;

        // Y tampoco el deslizador mientras el usuario lo mueve, ni mientras su orden va de camino: el
        // sondeo llega una vez por segundo y se lo quitaría de las manos, y el estado que llega antes
        // de que el servicio la atienda todavía trae el brillo de antes. La barra volvía atrás un
        // segundo con el panel ya cambiado.
        if (!_brightnessDelay.IsEnabled && _brightnessSending == 0)
        {
            _brightness.Show(status.Brightness);
            _brightnessValue.Text = status.Brightness.ToString(Loc.Culture);
        }
    }

    private void ShowLink(ServiceStatus status)
    {
        bool painting = status.State == ServiceState.Painting;

        _rateValue.Text = painting ? Loc.Number(status.BytesPerSecond) : "—";
        _blitsValue.Text = Loc.Number(status.Blits);
        _bytesValue.Text = Bytes(status.BytesSent);

        if (painting) _spark.Add(status.BytesPerSecond);

        // Escondido mientras no haya dos muestras: una caja vacía de cuarenta y ocho píxeles en
        // medio de la tarjeta parece que falta algo, y lo que falta es tiempo.
        _spark.IsVisible = _spark.Extent is not null;

        double percent = Math.Clamp(status.LinkUtilization * 100, 0, 100);
        _budget.Value = percent;
        _budget.Foreground = status.LinkUtilization > 0.7 ? Palette.WarnBrush : Palette.AccentBrush;
        _budgetValue.Text = (percent / 100).ToString("P0", Loc.Culture);

        // El presupuesto es una fracción del caudal del panel, no el caudal entero: el planificador
        // reparte solo una parte para que el panel siga contestando a las órdenes. Decir el número
        // absoluto evita que el porcentaje parezca medido contra algo que no es.
        Loc.Set(_budgetNote, TextBlock.TextProperty, status.ThroughputBytesPerSecond > 0
            ? Loc.L("panel.link.budget.note", status.ThroughputBytesPerSecond * ByteBudgetScheduler.DefaultUtilization)
            : Text.Empty);
    }

    private void ShowWarnings(IReadOnlyList<Notice> warnings)
    {
        _warnings.Children.Clear();

        if (warnings.Count == 0)
        {
            _warningSince.Clear();
            Recolour(_warningsFlag, Tone.Good);
            _warningsFlag.Child = FlagContent(Loc.L("panel.warnings.none"), Tone.Good);
            _warnings.Children.Add(Blocks.Empty(
                Icons.Bell,
                Loc.L("panel.warnings.empty"),
                Loc.L("panel.warnings.empty.note")));
            return;
        }

        Recolour(_warningsFlag, Tone.Warn);
        _warningsFlag.Child = FlagContent(Text.Raw(warnings.Count.ToString(Loc.Culture)), Tone.Warn);

        var now = DateTime.Now;
        foreach (var warning in warnings)
        {
            if (!_warningSince.TryGetValue(warning, out var since))
            {
                since = now;
                _warningSince[warning] = since;
            }

            _warnings.Children.Add(Warning(Loc.Notice(warning), since));
        }

        // Los que ya no llegan dejan de contar: si el aviso vuelve, vuelve con hora nueva, que es la
        // verdad. Guardarlos para siempre haría crecer el diccionario sin fin.
        foreach (var stale in _warningSince.Keys.Where(k => !warnings.Contains(k)).ToList())
            _warningSince.Remove(stale);
    }

    private static Control Warning(Text text, DateTime since)
    {
        var body = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Loc.Set(new TextBlock
                {
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Palette.TextBrush,
                }, TextBlock.TextProperty, text),
                Blocks.Data(since.ToString("t", Loc.Culture), 13, Palette.FaintBrush),
            },
        };

        // Una rejilla y no un StackPanel horizontal: ese mide a sus hijos con ancho infinito, el texto
        // nunca encuentra dónde partir y se sale de la tarjeta. La columna * le da lo que sobra.
        var icon = Icons.Make(Icons.Alert, Palette.WarnBrush, 18);
        icon.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(body, 1);

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 10,
            Children = { icon, body },
        };

        return new Border
        {
            Background = Palette.WarnSoftBrush,
            BorderBrush = Palette.WarnEdgeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10),
            Child = row,
        };
    }

    /// <summary>
    /// Pone al día la lista y deja marcado lo que el panel está haciendo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Con el azar puesto, el desplegable no puede enseñar el layout que se está pintando: enseña el
    /// modo, que es lo que el usuario eligió. Cuál ha salido lo dice la línea de al lado, porque
    /// seguir sabiéndolo de un vistazo es medio motivo para tener la pestaña abierta.
    /// </para>
    /// <para>
    /// Un layout de fuera de la carpeta —puesto con la CLI, o de antes de que esto existiera— no
    /// está en la lista. No se esconde: se dice su ruta, que es lo único honrado que se puede decir
    /// de algo que el editor no sabe abrir.
    /// </para>
    /// </remarks>
    private void ShowLayouts(ServiceStatus status)
    {
        string? painting = status.LayoutPath;
        string? name = LayoutPaths.NameOf(painting);

        var names = SavedLayouts();
        if (!names.SequenceEqual(_shownLayouts, StringComparer.Ordinal))
        {
            _shownLayouts = names;
            _layouts.ItemsSource = new List<object>(names.Count + 1) { AtRandom }.Concat(names).ToList();
        }

        _layouts.SelectedItem = status.RandomLayout ? AtRandom : name;
        Loc.Set(_layouts, ComboBox.PlaceholderTextProperty, painting is null ? Loc.L("panel.layout.blank") : Text.Empty);

        Loc.Set(_themeName, TextBlock.TextProperty,
            name is not null ? Text.Raw(name) : painting is null ? Loc.L("panel.theme.none") : Text.Raw(Path.GetFileName(painting)));
        _previewCaption.Text = name is null ? "" : $"{name} · {status.Width}×{status.Height}";

        Recolour(_themeFlag, painting is null ? Tone.Neutral : Tone.Good);
        _themeFlag.Child = FlagContent(painting is null ? Loc.L("panel.theme.none") : Loc.L("panel.theme.applied"), painting is null ? Tone.Neutral : Tone.Good);

        var note = (status.RandomLayout, painting, name) switch
        {
            (true, _, not null) => Loc.L("panel.theme.random.another"),
            (true, _, null) => Loc.L("panel.theme.random.first"),
            (false, not null, null) => Loc.L("panel.theme.outside", painting),
            _ => Text.Empty,
        };
        Loc.Set(_themeNote, TextBlock.TextProperty, note);
        _themeNote.IsVisible = !note.IsEmpty;
        _reroll.IsVisible = status.RandomLayout;
    }

    /// <summary>Los layouts guardados, preguntándole al disco como mucho una vez cada dos segundos.</summary>
    private IReadOnlyList<string> SavedLayouts()
    {
        var now = _clock.Elapsed;
        if (_savedRead is { } last && now - last < LayoutsMemo) return _savedLayouts;

        _savedLayouts = LayoutPaths.All();
        _savedRead = now;
        return _savedLayouts;
    }

    /// <summary>
    /// Lanza una orden hacia el servicio sin dejar que un fallo se lleve la ventana.
    /// </summary>
    /// <remarks>
    /// Los mandos del panel son «async void» por fuerza: nadie espera a un clic. Y lo que llega por
    /// la tubería no siempre encaja en lo que <see cref="ServiceLink"/> filtra —una línea truncada
    /// da un <c>JsonException</c>, por ejemplo—, así que sin esto una respuesta a medias cerraba la
    /// aplicación de golpe, sin mensaje y con la ventana en la bandeja.
    /// </remarks>
    private async void Run(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception e)
        {
            Report(Loc.L("panel.service.talk-failed", Loc.Notice(Notice.From(e))));
        }
    }

    private void Report(Text? error)
    {
        Loc.Set(_message, TextBlock.TextProperty, error ?? Text.Empty);
        _message.IsVisible = error is { IsEmpty: false };
    }

    /// <summary>Repinta una pastilla sin rehacerla: es un borde que ya está colocado en su tarjeta.</summary>
    private static void Recolour(Border flag, Tone tone)
    {
        (IBrush background, IBrush edge) = tone switch
        {
            Tone.Good => ((IBrush)Palette.GoodSoftBrush, (IBrush)Palette.GoodEdgeBrush),
            Tone.Warn => ((IBrush)Palette.WarnSoftBrush, (IBrush)Palette.WarnEdgeBrush),
            Tone.Bad => ((IBrush)Palette.BadSoftBrush, (IBrush)Palette.BadEdgeBrush),
            _ => ((IBrush)Palette.Surface2Brush, (IBrush)Palette.LineStrongBrush),
        };
        flag.Background = background;
        flag.BorderBrush = edge;
    }

    private static Control FlagContent(Text text, Tone tone)
    {
        var colour = tone switch
        {
            Tone.Good => Palette.GoodBrush,
            Tone.Warn => Palette.WarnBrush,
            Tone.Bad => Palette.BadBrush,
            _ => (IBrush)Palette.MutedBrush,
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (tone != Tone.Neutral)
            row.Children.Add(new Avalonia.Controls.Shapes.Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = colour,
                VerticalAlignment = VerticalAlignment.Center,
            });

        row.Children.Add(Loc.Set(new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeight.Medium,
            Foreground = colour,
            VerticalAlignment = VerticalAlignment.Center,
        }, TextBlock.TextProperty, text));

        return row;
    }

    /// <summary>
    /// La entrada del desplegable que no es un layout, sino una forma de elegirlo.
    /// </summary>
    /// <remarks>
    /// Un objeto y no una cadena reservada: «Aleatorio» es un nombre de layout perfectamente válido,
    /// y el día que alguien llame así a uno suyo, las dos entradas tienen que poder convivir.
    /// </remarks>
    private sealed record RandomLayoutEntry : ILabeled
    {
        public Text Label => Loc.L("panel.layout.random");

        public override string ToString() => Label.ToString();
    }

    private static readonly RandomLayoutEntry AtRandom = new();

    /// <summary>
    /// La orientación, para leer.
    /// </summary>
    /// <remarks>
    /// Por la tubería viaja el nombre del enumerado. Se traduce con la misma tabla que el resto de
    /// la ventana, y lo que no esté en ella se deja tal cual: es más honrado que esconderlo.
    /// </remarks>
    private static Text OrientationName(string? value) =>
        System.Enum.TryParse<VibeScreen.Protocol.Display.Orientation>(value, ignoreCase: true, out var parsed)
            ? Labels.Orientation(parsed).Lower()
            : Text.Raw(value?.ToLowerInvariant() ?? "");

    private static string Duration(long seconds) => seconds switch
    {
        < 60 => $"{seconds} s",
        < 3600 => $"{seconds / 60} min",
        < 86400 => $"{seconds / 3600} h {seconds % 3600 / 60} min",
        _ => $"{seconds / 86400} d {seconds % 86400 / 3600} h",
    };

    private static string Bytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => Loc.Number(bytes / 1024.0, "N1") + " KB",
        < 1024 * 1024 * 1024 => Loc.Number(bytes / (1024.0 * 1024), "N1") + " MB",
        _ => Loc.Number(bytes / (1024.0 * 1024 * 1024), "N2") + " GB",
    };

    public void Dispose()
    {
        if (_released) return;
        _released = true;
        _brightnessDelay.Stop();
        _renderer?.Dispose();
        _renderer = null;
        _previewSurface.Dispose();
        _thumbnailSurface.Dispose();
    }
}
