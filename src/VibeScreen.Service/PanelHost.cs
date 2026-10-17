using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VibeScreen.Contracts;
using VibeScreen.Core.Layout;
using VibeScreen.Core.Rendering;
using VibeScreen.Core.Sensors;
using VibeScreen.Protocol.Display;
using VibeScreen.Protocol.Rendering;

namespace VibeScreen.Service;

/// <summary>
/// El corazón del servicio: mantiene el panel abierto, pinta el layout vigente y sobrevive a que lo
/// desenchufen. Todas las órdenes del IPC acaban aquí.
/// </summary>
/// <remarks>
/// El panel no se comparte: nadie lo toca sin el cerrojo. El bucle de pintado lo coge y lo suelta en
/// cada ciclo de 50 ms, así que una orden que llegue por la tubería entra como mucho un ciclo después.
/// </remarks>
public sealed class PanelHost : IAsyncDisposable
{
    private readonly IConfigStore _store;
    private readonly ISensorProvider _sensors;
    private readonly IPanelConnector _connector;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private readonly RateWindow _rate = new(RateWindowLength);

    private ServiceConfig _config;
    private LayoutDocument? _layout;
    private PanelConnection? _panel;
    private LayoutRunner? _runner;
    private Stopwatch? _painting;
    private ServiceState _state = ServiceState.Starting;
    private Notice? _detail;
    private IReadOnlyList<Notice> _layoutWarnings = [];
    private int _sensorCount;
    private int _reconnects;
    private bool _connected;
    private int _stateLost;

    /// <summary>Cuánto mira atrás el medidor de caudal. Bastante para que no parpadee y poco para que reaccione.</summary>
    public static readonly TimeSpan RateWindowLength = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Cuánto se le deja arrancar al panel cuando el sistema vuelve de suspensión, antes de abrirlo.
    /// </summary>
    /// <remarks>
    /// El panel vuelve de quedarse sin corriente iniciando su pantalla, y una orientación que llegue
    /// mientras tanto la pisa el propio firmware. La implementación de referencia espera 5 s tras un
    /// RESET, que además incluye volver a aparecer en el USB; aquí el puerto ya suele estar, y cada
    /// segundo de espera es un segundo de pantalla en blanco.
    /// </remarks>
    public static readonly TimeSpan ResumeSettle = TimeSpan.FromSeconds(3);

    public PanelHost(IConfigStore store, ISensorProvider sensors, IPanelConnector connector, ILogger<PanelHost> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(sensors);
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _sensors = sensors;
        _connector = connector;
        _logger = logger;
        _config = store.Load();
    }

    /// <summary>
    /// Cómo se espera entre reintentos. Las comprobaciones lo sustituyen para no dormir de verdad.
    /// </summary>
    /// <remarks>
    /// <c>internal</c>, no público: es un punto de prueba y no API del servicio. Lo ve
    /// <c>VibeScreen.ServiceCheck</c> por el <c>InternalsVisibleTo</c> del proyecto.
    /// </remarks>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = (wait, cancellationToken) => Task.Delay(wait, cancellationToken);

    /// <summary>
    /// La carpeta en la que tienen que estar los temas que llegan por la tubería.
    /// </summary>
    /// <remarks>
    /// <c>internal</c> por lo mismo que <see cref="Delay"/>: las comprobaciones necesitan una carpeta
    /// suya para no escribir en la del usuario, y nada fuera del ensamblado puede aflojar la regla.
    /// </remarks>
    internal string LayoutRoot { get; set; } = LayoutPaths.Root;

    /// <summary>Conectar, pintar, y si el panel se cae, volver a intentarlo hasta que paren el servicio.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        CountSensors();
        LoadConfiguredLayout();

        int attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAsync(cancellationToken).ConfigureAwait(false);
                attempt = 0;
                await PaintAsync(cancellationToken).ConfigureAwait(false);

                // Pintar solo vuelve sin fallo cuando el panel ha podido perder su estado. Se cierra,
                // se le deja arrancar, y la siguiente vuelta lo abre y lo inicializa desde cero.
                _reconnects++;
                _connected = false;
                SetState(ServiceState.Searching, Notice.Of("panel.resumed", Math.Round(ResumeSettle.TotalSeconds)));
                _logger.LogInformation("El sistema vuelve de suspensión: se reabre el panel en {Segundos} s.",
                    ResumeSettle.TotalSeconds);

