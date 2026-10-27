using VibeScreen.Core.Transport;
using VibeScreen.Protocol.Display;

namespace VibeScreen.Core.Drivers;

/// <summary>La revisión A: Turing Smart Screen 3.5" y las UsbPCMonitor de 3.5", 5" y 7".</summary>
/// <remarks>
/// La identificación es pasiva: por número de serie y por VID/PID, sin escribir nada en el
/// puerto. El saludo solo se usa después, ya sobre un panel identificado, para distinguir
/// submodelos.
/// </remarks>
public sealed class RevADisplayFamily : IDisplayFamily
{
    /// <summary>WCH CH552T, el microcontrolador de estos paneles.</summary>
    /// <remarks>
    /// No es solo de la revisión A: según la implementación de referencia, la XuanFang de la revisión B
    /// lleva el mismo. Por eso el VID:PID cuenta como <see cref="MatchStrength.UsbId"/> y una familia que
    /// reconozca el puerto por su número de serie le gana.
    /// </remarks>
    public const int VendorId = 0x1A86;
    public const int ProductId = 0x5722;

    private static readonly string[] KnownSerialNumbers = ["USB35INCHIPS", "USB35INCHIPSV2"];

    /// <summary>
    /// Caudal sostenido medido sobre una Turing 3.5" rev. A el 2026-09-15: 261.600 B/s,
    /// constante desde 1 KB hasta el fotograma completo, con coste por comando nulo.
    /// Alimenta el planificador de presupuesto mientras no se mida el panel concreto.
    /// </summary>
    public const int MeasuredThroughputBytesPerSecond = 261_600;

    private const string Turing35 = "Turing Smart Screen 3.5\"";

    public HardwareRevision Revision => HardwareRevision.RevA;

    /// <summary>
    /// La Turing 3.5", que es la que no contesta al saludo. Si es otra, el saludo al abrirla la corrige.
    /// </summary>
    public DisplayCapabilities AssumedCapabilities => CreateCapabilities(320, 480, Turing35);

    public IEnumerable<DiscoveredDisplay> Probe(IReadOnlyList<SerialPortDescriptor> ports)
    {
        ArgumentNullException.ThrowIfNull(ports);

        foreach (var port in ports)
        {
            string? matchedBy = null;
            var strength = MatchStrength.Forced;

            if (port.SerialNumber is not null &&
                KnownSerialNumbers.Contains(port.SerialNumber, StringComparer.OrdinalIgnoreCase))
            {
                matchedBy = $"número de serie {port.SerialNumber}";
                strength = MatchStrength.SerialNumber;
            }
            else if (port.Matches(VendorId, ProductId))
            {
                matchedBy = $"VID:PID {VendorId:x4}:{ProductId:x4}";
                strength = MatchStrength.UsbId;
            }

            if (matchedBy is null) continue;

            yield return new DiscoveredDisplay
            {
                Port = port,
                MatchedBy = matchedBy,
                Capabilities = AssumedCapabilities,
                Family = this,
                Strength = strength,
            };
        }
    }

    /// <remarks>
    /// El saludo solo corrige el tamaño. Si confirma el que ya se suponía, se queda el nombre de la
    /// detección: la Turing 3.5" de pruebas, con número de serie <c>USB35INCHIPSV2</c>, contesta al
    /// saludo como la UsbPCMonitor de 320x480 (comprobado el 2026-10-27), y sin esto salía con un
    /// nombre que no es el suyo.
    /// </remarks>
    public async ValueTask<DisplayCapabilities> IdentifyAsync(
        DiscoveredDisplay display, IDisplayTransport transport, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(display);

        var identified = await IdentifySubModelAsync(transport, cancellationToken).ConfigureAwait(false);
        var detected = display.Capabilities;
        return identified.NativeWidth == detected.NativeWidth && identified.NativeHeight == detected.NativeHeight
            ? detected
            : identified;
    }

    public IDisplayDriver CreateDriver(IDisplayTransport transport, DisplayCapabilities capabilities) =>
        new RevADisplayDriver(transport, capabilities);

    public static DisplayCapabilities CreateCapabilities(int width, int height, string model) => new()
    {
        Model = model,
        Revision = HardwareRevision.RevA,
        NativeWidth = width,
        NativeHeight = height,
        ThroughputBytesPerSecond = MeasuredThroughputBytesPerSecond,
        PixelFormat = PixelFormat.Rgb565LittleEndian,
        SupportsBrightness = true,
        SupportsOrientation = true,
        SupportsScreenPower = true,
    };

    /// <summary>
    /// Pregunta el submodelo a un panel ya identificado. La Turing 3.5" original no contesta,
    /// y ese silencio es en sí mismo la respuesta: es la de 320x480.
    /// </summary>
    /// <remarks>
    /// La implementación de referencia saluda igual en cada arranque, así que a la 3.5" no le hace
    /// nada que no le hiciera ya. Al panel que calla le cuesta esperar el silencio: lo que tarde en
    /// vencer la lectura del transporte, un segundo con el puerto serie, en cada conexión. No todas
    /// las 3.5" callan: la de pruebas contesta como la UsbPCMonitor de 320x480.
    /// </remarks>
    public static async ValueTask<DisplayCapabilities> IdentifySubModelAsync(
        IDisplayTransport transport, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);

        if (!transport.IsOpen) await transport.OpenAsync(cancellationToken).ConfigureAwait(false);

        await transport.WriteAsync(RevAProtocol.Hello(), cancellationToken).ConfigureAwait(false);
        await transport.FlushAsync(cancellationToken).ConfigureAwait(false);

        var buffer = new byte[6];
        int read = await FillAsync(transport, buffer, cancellationToken).ConfigureAwait(false);
        var parsed = read >= buffer.Length ? RevAProtocol.ParseHelloResponse(buffer) : null;

        return parsed is null
            ? CreateCapabilities(320, 480, Turing35)
            : CreateCapabilities(parsed.Value.Width, parsed.Value.Height,
                                 $"UsbPCMonitor {parsed.Value.Width}x{parsed.Value.Height}");
    }

    /// <summary>
    /// Llena el búfer, o devuelve lo que haya llegado cuando el panel deja de hablar.
    /// </summary>
    /// <remarks>
    /// Una lectura puede volver con menos bytes de los pedidos aunque vengan más de camino: sobre
    /// CDC-ACM los seis de la respuesta pueden repartirse en varias transferencias USB. Sin el
    /// bucle, una respuesta partida se confunde con el silencio de la 3.5" y el panel se identifica
    /// con un tamaño que no es el suyo, sin que nada lo delate.
    /// </remarks>
    private static async ValueTask<int> FillAsync(
        IDisplayTransport transport, byte[] buffer, CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await transport.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (read <= 0) break; // El panel no dice más, y ese silencio es en sí mismo la respuesta.
            total += read;
        }

        return total;
    }
}
