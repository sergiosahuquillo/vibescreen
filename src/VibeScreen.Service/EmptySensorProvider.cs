using VibeScreen.Contracts;
using VibeScreen.Core.Sensors;

namespace VibeScreen.Service;

/// <summary>
/// Lo que se usa cuando no hay backend de sensores, hoy Linux. Así el servicio arranca igual —el panel
/// y el IPC funcionan— y el motivo llega hasta la GUI en vez de morir en una excepción de arranque.
/// </summary>
public sealed class EmptySensorProvider(Notice reason) : ISensorProvider
{
    public IReadOnlyList<Notice> Warnings { get; } = [reason];

    public IReadOnlyList<SensorReading> ReadAll() => [];

    public SensorReading? Read(string id) => null;

    public void Update(IEnumerable<string> ids)
    {
    }

    public void UpdateAll()
    {
    }

    public void Dispose()
    {
    }
}
