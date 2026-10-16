namespace VibeScreen.Core.Sensors;

/// <summary>
/// Magnitud física de una unidad. Dos unidades solo se convierten entre sí si comparten dimensión:
/// unos grados no son un porcentaje por mucho que los dos quepan en un <c>float</c>.
/// </summary>
public enum UnitDimension
{
    None,
    Temperature,
    Ratio,
    Frequency,
    Voltage,
    Current,
    Power,
    Energy,
    RotationSpeed,
    Flow,
    Data,
    Throughput,
    Time,
    Noise,
    Conductivity,
}

/// <summary>
/// Unidad en la que se muestra un valor. <see cref="Auto"/> es la nativa del sensor; el resto son
/// unidades concretas a las que se convierte.
/// </summary>
/// <remarks>
/// Los sensores llegan siempre en la unidad nativa de su <see cref="SensorKind"/>: la memoria en GB,
/// los relojes en MHz. Quien quiera verlo de otra forma lo pide aquí y la conversión la hace
/// <see cref="SensorUnits"/>, no el layout a base de multiplicar por números mágicos.
/// </remarks>
public enum SensorUnit
{
    /// <summary>La unidad nativa del sensor. Es lo que vale cuando el layout no dice nada.</summary>
    Auto,

    /// <summary>Sin unidad: un número a secas, como un factor de multiplicación.</summary>
    None,

    Celsius,
    Fahrenheit,

    Percent,

    Hertz,
    Kilohertz,
    Megahertz,
    Gigahertz,

    Volts,
    Millivolts,

    Amperes,
    Milliamperes,

    Watts,
    Milliwatts,
    Kilowatts,

    MilliwattHours,
    WattHours,
    KilowattHours,

    Rpm,

    LitersPerHour,

    Bytes,
    Kilobytes,
    Megabytes,
    Gigabytes,
    Terabytes,

    BytesPerSecond,
    KilobytesPerSecond,
    MegabytesPerSecond,
    KilobitsPerSecond,
    MegabitsPerSecond,

    Seconds,
    Minutes,
    Hours,
    Days,

    Decibels,

    MicrosiemensPerCentimeter,
}

/// <summary>
/// Símbolos, dimensiones y conversiones de las unidades.
/// </summary>
/// <remarks>
/// Cada unidad se describe respecto a la base de su dimensión con un factor y un desplazamiento:
/// <c>valor_en_unidad = valor_en_base * factor + desplazamiento</c>. Con eso, convertir entre dos
/// unidades de la misma dimensión es pasar por la base, y no hace falta una tabla de pares.
/// </remarks>
public static class SensorUnits
{
    private readonly record struct UnitInfo(UnitDimension Dimension, string Symbol, double Factor, double Offset);

    private const double Kibi = 1024;
    private const double Mebi = Kibi * 1024;
    private const double Gibi = Mebi * 1024;
    private const double Tebi = Gibi * 1024;