                await CloseAsync().ConfigureAwait(false);
                await Delay(ResumeSettle, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e)
            {
                if (_connected)
                {
                    _reconnects++;
                    _connected = false;
                }

                attempt++;
                var wait = ReconnectDelay.For(attempt);

                if (IsPanelFailure(e))
                {
                    SetState(ServiceState.Searching, Notice.Of("panel.retry", Math.Round(wait.TotalSeconds)).With(Notice.From(e)));
                    _logger.LogWarning("Sin panel: {Mensaje} Siguiente intento en {Segundos} s.", e.Message, wait.TotalSeconds);
                }
                else
                {
                    // Lo que no es el panel cayéndose tampoco puede matar el bucle. El caso real es el
                    // backend de sensores: LibreHardwareMonitor habla con el driver de ring0 y puede
                    // tirar cualquier cosa al volver de suspensión o si le actualizan el driver debajo.
                    // Se anota entera —con traza, porque no se esperaba— y se reintenta igual.
                    SetState(ServiceState.Searching,
                        Notice.Of("panel.unexpected", Math.Round(wait.TotalSeconds)).With(Notice.From(e)));
                    _logger.LogError(e, "Fallo inesperado en el bucle del panel. Siguiente intento en {Segundos} s.",
                        wait.TotalSeconds);
                }

                await CloseAsync().ConfigureAwait(false);
                try
                {
                    await Delay(wait, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        SetState(ServiceState.Stopping, null);
        await CloseAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Avisa de que el panel ha podido volver a su estado de fábrica sin que el puerto se entere: lo
    /// que pasa al volver de suspensión, de hibernación o de un «Apagar» con el inicio rápido de Windows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// En los tres el proceso sigue vivo, pero el panel se queda sin corriente y vuelve en vertical y
    /// en blanco. El puerto, en cambio, no falla: Windows reaprovecha el mismo dispositivo al reanudar.
    /// Así que el bucle seguía mandando solo lo que cambiaba, con coordenadas apaisadas, a un panel en
    /// vertical, y salían franjas giradas sobre fondo blanco hasta que alguien reiniciaba el servicio.
    /// La revisión A no contesta a nada, así que el aviso tiene que venir del sistema.
    /// </para>
    /// <para>
    /// Sin cerrojo y desde cualquier hilo: solo levanta una marca, que el bucle de pintado recoge en
    /// el siguiente ciclo. Avisar dos veces es lo mismo que avisar una.
    /// </para>
    /// </remarks>
    public void PanelMayHaveReset() => Volatile.Write(ref _stateLost, 1);

    /// <summary>
    /// El estado se lee sin cerrojo a propósito: son campos sueltos y el peor caso es una lectura de
    /// hace un ciclo, que para una bandeja o una ventana de estado da igual.
    /// </summary>
    public ServiceStatus GetStatus()
    {
        var panel = _panel;
        var runner = _runner;
        double bytesPerSecond = _rate.PerSecond;
        int throughput = panel?.Driver.Capabilities.ThroughputBytesPerSecond ?? 0;

        return new ServiceStatus
        {
            State = _state,
            Detail = _detail,
            PortName = panel?.PortName,
            Model = panel?.Model,
            Width = panel?.Driver.Width ?? 0,
            Height = panel?.Driver.Height ?? 0,
            Orientation = panel?.Driver.Orientation.ToString(),
            LayoutPath = _config.LayoutPath,
            RandomLayout = _config.RandomLayout,
            LayoutName = _layout?.Meta?.Name,
            Brightness = _config.Brightness,
            ScreenOn = _config.ScreenOn,
            Blits = runner?.BlitCount ?? 0,
            BytesSent = runner?.BytesSent ?? 0,
            BytesPerSecond = bytesPerSecond,
            // Contra el presupuesto del planificador, no contra el caudal íntegro: si no, este
            // número no puede pasar del 25 % ni con el enlace saturado, y no avisa de nada.
            LinkUtilization = throughput > 0
                ? bytesPerSecond / (throughput * ByteBudgetScheduler.DefaultUtilization)
                : 0,
            ThroughputBytesPerSecond = throughput,
            SensorCount = _sensorCount,
            Warnings = [.. _sensors.Warnings, .. _layoutWarnings],
            Language = Language,
            Reconnects = _reconnects,
            UptimeSeconds = (long)_uptime.Elapsed.TotalSeconds,
        };
    }

    /// <summary>
    /// Los sensores que ve el servicio. Con <paramref name="ids"/> solo se refrescan esos, y
    /// <paramref name="filter"/> no pinta nada.
    /// </summary>
    public async Task<IReadOnlyList<SensorInfo>> GetSensorsAsync(
        IReadOnlyList<string>? ids, string? filter, CancellationToken cancellationToken = default)
    {
        // Antes de coger el cerrojo, que es el mismo con el que pinta el panel: lo que viene de la
        // tubería no puede decidir cuánto trabajo se hace con él cogido.
        //
        // Sin nombre de parámetro: estos mensajes viajan por la tubería y los acaba leyendo una
        // persona en la CLI o en la ventana, y ArgumentException le pegaría detrás un «(Parameter
        // 'ids')» que parece que el programa se ha roto.
        if (ids is { Count: > SensorsRequest.MaxIds })
            throw new NoticeArgumentException(Notice.Of("sensors.too-many", SensorsRequest.MaxIds, ids.Count));

        // Con el cerrojo: refrescar los sensores mientras el bucle de pintado los lee sería pedirle al
        // backend que sea reentrante, y no lo es.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool byId = ids is { Count: > 0 };

            // En conjunto y no en lista: la búsqueda de abajo recorre todos los sensores, y con una
            // lista eso era un repaso entero por cada lectura. De paso quedan fuera los repetidos y los
            // nulos, que son posibles porque el serializador del IPC no comprueba las anotaciones de
            // nulabilidad de los elementos de una colección: un null acababa en ArgumentNullException y
            // el cliente recibía «fallo inesperado» en lugar de lo que había pedido.
            var wanted = byId
                ? new HashSet<string>(ids!.Where(id => id is not null), StringComparer.Ordinal)
                : null;

            // Los sensores que el panel está pintando los muestrea él, en el refresco de cada
            // widget, y aquí se sirve lo que leyó. Volver a muestrearlos sería un segundo observador
            // del mismo hardware: los sensores que se calculan por diferencia entre dos lecturas
            // —velocidades de red y de disco, relojes efectivos— miden el hueco entre una
            // actualización y la siguiente, así que este sondeo se lo partía en dos y dejaba al panel
            // promediando el trozo que le tocara. La ventana empeoraba lo que mide la pantalla.
            //
            // Solo en la rama por identificadores, que es la de la vista previa. La del filtro es la
            // del explorador y la de «vibescreen service sensors», y su trabajo es justo el
            // contrario: decir qué marca el sensor ahora. Por eso pueden enseñar números distintos
            // del mismo sensor, y está bien que lo hagan: uno dice lo que marca y el otro lo que hay
            // en la pantalla.
            var painted = byId ? Painted() : null;

            if (byId) _sensors.Update(painted is null ? wanted! : wanted!.Where(id => !painted.ContainsKey(id)));
            else _sensors.UpdateAll();

            return _sensors.ReadAll()
                .Where(r => byId
                            ? wanted!.Contains(r.Id)
                            : filter is null
                              || r.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)
                              || r.Native.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .Select(r => new SensorInfo
                {
                    Id = r.Id,
                    Name = r.Native.Name,
                    Hardware = r.Native.HardwareName,
                    Kind = r.Kind.Segment(),
                    Unit = r.Unit,
                    Value = Reported(r, painted),
                })
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Lo que el panel leyó para pintar, o null si ahora mismo no pinta nadie: sin layout, con la
    /// pantalla apagada o sin panel no hay quien refresque, y entonces sí hay que ir al hardware.
    /// </summary>
    /// <remarks>La condición es la misma que la del bucle de pintado, y tiene que seguir siéndolo.</remarks>
    private IReadOnlyDictionary<string, float?>? Painted() =>
        _runner is { } runner && _painting is not null && _config.ScreenOn ? runner.Renderer.LastRead : null;

    /// <summary>El valor que se cuenta de un sensor: el que pintó el panel si lo pintó, y si no el suyo.</summary>
    private static double? Reported(SensorReading reading, IReadOnlyDictionary<string, float?>? painted)
    {
        float? value = painted is not null && painted.TryGetValue(reading.Id, out float? shown)
            ? shown
            : reading.Value;

        // Un NaN no es un número de JSON y el serializador se planta. Un sensor que devuelve NaN es
        // un sensor sin lectura, que es exactamente lo que dice el null.
        return value is { } number && float.IsFinite(number) ? number : null;
    }

    /// <summary>Carga un layout, lo guarda en la configuración y empieza a pintarlo.</summary>
    /// <remarks>
    /// <paramref name="chosen"/> separa las dos cosas que parecen la misma. Elegir uno a mano es
    /// decir «este», y apaga el sorteo: si no, el usuario pondría un layout, lo vería puesto, y al
    /// arrancar mañana el ordenador se encontraría otro sin saber por qué. Pintar uno desde el
    /// editor es otra cosa —mirar lo que estás haciendo— y ahí el sorteo se queda como estaba.
    /// </remarks>
    /// <exception cref="LayoutException">El layout no es válido. La excepción lleva todos los problemas.</exception>
    public Task SetLayoutAsync(string path, bool chosen = true, CancellationToken cancellationToken = default) =>
        UseLayoutAsync(path, random: !chosen && _config.RandomLayout, cancellationToken);

    /// <summary>
    /// Pone o quita el elegir al azar en cada arranque, y al ponerlo elige uno ya.
    /// </summary>
    /// <remarks>
    /// Quitarlo no cambia lo que se está pintando: el último que salió se queda, que es lo que el
    /// usuario tiene delante cuando decide que ese le vale.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No hay layouts guardados entre los que elegir.</exception>
    public async Task SetRandomLayoutAsync(bool on, CancellationToken cancellationToken = default)
    {
        if (!on)
        {
            // Con el cerrojo, como todas las demás escrituras de la configuración. Era la única que no
            // lo cogía: dos órdenes a la vez se pisaban sobre _config y, peor, sobre el fichero, porque
            // AtomicFile escribe siempre en el mismo «.tmp» y el catch de una puede borrar el temporal
            // que la otra está a punto de mover.
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _config.RandomLayout = false;
                _store.Save(_config);
            }
            finally
            {
                _gate.Release();
            }

            return;
        }

        string chosen = LayoutChoice.AtRandom(LayoutPaths.All(), LayoutPaths.NameOf(_config.LayoutPath))
                        ?? throw new NoticeInvalidOperationException(Notice.Of("layout.none-saved"));

        await UseLayoutAsync(LayoutPaths.For(chosen), random: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task UseLayoutAsync(string path, bool random, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Antes de tocar el disco: esta orden llega por una tubería abierta a todos los usuarios
        // locales, y en Windows la atiende un proceso que corre como SYSTEM.
        // Sin nombre de parámetro, por lo mismo que arriba: esto lo lee una persona.
        if (LayoutPathRule.Problem(path, out string full, LayoutRoot) is { } bad)
            throw new NoticeArgumentException(bad);
        if (LayoutPathRule.SizeProblem(full) is { } tooBig)
            throw new NoticeArgumentException(tooBig);

        var layout = LayoutLoader.Load(full);

        // La imagen de fondo la abre el mismo proceso, así que le vale la misma regla.
        if (layout.Background is ImageBackground image && LayoutPathRule.ResourceProblem(image.Path) is { } badImage)
            throw new NoticeArgumentException(badImage);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _layout = layout;
            _config.LayoutPath = full;
            _config.RandomLayout = random;
            _store.Save(_config);
            await ApplyLayoutAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Vuelve a leer del disco el layout vigente, para ver un cambio sin reiniciar el servicio.</summary>
    /// <remarks>Recargar no es elegir, así que deja el modo aleatorio como estuviera.</remarks>
    public Task ReloadLayoutAsync(CancellationToken cancellationToken = default) =>
        _config.LayoutPath is { } path
            ? UseLayoutAsync(path, _config.RandomLayout, cancellationToken)
            : throw new NoticeInvalidOperationException(Notice.Of("layout.none-configured"));

    public async Task SetBrightnessAsync(int percent, CancellationToken cancellationToken = default)
    {
        if (percent is < 0 or > 100)
            throw new NoticeArgumentException(Notice.Of("brightness.range"));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _config.Brightness = percent;
            _store.Save(_config);
            if (_panel is not null)
                await _panel.Driver.SetBrightnessAsync(percent, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetScreenPowerAsync(bool screenOn, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _config.ScreenOn = screenOn;
            _store.Save(_config);
            if (_panel is null) return;

            await _panel.Driver.SetScreenPowerAsync(screenOn, cancellationToken).ConfigureAwait(false);

            // Al encender hay que repintarlo todo: mientras estuvo apagada no se envió nada y el panel
            // conserva lo último que recibió.
            if (screenOn) await ApplyLayoutAsync(cancellationToken).ConfigureAwait(false);
            else SetState(ServiceState.Connected, Notice.Of("panel.screen-off"));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// En qué idioma escribe el panel lo que es texto de reloj, que se guarda y se pinta ya.
    /// </summary>
    /// <remarks>
    /// Un idioma que no se conoce no es un error de quien lo manda —una ventana más nueva puede tener
    /// uno que este servicio no—: se guarda igual y se pinta en español hasta que se entienda.
    /// </remarks>
    public async Task SetLanguageAsync(string language, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (string.Equals(_config.Language, language, StringComparison.Ordinal)) return;

            _config.Language = language;
            _store.Save(_config);

            // Los relojes se montan con su idioma: rehacerlos es volver a aplicar el layout.
            if (_panel is not null) await ApplyLayoutAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>El idioma de los relojes del panel: el guardado, o español si no hay ninguno.</summary>
    private string Language => _config.Language ?? ClockFormat.DefaultLanguage;

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    /// <summary>
    /// Cuántos sensores ve esta máquina. Va aparte porque es la primera vez que se toca el backend de
    /// sensores, que en Windows baja a ring0: si no puede, el servicio sigue y lo dice en los avisos.
    /// </summary>
    private void CountSensors()
    {
        try
        {
            _sensorCount = _sensors.ReadAll().Count;
        }
        catch (Exception e)
        {
            _sensorCount = 0;
            _logger.LogError(e, "No se han podido leer los sensores al arrancar.");
        }
    }

    /// <summary>
    /// Echa a suertes el layout de este arranque, si es lo que se ha pedido.
    /// </summary>
    /// <remarks>
    /// Aquí y no en cada reconexión: perder el panel y recuperarlo es la misma sesión, y cambiar de
    /// layout por eso sería un cambio que el usuario no ha pedido y no sabría explicarse. Lo que se
    /// elige se escribe en la configuración, así que a partir de ese momento es el layout vigente
    /// para todo el mundo.
    /// </remarks>
    private void ChooseLayoutAtRandom()
    {
        var names = LayoutPaths.All();
        if (LayoutChoice.AtRandom(names, LayoutPaths.NameOf(_config.LayoutPath)) is not { } chosen)
        {
            _logger.LogWarning("No hay layouts guardados entre los que elegir al azar.");
            return;
        }

        _config.LayoutPath = LayoutPaths.For(chosen);
        _store.Save(_config);
        _logger.LogInformation("Layout de este arranque, elegido al azar entre {Cuantos}: {Nombre}.", names.Count, chosen);
    }

    private void LoadConfiguredLayout()
    {
        if (_config.RandomLayout) ChooseLayoutAtRandom();
        if (_config.LayoutPath is not { } path) return;

        try
        {
            _layout = LayoutLoader.Load(path);
        }
        catch (Exception e) when (e is LayoutException or IOException or UnauthorizedAccessException)
        {
            // Un layout roto no puede impedir que el servicio arranque: se conecta igual, deja el panel
            // limpio y el estado explica por qué no pinta.
            _logger.LogError("No se ha podido cargar el layout {Ruta}: {Mensaje}", path, e.Message);
            _layoutWarnings = [Notice.Of("layout.load-failed", path).With(Notice.From(e))];
        }
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        SetState(ServiceState.Searching, _detail);
        var orientation = _layout?.Display.Orientation ?? Orientation.Portrait;
        var panel = await _connector.OpenAsync(_config.PortName, orientation, _config.ScreenOn, cancellationToken).ConfigureAwait(false);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _panel = panel;
            _connected = true;
            await panel.Driver.SetBrightnessAsync(_config.Brightness, cancellationToken).ConfigureAwait(false);
            await ApplyLayoutAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Panel {Modelo} en {Puerto}, {Ancho}x{Alto} {Orientacion}.",
                panel.Model, panel.PortName, panel.Driver.Width, panel.Driver.Height, panel.Driver.Orientation);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Pinta hasta que paren el servicio, hasta que el panel falle o hasta que haya perdido su estado.</summary>
    /// <remarks>
    /// La marca no se borra al conectar, y es a propósito. Al reanudar, una escritura puede fallar antes
    /// de que llegue el aviso; si el aviso llega durante esa espera corta y conectar lo borrara, el
    /// panel se abriría en pleno arranque y la orientación se perdería otra vez. Así, el peor caso es
    /// reabrirlo una vez de más.
    /// </remarks>
    private async Task PaintAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(LayoutRunner.Tick);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Interlocked.Exchange(ref _stateLost, 0) == 1) return;

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Sin layout o con la pantalla apagada no se envía nada, así que una desconexión no se
                // nota hasta la siguiente orden. Es el único caso en que el estado va por detrás.
                if (_runner is null || _painting is null || !_config.ScreenOn) continue;

                await _runner.TickAsync(_painting.Elapsed, cancellationToken).ConfigureAwait(false);
                _rate.Add(_painting.Elapsed, _runner.BytesSent);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    /// <summary>Deja el panel pintando el layout vigente. Se llama siempre con el cerrojo cogido.</summary>
    private async Task ApplyLayoutAsync(CancellationToken cancellationToken)
    {
        _runner?.Dispose();
        _runner = null;
        _painting = null;
        _rate.Reset();

        // Antes de cualquier salida: si no, un layout roto al arrancar deja su aviso puesto aunque
        // después se cargue uno bueno, y el estado sigue quejándose de un fichero que ya no se usa.
        _layoutWarnings = _layout is null ? [] : LayoutInspector.Warnings(_layout, _sensors);

        if (_panel is null) return;
        var driver = _panel.Driver;

        if (_layout is null)
        {
            await driver.ClearAsync(cancellationToken).ConfigureAwait(false);
            SetState(ServiceState.Connected, Notice.Of("panel.no-layout"));
            return;
        }

        if (driver.Orientation != _layout.Display.Orientation)
            await driver.SetOrientationAsync(_layout.Display.Orientation, cancellationToken).ConfigureAwait(false);

        LayoutRunner runner;
        try
        {
            // CA2000 no ve la cesión de propiedad: el runner queda en _runner, y lo sueltan esta misma
            // función en la siguiente pasada y CloseAsync al cerrar el panel.
#pragma warning disable CA2000
            runner = new LayoutRunner(driver, _layout, _sensors, language: Language);
#pragma warning restore CA2000
        }
        catch (ArgumentException e)
        {
            // El layout no es para este panel. Ni es motivo para reconectar ni para caerse: se queda
            // conectado sin pintar y el estado dice por qué.
            _layoutWarnings = [Notice.From(e)];
            SetState(ServiceState.Connected, Notice.From(e));
            _logger.LogError("El layout no encaja con el panel: {Mensaje}", e.Message);
            return;
        }

        _runner = runner;

        if (!_config.ScreenOn)
        {
            SetState(ServiceState.Connected, Notice.Of("panel.screen-off"));
            return;
        }

        _painting = Stopwatch.StartNew();
        _rate.Add(TimeSpan.Zero, 0);
        await runner.StartAsync(_painting.Elapsed, cancellationToken).ConfigureAwait(false);
        _rate.Add(_painting.Elapsed, runner.BytesSent);
        SetState(ServiceState.Painting, null);
    }

    private async Task CloseAsync()
    {
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // El host ya se ha soltado, y soltarlo pasa por aquí: no queda nada que cerrar. Sin esto,
            // una parada que va mal acababa lanzando desde dentro del catch del bucle, y esa sí que
            // no la recogía nadie.
            return;
        }

        try
        {
            _runner?.Dispose();
            _runner = null;
            _painting = null;
            _rate.Reset();

            var panel = _panel;
            _panel = null;
            if (panel is null) return;

            try
            {
                await panel.Driver.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception e) when (IsPanelFailure(e))
            {
                // Con el cable fuera, cerrar el puerto también falla. No hay nada que hacer al respecto.
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void SetState(ServiceState state, Notice? detail)
    {
        _state = state;
        _detail = detail;
    }

    /// <summary>Lo que significa «el panel no está»: se reintenta en vez de tirar el servicio.</summary>
    private static bool IsPanelFailure(Exception e) =>
        e is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or ObjectDisposedException;
}
