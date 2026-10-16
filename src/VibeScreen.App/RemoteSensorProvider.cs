using VibeScreen.Contracts;
using VibeScreen.Core;
using VibeScreen.Core.Sensors;

namespace VibeScreen.App;

/// <summary>
/// Los sensores vistos desde la GUI: lo que ha contado el servicio la última vez.
/// </summary>
/// <remarks>
/// La GUI no lee sensores por su cuenta. No va elevada, así que sin ring0 no tendría ni temperatura
/// de CPU ni sensores de placa, y encima estaría abriendo el hardware por segunda vez. Implementa
/// <see cref="ISensorProvider"/> para que la vista previa use el mismo renderizador que el panel sin
/// enterarse de dónde salen los valores.
/// </remarks>
public sealed class RemoteSensorProvider : ISensorProvider
{
    private readonly Dictionary<string, SensorReading> _sensors = new(StringComparer.Ordinal);

    // La lista ordenada se rehace solo cuando cambia el conjunto de sensores, no cuando cambian sus
    // valores: ReadAll se llama dos o tres veces por segundo y son más de trescientos.
    private List<SensorReading>? _ordered;

    /// <summary>Mete o actualiza lo que acaba de contar el servicio, sin tirar lo que no venga.</summary>
    public void Merge(IEnumerable<SensorInfo> sensors)
    {
        ArgumentNullException.ThrowIfNull(sensors);

        foreach (var sensor in sensors)
        {
            if (!_sensors.ContainsKey(sensor.Id)) _ordered = null;
            _sensors[sensor.Id] = Convert(sensor);
        }

        // Los valores viven en la lista, así que hay que rehacerla igual; lo que se ahorra es ordenar.
        if (_ordered is not null) Reorder();
    }

    /// <summary>Sin servicio no hay lecturas: la vista previa tiene que enseñar el marcador, no un valor viejo.</summary>
    public void Clear()
    {
        _sensors.Clear();
        _ordered = null;
    }

    public IReadOnlyList<SensorReading> ReadAll() =>
        _ordered ??= [.. _sensors.Values.OrderBy(s => s.Id, StringComparer.Ordinal)];

    private void Reorder()
    {
        for (int i = 0; i < _ordered!.Count; i++)
            if (_sensors.TryGetValue(_ordered[i].Id, out var fresh)) _ordered[i] = fresh;
    }

    public SensorReading? Read(string id) => _sensors.GetValueOrDefault(id);

    /// <summary>El refresco lo pide la ventana por la tubería; aquí no hay hardware que tocar.</summary>
    public void Update(IEnumerable<string> ids)
    {
    }

    public void UpdateAll()
    {
    }

    public IReadOnlyList<Notice> Warnings { get; set; } = [];

    public void Dispose()
    {
    }

    private static SensorReading Convert(SensorInfo sensor)
    {
        if (!StrictEnum.TryParse(sensor.Kind, out SensorKind kind)) kind = SensorKind.Factor;

        // La familia sale del propio identificador canónico: «gpu/0/load/core» es de la GPU 0.
        string family = sensor.Id.Split('/', 2)[0];
        if (!StrictEnum.TryParse(family, out HardwareClass hardware)) hardware = HardwareClass.Other;

        return new SensorReading
        {
            Id = sensor.Id,
            Value = sensor.Value is { } value ? (float)value : null,
            Native = new NativeSensor
            {
                HardwareClass = hardware,
                HardwareId = sensor.Hardware,
                HardwareName = sensor.Hardware,
                Kind = kind,
                Name = sensor.Name,
                NativeId = sensor.Id,
            },
        };
    }
}
