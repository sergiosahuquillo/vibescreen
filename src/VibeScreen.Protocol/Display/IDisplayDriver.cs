namespace VibeScreen.Protocol.Display;

/// <summary>
/// Un panel, visto por la capa de render. Nadie por encima de esta interfaz debería
/// saber de qué revisión es el hardware.
/// </summary>
public interface IDisplayDriver : IAsyncDisposable
{
    DisplayCapabilities Capabilities { get; }

    /// <summary>Orientación vigente. El driver la fija al inicializar, nunca la hereda.</summary>
    Orientation Orientation { get; }

    /// <summary>Ancho útil en la orientación vigente.</summary>
    int Width { get; }

    /// <summary>Alto útil en la orientación vigente.</summary>
    int Height { get; }

    /// <summary>
    /// Abre el canal y fija la orientación y el encendido, sin heredar los que dejara el programa
    /// anterior. Con <paramref name="screenOn"/> a <c>false</c> la pantalla se apaga sin encenderla antes.
    /// </summary>
    ValueTask InitializeAsync(Orientation orientation, bool screenOn = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fija el brillo, en porcentaje. El driver lo recuerda: apagar y encender la pantalla la deja
    /// al último brillo pedido, aunque el panel no lo conserve por su cuenta.
    /// </summary>
    ValueTask SetBrightnessAsync(int percent, CancellationToken cancellationToken = default);

    ValueTask SetOrientationAsync(Orientation orientation, CancellationToken cancellationToken = default);

    /// <summary>Vuelca un rectángulo ya codificado en RGB565 con el orden de bytes que pida el panel.</summary>
    ValueTask BlitAsync(ReadOnlyMemory<byte> pixels, PixelRect rect, CancellationToken cancellationToken = default);

    ValueTask ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>Enciende o apaga la pantalla, que vuelve al brillo de <see cref="SetBrightnessAsync"/>.</summary>
    ValueTask SetScreenPowerAsync(bool screenOn, CancellationToken cancellationToken = default);
}
