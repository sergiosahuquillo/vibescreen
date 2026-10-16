using VibeScreen.App.Localization;
using VibeScreen.Contracts;

namespace VibeScreen.App;

/// <summary>
/// El lado de la GUI de la tubería del servicio: conexiones que se rehacen solas.
/// </summary>
/// <remarks>
/// <para>
/// El servicio puede pararse, reiniciarse o no estar instalado, y la ventana tiene que seguir
/// abierta diciéndolo en vez de caerse. Cada petición reintenta una vez con una conexión nueva, que
/// es lo que hace falta cuando el servicio se ha reiniciado entre dos sondeos.
/// </para>
/// <para>
/// Van <b>dos</b> conexiones, una para el sondeo y otra para los mandos, y es la diferencia entre
/// una ventana que se queda tiesa y una que no. El servicio atiende las órdenes con el mismo
/// cerrojo con el que pinta el panel, y una escritura al panel tiene 30 s de plazo: con un solo
/// turno, un brillo esperando a un panel atascado dejaba detrás al sondeo del estado, y la ventana
/// entera se congelaba sin decir por qué. Separadas, el sondeo sigue contando lo que pasa mientras
/// la orden espera su turno.
/// </para>
/// </remarks>
public sealed class ServiceLink : IDisposable
{
    /// <summary>
    /// Lo que se le concede a una petición antes de darla por perdida.
    /// </summary>
    /// <remarks>
    /// Más corto que los 30 s del panel a propósito: si el panel está atascado, lo que hay que hacer
    /// es contárselo al usuario, no dejarle la ventana muda medio minuto. Holgado de todas formas,
    /// porque aplicar un tema repinta el fotograma entero y son unos 307 KB a 261 KB/s.
    /// </remarks>
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    /// <summary>El sondeo del estado y de los sensores, que es lo que mantiene la ventana al día.</summary>
    private readonly Channel _polling = new();

    /// <summary>Lo que el usuario pulsa: brillo, encendido, tema.</summary>
    private readonly Channel _commands = new();

    /// <summary>Si el último sondeo llegó al servicio. Es lo que dice la barra de estado.</summary>
    public bool Connected => _polling.Connected;

    /// <summary>Por qué no se pudo sondear, para enseñarlo en la ventana.</summary>
    public Text? LastError => _polling.LastError;

    public async Task<ServiceStatus?> StatusAsync(CancellationToken cancellationToken = default) =>
        (await _polling.SendAsync(new StatusRequest(), cancellationToken).ConfigureAwait(false))?.Status;

    /// <summary>
    /// Los sensores que ve el servicio. Con <paramref name="ids"/> solo se refresca ese hardware,
    /// que es lo que hace la vista previa: refrescarlo todo incluye discos y red, y se nota.
    /// </summary>
    public async Task<IReadOnlyList<SensorInfo>> SensorsAsync(
        IReadOnlyList<string>? ids = null, string? filter = null, CancellationToken cancellationToken = default)
    {
        var response = await _polling.SendAsync(new SensorsRequest { Ids = ids, Filter = filter }, cancellationToken)
            .ConfigureAwait(false);
        return response?.Sensors ?? [];
    }

    /// <summary>Null si fue bien; si no, el error para enseñarlo.</summary>
    /// <param name="chosen">
    /// Si es el usuario eligiendo layout. Aplicar desde el editor no lo es: pinta el tema sin tocar
    /// quién elige en el siguiente arranque.
    /// </param>
    public Task<Text?> SetLayoutAsync(string path, bool chosen = true, CancellationToken cancellationToken = default) =>
        RunAsync(new SetLayoutRequest { Path = path, Chosen = chosen }, cancellationToken);

    /// <summary>Elegir layout al azar en cada arranque del servicio, o dejar de hacerlo.</summary>
    public Task<Text?> SetRandomLayoutAsync(bool on, CancellationToken cancellationToken = default) =>
        RunAsync(new SetRandomLayoutRequest { On = on }, cancellationToken);

    public Task<Text?> ReloadLayoutAsync(CancellationToken cancellationToken = default) =>
        RunAsync(new ReloadLayoutRequest(), cancellationToken);

    public Task<Text?> SetBrightnessAsync(int percent, CancellationToken cancellationToken = default) =>
        RunAsync(new SetBrightnessRequest { Percent = percent }, cancellationToken);

