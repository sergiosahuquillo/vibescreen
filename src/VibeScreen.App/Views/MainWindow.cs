using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VibeScreen.App.Localization;
using VibeScreen.App.Theme;
using VibeScreen.Contracts;
using VibeScreen.Core.Rendering;

namespace VibeScreen.App.Views;

/// <summary>La ventana: cuatro pestañas, una barra de estado y el único temporizador de la aplicación.</summary>
/// <remarks>
/// El sondeo del servicio está aquí y no en cada vista para que no haya tres relojes pidiendo lo
/// mismo por la tubería. La ventana pregunta y reparte.
/// </remarks>
public sealed class MainWindow : Window, IDisposable
{
    private readonly ServiceLink _service;
    private readonly AppSettings _settings;
    private readonly RemoteSensorProvider _sensors = new();
    private readonly DispatcherTimer _timer;

    private readonly PanelView _panel;
    private readonly EditorView _editor;
    private readonly SensorsView _explorer;
    private readonly SettingsView _settingsView;

    private readonly TitleBar _titleBar;
    private readonly NavigationRail _rail;
    private readonly StatusBar _status;
    private readonly ContentControl _body;
    private readonly Toasts _toasts = new();

    /// <summary>Qué sección se está viendo. El sondeo lo mira para no trabajar de más.</summary>
    private AppView _view = AppView.Panel;

    private bool _busy;
    private bool _catalogue;
    private int _ticks;
    private bool _released;

    /// <summary>El idioma con el que se pintó lo que no se cambia solo. Ver <see cref="ApplyLanguage"/>.</summary>
    private string _language = Loc.Current;

    private bool _syncingLanguage;

    /// <summary>El idioma que el servicio no ha querido: no se le vuelve a pedir hasta cambiar de idioma.</summary>
    private string? _languageRefused;

    public MainWindow(ServiceLink service, AppSettings settings, Action restart)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(restart);
        _service = service;
        _settings = settings;

        Title = "VibeScreen";
        Icon = AppIcon.Current();
        Width = 1440;
        Height = 900;
        MinWidth = 960;
        MinHeight = 640;
        Background = Palette.BackgroundBrush;

        // La barra de título se dibuja aquí dentro, así que la ventana se queda con todo el hueco.
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = 40;

        // La monoespaciada de los datos, antes de construir nada que la lleve. La ventana también: el
        // cromo y las vistas se ponen encima la de interfaz, pero lo poco que siga heredando de aquí
        // es mejor que vaya en la elegida que en la de fábrica.
        AppFonts.UseData(settings.Font);
        AppFonts.Mono(this);

        _panel = new PanelView(service, _sensors);
        _editor = new EditorView(service, _sensors);
        _explorer = new SensorsView(_sensors, _editor);
        _settingsView = new SettingsView(settings, ApplySettings, restart);

        _titleBar = new TitleBar(this, ToggleTheme);
        _rail = new NavigationRail(Show);
        _status = new StatusBar();

        _body = new ContentControl { Content = _panel };

        var middle = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(_body, 1);
        middle.Children.Add(_rail);
        middle.Children.Add(_body);

