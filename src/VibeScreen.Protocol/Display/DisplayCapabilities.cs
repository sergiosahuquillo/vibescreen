namespace VibeScreen.Protocol.Display;

/// <summary>Qué sabe hacer un panel concreto. La capa de render consulta esto, nunca la revisión.</summary>
public sealed record DisplayCapabilities
{
    public required string Model { get; init; }
    public required HardwareRevision Revision { get; init; }

    /// <summary>Ancho nativo en píxeles, en orientación vertical.</summary>
    public required int NativeWidth { get; init; }

    /// <summary>Alto nativo en píxeles, en orientación vertical.</summary>
    public required int NativeHeight { get; init; }

    /// <summary>Caudal sostenido medido o estimado, en bytes por segundo. Alimenta el planificador.</summary>
    public required int ThroughputBytesPerSecond { get; init; }

    /// <summary>Cómo viaja un píxel por el cable. La revisión A manda RGB565 con el byte bajo delante.</summary>
    public PixelFormat PixelFormat { get; init; } = PixelFormat.Rgb565LittleEndian;

    /// <summary>Lo que ocupa un píxel en el cable. Sale del formato, así que no puede contradecirlo.</summary>
    public int BytesPerPixel => PixelFormat.BytesPerPixel();

    /// <summary>Si el panel regula el brillo. Sin él, el servicio no lo manda y la ventana no lo ofrece.</summary>
    public bool SupportsBrightness { get; init; } = true;

    /// <summary>
    /// Si el panel gira por su cuenta. Sin él, el panel solo pinta en vertical: el servicio no le pide
    /// otra orientación y un layout apaisado se queda sin pintar, con el aviso de que no encaja.
    /// </summary>
    /// <remarks>
    /// Un driver que gire los píxeles por software antes de enviarlos lo declara como que sí gira:
    /// para quien está por encima, el resultado es el mismo.
    /// </remarks>
    public bool SupportsOrientation { get; init; } = true;

    /// <summary>
    /// Si el panel se puede apagar. Sin él, apagar es dejarlo en negro y dejar de pintar, que es lo
    /// más parecido que se puede hacer con la retroiluminación encendida.
    /// </summary>
    public bool SupportsScreenPower { get; init; } = true;

    public bool SupportsBackplateLed { get; init; }
    public bool SupportsVideoBackground { get; init; }

    /// <summary>Lo que ocupa un fotograma entero en el cable.</summary>
    public long FullFrameBytes => (long)NativeWidth * NativeHeight * BytesPerPixel;

    /// <summary>Bytes que ocupa un rectángulo en el cable con el formato de este panel.</summary>
    public int ByteCount(in PixelRect rect) => PixelFormat.ByteCount(rect);
}

public enum HardwareRevision
{
    Unknown = 0,
    RevA,
    RevB,
    RevC,
    RevD,
    Simulated,
}
