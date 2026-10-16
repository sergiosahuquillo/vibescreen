namespace VibeScreen.Core.Sensors;

/// <summary>
/// Familia de hardware: el primer segmento del identificador canónico. Es neutra a propósito, sin
/// fabricante: <c>gpu/0</c> es la primera GPU, sea NVIDIA, AMD o Intel.
/// </summary>
public enum HardwareClass
{
    Cpu,
    Gpu,
    Motherboard,
    Memory,
    Storage,
    Network,
    Other,
}
