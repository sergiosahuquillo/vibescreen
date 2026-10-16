using VibeScreen.Core.Transport;
using VibeScreen.Protocol.Display;

namespace VibeScreen.Core.Drivers;

/// <summary>Un panel encontrado y ya identificado, listo para abrir.</summary>
public sealed record DiscoveredDisplay
{
    public required SerialPortDescriptor Port { get; init; }
    public required DisplayCapabilities Capabilities { get; init; }
    public required string MatchedBy { get; init; }
}

/// <summary>Identifica paneles de una revisión concreta entre los puertos serie del sistema.</summary>
public interface IDisplayProbe
{
    HardwareRevision Revision { get; }
    IEnumerable<DiscoveredDisplay> Probe(IReadOnlyList<SerialPortDescriptor> ports);
}

/// <summary>Detección de paneles de la revisión A.</summary>
/// <remarks>
/// La identificación es pasiva: por número de serie y por VID/PID, sin escribir nada en el
/// puerto. Escribir a ciegas para ver quién contesta es inaceptable cuando el puerto podría
/// ser un SAI o una placa de desarrollo. El saludo solo se usa después, ya sobre un panel
/// identificado, para distinguir submodelos.
/// </remarks>
public sealed class RevADisplayProbe : IDisplayProbe
{
    /// <summary>WCH CH552T, el microcontrolador de estos paneles.</summary>
    public const int VendorId = 0x1A86;
    public const int ProductId = 0x5722;

    private static readonly string[] KnownSerialNumbers = ["USB35INCHIPS", "USB35INCHIPSV2"];

    /// <summary>
    /// Caudal sostenido medido sobre una Turing 3.5" rev. A el 2026-09-15: 261.600 B/s,
    /// constante desde 1 KB hasta el fotograma completo, con coste por comando nulo.
    /// Alimenta el planificador de presupuesto mientras no se mida el panel concreto.
    /// </summary>
    public const int MeasuredThroughputBytesPerSecond = 261_600;

    public HardwareRevision Revision => HardwareRevision.RevA;

    public IEnumerable<DiscoveredDisplay> Probe(IReadOnlyList<SerialPortDescriptor> ports)
    {
        ArgumentNullException.ThrowIfNull(ports);

        foreach (var port in ports)
        {
            string? matchedBy = null;

            if (port.SerialNumber is not null &&
                KnownSerialNumbers.Contains(port.SerialNumber, StringComparer.OrdinalIgnoreCase))
            {
                matchedBy = $"número de serie {port.SerialNumber}";
            }
            else if (port.Matches(VendorId, ProductId))
            {
                matchedBy = $"VID:PID {VendorId:x4}:{ProductId:x4}";
            }

            if (matchedBy is null) continue;

            yield return new DiscoveredDisplay
            {
                Port = port,
                MatchedBy = matchedBy,
                Capabilities = CreateCapabilities(320, 480, "Turing Smart Screen 3.5\""),
            };
        }
    }

    public static DisplayCapabilities CreateCapabilities(int width, int height, string model) => new()
    {
        Model = model,
        Revision = HardwareRevision.RevA,
        NativeWidth = width,
        NativeHeight = height,
        ThroughputBytesPerSecond = MeasuredThroughputBytesPerSecond,
        SupportsBrightness = true,
        SupportsOrientation = true,
        SupportsScreenPower = true,
    };

    /// <summary>
    /// Pregunta el submodelo a un panel ya identificado. La Turing 3.5" original no contesta,
    /// y ese silencio es en sí mismo la respuesta: es la de 320x480.
    /// </summary>
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
            ? CreateCapabilities(320, 480, "Turing Smart Screen 3.5\"")
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
