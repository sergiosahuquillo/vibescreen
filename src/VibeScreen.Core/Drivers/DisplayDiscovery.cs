using VibeScreen.Contracts;
using VibeScreen.Core.Transport;
using VibeScreen.Protocol.Display;

namespace VibeScreen.Core.Drivers;

/// <summary>
/// Busca paneles compatibles y los abre con el driver de su familia. Las revisiones se registran en
/// <see cref="Supported"/>, así que añadir la B, la C o la D no toca nada por encima de esta clase.
/// </summary>
/// <param name="openTransport">
/// Cómo se llega al puerto. Por omisión, el puerto serie; las comprobaciones ponen uno de mentira para
/// ver qué se le dice al panel al abrirlo sin tener uno delante.
/// </param>
public sealed class DisplayDiscovery(
    ISerialPortEnumerator? enumerator = null,
    IEnumerable<IDisplayFamily>? families = null,
    Func<string, IDisplayTransport>? openTransport = null)
{
    /// <summary>Las familias que hay implementadas, por orden de preferencia cuando dos empatan.</summary>
    public static IReadOnlyList<IDisplayFamily> Supported { get; } = [new RevADisplayFamily()];

    private readonly ISerialPortEnumerator _enumerator = enumerator ?? SerialPortEnumerator.Create();
    private readonly IReadOnlyList<IDisplayFamily> _families = (families ?? Supported).ToList();
    private readonly Func<string, IDisplayTransport> _openTransport =
        openTransport ?? (static port => new SerialDisplayTransport(port));

    public IReadOnlyList<IDisplayFamily> Families => _families;

    public IReadOnlyList<SerialPortDescriptor> ListPorts() => _enumerator.List();

    /// <summary>Todos los paneles que se ven, uno por puerto.</summary>
    public IReadOnlyList<DiscoveredDisplay> FindAll() => Resolve(_families, _enumerator.List());

    public DiscoveredDisplay? FindFirst() => FindAll().FirstOrDefault();

    /// <summary>
    /// El panel que hay que abrir: el del puerto indicado, o el primero que aparezca.
    /// </summary>
    /// <param name="revision">
    /// La revisión, si se fuerza a mano. Entonces solo cuenta esa familia, y un puerto indicado que no
    /// reconozca se abre con ella en vez de suponer la primera.
    /// </param>
    /// <remarks>
    /// Una sola pasada por los puertos. Enumerarlos en Windows es SetupAPI y no es gratis, y quien
    /// reintenta cada segundo lo paga; peor aún, entre dos pasadas el panel puede desaparecer y la
    /// segunda caería en la rama del puerto forzado, dando por buenas unas capacidades que no se han
    /// comprobado.
    /// </remarks>
    /// <exception cref="NoticeInvalidOperationException">No hay familia para la revisión forzada.</exception>
    public DiscoveredDisplay? Find(string? portName, HardwareRevision? revision = null)
    {
        var families = _families;
        if (revision is { } wanted)
        {
            var family = _families.FirstOrDefault(f => f.Revision == wanted)
                ?? throw new NoticeInvalidOperationException(Notice.Of("panel.revision-unsupported",
                       wanted, string.Join(", ", _families.Select(f => f.Revision))));
            families = [family];
        }

        var all = Resolve(families, _enumerator.List());
        var display = portName is null
            ? all.FirstOrDefault()
            : all.FirstOrDefault(d => string.Equals(d.Port.PortName, portName, StringComparison.OrdinalIgnoreCase));

        if (display is null && portName is not null)
        {
            // El usuario ha forzado un puerto que no hemos sabido identificar: se respeta, con la
            // familia que haya dicho o, si no ha dicho ninguna, con la primera. El tamaño de verdad
            // lo dirá el saludo al abrirlo.
            var family = families[0];
            display = new DiscoveredDisplay
            {
                Port = new SerialPortDescriptor { PortName = portName },
                Capabilities = family.AssumedCapabilities,
                MatchedBy = "puerto indicado a mano",
                Family = family,
                Strength = MatchStrength.Forced,
            };
        }

        return display;
    }

    /// <summary>Abre el primer panel encontrado, o el del puerto indicado, y lo deja inicializado.</summary>
    public async ValueTask<IDisplayDriver> OpenAsync(
        string? portName = null,
        Orientation orientation = Orientation.Portrait,
        bool screenOn = true,
        HardwareRevision? revision = null,
        CancellationToken cancellationToken = default)
    {
        var display = Find(portName, revision)
            ?? throw new NoticeInvalidOperationException(Notice.Of("panel.not-found.port-hint"));

        return await OpenAsync(display, orientation, screenOn, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Abre un panel ya identificado, sin volver a enumerar los puertos: le pregunta el submodelo, le
    /// pone el driver de su familia y lo deja inicializado.
    /// </summary>
    /// <remarks>
    /// El submodelo se pregunta aquí y no al detectar, porque detectar no escribe en ningún puerto.
    /// Sin preguntarlo, una UsbPCMonitor de 5" se abría como la 3.5" con la que comparte número de
    /// serie, y el layout de su tamaño no encajaba nunca.
    /// </remarks>
    public async ValueTask<IDisplayDriver> OpenAsync(
        DiscoveredDisplay display,
        Orientation orientation = Orientation.Portrait,
        bool screenOn = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(display);

        // Con plazo: una escritura al panel que no vuelve colgaba el bucle entero, y sin excepción
        // el catch de reconexión no llegaba a enterarse. El saludo va por el mismo transporte.
        //
        // CA2000 no ve la cesión de propiedad: el transporte pasa a ser del driver, que lo suelta en su
        // DisposeAsync, y si algo falla antes lo suelta el catch de más abajo.
#pragma warning disable CA2000
        var transport = new TimeoutDisplayTransport(_openTransport(display.Port.PortName));
#pragma warning restore CA2000
        IDisplayDriver? driver = null;
        try
        {
            var capabilities = await display.Family.IdentifyAsync(display, transport, cancellationToken)
                                                    .ConfigureAwait(false);
            driver = display.Family.CreateDriver(transport, capabilities);

            // Un panel que no gira solo pinta en vertical. Pedirle otra cosa sería dejarlo a medias; así
            // abre, y el layout apaisado se queda sin pintar con el aviso de que no encaja.
            var effective = capabilities.SupportsOrientation ? orientation : Orientation.Portrait;
            await driver.InitializeAsync(effective, screenOn, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Inicializar abre el puerto. Si falla después, quien llama no recibe el driver y no puede
            // cerrarlo: el puerto quedaría ocupado hasta que actúe el recolector y el siguiente intento
            // de abrirlo fallaría.
            try
            {
                if (driver is not null)
                {
                    await driver.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    await transport.CloseAsync().ConfigureAwait(false);
                    await transport.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Con el panel desenchufado, cerrar también puede fallar. La excepción útil es la original.
            }
            throw;
        }
        return driver;
    }

    /// <summary>
    /// Un panel por puerto. Si dos familias reconocen el mismo, gana la que esté más segura y, a la
    /// par, la que vaya antes en la lista.
    /// </summary>
    /// <remarks>
    /// Antes se juntaban sin más los resultados de cada familia: un puerto que reconocieran dos salía
    /// dos veces, y se abría con la primera aunque la segunda lo hubiera reconocido por su número de
    /// serie y la primera solo por un VID:PID que comparten.
    /// </remarks>
    private static List<DiscoveredDisplay> Resolve(
        IReadOnlyList<IDisplayFamily> families, IReadOnlyList<SerialPortDescriptor> ports)
    {
        var best = new Dictionary<string, DiscoveredDisplay>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var family in families)
        {
            foreach (var display in family.Probe(ports))
            {
                string name = display.Port.PortName;
                if (!best.TryGetValue(name, out var current))
                {
                    best[name] = display;
                    order.Add(name);
                }
                else if (display.Strength > current.Strength)
                {
                    best[name] = display;
                }
            }
        }

        return order.Select(name => best[name]).ToList();
    }
}
