using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VibeScreen.App.Localization;
using VibeScreen.App.Theme;
using VibeScreen.Core.Rendering;

namespace VibeScreen.App.Views;

/// <summary>Los gustos de quien mira la ventana: el realce, la letra, el idioma y con qué se pinta.</summary>
/// <remarks>
/// Aparte de la pestaña «Panel» a propósito: allí todo manda sobre el panel y sale por la tubería del
/// servicio, y esto no sale de esta máquina ni de este usuario. Se guarda al tocarlo, sin botón de
/// aceptar: lo que se ve al cambiarlo ya es el resultado, así que un botón solo añadiría una duda.
/// </remarks>
public sealed class SettingsView : UserControl
{
    private readonly AppSettings _settings;
    private readonly Action _changed;
    private readonly Action _restart;

    private readonly List<(string Key, Button Button)> _swatches = [];

    // Lo crea Build, que corre en el constructor y otra vez al volver a los ajustes de fábrica: un
    // control no puede estar en dos árboles, y reutilizarlo tiraba la ventana. Sin inicializador aquí,
    // que solo haría creer que este es el que se usa.
    private TextBlock _message = null!;

    /// <summary>
    /// El guardado espera a que el usuario pare.
    /// </summary>
    /// <remarks>
    /// El selector de color dispara su evento con cada movimiento del ratón, y cada uno escribía el
    /// fichero entero en el hilo de la interfaz. Lo que se ve se aplica al instante —para eso está
    /// el selector—; lo que va al disco, cuando la mano se detiene.
    /// </remarks>
    private readonly DispatcherTimer _saveDelay = new() { Interval = TimeSpan.FromMilliseconds(400) };

    /// <param name="changed">Se llama tras cada cambio, para que la ventana se lo aplique a sí misma.</param>
    /// <param name="restart">Vuelve a abrir la aplicación, que es lo único que cambia el modo de render.</param>
    public SettingsView(AppSettings settings, Action changed, Action restart)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(changed);
        ArgumentNullException.ThrowIfNull(restart);

        _settings = settings;
        _changed = changed;
        _restart = restart;

        _saveDelay.Tick += (_, _) =>
        {
            _saveDelay.Stop();
            Loc.Set(_message, TextBlock.TextProperty, AppSettingsStore.Save(_settings) ?? Text.Empty);
        };