    public Task<Text?> SetScreenPowerAsync(bool screenOn, CancellationToken cancellationToken = default) =>
        RunAsync(new SetScreenPowerRequest { On = screenOn }, cancellationToken);

    /// <summary>El idioma en el que el panel escribe los nombres de los días y los meses de sus relojes.</summary>
    public Task<Text?> SetLanguageAsync(string language, CancellationToken cancellationToken = default) =>
        RunAsync(new SetLanguageRequest { Language = language }, cancellationToken);

    private async Task<Text?> RunAsync(ServiceRequest request, CancellationToken cancellationToken)
    {
        var response = await _commands.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response is null) return _commands.LastError ?? Loc.L("service.unavailable");
        return response.Ok ? (Text?)null : Describe(response);
    }

    private static Text Describe(ServiceResponse response) =>
        response.Problems is { Count: > 0 } problems
            ? Loc.Notices(problems, bullet: "")
            : response.Error is { } error ? Loc.Notice(error) : Loc.L("service.failed-silently");

    /// <summary>
    /// Suelta las dos conexiones sin esperar a nadie.
    /// </summary>
    /// <remarks>
    /// En el cierre no hay nada que recoger y tampoco se puede esperar: <c>desktop.Exit</c> es
    /// síncrono, y bloquear ahí el hilo de la interfaz para cerrar una tubería —que es lo que hacía
    /// este método— era un cuelgue esperando a que alguna versión de <c>NamedPipeClientStream</c>
    /// devolviera de verdad una espera. Lo que quede abierto lo cierra el sistema al terminar.
    /// </remarks>
    public void Dispose()
    {
        _polling.Dispose();
        _commands.Dispose();
    }

    /// <summary>Una conexión con su turno: una petición detrás de otra, y se rehace si se rompe.</summary>
    private sealed class Channel : IDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private ServiceClient? _client;
        private bool _disposed;

        public bool Connected { get; private set; }

        public Text? LastError { get; private set; }

        public async Task<ServiceResponse?> SendAsync(ServiceRequest request, CancellationToken cancellationToken)
        {
            // Plazo propio: sin él, una petición que espera al cerrojo del panel no tiene límite y
            // quien la pulsó no se entera de nada.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(Deadline);

            // El turno se coge fuera del try que lo suelta: si no se llega a coger —porque venció el
            // plazo esperándolo—, soltarlo devolvería un permiso que nunca se pidió.
            try
            {
                await _gate.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                LastError = TooSlow();
                return null;
            }
            catch (ObjectDisposedException)
            {
                // La ventana se está cerrando con una petición en vuelo.
                return null;
            }

            try
            {
                // Dos vueltas: la primera con la conexión que hubiera, la segunda con una nueva por si
                // el servicio se reinició desde el último sondeo.
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        _client ??= await ServiceClient.ConnectAsync(cancellationToken: deadline.Token).ConfigureAwait(false);
                        var response = await _client.SendAsync(request, deadline.Token).ConfigureAwait(false);
                        Connected = true;
                        LastError = response.Ok || response.Error is null ? (Text?)null : Loc.Notice(response.Error);
                        return response;
                    }
                    catch (ServiceUnavailableException e)
                    {
                        Drop();
                        LastError = Loc.Notice(Notice.From(e));
                        return null;
                    }
                    catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
                    {
                        Drop();
                        LastError = Loc.Notice(Notice.From(e));
                    }
                }

                return null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // El plazo, no el usuario: el servicio está ahí pero no contesta, casi siempre porque
                // el panel tiene cogido su cerrojo. La conexión se tira, que a saber por dónde va.
                Drop();
                LastError = TooSlow();
                return null;
            }
            finally
            {
                TryRelease();
            }
        }

        private static Text TooSlow() => Loc.L("service.too-slow", Deadline.TotalSeconds);

        /// <summary>Tira la conexión: la siguiente petición abre otra.</summary>
        private void Drop()
        {
            Connected = false;
            var client = _client;
            _client = null;
            if (client is not null) _ = Forget(client);
        }

        /// <summary>Cierra una conexión que ya no sirve, sin que su fallo llegue a ninguna parte.</summary>
        private static async Task Forget(ServiceClient client)
        {
            try
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
                // La conexión ya estaba rota: por eso se está soltando.
            }
        }

        private void TryRelease()
        {
            try
            {
                _gate.Release();
            }
            catch (ObjectDisposedException)
            {
                // Se ha cerrado la ventana mientras había una petición en vuelo.
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Drop();
            _gate.Dispose();
        }
    }
}
