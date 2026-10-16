namespace VibeScreen.Protocol.Display;

/// <summary>Códigos de comando de la revisión A (Turing 3.5" y UsbPCMonitor).</summary>
public enum RevACommand : byte
{
    Reset = 101,
    Clear = 102,
    ToBlack = 103,
    ScreenOff = 108,
    ScreenOn = 109,
    SetBrightness = 110,
    SetOrientation = 121,
    DisplayBitmap = 197,

    /// <summary>Solo en las de nueva generación: pide el modelo al panel.</summary>
    Hello = 69,

    /// <summary>Solo en las de nueva generación: píxeles no contiguos en un solo comando.</summary>
    DisplayPixels = 195,

    SetMirror = 122,
}
