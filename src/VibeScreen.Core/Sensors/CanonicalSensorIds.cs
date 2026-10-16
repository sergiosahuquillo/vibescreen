using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VibeScreen.Core.Sensors;

/// <summary>Un sensor con el identificador canónico que le ha correspondido.</summary>
public readonly record struct SensorAssignment(string Id, NativeSensor Sensor);

/// <summary>
/// Da a cada sensor su identificador canónico, <c>{familia}/{n}/{tipo}/{nombre}</c>, por ejemplo
/// <c>cpu/0/temperature/package</c>.
/// </summary>
/// <remarks>
/// <para>
/// Los layouts no pueden usar los identificadores del backend, o un tema hecho en una máquina AMD
/// fallaría en una Intel. Los sensores habituales reciben un nombre neutro (<c>package</c>,
/// <c>total</c>, <c>core</c>) y el resto se nombra a partir del nombre que da el backend.
/// </para>
/// <para>
/// El identificador de LibreHardwareMonitor no sirve de clave: en el equipo de referencia
/// <c>/gpu-nvidia/0/load/3</c> aparece dos veces, como «GPU Bus» y como «GPU Memory». La clave es
/// familia, posición del hardware, tipo y nombre; si aun así se repite (hay dos «D3D Copy»), se
/// desempata con <c>#2</c>, <c>#3</c>… en el orden del backend. El resultado no depende del orden en
/// que lleguen los sensores.
/// </para>
/// </remarks>
public static partial class CanonicalSensorIds
{
    public static IReadOnlyList<SensorAssignment> Assign(IEnumerable<NativeSensor> sensors)
    {
        ArgumentNullException.ThrowIfNull(sensors);
        var list = sensors.ToList();

        // Posición de cada hardware en su familia: primero el rango que da el backend (la GPU dedicada
        // antes que la integrada) y después el identificador, para que no dependa del orden de llegada.
        var hardwareIndex = new Dictionary<(HardwareClass, string), int>();
        var hardware = list
            .GroupBy(s => (s.HardwareClass, s.HardwareId))
            .Select(g => (g.Key, Rank: g.Min(s => s.HardwareRank)));
        foreach (var family in hardware.GroupBy(h => h.Key.HardwareClass))
        {
            int index = 0;
            foreach (var h in family.OrderBy(h => h.Rank).ThenBy(h => h.Key.HardwareId, StringComparer.Ordinal))
                hardwareIndex[h.Key] = index++;
        }

        var ordered = list
            .OrderBy(s => s.HardwareClass)
            .ThenBy(s => hardwareIndex[(s.HardwareClass, s.HardwareId)])
            .ThenBy(s => s.Kind)
            .ThenBy(s => s.NativeIndex)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ThenBy(s => s.NativeId, StringComparer.Ordinal)
            .ToList();

        var ids = new string?[ordered.Count];
        var taken = new HashSet<string>(StringComparer.Ordinal);

        // Los nombres neutros se reparten primero y por orden de preferencia de las reglas: «package»
        // es del mejor candidato aunque el backend liste antes otro sensor que también encaje.
        foreach (var rule in Rules)
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                var s = ordered[i];
                if (ids[i] is not null || s.HardwareClass != rule.Class || s.Kind != rule.Kind) continue;
                if (rule.Map(s.Name) is not { } name) continue;

                string id = Prefix(s, hardwareIndex) + name;
                if (taken.Add(id)) ids[i] = id;
            }
        }

        var result = new List<SensorAssignment>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            var s = ordered[i];
            string? id = ids[i];
            if (id is null)
            {
                string baseId = Prefix(s, hardwareIndex) + Slug(s.Name);
                id = baseId;
                for (int n = 2; !taken.Add(id); n++) id = $"{baseId}#{n}";
            }
            result.Add(new SensorAssignment(id, s));
        }

        return result;
    }

    /// <summary>«Core (Tctl/Tdie)» da <c>core-tctl-tdie</c>: letras ASCII en minúscula, cifras y guiones.</summary>
    public static string Slug(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var sb = new StringBuilder(name.Length);
        bool separator = false;
        foreach (char c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                separator = sb.Length > 0;
                continue;
            }
            if (separator) sb.Append('-');
            sb.Append(char.ToLowerInvariant(c));
            separator = false;
        }
        return sb.Length == 0 ? "sensor" : sb.ToString();
    }

    /// <summary>Las familias en minúsculas, por lo mismo que <see cref="SensorKindExtensions.Segment"/>.</summary>
    private static readonly string[] Families =
        [.. Enum.GetValues<HardwareClass>().Select(hardware => hardware.ToString().ToLowerInvariant())];

    private static string Family(HardwareClass hardware) =>
        (uint)hardware < (uint)Families.Length ? Families[(int)hardware] : hardware.ToString().ToLowerInvariant();

    private static string Prefix(NativeSensor s, Dictionary<(HardwareClass, string), int> hardwareIndex) =>
        $"{Family(s.HardwareClass)}/{hardwareIndex[(s.HardwareClass, s.HardwareId)]}/{s.Kind.Segment()}/";

    private sealed record Rule(HardwareClass Class, SensorKind Kind, Func<string, string?> Map);

    private static Rule Named(HardwareClass hardware, SensorKind kind, string name, string canonical) =>
        new(hardware, kind, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase) ? canonical : null);

    private static Rule Numbered(HardwareClass hardware, SensorKind kind, Regex pattern, Func<Match, string> canonical) =>
        new(hardware, kind, n => pattern.Match(n) is { Success: true } m ? canonical(m) : null);

    // El orden es la preferencia. Los nombres son los de LibreHardwareMonitor.
    private static readonly Rule[] Rules =
    [
        // Ryzen publica Tctl/Tdie juntos o por separado; Intel, «CPU Package».
        Named(HardwareClass.Cpu, SensorKind.Temperature, "Core (Tctl/Tdie)", "package"),
        Named(HardwareClass.Cpu, SensorKind.Temperature, "CPU Package", "package"),
        Named(HardwareClass.Cpu, SensorKind.Temperature, "Core (Tdie)", "package"),
        Named(HardwareClass.Cpu, SensorKind.Temperature, "Core (Tctl)", "package"),
        Named(HardwareClass.Cpu, SensorKind.Load, "CPU Total", "total"),
        Numbered(HardwareClass.Cpu, SensorKind.Load, CpuThreadLoad(), m => $"core/{Ordinal(m, 1)}/thread/{Ordinal(m, 2)}"),
        Numbered(HardwareClass.Cpu, SensorKind.Load, CpuCoreLoad(), m => $"core/{Ordinal(m, 1)}"),
        Numbered(HardwareClass.Cpu, SensorKind.Clock, CoreClock(), m => $"core/{Ordinal(m, 1)}"),
        // En Ryzen la tensión fiable es la telemetría del SMU; la del Super I/O se queda en la placa.
        Named(HardwareClass.Cpu, SensorKind.Voltage, "Core (SVI2 TFN)", "vcore"),
        Named(HardwareClass.Cpu, SensorKind.Voltage, "CPU Core", "vcore"),
        Named(HardwareClass.Cpu, SensorKind.Power, "CPU Package", "package"),

        Named(HardwareClass.Gpu, SensorKind.Temperature, "GPU Core", "core"),
        Named(HardwareClass.Gpu, SensorKind.Temperature, "GPU Hot Spot", "hotspot"),
        Named(HardwareClass.Gpu, SensorKind.Temperature, "GPU Memory Junction", "memory"),
        Named(HardwareClass.Gpu, SensorKind.Load, "GPU Core", "core"),
        Named(HardwareClass.Gpu, SensorKind.Clock, "GPU Core", "core"),
        Named(HardwareClass.Gpu, SensorKind.Clock, "GPU Memory", "memory"),
        Numbered(HardwareClass.Gpu, SensorKind.Fan, GpuFan(), m => $"{Ordinal(m, 1)}"),
        Numbered(HardwareClass.Gpu, SensorKind.Control, GpuFan(), m => $"{Ordinal(m, 1)}"),
        Named(HardwareClass.Gpu, SensorKind.Power, "GPU Package", "package"),

        Named(HardwareClass.Memory, SensorKind.Load, "Memory", "used"),
        Named(HardwareClass.Memory, SensorKind.Data, "Memory Used", "used"),
        Named(HardwareClass.Memory, SensorKind.Data, "Memory Available", "available"),
    ];

    /// <summary>Los nombres del backend cuentan desde 1 y los identificadores canónicos desde 0. Sin número, 0.</summary>
    private static int Ordinal(Match match, int group) =>
        match.Groups[group].Success
            ? Math.Max(0, int.Parse(match.Groups[group].ValueSpan, CultureInfo.InvariantCulture) - 1)
            : 0;

    [GeneratedRegex(@"^CPU Core #(\d+) Thread #(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CpuThreadLoad();

    [GeneratedRegex(@"^CPU Core #(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CpuCoreLoad();

    [GeneratedRegex(@"^(?:CPU )?Core #(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CoreClock();

    [GeneratedRegex(@"^GPU Fan(?: (\d+))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GpuFan();
}
