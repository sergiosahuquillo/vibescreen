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

    /// <summary>Lo que ocupa un píxel en el cable. La revisión A manda RGB565, que son dos.</summary>
    public int BytesPerPixel { get; init; } = 2;
    public bool SupportsBrightness { get; init; } = true;
    public bool SupportsOrientation { get; init; } = true;
    public bool SupportsScreenPower { get; init; } = true;
    public bool SupportsBackplateLed { get; init; }
    public bool SupportsVideoBackground { get; init; }

    /// <summary>Lo que ocupa un fotograma entero en el cable.</summary>
    public long FullFrameBytes => (long)NativeWidth * NativeHeight * BytesPerPixel;
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
