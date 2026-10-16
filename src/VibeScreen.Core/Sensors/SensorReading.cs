namespace VibeScreen.Core.Sensors;

/// <summary>Un sensor con su identificador canónico y su última lectura.</summary>
public sealed record SensorReading
{
    /// <summary>Identificador canónico, el que usan los layouts: <c>cpu/0/temperature/package</c>.</summary>
    public required string Id { get; init; }

    public required NativeSensor Native { get; init; }

    /// <summary>
    /// Última lectura, o null si el sensor existe pero aún no ha dado valor. Un 0 es una lectura válida:
    /// un ventilador parado no es un ventilador que no existe.
    /// </summary>
    public float? Value { get; init; }

    public SensorKind Kind => Native.Kind;

    public string Unit => Native.Kind.Unit();
}
