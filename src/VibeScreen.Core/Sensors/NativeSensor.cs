namespace VibeScreen.Core.Sensors;

/// <summary>
/// Un sensor tal como lo expone el backend, antes de darle identificador canónico.
/// </summary>
/// <remarks>
/// Cada backend lo rellena a partir de sus propios tipos. El nombrado canónico solo trabaja con esto,
/// así que se verifica sin hardware y sin LibreHardwareMonitor.
/// </remarks>
public sealed record NativeSensor
{
    public required HardwareClass HardwareClass { get; init; }

    /// <summary>
    /// Identificador del hardware de primer nivel. Los sensores de un subhardware, como el Super I/O
    /// de la placa base, llevan el de su padre: son la misma placa.
    /// </summary>
    public required string HardwareId { get; init; }

    /// <summary>Orden dentro de la familia, de menor a mayor. Sirve para que la GPU dedicada sea la 0.</summary>
    public int HardwareRank { get; init; }

    /// <summary>Nombre del hardware que contiene el sensor, subhardware incluido: «Nuvoton NCT6687D».</summary>
    public required string HardwareName { get; init; }

    public required SensorKind Kind { get; init; }

    /// <summary>Nombre que da el backend: «Core (Tctl/Tdie)».</summary>
    public required string Name { get; init; }

    /// <summary>Identificador del backend. No es único: LibreHardwareMonitor repite alguno.</summary>
    public required string NativeId { get; init; }

    /// <summary>Posición del sensor en el backend. Solo sirve para desempatar de forma estable.</summary>
    public int NativeIndex { get; init; }
}
