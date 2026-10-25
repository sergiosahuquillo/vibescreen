using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Themes.Fluent;
using VibeScreen.App.Localization;
using VibeScreen.App.Updates;
using VibeScreen.App.Views;

namespace VibeScreen.App;

/// <summary>
/// La aplicación de escritorio. Vive en la bandeja: cerrar la ventana no la termina, porque el
/// usuario espera que el panel siga funcionando y que la GUI vuelva de un clic.
/// </summary>
/// <remarks>
/// Sin XAML a propósito. La interfaz se construye en C#, que es lo que le va bien a un panel de
/// propiedades que cambia entero según el widget seleccionado, y deja todos los errores en la
/// compilación en lugar de a mitad de la ejecución.
/// </remarks>
public sealed class VibeScreenApp : Application, IDisposable
{
    private readonly AppSettings _settings = AppSettingsStore.Load();
    private readonly Updater _updater;
    private ServiceLink? _service;
    private MainWindow? _window;
    private TrayIcon? _tray;

    public VibeScreenApp() => _updater = new Updater(_settings);

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://VibeScreen.App/"))
        {
            Source = new Uri("avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml"),
        });
        Styles.Add(Palette.Styles());
        Styles.Add(Theme.ControlThemes.Styles());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Aquí y no en Initialize: la paleta fija la variante de Fluent y repinta sus recursos de
        // realce, y para eso la aplicación tiene que estar del todo en pie.
        Palette.Apply(_settings.Theme, _settings.Accent);

        // El idioma, antes de la bandeja: el menú de la bandeja se construye antes que la ventana, y
        // con la aplicación ya en pie es cuando el idioma llega a los recursos y lo enlazado lo sigue.
        Loc.Use(_settings.Language);

        // Con «el del sistema» puesto, seguirlo de verdad: cambiarlo en Windows tiene que repintar
        // la ventana sin reiniciarla. Con oscuro o claro elegidos a mano no se toca nada, que para
        // eso se han elegido a mano.
        if (PlatformSettings is { } platform)
            platform.ColorValuesChanged += (_, _) =>
            {
                if (_settings.Theme != AppTheme.System) return;
                Palette.Apply(AppTheme.System, _settings.Accent);

                // Si la ventana no existe todavía, nace con la paleta ya puesta y no hay que avisarla.
                _window?.PaletteChanged();
            };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _service = new ServiceLink();

            // La ventana se cierra a la bandeja, así que quien decide cuándo termina el proceso es
            // el menú de la bandeja y no la última ventana.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) =>
            {
                // La ventana cancela su propio cierre para irse a la bandeja, así que OnClosed no
                // llega nunca y hay que soltarle las cosas aquí, que es donde el proceso sí termina.
                _window?.Dispose();

                // Por si el guardado de los ajustes estaba esperando a que el usuario soltara el
                // ratón: lo que hay en memoria siempre es lo último, así que esto lo cierra.
                AppSettingsStore.Save(_settings);
                Dispose();
            };

            _tray = BuildTrayIcon(desktop);
            TrayIcon.SetIcons(this, [_tray]);

            // Abrirla otra vez —el acceso directo con ella escondida en la bandeja— la trae delante en
            // vez de arrancar una segunda.
            if (Program.Instance is { } instance)
                instance.Woken += () => Dispatcher.UIThread.Post(Show);

            // El icono lleva el realce, igual que la marca de la barra de título, así que se rehace
            // cuando él cambia: al elegir otro en Ajustes y al pasar entre claro y oscuro, donde el
            // mismo realce es otro tono. Se mira el pincel porque es por donde pasan los dos caminos.
            //
            // Instalada, la barra de tareas no lo sigue y se queda con el rojo del acceso directo de
            // Inicio, y es a propósito: arreglarlo con un AppUserModelID propio duplica el botón al
            // anclar desde Inicio. Ver «Publicar una versión» en el README.
            Palette.AccentBrush.PropertyChanged += (_, e) =>
            {
                if (e.Property != SolidColorBrush.ColorProperty) return;

                var icon = AppIcon.Current();
                if (_tray is not null) _tray.Icon = icon;
                if (_window is not null) _window.Icon = icon;
            };

            Show();

            // Con la ventana ya abierta: según el modo, busca dentro de un rato o no hace nada.
            _updater.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private TrayIcon BuildTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var abrir = Loc.Set(new NativeMenuItem(), NativeMenuItem.HeaderProperty, Loc.L("tray.open"));
        abrir.Click += (_, _) => Show();

        var salir = Loc.Set(new NativeMenuItem(), NativeMenuItem.HeaderProperty, Loc.L("tray.quit"));
        salir.Click += (_, _) =>
        {
            _window?.Close();
            desktop.Shutdown();
        };

        var tray = new TrayIcon
        {
            Icon = AppIcon.Current(),
            ToolTipText = "VibeScreen",
            Menu = [abrir, new NativeMenuItemSeparator(), salir],
        };
        tray.Clicked += (_, _) => Show();
        return tray;
    }

    /// <summary>
    /// Vuelve a abrir la aplicación. Es lo único que cambia el modo de render, que se fija al
    /// construirla, así que sin esto habría que pedirle al usuario que la cerrara y la abriera él.
    /// </summary>
    private void Restart()
    {
        if (Environment.ProcessPath is not { } exe) return;

        // El ciclo de vida se comprueba antes de arrancar nada: si este proceso no se va a poder
        // cerrar, lanzar el otro solo deja dos ventanas abiertas peleándose por los mismos ajustes.
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        // Y el proceso nuevo antes de cerrar: si arrancarlo falla, al menos este sigue en pie. Con el
        // aviso de que viene a sustituirlo: si no, encontraría este todavía en marcha, lo despertaría
        // creyendo que es una segunda ventana y se iría, y al cerrar este no quedaría ninguna.
        Process.Start(new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            ArgumentList = { SingleInstance.RestartArgument },
        });

        _window?.Close();
        desktop.Shutdown();
    }

    /// <summary>
    /// Suelta la tubería del servicio y el icono de la bandeja.
    /// </summary>
    /// <remarks>
    /// El icono lleva un recurso del área de notificación, y soltarlo es lo que lo quita de ahí al
    /// terminar en lugar de dejar un hueco que solo desaparece cuando alguien pasa el ratón por
    /// encima. Se llama desde <c>desktop.Exit</c>, que es donde el proceso sí termina.
    /// </remarks>
    public void Dispose()
    {
        _updater.Dispose();
        _service?.Dispose();
        _service = null;
        _tray?.Dispose();
        _tray = null;
    }

    private void Show()
    {
        if (_window is null)
        {
            _window = new MainWindow(_service!, _settings, _updater, Restart);
            _window.Closing += (_, e) =>
            {
                // A la bandeja, no al cementerio.
                e.Cancel = true;
                _window.Hide();
            };
        }

        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }
}