    // Base de cada dimensión: Celsius, %, Hz, V, A, W, Wh, RPM, L/h, B, B/s, s, dBA y µS/cm.
    private static readonly Dictionary<SensorUnit, UnitInfo> Table = new()
    {
        [SensorUnit.None] = new(UnitDimension.None, "", 1, 0),

        [SensorUnit.Celsius] = new(UnitDimension.Temperature, "°C", 1, 0),
        [SensorUnit.Fahrenheit] = new(UnitDimension.Temperature, "°F", 1.8, 32),

        [SensorUnit.Percent] = new(UnitDimension.Ratio, "%", 1, 0),

        [SensorUnit.Hertz] = new(UnitDimension.Frequency, "Hz", 1, 0),
        [SensorUnit.Kilohertz] = new(UnitDimension.Frequency, "kHz", 1e-3, 0),
        [SensorUnit.Megahertz] = new(UnitDimension.Frequency, "MHz", 1e-6, 0),
        [SensorUnit.Gigahertz] = new(UnitDimension.Frequency, "GHz", 1e-9, 0),

        [SensorUnit.Volts] = new(UnitDimension.Voltage, "V", 1, 0),
        [SensorUnit.Millivolts] = new(UnitDimension.Voltage, "mV", 1e3, 0),

        [SensorUnit.Amperes] = new(UnitDimension.Current, "A", 1, 0),
        [SensorUnit.Milliamperes] = new(UnitDimension.Current, "mA", 1e3, 0),

        [SensorUnit.Watts] = new(UnitDimension.Power, "W", 1, 0),
        [SensorUnit.Milliwatts] = new(UnitDimension.Power, "mW", 1e3, 0),
        [SensorUnit.Kilowatts] = new(UnitDimension.Power, "kW", 1e-3, 0),

        [SensorUnit.WattHours] = new(UnitDimension.Energy, "Wh", 1, 0),
        [SensorUnit.MilliwattHours] = new(UnitDimension.Energy, "mWh", 1e3, 0),
        [SensorUnit.KilowattHours] = new(UnitDimension.Energy, "kWh", 1e-3, 0),

        [SensorUnit.Rpm] = new(UnitDimension.RotationSpeed, "RPM", 1, 0),

        [SensorUnit.LitersPerHour] = new(UnitDimension.Flow, "L/h", 1, 0),

        // Los múltiplos son binarios, como los de LibreHardwareMonitor: 1 GB son 1024 MB.
        [SensorUnit.Bytes] = new(UnitDimension.Data, "B", 1, 0),
        [SensorUnit.Kilobytes] = new(UnitDimension.Data, "KB", 1 / Kibi, 0),
        [SensorUnit.Megabytes] = new(UnitDimension.Data, "MB", 1 / Mebi, 0),
        [SensorUnit.Gigabytes] = new(UnitDimension.Data, "GB", 1 / Gibi, 0),
        [SensorUnit.Terabytes] = new(UnitDimension.Data, "TB", 1 / Tebi, 0),

        [SensorUnit.BytesPerSecond] = new(UnitDimension.Throughput, "B/s", 1, 0),
        [SensorUnit.KilobytesPerSecond] = new(UnitDimension.Throughput, "KB/s", 1 / Kibi, 0),
        [SensorUnit.MegabytesPerSecond] = new(UnitDimension.Throughput, "MB/s", 1 / Mebi, 0),
        [SensorUnit.KilobitsPerSecond] = new(UnitDimension.Throughput, "Kb/s", 8 / Kibi, 0),
        [SensorUnit.MegabitsPerSecond] = new(UnitDimension.Throughput, "Mb/s", 8 / Mebi, 0),

        [SensorUnit.Seconds] = new(UnitDimension.Time, "s", 1, 0),
        [SensorUnit.Minutes] = new(UnitDimension.Time, "min", 1 / 60.0, 0),
        [SensorUnit.Hours] = new(UnitDimension.Time, "h", 1 / 3600.0, 0),
        [SensorUnit.Days] = new(UnitDimension.Time, "d", 1 / 86400.0, 0),

        [SensorUnit.Decibels] = new(UnitDimension.Noise, "dBA", 1, 0),

        [SensorUnit.MicrosiemensPerCentimeter] = new(UnitDimension.Conductivity, "µS/cm", 1, 0),
    };

    /// <summary>Unidad en la que el backend entrega los valores de esa magnitud.</summary>
    public static SensorUnit NativeUnit(this SensorKind kind) => kind switch
    {
        SensorKind.Temperature => SensorUnit.Celsius,
        SensorKind.Load or SensorKind.Control or SensorKind.Level or SensorKind.Humidity => SensorUnit.Percent,
        SensorKind.Clock => SensorUnit.Megahertz,
        SensorKind.Voltage => SensorUnit.Volts,
        SensorKind.Current => SensorUnit.Amperes,
        SensorKind.Power => SensorUnit.Watts,
        SensorKind.Energy => SensorUnit.MilliwattHours,
        SensorKind.Fan => SensorUnit.Rpm,
        SensorKind.Flow => SensorUnit.LitersPerHour,
        SensorKind.Frequency => SensorUnit.Hertz,
        SensorKind.Data => SensorUnit.Gigabytes,
        SensorKind.SmallData => SensorUnit.Megabytes,
        SensorKind.Throughput => SensorUnit.BytesPerSecond,
        SensorKind.TimeSpan => SensorUnit.Seconds,
        SensorKind.Noise => SensorUnit.Decibels,
        SensorKind.Conductivity => SensorUnit.MicrosiemensPerCentimeter,
        _ => SensorUnit.None,
    };

    /// <summary>Lo que se escribe detrás del número: <c>°C</c>, <c>%</c>, <c>GB</c>. Vacío si no lleva.</summary>
    public static string Symbol(this SensorUnit unit) =>
        Table.TryGetValue(unit, out var info) ? info.Symbol : "";

