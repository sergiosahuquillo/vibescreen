#if WINDOWS
using System.Runtime.Versioning;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;
using VibeScreen.Contracts;

namespace VibeScreen.Core.Sensors;

/// <summary>Sensores de Windows, con LibreHardwareMonitor.</summary>
/// <remarks>
/// <para>
/// El acceso ring0 va por PawnIO, un driver de kernel que LibreHardwareMonitor usa si está instalado pero
/// no instala. Sin él, o sin permisos de administrador, no hay temperatura de CPU ni sensores de la placa.
/// VibeScreen cuenta con ring0 por defecto, así que su falta se avisa en lugar de pasar en silencio.
/// </para>
/// <para>
/// Algunos sensores solo existen después de la primera actualización, así que se actualiza todo el
/// hardware antes de enumerarlos.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class LibreHardwareMonitorSensorProvider : ISensorProvider
{
    private readonly Computer _computer;
    private readonly List<Entry> _entries;
    private readonly Dictionary<string, Entry> _byId;
    private readonly List<Notice> _warnings = [];

    public LibreHardwareMonitorSensorProvider()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = true,
        };
        _computer.Open();
        UpdateAll();

        var found = new Dictionary<NativeSensor, (ISensor Sensor, IHardware Hardware)>(ReferenceEqualityComparer.Instance);
        foreach (var (hardware, root) in Flatten(_computer.Hardware))
        {
            var (hardwareClass, rank) = Classify(root);
            foreach (var sensor in hardware.Sensors)
            {
                // Un tipo de sensor que el modelo canónico aún no conoce se deja fuera.
                if (!Enum.TryParse(sensor.SensorType.ToString(), out SensorKind kind)) continue;

                var native = new NativeSensor
                {
                    HardwareClass = hardwareClass,
                    HardwareId = root.Identifier.ToString(),
                    HardwareRank = rank,
                    HardwareName = hardware.Name,
                    Kind = kind,
                    Name = sensor.Name,
                    NativeId = sensor.Identifier.ToString(),
                    NativeIndex = sensor.Index,
                };
                found[native] = (sensor, hardware);
            }
        }

        _entries = CanonicalSensorIds.Assign(found.Keys)
            .Select(a => new Entry(a.Id, a.Sensor, found[a.Sensor].Sensor, found[a.Sensor].Hardware))
            .ToList();
        _byId = _entries.ToDictionary(e => e.Id, StringComparer.Ordinal);

        CheckRing0();
    }

    public IReadOnlyList<Notice> Warnings => _warnings;

    /// <summary>
    /// Todas las lecturas.
    /// </summary>
    /// <remarks>
    /// Una lista nueva cada vez, porque los valores cambian y quien la recibe se la queda. Lo que se
    /// ahorra respecto al LINQ de antes es el enumerador y el crecimiento de la lista, y con más de
    /// trescientos sensores y una llamada por cada respuesta de «sensors» eso ya se nota.
    /// </remarks>
    public IReadOnlyList<SensorReading> ReadAll()
    {
        var readings = new List<SensorReading>(_entries.Count);
        foreach (var entry in _entries) readings.Add(ToReading(entry));
        return readings;
    }

    public SensorReading? Read(string id) => _byId.TryGetValue(id, out var entry) ? ToReading(entry) : null;

    public void Update(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var hardware = new HashSet<IHardware>(ReferenceEqualityComparer.Instance);
        foreach (string id in ids)
        {
            if (_byId.TryGetValue(id, out var entry)) hardware.Add(entry.Hardware);
        }

        foreach (var item in hardware) item.Update();
    }

    public void UpdateAll()
    {
        foreach (var (hardware, _) in Flatten(_computer.Hardware)) hardware.Update();
    }

    public void Dispose() => _computer.Close();

    private void CheckRing0()
    {
        if (!Environment.IsPrivilegedProcess)
        {
            _warnings.Add(Notice.Of("sensors.no-admin"));
        }
        else if (!PawnIo.IsInstalled)
        {
            _warnings.Add(Notice.Of("sensors.no-pawnio"));
        }
        else if (!_entries.Any(e => e.Native.HardwareClass == HardwareClass.Cpu && e.Native.Kind == SensorKind.Temperature))
        {
            _warnings.Add(Notice.Of("sensors.no-cpu-temperature", PawnIo.Version));
        }
    }

    private static SensorReading ToReading(Entry entry) => new()
    {
        Id = entry.Id,
        Native = entry.Native,
        Value = entry.Sensor.Value,
    };

    /// <summary>Familia y rango del hardware de primer nivel. La GPU dedicada primero: la de Intel suele ser la integrada.</summary>
    private static (HardwareClass Class, int Rank) Classify(IHardware root) => root.HardwareType switch
    {
        HardwareType.Cpu => (HardwareClass.Cpu, 0),
        HardwareType.GpuNvidia => (HardwareClass.Gpu, 0),
        HardwareType.GpuAmd => (HardwareClass.Gpu, 1),
        HardwareType.GpuIntel => (HardwareClass.Gpu, 2),
        HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController => (HardwareClass.Motherboard, 0),
        HardwareType.Memory => (HardwareClass.Memory, MemoryRank(root.Name)),
        HardwareType.Storage => (HardwareClass.Storage, 0),
        HardwareType.Network => (HardwareClass.Network, 0),
        _ => (HardwareClass.Other, 0),
    };

    /// <summary>
    /// Rango dentro de la familia de memoria. LibreHardwareMonitor enumera un hardware por módulo DIMM y
    /// otro para la memoria virtual, y los lista antes que el total, así que sin esto la RAM física caía
    /// en <c>memory/4</c>. Un layout que pide <c>memory/0</c> espera la RAM física.
    /// </summary>
    private static int MemoryRank(string name) =>
        name.StartsWith("DIMM", StringComparison.OrdinalIgnoreCase) ? 2
        : name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ? 1
        : 0;

    /// <summary>Todo el hardware, subhardware incluido, junto al hardware de primer nivel del que cuelga.</summary>
    private static IEnumerable<(IHardware Hardware, IHardware Root)> Flatten(IEnumerable<IHardware> roots)
    {
        foreach (var root in roots)
        {
            var pending = new Stack<IHardware>([root]);
            while (pending.TryPop(out var hardware))
            {
                yield return (hardware, root);
                foreach (var sub in hardware.SubHardware) pending.Push(sub);
            }
        }
    }

    private sealed record Entry(string Id, NativeSensor Native, ISensor Sensor, IHardware Hardware);
}
#endif