        var root = new DockPanel();
        DockPanel.SetDock(_titleBar, Dock.Top);
        root.Children.Add(_titleBar);
        if (Trial(restart) is { } trial)
        {
            DockPanel.SetDock(trial, Dock.Top);
            root.Children.Add(trial);
        }
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);
        root.Children.Add(middle);

        // El resplandor de la esquina: casi no se ve, y sin él la ventana es una mancha plana. Va en
        // una capa aparte y no en el fondo de la ventana porque tiene que quedar por debajo de todo
        // sin teñir las tarjetas.
        var glow = new Border
        {
            Background = new RadialGradientBrush
            {
                Center = new RelativePoint(0.18, -0.08, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.18, -0.08, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.55, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.55, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Palette.GlowBrush.Color, 0),
                    new GradientStop(Colors.Transparent, 1),
                },
            },
            IsHitTestVisible = false,
        };

        Content = new Panel { Children = { glow, root, _toasts } };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Poll;

        Opened += (_, _) =>
        {
            // El rescate de Program solo salta si no se llegó a abrir nada: una avería de aquí en
            // adelante no es del modo de render y no tiene por qué cambiarlo.
            Program.WindowShown = true;
            Poll(null, EventArgs.Empty);
        };
    }

    /// <summary>
    /// El sondeo solo corre con la ventana a la vista.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cerrar esta ventana no la cierra: la aplicación vive en la bandeja y cancela el cierre para
    /// esconderla. Eso quiere decir que <c>OnClosed</c> no llega nunca, y el temporizador seguía
    /// vivo con la ventana oculta: cada segundo repintaba con Skia una vista previa que no ve nadie,
    /// preguntaba el estado por la tubería y hacía que el servicio releyera los sensores del tema.
    /// Ocho horas en la bandeja son ocho horas de eso, y en un portátil se nota.
    /// </para>
    /// <para>
    /// Se engancha a la visibilidad y no a <c>Closing</c> porque así da igual quién la esconda.
    /// </para>
    /// </remarks>
    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != IsVisibleProperty) return;

        if (IsVisible)
        {
            _timer.Start();
            Poll(null, EventArgs.Empty);
        }
        else
        {
            _timer.Stop();
        }
    }

    /// <summary>
    /// Saca un aviso flotante.
    /// </summary>
    /// <remarks>
    /// Lo pide la vista con <c>TopLevel.GetTopLevel(this) is MainWindow</c>, igual que el explorador
    /// pide saltar al editor: la capa de avisos es de la ventana, y pasarla por el constructor de
    /// cada vista sería arrastrarla por media aplicación para tres llamadas.
    /// </remarks>
    public void Toast(Text text, StreamGeometry? icon = null) => _toasts.Show(text, icon);

    /// <summary>Lleva al editor a trabajar con un sensor concreto, desde el explorador.</summary>
    public void GoToEditor() => Show(AppView.Editor);

    /// <summary>Cambia de sección.</summary>
    private void Show(AppView view)
    {
        _view = view;
        _rail.Show(view);
        _body.Content = view switch
        {
            AppView.Editor => _editor,
            AppView.Sensors => _explorer,
            AppView.Settings => _settingsView,
            _ => _panel,
        };

        // Un sondeo en cuanto se cambia de sección: la que llega no se ha refrescado mientras
        // estaba escondida, y esperar al siguiente segundo se nota como un parpadeo de datos viejos.
        Poll(null, EventArgs.Empty);
    }

    /// <summary>
    /// Conmuta entre claro y oscuro desde la barra de título, y dice cómo ha quedado.
    /// </summary>
    /// <remarks>
    /// Es el atajo, no el ajuste: cambiar aquí deja elegido «claro» u «oscuro» a secas, nunca «el
    /// del sistema». Quien quiera seguir al sistema lo dice en Ajustes, y entonces este botón sigue
    /// sirviendo para salirse de ahí un rato.
    /// </remarks>
    private bool ToggleTheme()
    {
        var next = Palette.IsLight ? AppTheme.Dark : AppTheme.Light;
        _settings.Theme = next;
        Palette.Apply(next, _settings.Accent);
        AppSettingsStore.Save(_settings);

        // Los ajustes enseñan el tema puesto con un segmentado, y acaba de dejar de ser verdad.
        _settingsView.ThemeChanged();

        // El cambio se ve solo, pero el aviso dice cuál de los dos ha quedado: con el atajo se pasa
        // de largo, y desde «el del sistema» no es evidente hacia dónde ha saltado.
        Toast(Palette.IsLight ? Loc.L("toast.theme.light") : Loc.L("toast.theme.dark"), Palette.IsLight ? Icons.Sun : Icons.Moon);
        return Palette.IsLight;
    }

    /// <summary>
    /// La paleta ha cambiado desde fuera de la ventana —Windows, con «el del sistema» elegido— y lo
    /// que la enseña tiene que enterarse.
    /// </summary>
    /// <remarks>
    /// Los colores cambian solos, porque los pinceles son compartidos; lo que no cambia solo es lo que
    /// dice cuál está puesta: el icono del conmutador de la barra y el formulario de Ajustes, cuyas
    /// muestras de realce llevan el tono resuelto para la paleta de antes.
    /// </remarks>
    public void PaletteChanged()
    {
        _titleBar.ShowTheme(Palette.IsLight);
        _settingsView.ThemeChanged();
    }

    /// <summary>
    /// La barra de «¿se ve bien?», o null si este arranque no está a prueba.
    /// </summary>
    /// <remarks>
    /// Es la mitad que falta de la vuelta atrás: el arranque ya borró el intento del disco, así que
    /// quedarse callado o cerrar la ventana devuelve el modo anterior al siguiente arranque. Esto es
    /// lo que hace falta para lo contrario, quedarse con el nuevo, y solo se puede pulsar si la
    /// ventana se ve, que es exactamente lo que se está preguntando.
    /// </remarks>
    private Control? Trial(Action restart)
    {
        if (Program.Render.Trying is not { } trying) return null;

        var answer = new Border
        {
            Background = Palette.SelectionBrush,
            BorderBrush = Palette.AccentBrush,
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
            Padding = new Avalonia.Thickness(14, 10),
        };

        var keep = Forms.Action(Loc.L("render.trial.keep"), () =>
        {
            _settings.Render = trying;
            _settings.RenderTrying = null;
            AppSettingsStore.Save(_settings);
            answer.IsVisible = false;
        });

        answer.Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            Children =
            {
                Loc.Set(new TextBlock
                {
                    Foreground = Palette.TextBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                }, TextBlock.TextProperty, Loc.L("render.trial.question", AppRender.Label(trying))),
                keep,
                Forms.Action(Loc.L("render.trial.revert", AppRender.Label(_settings.Render)), restart),
                Loc.Set(new TextBlock
                {
                    Foreground = Palette.MutedBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                }, TextBlock.TextProperty, Loc.L("render.trial.note")),
            },
        };

        return answer;
    }

    /// <summary>
    /// Lo que la ventana tiene que hacer por su cuenta cuando cambian los ajustes. El color viaja
    /// solo —los pinceles son compartidos— y la letra casi: hay que cambiarla en los recursos, y lo
    /// que la lleva se repinta.
    /// </summary>
    /// <remarks>
    /// Y el conmutador de la barra de título, que enseña el modo al que lleva: el tema se ha podido
    /// cambiar desde Ajustes, o volver al de fábrica, y el icono seguía diciendo el de antes. Al revés
    /// ya iba, porque el conmutador avisa a Ajustes en <see cref="ToggleTheme"/>.
    /// </remarks>
    private void ApplySettings()
    {
        AppFonts.UseData(_settings.Font);
        _titleBar.ShowTheme(Palette.IsLight);

        if (_language != Loc.Current) ApplyLanguage();
    }

    /// <summary>
    /// Lo que el cambio de idioma no arrastra solo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Casi todo se cambia solo: cada texto está enlazado al idioma con <see cref="Loc.Set{T}"/> y se
    /// vuelve a pintar, esté su vista delante o escondida. Lo que queda es lo que no es una propiedad
    /// de un control —lo que cada vista guarda ya escrito para no rehacerlo, la vista previa del
    /// panel, que dibuja el reloj con los nombres del idioma— y eso se avisa aquí a las cuatro, no
    /// solo a la que se ve.
    /// </para>
    /// </remarks>
    private void ApplyLanguage()
    {
        _language = Loc.Current;
        _panel.LanguageChanged();
        _editor.LanguageChanged();
        _explorer.LanguageChanged();
        Poll(null, EventArgs.Empty);
    }

    /// <summary>
    /// Le dice al servicio en qué idioma tiene que escribir el panel los días y los meses, si no es ya
    /// el de la ventana.
    /// </summary>
    /// <remarks>
    /// Se mira en cada sondeo y no solo al cambiar de idioma: así lo arregla también al conectar con un
    /// servicio que se arrancó con otro, o al que se le cambió desde otra sesión. Un servicio que no
    /// entiende la orden —uno más viejo que la ventana— no se vuelve a intentar con el mismo idioma: se
    /// queda en español y ya está.
    /// </remarks>
    private async void SyncPanelLanguage(ServiceStatus? status)
    {
        string wanted = Loc.Current;
        if (status is null || _syncingLanguage || status.Language == wanted || _languageRefused == wanted) return;

        _syncingLanguage = true;
        try
        {
            if (await _service.SetLanguageAsync(wanted).ConfigureAwait(true) is not null) _languageRefused = wanted;
        }
        catch (Exception)
        {
            // «async void»: lo que se escape tira la ventana. Se intenta otra vez en el siguiente sondeo.
        }
        finally
        {
            _syncingLanguage = false;
        }
    }

    private async void Poll(object? sender, EventArgs e)
    {
        if (_busy) return;
        _busy = true;
        _ticks++;

        try
        {
            // Solo repinta la vista que se está mirando. Antes el editor redibujaba con Skia una
            // vez por segundo estuviera o no delante, y con la previa del panel eso serían dos
            // repintados por segundo para enseñar uno. Así el total se queda en uno, y con el Panel
            // delante se hace menos trabajo que antes del rediseño.
            if (_view == AppView.Editor) _editor.Tick();
            else if (_view == AppView.Panel) _panel.Tick();

            var status = await _service.StatusAsync().ConfigureAwait(true);
            ShowConnection(status);
            SyncPanelLanguage(status);
            _panel.OnStatus(status);
            _editor.OnStatus(status);

            if (!_service.Connected)
            {
                _catalogue = false;
                _sensors.Clear();
                _sensors.Warnings = [];
                _editor.SensorsChanged();
                _explorer.SensorsChanged();
                return;
            }

            _sensors.Warnings = status?.Warnings ?? [];

            // La lista completa, una vez, en cuanto hay servicio: el editor la necesita para ofrecer
            // sensores aunque el usuario no pase por el explorador.
            if (!_catalogue)
            {
                _catalogue = true;
                _sensors.Merge(await _service.SensorsAsync().ConfigureAwait(true));
                _editor.SensorsChanged();
                _explorer.SensorsChanged();
            }

            // Para la previa basta con los sensores del tema que se está mirando: así el servicio
            // no refresca discos y red veinte veces por minuto. Una sola petición y no dos, aunque
            // haya dos previas: son vistas distintas y solo una está delante.
            var needed = _view switch
            {
                AppView.Editor => _editor.SensorIds,
                AppView.Panel => _panel.SensorIds,
                _ => [],
            };

            if (needed.Count > 0)
            {
                _sensors.Merge(await _service.SensorsAsync(needed).ConfigureAwait(true));
                if (_view == AppView.Editor) _editor.SensorsChanged();
                else _panel.SensorsChanged();
            }

            // La lista entera solo mientras el explorador está a la vista, y a la mitad de ritmo.
            if (_view == AppView.Sensors && _ticks % 2 == 0)
            {
                _sensors.Merge(await _service.SensorsAsync().ConfigureAwait(true));
                _explorer.SensorsChanged();
            }
        }
        catch (Exception error)
        {
            // El sondeo es «async void»: lo que se escape de aquí se lleva la aplicación por delante.
            // Ninguna avería hablando con el servicio justifica cerrarle la ventana al usuario, así
            // que se cuenta en la barra de estado y el siguiente ciclo lo vuelve a intentar.
            _status.Show(Loc.L("status.error"), Loc.Notice(Notice.From(error)), DeviceHealth.Off);
            _status.ShowRate(null);
            _rail.ShowDevice(null, Loc.L("rail.device.not-answering"), DeviceHealth.Off);
        }
        finally
        {
            _busy = false;
        }
    }

    private void ShowConnection(ServiceStatus? status)
    {
        if (status is null)
        {
            _status.Show(Loc.L("status.service-stopped"), _service.LastError ?? Loc.L("status.nobody-listening"), DeviceHealth.Off);
            _status.ShowRate(null);
            _rail.ShowDevice(null, Loc.L("rail.device.no-service"), DeviceHealth.Off);
            return;
        }

        var health = status.State switch
        {
            ServiceState.Painting => DeviceHealth.Painting,
            ServiceState.Searching => DeviceHealth.Searching,
            _ => DeviceHealth.Off,
        };

        Text detail = status.Detail is { } said ? Loc.Notice(said)
            : status.PortName is { } port ? Loc.L("status.model-on-port", status.Model ?? "", port)
            : Text.Empty;
        _status.Show(Labels.ServiceState(status.State), detail, health);
        _status.ShowRate(status.State == ServiceState.Painting ? status.BytesPerSecond : null);

        // En el rail cabe menos y hace falta menos: el modelo, y debajo el puerto con en qué anda.
        _rail.ShowDevice(
            status.Model,
            status.PortName is { } name ? Loc.L("rail.device.port-state", name, Labels.ServiceState(status.State).Lower()) : Loc.L("rail.device.no-panel"),
            health);
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    /// <summary>
    /// Suelta lo que la ventana tiene abierto: el temporizador, el editor y los sensores.
    /// </summary>
    /// <remarks>
    /// Se llama por dos caminos que no se excluyen —<see cref="OnClosed"/> y la salida de la bandeja,
    /// que es la que de verdad ocurre porque la ventana cancela su propio cierre—, así que lleva
    /// guardia: soltar dos veces no puede costar una excepción en el cierre del proceso.
    /// </remarks>
    public void Dispose()
    {
        if (_released) return;
        _released = true;

        _timer.Stop();
        _panel.Dispose();
        _editor.Dispose();
        _sensors.Dispose();
    }
}
