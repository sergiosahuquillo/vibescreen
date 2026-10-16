using VibeScreen.Contracts;
using VibeScreen.Core.Drivers;
using VibeScreen.Protocol.Display;

namespace VibeScreen.Service;

/// <summary>Un panel abierto, con lo que hace falta para contarlo en el estado.</summary>
public sealed record PanelConnection(IDisplayDriver Driver, string PortName, string Model);

/// <summary>
/// Abre el panel. Es una interfaz para que las comprobaciones puedan simular un panel que se cae y
/// vuelve, que es justo lo que el servicio tiene que aguantar.
/// </summary>
public interface IPanelConnector
{
    ValueTask<PanelConnection> OpenAsync(
        string? portName,
        Orientation orientation,
        bool screenOn,
        CancellationToken cancellationToken = default);
}

/// <summary>El panel de verdad, por puerto serie.</summary>
public sealed class SerialPanelConnector(DisplayDiscovery? discovery = null) : IPanelConnector
{
    private readonly DisplayDiscovery _discovery = discovery ?? new DisplayDiscovery();

    public async ValueTask<PanelConnection> OpenAsync(
        string? portName,
        Orientation orientation,
        bool screenOn,
        CancellationToken cancellationToken = default)
    {
        // Se vuelve a detectar en cada intento a propósito: un RESET del panel le cambia el puerto COM,
        // así que el de la conexión anterior puede que ya no exista. Pero una sola vez por intento:
        // buscar y abrir por separado enumeraba los puertos dos veces.
        var display = _discovery.Find(portName)
                      ?? throw new NoticeInvalidOperationException(Notice.Of("panel.not-found"));

        var driver = await _discovery.OpenAsync(display, orientation, screenOn, cancellationToken).ConfigureAwait(false);
        return new PanelConnection(driver, display.Port.PortName, driver.Capabilities.Model);
    }
}

/// <summary>Cuánto se espera entre reintentos de conexión.</summary>
public static class ReconnectDelay
{
    private static readonly TimeSpan[] Steps =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
    ];

    /// <summary>Sube hasta un tope y se queda ahí: enumerar puertos es barato y el panel puede volver en cualquier momento.</summary>
    public static TimeSpan For(int attempt) => Steps[Math.Clamp(attempt - 1, 0, Steps.Length - 1)];
}
