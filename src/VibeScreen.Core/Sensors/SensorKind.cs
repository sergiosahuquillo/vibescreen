namespace VibeScreen.Core.Sensors;

/// <summary>Magnitud que mide un sensor. Da el tercer segmento del identificador canónico y la unidad.</summary>
public enum SensorKind
{
    Temperature,
    Load,
    Clock,
    Voltage,
    Current,
    Power,
    Energy,
    Fan,
    Control,
    Flow,
    Level,
    Factor,
    Frequency,
    Data,
    SmallData,
    Throughput,
    TimeSpan,
    Noise,
    Humidity,
    Conductivity,
}

public static class SensorKindExtensions
{
    /// <summary>
    /// Los segmentos en minúsculas, uno por valor y en su orden.
    /// </summary>
    /// <remarks>
    /// Antes se hacía con <c>ToString().ToLowerInvariant()</c> en cada llamada, y <see cref="Segment"/>
    /// se llama una vez por sensor en cada respuesta de «sensors»: con la GUI abierta son más de
    /// trescientas veces por segundo, dos cadenas cada una, para devolver siempre lo mismo.
    /// </remarks>
    private static readonly string[] Segments =
        [.. Enum.GetValues<SensorKind>().Select(kind => kind.ToString().ToLowerInvariant())];

    /// <summary>Segmento del identificador canónico, en minúsculas: <c>temperature</c>, <c>smalldata</c>.</summary>
    public static string Segment(this SensorKind kind) =>
        (uint)kind < (uint)Segments.Length ? Segments[(int)kind] : kind.ToString().ToLowerInvariant();

    /// <summary>Símbolo de la unidad en la que llega el valor: <c>°C</c>, <c>%</c>, <c>MHz</c>.</summary>
    public static string Unit(this SensorKind kind) => kind.NativeUnit().Symbol();
}
