using VibeScreen.Contracts;

namespace VibeScreen.Core.Sensors;

/// <summary>
/// Sensores de una máquina. Por encima de esta interfaz solo existen identificadores canónicos, nunca
/// los del backend.
/// </summary>
public interface ISensorProvider : IDisposable
{
    /// <summary>Todos los sensores disponibles con su última lectura, en orden estable: familia, hardware y tipo.</summary>
    IReadOnlyList<SensorReading> ReadAll();

    /// <summary>Última lectura de un sensor, o null si no existe en esta máquina.</summary>
    SensorReading? Read(string id);

    /// <summary>
    /// Refresca esos sensores. Solo se actualiza el hardware del que dependen: refrescarlo todo cuesta,
    /// porque incluye discos y red.
    /// </summary>
    void Update(IEnumerable<string> ids);

    void UpdateAll();

    /// <summary>Por qué faltan sensores que cabría esperar, por ejemplo sin acceso ring0.</summary>
    IReadOnlyList<Notice> Warnings { get; }
}
