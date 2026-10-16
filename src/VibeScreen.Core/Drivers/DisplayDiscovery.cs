using VibeScreen.Contracts;
using VibeScreen.Core.Transport;
using VibeScreen.Protocol.Display;

namespace VibeScreen.Core.Drivers;

/// <summary>
/// Busca paneles compatibles. Las revisiones se registran aquí, así que añadir la B, la C o la D
/// no toca nada por encima de esta clase.
/// </summary>
public sealed class DisplayDiscovery(ISerialPortEnumerator? enumerator = null, IEnumerable<IDisplayProbe>? probes = null)
{
    private readonly ISerialPortEnumerator _enumerator = enumerator ?? SerialPortEnumerator.Create();
    private readonly IReadOnlyList<IDisplayProbe> _probes = (probes ?? [new RevADisplayProbe()]).ToList();

    public IReadOnlyList<SerialPortDescriptor> ListPorts() => _enumerator.List();

    public IReadOnlyList<DiscoveredDisplay> FindAll()
    {
        var ports = _enumerator.List();
        return _probes.SelectMany(p => p.Probe(ports)).ToList();
    }

    public DiscoveredDisplay? FindFirst() => FindAll().FirstOrDefault();

    /// <summary>
    /// El panel que hay que abrir: el del puerto indicado, o el primero que aparezca.
    /// </summary>
    /// <remarks>
    /// Una sola pasada por los puertos. Enumerarlos en Windows es SetupAPI y no es gratis, y quien
    /// reintenta cada segundo lo paga; peor aún, entre dos pasadas el panel puede desaparecer y la
    /// segunda caería en la rama del puerto forzado, dando por buenas unas capacidades que no se han
    /// comprobado.
    /// </remarks>
    public DiscoveredDisplay? Find(string? portName)
    {
        var all = FindAll();
        var display = portName is null
            ? all.FirstOrDefault()
            : all.FirstOrDefault(d => string.Equals(d.Port.PortName, portName, StringComparison.OrdinalIgnoreCase));

        if (display is null && portName is not null)
        {
            // El usuario ha forzado un puerto que no hemos sabido identificar: se respeta,
            // pero asumiendo revisión A, que es lo único implementado.
            display = new DiscoveredDisplay
            {
                Port = new SerialPortDescriptor { PortName = portName },
                Capabilities = RevADisplayProbe.CreateCapabilities(320, 480, "Turing Smart Screen 3.5\" (forzado)"),
                MatchedBy = "puerto indicado a mano",
            };
        }

        return display;
    }

    /// <summary>Abre el primer panel encontrado, o el del puerto indicado, y lo deja inicializado.</summary>
    public async ValueTask<IDisplayDriver> OpenAsync(
        string? portName = null,
        Orientation orientation = Orientation.Portrait,
        bool screenOn = true,
        CancellationToken cancellationToken = default)
    {
        var display = Find(portName)
            ?? throw new NoticeInvalidOperationException(Notice.Of("panel.not-found.port-hint"));

        return await OpenAsync(display, orientation, screenOn, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Abre un panel ya identificado, sin volver a enumerar los puertos.</summary>
    public async ValueTask<IDisplayDriver> OpenAsync(
        DiscoveredDisplay display,
        Orientation orientation = Orientation.Portrait,
        bool screenOn = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(display);

        // Con plazo: una escritura al panel que no vuelve colgaba el bucle entero, y sin excepción
        // el catch de reconexión no llegaba a enterarse.
        //
        // CA2000 no ve la cesión de propiedad: el transporte pasa a ser del driver, que lo suelta en su
        // DisposeAsync, y si inicializar falla lo suelta el catch de más abajo.
#pragma warning disable CA2000
        var transport = new TimeoutDisplayTransport(new SerialDisplayTransport(display.Port.PortName));
        var driver = new RevADisplayDriver(transport, display.Capabilities);
#pragma warning restore CA2000
        try
        {
            await driver.InitializeAsync(orientation, screenOn, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Inicializar abre el puerto. Si falla después, quien llama no recibe el driver y no puede
            // cerrarlo: el puerto quedaría ocupado hasta que actúe el recolector y el siguiente intento
            // de abrirlo fallaría.
            try
            {
                await driver.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Con el panel desenchufado, cerrar también puede fallar. La excepción útil es la original.
            }
            throw;
        }
        return driver;
    }
}
