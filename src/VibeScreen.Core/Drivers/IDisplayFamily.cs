using VibeScreen.Core.Transport;
using VibeScreen.Protocol.Display;

namespace VibeScreen.Core.Drivers;

/// <summary>Cuánto se fía la detección de que un puerto es un panel de una familia concreta.</summary>
/// <remarks>
/// Hace falta porque las familias se pisan: el CH552T va en paneles de más de una revisión, así que
/// su VID:PID no dice cuál es. Cuando dos familias reconocen el mismo puerto, gana la más segura.
/// </remarks>
public enum MatchStrength
{
    /// <summary>Puerto indicado a mano que nadie ha reconocido: se abre con la familia que se diga.</summary>
    Forced = 0,

    /// <summary>Solo el VID:PID, que puede ser de más de una familia.</summary>
    UsbId = 1,

    /// <summary>Un número de serie que solo graba esta familia.</summary>
    SerialNumber = 2,
}

/// <summary>Un panel encontrado y ya identificado, listo para abrir.</summary>
public sealed record DiscoveredDisplay
{
    public required SerialPortDescriptor Port { get; init; }

    /// <summary>
    /// Lo que se sabe del panel sin haberle hablado. Las capacidades definitivas las da
    /// <see cref="IDisplayFamily.IdentifyAsync"/> al abrirlo, y pueden ser otras: dos submodelos
    /// pueden compartir número de serie y no tamaño.
    /// </summary>
    public required DisplayCapabilities Capabilities { get; init; }

    public required string MatchedBy { get; init; }

    /// <summary>La familia que lo ha reconocido, y la que lo va a abrir.</summary>
    public required IDisplayFamily Family { get; init; }

    public required MatchStrength Strength { get; init; }
}

/// <summary>
/// Todo lo que cambia de una revisión a otra: cómo se reconoce, cómo se le pregunta el submodelo y
/// qué driver le habla. Añadir una revisión es escribir una de estas y registrarla en
/// <see cref="DisplayDiscovery.Supported"/>; nada por encima de <see cref="DisplayDiscovery"/> se entera.
/// </summary>
public interface IDisplayFamily
{
    HardwareRevision Revision { get; }

    /// <summary>Lo que se supone de un puerto forzado a mano que la detección no ha reconocido.</summary>
    DisplayCapabilities AssumedCapabilities { get; }

    /// <summary>
    /// Reconoce los paneles de la familia entre los puertos serie del sistema, sin escribir en ellos.
    /// </summary>
    /// <remarks>
    /// Pasiva a propósito, por VID/PID y número de serie: escribir a ciegas para ver quién contesta es
    /// inaceptable cuando el puerto podría ser un SAI o una placa de desarrollo.
    /// </remarks>
    IEnumerable<DiscoveredDisplay> Probe(IReadOnlyList<SerialPortDescriptor> ports);

    /// <summary>
    /// Pregunta el submodelo a un panel ya reconocido y devuelve sus capacidades de verdad.
    /// </summary>
    /// <remarks>
    /// Se llama con el transporte ya creado y antes de inicializar el driver, que necesita el tamaño
    /// nativo para fijar la orientación. Abre el transporte si hace falta y lo deja abierto.
    /// </remarks>
    ValueTask<DisplayCapabilities> IdentifyAsync(
        DiscoveredDisplay display, IDisplayTransport transport, CancellationToken cancellationToken = default);

    /// <summary>El driver que le habla a este panel. Se queda con el transporte.</summary>
    IDisplayDriver CreateDriver(IDisplayTransport transport, DisplayCapabilities capabilities);
}