    public static UnitDimension Dimension(this SensorUnit unit) =>
        Table.TryGetValue(unit, out var info) ? info.Dimension : UnitDimension.None;

    /// <summary><see cref="SensorUnit.Auto"/> se convierte en la unidad nativa del sensor; el resto se queda igual.</summary>
    public static SensorUnit Resolve(SensorUnit unit, SensorKind kind) =>
        unit == SensorUnit.Auto ? kind.NativeUnit() : unit;

    /// <summary>True si la conversión tiene sentido. <see cref="SensorUnit.Auto"/> vale con cualquiera.</summary>
    public static bool CanConvert(SensorUnit from, SensorUnit to) =>
        from == SensorUnit.Auto || to == SensorUnit.Auto || from.Dimension() == to.Dimension();

    /// <summary>
    /// Pasa un valor de una unidad a otra de la misma dimensión. Entre dimensiones distintas
    /// devuelve el valor tal cual: un layout que pida una unidad imposible enseña el número crudo
    /// en lugar de un número inventado, y el aviso lo da quien conoce el sensor.
    /// </summary>
    public static double Convert(double value, SensorUnit from, SensorUnit to)
    {
        if (from == to || !CanConvert(from, to)) return value;
        if (!Table.TryGetValue(from, out var source) || !Table.TryGetValue(to, out var target)) return value;

        double baseValue = (value - source.Offset) / source.Factor;
        return baseValue * target.Factor + target.Offset;
    }

    /// <summary>Las unidades entre las que puede elegir el usuario para ese sensor, la nativa primero.</summary>
    public static IReadOnlyList<SensorUnit> CompatibleWith(SensorUnit unit)
    {
        if (unit == SensorUnit.Auto) return [];

        var dimension = unit.Dimension();
        return Table.Where(e => e.Value.Dimension == dimension)
            .Select(e => e.Key)
            .OrderByDescending(u => u == unit)
            .ThenBy(u => (int)u)
            .ToList();
    }

    /// <summary>Las unidades que puede mostrar un sensor de esa magnitud.</summary>
    public static IReadOnlyList<SensorUnit> For(SensorKind kind) => CompatibleWith(kind.NativeUnit());

    /// <summary>
    /// La lectura en la unidad pedida, o null si el sensor no está o todavía no ha dado valor.
    /// </summary>
    /// <remarks>
    /// Es el único sitio donde se pasa de la lectura cruda a lo que ve el usuario. El número que se
    /// escribe, el recorrido que dibuja una barra y el tramo de color tienen que salir de la misma
    /// cuenta: si no, el texto podría decir 39 mientras el arco ya se ha puesto en rojo.
    /// </remarks>
    /// <param name="reading">La lectura, o null si no hay sensor.</param>
    /// <param name="chosen">La unidad del widget. <see cref="SensorUnit.Auto"/> usa la nativa.</param>
    public static double? Shown(SensorReading? reading, SensorUnit chosen)
    {
        // No finito es no tener lectura. Un NaN es lo que da un sensor sin valor, y un infinito —que
        // sale de dividir por un intervalo de cero al calcular una velocidad— se escribiría tal cual
        // como «∞» en el panel.
        if (reading?.Value is not { } value || !float.IsFinite(value)) return null;

        return Convert(value, reading.Kind.NativeUnit(), Resolve(chosen, reading.Kind));
    }

    /// <summary>
    /// Hasta dónde suele llegar un sensor de esa magnitud, para estrenar un widget con un recorrido
    /// que se parezca al que va a necesitar.
    /// </summary>
    /// <remarks>
    /// Va en la unidad nativa de la magnitud. Es una tabla de conveniencia, no una verdad sobre el
    /// hardware: el usuario cambia el máximo en cuanto ve el suyo.
    /// </remarks>
    public static float Ceiling(this SensorKind kind) => kind switch
    {
        SensorKind.Clock => 5000,
        SensorKind.Fan => 3000,
        SensorKind.Power => 250,
        SensorKind.Voltage => 2,
        SensorKind.Data => 64,
        SensorKind.SmallData => 1024,
        SensorKind.Throughput => 12_500_000,
        SensorKind.Energy => 100_000,
        _ => 100,
    };
}
