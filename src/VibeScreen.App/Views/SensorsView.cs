using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using VibeScreen.App.Localization;
using VibeScreen.App.Theme;
using VibeScreen.Core.Layout;
using VibeScreen.Core.Sensors;

namespace VibeScreen.App.Views;

/// <summary>
/// El explorador de sensores: todo lo que ve el servicio en esta máquina, agrupado por hardware y con
/// el valor actualizándose.
/// </summary>
/// <remarks>
/// Las filas se construyen una vez y después solo se les cambia el número. Con 328 sensores, rehacer
/// la lista en cada sondeo se nota.
/// </remarks>
public sealed class SensorsView : UserControl
{
    /// <summary>El reparto de las tres columnas. Lo comparten la cabecera y todas las filas.</summary>
    private const string Columns = "40*,38*,22*";

    private readonly RemoteSensorProvider _sensors;
    private readonly EditorView _editor;

    private readonly TextBox _filter = new() { PlaceholderText = "temperature, vcore, gpu…" };
    private readonly ComboBox _device = Forms.Labeled(new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch });
    private readonly StackPanel _rows = new();
    private readonly ScrollViewer _scroll;

    // Los dos avisos de la tabla vacía, en el sitio de las filas: que nada coincide con el filtro, y
    // que no hay servicio y por tanto ni filas.
    private readonly Control _empty;
    private readonly Control _offline;

    private readonly TextBlock _count = Blocks.Data("", 14, Palette.MutedBrush);

    /// <summary>
    /// El punto entre el recuento y el elegido. Sin servicio no hay ni lo uno ni lo otro, y se quedaba
    /// él solo encima de la tabla.
    /// </summary>
    private readonly TextBlock _dot = new()
    {
        Text = "·",
        FontSize = 14,
        Foreground = Palette.FaintBrush,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock _chosen = new()
    {
        FontSize = 14,
        Foreground = Palette.FaintBrush,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>El enlace de <see cref="_chosen"/> a la letra de los datos, mientras enseña un identificador.</summary>
    private IDisposable? _chosenMono;

    private readonly Button _copy;
    private readonly Button _use;

    private readonly Dictionary<string, SensorRow> _built = new(StringComparer.Ordinal);
    private string _shownFilter = "";
    private string _shownDevices = "";
    private int _shownCount = -1;
    private string? _selected;
    private bool _suspend;

    public SensorsView(RemoteSensorProvider sensors, EditorView editor)
    {
        ArgumentNullException.ThrowIfNull(sensors);
        ArgumentNullException.ThrowIfNull(editor);
        _sensors = sensors;
        _editor = editor;

        _filter.TextChanged += (_, _) => Rebuild();

        _device.Items.Add(EveryDevice);
        _device.SelectedIndex = 0;
        _device.SelectionChanged += (_, _) =>
        {
            if (_suspend) return;
            Rebuild();
        };

        _copy = Blocks.Action(Loc.L("sensors.copy"), Icons.Copy, () => _ = CopyAsync());
        _use = Blocks.Action(Loc.L("sensors.use"), Icons.Send, UseInEditor, "primary");
        _copy.IsEnabled = false;
        _use.IsEnabled = false;

        _empty = Blocks.EmptyTable(
            Icons.Search,
            Loc.L("sensors.empty"),
            Loc.L("sensors.empty.note"),
            Blocks.Action(Loc.L("sensors.clear-filters"), null, ClearFilters));
        _empty.Name = "sensor-empty";
        _empty.IsVisible = false;

        // Antes entraba como una fila más y salía pegado al canto de arriba a la izquierda. Ahora va
        // donde el de sin coincidencias, centrado en el hueco de las filas, y se enciende o se apaga.
        _offline = Blocks.EmptyTable(
            Icons.Alert,
            Loc.L("sensors.offline"),
            Loc.L("sensors.offline.note"));
        _offline.Name = "sensor-offline";
        _offline.IsVisible = false;

        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new Panel { Children = { _rows, _empty, _offline } },
        };

        Content = Build();
        Rebuild();
    }

    private Control Build()
    {
        var search = Search();
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children = { _copy, _use },
        };

        var deviceField = Blocks.Field(Loc.L("sensors.device"), _device);
        var filterField = Blocks.Field(Loc.L("sensors.filter"), search);

        // Los dos botones miden sus buenos cuatrocientos píxeles y no encogen. Con la ventana en su
        // mínimo no caben al lado de los filtros, y antes se comían la caja de búsqueda hasta
        // dejarla en un cuadrado con la lupa dentro. Cuando no caben, bajan a su propia fila.
        var bar = new Grid { ColumnSpacing = 14, RowSpacing = 12 };
        void Lay(bool narrow)
        {
            bar.Children.Clear();
            if (narrow)
            {
                bar.ColumnDefinitions = new ColumnDefinitions("260,*");
                bar.RowDefinitions = new RowDefinitions("Auto,Auto");
                Grid.SetColumn(deviceField, 0);
                Grid.SetRow(deviceField, 0);
                Grid.SetColumn(filterField, 1);
                Grid.SetRow(filterField, 0);
                Grid.SetColumn(actions, 0);
                Grid.SetRow(actions, 1);
                Grid.SetColumnSpan(actions, 2);
            }
            else
            {
                bar.ColumnDefinitions = new ColumnDefinitions("260,*,Auto");
                bar.RowDefinitions = new RowDefinitions("Auto");
                Grid.SetColumn(deviceField, 0);
                Grid.SetRow(deviceField, 0);
                Grid.SetColumn(filterField, 1);
                Grid.SetRow(filterField, 0);
                Grid.SetColumn(actions, 2);
                Grid.SetRow(actions, 0);
                Grid.SetColumnSpan(actions, 1);
            }

            bar.Children.Add(deviceField);
            bar.Children.Add(filterField);
            bar.Children.Add(actions);
        }

        Lay(false);
        Reflow.OnWidth(bar, 860, Lay);

        var meta = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _count, _dot, _chosen },
        };

        // La cabecera vive fuera del desplazamiento, así que se queda quieta con las filas
        // corriendo por debajo. Las cabeceras de grupo no pueden hacer lo mismo —irían dentro— y se
        // van con el resto: pegarlas obligaría a un adorno propio por un detalle que no lo vale.
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), ColumnSpacing = 16 };
        var value = Header(Loc.L("sensors.column.value"));
        value.TextAlignment = Avalonia.Media.TextAlignment.Right;
        Grid.SetColumn(value, 2);
        var native = Header(Loc.L("sensors.column.native"));
        Grid.SetColumn(native, 1);
        head.Children.Add(Header(Loc.L("sensors.column.id")));
        head.Children.Add(native);
        head.Children.Add(value);

        var table = new Border
        {
            Background = Palette.SurfaceBrush,
            BorderBrush = Palette.LineBrush,
            BorderThickness = new Thickness(1, 1, 1, 0),
            CornerRadius = new CornerRadius(12, 12, 0, 0),
            Child = new DockPanel
            {
                Children =
                {
                    new Border
                    {
                        Background = Palette.Surface2Brush,
                        BorderBrush = Palette.LineStrongBrush,
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        CornerRadius = new CornerRadius(12, 12, 0, 0),
                        Padding = new Thickness(16, 10),
                        [DockPanel.DockProperty] = Dock.Top,
                        Child = head,
                    },
                    _scroll,
                },
            },
        };

        var page = new DockPanel { Margin = new Thickness(32, 26, 32, 0) };
        var top = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(0, 0, 0, 14),
            [DockPanel.DockProperty] = Dock.Top,
            Children =
            {
                Blocks.ViewHeader(Loc.L("sensors.title"), Loc.L("sensors.subtitle")),
                bar,
                meta,
            },
        };
        page.Children.Add(top);
        page.Children.Add(table);

        return Blocks.Ui(page);
    }

    private Control Search()
    {
        _filter.Padding = new Thickness(36, 0, 12, 0);
        AppFonts.Mono(_filter);

        var glyph = Icons.Make(Icons.Search, Palette.FaintBrush, 17);
        glyph.HorizontalAlignment = HorizontalAlignment.Left;
        glyph.Margin = new Thickness(11, 0, 0, 0);
        glyph.IsHitTestVisible = false;

        return new Panel { Children = { _filter, glyph } };
    }

    private static TextBlock Header(Text text) => Loc.Set(new TextBlock
    {
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        LetterSpacing = 1,
        Foreground = Palette.FaintBrush,
    }, TextBlock.TextProperty, text.Upper());

    /// <summary>
    /// Ha cambiado el idioma: las lecturas se vuelven a escribir con los separadores del nuevo.
    /// </summary>
    /// <remarks>
    /// Sin rehacer las filas, que es lo que llevaría la lista arriba del todo: cada una cambia su
    /// número como en cualquier sondeo.
    /// </remarks>
    public void LanguageChanged()
    {
        foreach (var reading in _sensors.ReadAll())
            if (_built.TryGetValue(reading.Id, out var row)) row.Update(reading);
    }

    public void SensorsChanged()
    {
        var all = _sensors.ReadAll();
        RefreshDevices(all);

        // El «?? ""» no sobra: TextBox.Text vale null hasta que alguien escribe, y comparar null
        // con "" daba siempre distinto, así que la vía rápida no se tomaba nunca y la lista se
        // rehacía entera —y el desplazamiento saltaba arriba— cada dos segundos.
        if (all.Count != _shownCount || (_filter.Text ?? "") != _shownFilter)
        {
            Rebuild();
            return;
        }

        foreach (var reading in all)
            if (_built.TryGetValue(reading.Id, out var row)) row.Update(reading);
    }

    /// <summary>
    /// Los dispositivos que hay ahora mismo. Se rehace la lista solo si cambian: tocarla en cada
    /// sondeo cerraría el desplegable en las narices del usuario.
    /// </summary>
    private void RefreshDevices(IReadOnlyList<SensorReading> all)
    {
        var devices = all
            .Select(r => r.Native.HardwareName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Create(Loc.Culture, ignoreCase: true))
            .ToList();

        string signature = string.Join('\n', devices);
        if (signature == _shownDevices) return;
        _shownDevices = signature;

        _suspend = true;
        try
        {
            string? chosen = _device.SelectedItem as string;
            _device.Items.Clear();
            _device.Items.Add(EveryDevice);
            foreach (string device in devices) _device.Items.Add(device);
            _device.SelectedItem = chosen is not null && devices.Contains(chosen, StringComparer.Ordinal) ? chosen : EveryDevice;
        }
        finally
        {
            _suspend = false;
        }
    }

    private void Rebuild()
    {
        string filter = _filter.Text ?? "";
        string? device = _device.SelectedItem as string;

        var all = _sensors.ReadAll();
        _shownFilter = filter;
        _shownCount = all.Count;

        _built.Clear();
        _rows.Children.Clear();

        var matching = all
            .Where(r => device is null || string.Equals(r.Native.HardwareName, device, StringComparison.Ordinal))
            .Where(r => filter.Length == 0
                        || r.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || r.Native.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _offline.IsVisible = all.Count == 0;
        _dot.IsVisible = all.Count > 0;
        if (all.Count == 0)
        {
            _empty.IsVisible = false;
            Loc.Set(_count, TextBlock.TextProperty, Text.Empty);
            Loc.Set(_chosen, TextBlock.TextProperty, Text.Empty);
            return;
        }

        _empty.IsVisible = matching.Count == 0;

        foreach (var hardware in matching.GroupBy(r => r.Native.HardwareName))
        {
            _rows.Children.Add(Group(hardware.Key));
            foreach (var reading in hardware)
            {
                var row = new SensorRow(reading, Choose);
                _built[reading.Id] = row;
                _rows.Children.Add(row);
            }
        }

        Loc.Set(_count, TextBlock.TextProperty, filter.Length == 0 && device is null
            ? Loc.Plural("sensors.count", all.Count)
            : Loc.Plural("sensors.count.filtered", all.Count, matching.Count));

        ShowChosen();
        if (_selected is { } selected && _built.TryGetValue(selected, out var chosen)) chosen.Highlight(true);
    }

    /// <summary>La cabecera de un aparato. En el realce, que es lo que separa un bloque del siguiente.</summary>
    private static Control Group(string name) => new Border
    {
        Background = Palette.Surface2Brush,
        BorderBrush = Palette.LineBrush,
        BorderThickness = new Thickness(0, 1, 0, 1),
        Padding = new Thickness(16, 9),
        Child = new TextBlock
        {
            Text = name.ToUpperInvariant(),
            FontFamily = AppFonts.Display,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = 1,
            Foreground = Palette.AccentBrush,
        },
    };

    private void ClearFilters()
    {
        _suspend = true;
        try
        {
            _filter.Text = "";
            _device.SelectedIndex = 0;
        }
        finally
        {
            _suspend = false;
        }

        Rebuild();
    }

    private void Choose(string id)
    {
        if (_selected is { } previous && _built.TryGetValue(previous, out var old)) old.Highlight(false);
        _selected = id;
        if (_built.TryGetValue(id, out var row)) row.Highlight(true);
        ShowChosen();
    }

    private void ShowChosen()
    {
        bool has = _selected is not null && _built.ContainsKey(_selected);
        _copy.IsEnabled = has;
        _use.IsEnabled = has;

        if (!has)
        {
            Loc.Set(_chosen, TextBlock.TextProperty, Loc.L("sensors.none-chosen"));

            // Soltando antes el enlace a la de los datos: con él vivo, cambiar la letra en Ajustes
            // ponía esta frase en monoespaciada.
            _chosenMono?.Dispose();
            _chosenMono = null;
            _chosen.FontFamily = AppFonts.Ui;
            _chosen.Foreground = Palette.FaintBrush;
            return;
        }

        Loc.Set(_chosen, TextBlock.TextProperty, Text.Raw(_selected));
        _chosenMono ??= AppFonts.FollowData(_chosen);
        _chosen.Foreground = Palette.MutedBrush;
    }

    /// <summary>
    /// Copia el identificador elegido. No lanza: la llaman sin esperarla.
    /// </summary>
    /// <remarks>
    /// El portapapeles es del sistema y lo puede tener cogido otro proceso. Como el botón la lanza y
    /// se olvida, lo que se escapara de aquí sería una excepción sin observar, y no copiar una cadena
    /// no merece ni eso.
    /// </remarks>
    private async Task CopyAsync()
    {
        if (_selected is null || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;

        try
        {
            await clipboard.SetTextAsync(_selected);
            if (TopLevel.GetTopLevel(this) is MainWindow window) window.Toast(Loc.L("sensors.copied"), Icons.Copy);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Loc.Set(_chosen, TextBlock.TextProperty, Loc.L("sensors.copy-failed", e.Message));
        }
    }

    private void UseInEditor()
    {
        if (_selected is null) return;

        _editor.UseSensor(_selected);
        if (TopLevel.GetTopLevel(this) is MainWindow window)
        {
            window.GoToEditor();
            window.Toast(Loc.L("sensors.used", _selected), Icons.Send);
        }
    }

    /// <summary>
    /// La primera entrada del desplegable de dispositivos, que no es un dispositivo.
    /// </summary>
    /// <remarks>
    /// Un objeto y no una cadena: un aparato podría llamarse igual, y la cadena cambia con el idioma.
    /// </remarks>
    private sealed record AllDevices : ILabeled
    {
        public Text Label => Loc.L("sensors.device.all");

        public override string ToString() => Label.ToString();
    }

    private static readonly AllDevices EveryDevice = new();

    /// <summary>Una fila: identificador canónico, nombre del backend y la última lectura.</summary>
    private sealed class SensorRow : Border
    {
        /// <summary>
        /// El cursor de la mano, uno para todas las filas.
        /// </summary>
        /// <remarks>
        /// Las filas se rehacen con cada tecla del filtro y son más de trescientas, así que uno por
        /// fila eran trescientos objetos por tecla. No se suelta a propósito: Avalonia guarda los
        /// cursores estándar en una caché estática suya, y soltarlo tiraría un recurso compartido.
        /// </remarks>
        private static readonly Cursor Hand = new(StandardCursorType.Hand);

        private readonly TextBlock _id;
        private readonly TextBlock _value;
        private readonly TextBlock _unit;
        private readonly Border _mark;

        public SensorRow(SensorReading reading, Action<string> choose)
        {
            BorderBrush = Palette.LineSoftBrush;
            BorderThickness = new Thickness(0, 0, 0, 1);
            Cursor = Hand;
            Focusable = true;

            _id = AppFonts.Mono(new TextBlock
            {
                Text = reading.Id,
                FontSize = 15,
                Foreground = Palette.MutedBrush,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            var name = new TextBlock
            {
                Text = reading.Native.Name,
                FontSize = 15,
                Foreground = Palette.MutedBrush,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            _value = AppFonts.Mono(new TextBlock
            {
                FontSize = 15,
                Foreground = Palette.TextBrush,
                VerticalAlignment = VerticalAlignment.Center,
            });

            _unit = AppFonts.Mono(new TextBlock
            {
                FontSize = 15,
                Foreground = Palette.FaintBrush,
                Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var reading2 = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { _value, _unit },
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions(Columns),
                ColumnSpacing = 16,
                Margin = new Thickness(16, 7),
            };
            Grid.SetColumn(name, 1);
            Grid.SetColumn(reading2, 2);
            grid.Children.Add(_id);
            grid.Children.Add(name);
            grid.Children.Add(reading2);

            // El filo del realce va por dentro, en los tres primeros píxeles, y no como borde de la
            // fila: el borde de Avalonia es de un solo color para los cuatro lados, y abajo hace
            // falta la línea de separación. Así el texto tampoco se mueve al elegir la fila.
            _mark = new Border
            {
                Width = 3,
                Background = Palette.AccentBrush,
                HorizontalAlignment = HorizontalAlignment.Left,
                IsVisible = false,
            };

            Child = new Panel { Children = { _mark, grid } };

            Update(reading);
            AutomationProperties.SetName(this, reading.Id);

            PointerPressed += (_, _) => choose(reading.Id);
            PointerEntered += (_, _) => { if (Background is null) _id.Foreground = Palette.AccentBrush; };
            PointerExited += (_, _) => { if (Background is null) _id.Foreground = Palette.MutedBrush; };

            // Con el teclado: las flechas mueven el foco entre filas y elegir es pulsar. Sin esto, la
            // lista solo se puede recorrer con el ratón, que con trescientas filas es un castigo.
            KeyDown += (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Space)
                {
                    e.Handled = true;
                    choose(reading.Id);
                }
                else if (e.Key is Key.Down or Key.Up)
                {
                    e.Handled = true;
                    Step(e.Key == Key.Down, choose);
                }
            };
        }

        /// <summary>Salta a la fila de al lado, saltándose las cabeceras de grupo.</summary>
        private void Step(bool down, Action<string> choose)
        {
            if (Parent is not StackPanel list) return;

            int index = list.Children.IndexOf(this);
            for (int i = index + (down ? 1 : -1); i >= 0 && i < list.Children.Count; i += down ? 1 : -1)
            {
                if (list.Children[i] is not SensorRow next) continue;

                next.Focus();
                next.BringIntoView();
                if (AutomationProperties.GetName(next) is { } id) choose(id);
                return;
            }
        }

        public void Update(SensorReading reading)
        {
            if (reading.Value is { } value)
            {
                _value.Text = value.ToString("0.###", Loc.Culture);
                _unit.Text = reading.Unit;
            }
            else
            {
                _value.Text = TextComposer.MissingValue;
                _unit.Text = "";
            }
        }

        public void Highlight(bool on)
        {
            Background = on ? Palette.AccentSoftBrush : null;
            _id.Foreground = on ? Palette.AccentBrush : Palette.MutedBrush;
            _mark.IsVisible = on;
        }
    }
}