        Content = Build();
    }

    private Control Build()
    {
        _message = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Foreground = Palette.BadBrush,
        };

        var grid = new Grid12();
        grid.Children.Add(Grid12.Span(Appearance(), 7));
        grid.Children.Add(Grid12.Span(new StackPanel
        {
            Spacing = 20,
            Children = { BuildRender(), Storage() },
        }, 5));

        var page = new StackPanel
        {
            Spacing = 22,
            Children =
            {
                Blocks.ViewHeader(Loc.L("settings.title"), Loc.L("settings.subtitle")),
                grid,
                _message,
            },
        };

        return Blocks.Ui(new ScrollViewer
        {
            Padding = new Thickness(32, 26, 32, 32),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = page,
        });
    }

    private Control Appearance()
    {
        var theme = new Segmented<AppTheme>(
            [
                new(AppTheme.Dark, Loc.L("settings.theme.dark"), Icons.Moon),
                new(AppTheme.Light, Loc.L("settings.theme.light"), Icons.Sun),
                new(AppTheme.System, Loc.L("settings.theme.system"), Icons.Monitor),
            ],
            _settings.Theme,
            SetTheme);

        // Ocho y ninguno más, en vez de la rueda de color de antes. Un realce ya no es un color sino
        // una pareja —uno que se lee sobre casi negro y otro sobre blanco—, y un color a medida solo
        // puede ser la mitad de eso: el elegido mirando el modo oscuro se quedaría ilegible al pasar
        // al claro, y no hay forma de adivinar cuál querría el usuario al otro lado.
        var swatches = new WrapPanel { ItemSpacing = 10, LineSpacing = 10 };
        _swatches.Clear();
        foreach (var choice in Palette.Accents) swatches.Children.Add(Swatch(choice.Name, choice.Id));
        MarkChosen();

        var fonts = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 320 };

        // Sale marcada la que se está usando, que es la respuesta a la única pregunta que tiene quien
        // abre esto. Puede no ser la elegida, si aquella no está en esta máquina.
        string used = AppFonts.Resolve(_settings.Font);
        if (!AppFonts.Available.Contains(used, StringComparer.Ordinal)) fonts.Items.Add(used);
        foreach (string family in AppFonts.Available) fonts.Items.Add(family);
        fonts.SelectedItem = used;
        fonts.SelectionChanged += (_, _) =>
        {
            if (fonts.SelectedItem is not string family) return;
            _settings.Font = family;
            Apply();
            Toast(Loc.L("settings.font.changed"), Icons.Check);
        };

        return Blocks.Card(Loc.L("settings.appearance"), null,
            Blocks.Field(Loc.L("settings.theme"), theme, Loc.L("settings.theme.help")),
            Blocks.Field(Loc.L("settings.accent"), swatches, Loc.L("settings.accent.help")),
            Blocks.Field(Loc.L("settings.font"), fonts, Loc.L("settings.font.help")),
            Blocks.Field(Loc.L("settings.language"), Languages(), Loc.L("settings.language.help")),
            Sample());
    }

    /// <summary>
    /// El desplegable del idioma.
    /// </summary>
    /// <remarks>
    /// Cada idioma va escrito en su propia lengua, y no en la de la ventana: quien no entiende el
    /// idioma puesto tiene que poder encontrar el suyo. «El del sistema» sí va traducido, con el
    /// idioma al que lleva entre paréntesis, que desde aquí no se ve.
    /// </remarks>
    private ComboBox Languages()
    {
        var box = Forms.Labeled(new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch });

        string system = Loc.Languages.First(l => l.Code == Loc.System).Name;
        box.Items.Add(new LanguageOption(null, Loc.L("settings.language.system", system)));
        foreach (var (code, name) in Loc.Languages) box.Items.Add(new LanguageOption(code, Text.Raw(name)));

        box.SelectedItem = box.Items.OfType<LanguageOption>().FirstOrDefault(o => o.Code == _settings.Language)
                           ?? box.Items[0];
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is not LanguageOption option || option.Code == _settings.Language) return;

            _settings.Language = option.Code;
            Loc.Use(option.Code);
            _changed();

            _saveDelay.Stop();
            _saveDelay.Start();

            string name = Loc.Languages.First(l => l.Code == Loc.Current).Name;
            Toast(Loc.L("settings.language.changed", name), Icons.Check);
        };
        return box;
    }

    /// <summary>Una opción del desplegable de idioma. Null es «el del sistema».</summary>
    private sealed record LanguageOption(string? Code, Text Label) : ILabeled
    {
        public override string ToString() => Label.ToString();
    }

    /// <summary>
    /// La muestra: lo que se va a ver de verdad con la letra y el realce elegidos.
    /// </summary>
    /// <remarks>
    /// Un identificador, una fila de cifras con sus unidades, una barra y las dos pastillas de
    /// estado. Es lo que hay en las otras tres secciones, en pequeño: elegir una letra por su nombre
    /// en un desplegable no dice nada sobre si el 0 se distingue de la O.
    /// </remarks>
    private Control Sample()
    {
        var stack = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Loc.Set(new TextBlock
                {
                    FontFamily = AppFonts.Display,
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold,
                    LetterSpacing = 1.2,
                    Foreground = Palette.AccentBrush,
                }, TextBlock.TextProperty, Loc.L("settings.sample").Upper()),
                Blocks.Data("cpu/0/temperature/package", 15),

                // Las cifras con los separadores del idioma, que es como salen en el resto de la ventana.
                Loc.Set(Blocks.Data("", 14, Palette.MutedBrush), TextBlock.TextProperty,
                    Text.Of(() => $"{Loc.Number(49)} °C · {Loc.Number(37923)} B/s · 0123456789")),
                new ProgressBar { Minimum = 0, Maximum = 100, Value = 62, Height = 8 },
                new WrapPanel
                {
                    ItemSpacing = 8,
                    LineSpacing = 8,
                    Children =
                    {
                        Blocks.Flag(Loc.L("settings.sample.good"), Tone.Good),
                        Blocks.Flag(Loc.L("settings.sample.warn"), Tone.Warn, Icons.Alert),
                    },
                },
            },
        };

        return new Border
        {
            Background = Palette.Surface2Brush,
            BorderBrush = Palette.LineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 14),
            Child = stack,
        };
    }

    /// <summary>Dónde vive el fichero de ajustes, y el botón que lo deja como estaba de fábrica.</summary>
    private Control Storage()
    {
        var path = Blocks.Data(AppSettingsStore.File, 14, Palette.MutedBrush);
        path.TextWrapping = TextWrapping.NoWrap;
        path.TextTrimming = TextTrimming.CharacterEllipsis;

        var copy = new Button
        {
            Classes = { "icon" },
            Content = Icons.Make(Icons.Copy, Palette.TextBrush, 17),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Loc.Set(copy, Avalonia.Automation.AutomationProperties.NameProperty, Loc.L("settings.storage.copy"));
        copy.Click += (_, _) => _ = CopyPathAsync();

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        Grid.SetColumn(copy, 1);
        row.Children.Add(new Border
        {
            Background = Palette.Surface2Brush,
            BorderBrush = Palette.LineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = Palette.ControlRadius,
            Padding = new Thickness(12, 8),
            Child = path,
        });
        row.Children.Add(copy);

        return Blocks.Card(Loc.L("settings.storage"), null,
            row,
            Blocks.Help(Loc.L("settings.storage.help")),
            Blocks.Action(Loc.L("settings.reset"), Icons.Restore, Reset, "danger"));
    }

    /// <summary>
    /// Copia la ruta. No lanza: la llaman sin esperarla.
    /// </summary>
    /// <remarks>
    /// El portapapeles es del sistema y lo puede tener cogido otro proceso. Como el botón la lanza y
    /// se olvida, lo que se escapara de aquí sería una excepción sin observar, y no copiar una
    /// cadena no merece ni eso.
    /// </remarks>
    private async Task CopyPathAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;

        try
        {
            await clipboard.SetTextAsync(AppSettingsStore.File);
            Toast(Loc.L("settings.storage.copied"), Icons.Copy);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Loc.Set(_message, TextBlock.TextProperty, Loc.L("settings.storage.copy-failed", e.Message));
        }
    }

    /// <summary>
    /// Con qué se pinta la ventana.
    /// </summary>
    /// <remarks>
    /// El modo se fija al construir la aplicación, antes de que exista ninguna ventana, así que
    /// cambiarlo obliga a reiniciar. A cambio, no hace falta que el usuario se arriesgue: el arranque
    /// apunta el intento y lo borra antes de usarlo, de modo que un modo con el que no se vea nada se
    /// queda en un arranque y el siguiente vuelve solo al anterior.
    /// </remarks>
    private Control BuildRender()
    {
        var forced = AppRender.Forced();

        // En automático, el modo elegido no dice con qué se está pintando: Avalonia recorre su lista
        // y se queda con el primero que arranque. Se enseña el de verdad, que es la pregunta que
        // tiene cualquiera que venga aquí con un parpadeo.
        var now = AppRender.Label(Program.Render.Mode);
        Text? note = null;
        if (AppRender.Effective() is { } running)
        {
            if (running != Program.Render.Mode) note = Loc.L("settings.render.running", AppRender.Short(running));
        }
        else if (Program.Render.Mode == RenderMode.Auto)
        {
            note = AppRender.Chain;
        }

        var modes = Forms.Labeled(new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = forced is null });
        foreach (var mode in AppRender.Available) modes.Items.Add(new EnumOption<RenderMode>(mode, AppRender.Label(mode)));

        var wanted = _settings.RenderTrying ?? _settings.Render;
        modes.SelectedItem = modes.Items.OfType<EnumOption<RenderMode>>().FirstOrDefault(o => o.Value == wanted);

        var next = Blocks.Stat(Loc.L("settings.render.next"), AppRender.Label(_settings.RenderTrying ?? _settings.Render), data: false);
        void ShowNext()
        {
            if (next is StackPanel stack && stack.Children[1] is TextBlock value)
                Loc.Set(value, TextBlock.TextProperty, AppRender.Label(_settings.RenderTrying ?? _settings.Render));
        }

        modes.SelectionChanged += (_, _) =>
        {
            if (modes.SelectedItem is not EnumOption<RenderMode> option) return;

            // Elegir el que ya se usa no es probar nada: se limpia el intento en vez de dejar una
            // confirmación pendiente por un arranque sin ningún riesgo.
            _settings.RenderTrying = option.Value == _settings.Render ? null : option.Value;
            ShowNext();
            Loc.Set(_message, TextBlock.TextProperty, AppSettingsStore.Save(_settings) ?? Text.Empty);
        };

        var stats = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16 };
        var current = Blocks.Stat(Loc.L("settings.render.now"), now, note, data: false);
        Grid.SetColumn(next, 1);
        stats.Children.Add(current);
        stats.Children.Add(next);

        return Blocks.Card(Loc.L("settings.render"), null,
            stats,
            Blocks.Field(Loc.L("settings.render.startup"), modes),
            Blocks.Action(Loc.L("settings.render.apply"), Icons.Restore, _restart),
            Blocks.Help(Loc.L("settings.render.help")),
            Blocks.Help(forced is { } hatch
                ? Loc.L("settings.render.forced", AppRender.Variable, hatch.ToString().ToLowerInvariant())
                : Loc.L("settings.render.hatch", AppRender.Variable)));
    }

    /// <summary>
    /// El color va en un borde dentro del botón y no en su fondo: el fondo de un botón lo pisan sus
    /// propios estados —pulsado, con el ratón encima— y el realce elegido se veía gris justo después
    /// de elegirlo. El botón se queda con el aro, que es lo que dice cuál está puesto.
    /// </summary>
    /// <param name="key">
    /// Lo que se guarda: el identificador de uno de los ocho, no su <c>#RRGGBB</c>. El color depende
    /// de la paleta que esté puesta, y guardar el ya resuelto dejaría un tono de fondo negro encima
    /// de fondo blanco en cuanto se cambiara de modo.
    /// </param>
    private Control Swatch(Text name, string key)
    {
        var color = Palette.ParseAccent(key);
        var button = new Button
        {
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(color),
            BorderThickness = new Thickness(1),
            BorderBrush = Palette.SwatchEdgeBrush,
            Content = Icons.Make(Icons.Check, Palette.OnAccentBrush, 17),
        };

        // La marca solo se ve en la elegida. Se deja puesta y transparente en las demás para que
        // todas midan lo mismo: sin ella, la elegida crecería al marcarla.
        if (button.Content is Viewbox box && box.Child is Avalonia.Controls.Shapes.Path path)
            path.StrokeThickness = 2.6;
        button.Opacity = 1;

        Loc.Set(button, ToolTip.TipProperty, name);
        Loc.Set(button, Avalonia.Automation.AutomationProperties.NameProperty, name);
        _swatches.Add((key, button));

        button.Click += (_, _) =>
        {
            _settings.Accent = key;
            Apply();
            Toast(Loc.L("settings.accent.changed", name.Lower()), Icons.Check);
        };
        return button;
    }

    /// <summary>Le pone el aro al que está puesto, que puede no ser ninguno si el color es a medida.</summary>
    /// <summary>
    /// Le pone el aro al realce que está puesto.
    /// </summary>
    /// <remarks>
    /// El aro va por fuera, separado del color por un anillo del fondo, que es lo que lo hace
    /// visible encima de un cuadro del mismo tono. Y la marca de dentro solo se enseña en el
    /// elegido: con ocho marcas no se sabría cuál es.
    /// </remarks>
    private void MarkChosen()
    {
        foreach ((string key, Button button) in _swatches)
        {
            bool on = string.Equals(key, _settings.Accent, StringComparison.OrdinalIgnoreCase);
            button.BorderBrush = on ? Palette.TextBrush : Palette.SwatchEdgeBrush;
            button.BorderThickness = new Thickness(on ? 2 : 1);
            if (button.Content is Viewbox box) box.IsVisible = on;
        }
    }

    /// <summary>
    /// La paleta ha cambiado desde fuera —el atajo de la barra de título— y el formulario tiene que
    /// enterarse: enseña el tema puesto y las muestras de color llevan el tono ya resuelto.
    /// </summary>
    public void ThemeChanged() => Content = Build();

    /// <summary>
    /// Cambia la paleta de la ventana.
    /// </summary>
    /// <remarks>
    /// Rehace el formulario entero, como el volver a fábrica. Los pinceles son compartidos y se
    /// repintan solos, pero las muestras de color llevan cada una su propio pincel con el tono del
    /// realce resuelto para la paleta de antes, y esos hay que volver a hacerlos. Rehacerlo todo es
    /// más corto que llevar la cuenta de qué sobrevive.
    /// </remarks>
    private void SetTheme(AppTheme theme)
    {
        _settings.Theme = theme;
        Palette.Apply(theme, _settings.Accent);
        Content = Build();
        _changed();

        _saveDelay.Stop();
        _saveDelay.Start();

        // Con «el del sistema» el aviso dice hacia dónde ha caído, que desde aquí no se sabe.
        Toast(theme switch
        {
            AppTheme.Light => Loc.L("toast.theme.light"),
            AppTheme.System => Palette.IsLight ? Loc.L("toast.theme.system-light") : Loc.L("toast.theme.system-dark"),
            _ => Loc.L("toast.theme.dark"),
        }, Palette.IsLight ? Icons.Sun : Icons.Moon);
    }

    private void Reset()
    {
        _settings.Theme = AppTheme.Dark;
        _settings.Accent = Palette.DefaultAccent;
        _settings.Font = null;
        _settings.Language = null;
        Loc.Use(null);
        Palette.Apply(_settings.Theme, _settings.Accent);

        // El render no se cambia en caliente, así que de fábrica se pide para el siguiente arranque,
        // igual que cualquier otro cambio de modo. Si ya se está en automático no hay nada que probar.
        _settings.RenderTrying = _settings.Render == RenderMode.Auto ? null : RenderMode.Auto;

        // El formulario entero antes de aplicar, porque el selector y los desplegables tienen que decir
        // lo que hay ahora y ninguno se deja cambiar sin disparar su propio evento.
        Content = Build();
        Apply();
        Toast(Loc.L("settings.reset.done"), Icons.Restore);
    }

    /// <summary>
    /// Saca un aviso flotante. La capa de avisos es de la ventana: si esto no está dentro de ella,
    /// no hay a quién contárselo.
    /// </summary>
    private void Toast(Text text, StreamGeometry icon)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow window) window.Toast(text, icon);
    }

    private void Apply()
    {
        Palette.SetAccent(Palette.ParseAccent(_settings.Accent));
        MarkChosen();
        _changed();

        _saveDelay.Stop();
        _saveDelay.Start();
    }
}
