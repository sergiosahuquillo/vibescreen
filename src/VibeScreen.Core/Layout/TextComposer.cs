using System.Globalization;
using VibeScreen.Core.Sensors;

namespace VibeScreen.Core.Layout;

/// <summary>
/// Compone lo que muestra un <see cref="TextWidget"/>: etiqueta, número, unidad y en qué orden.
/// </summary>
/// <remarks>
/// Está fuera del renderizador porque lo usan los dos lados: el servicio para pintar y el editor para
/// enseñar cómo va a quedar mientras el usuario toca los decimales.
/// </remarks>
public static class TextComposer
{
    /// <summary>Lo que muestra un widget cuyo sensor no existe en esta máquina o aún no tiene lectura.</summary>
    public const string MissingValue = "--";

    /// <summary>Decimales máximos. Más allá de esto el panel no tiene píxeles para enseñarlo.</summary>
    public const int MaxDecimals = 6;

    /// <summary>
    /// El texto del widget. <paramref name="reading"/> es la lectura del sensor, o null si el widget
    /// no tiene sensor, el sensor no existe en esta máquina o todavía no ha dado valor.
    /// </summary>
    public static string Compose(TextWidget widget, SensorReading? reading)
    {
        ArgumentNullException.ThrowIfNull(widget);

        if (widget.Sensor is null) return widget.Text ?? "";

        return Build(reading, Unit(widget.Unit, reading?.Kind), new Format(
            widget.Decimals, widget.ShowUnit, widget.UnitPlacement, widget.UnitSpace, widget.Prefix, widget.Suffix));
    }

    /// <summary>
    /// El valor que va escrito dentro del arco, o una cadena vacía si el arco no lleva ninguno.
    /// </summary>
    /// <remarks>
    /// Sale en la unidad del arco, que es la misma en la que están su mínimo y su máximo: un número
    /// en otra escala que la que dibuja la aguja diría lo contrario que el dibujo que tiene alrededor.
    /// </remarks>
    public static string Compose(GaugeWidget gauge, SensorReading? reading)
    {
        ArgumentNullException.ThrowIfNull(gauge);

        if (gauge.Label is not { } label) return "";

        return Build(reading, Unit(gauge.Unit, reading?.Kind), new Format(
            label.Decimals, label.ShowUnit, label.UnitPlacement, label.UnitSpace, label.Prefix, label.Suffix));
    }

    /// <summary>La unidad en la que sale el valor, ya resuelta contra el sensor.</summary>
    public static SensorUnit Unit(TextWidget widget, SensorKind? kind)
    {
        ArgumentNullException.ThrowIfNull(widget);
        return Unit(widget.Unit, kind);
    }

    /// <summary>La unidad elegida en el widget, o la nativa del sensor si está en automático.</summary>
    public static SensorUnit Unit(SensorUnit chosen, SensorKind? kind)
    {
        // Sin sensor no hay unidad nativa que resolver: un «auto» se queda sin unidad hasta que
        // aparezca la lectura, y una unidad elegida a mano se respeta.
        if (chosen != SensorUnit.Auto) return chosen;
        return kind is { } k ? k.NativeUnit() : SensorUnit.None;
    }

    /// <summary>Cómo se escribe un valor. Es lo único que comparten un texto y el número de un arco.</summary>
    private readonly record struct Format(
        int Decimals, bool ShowUnit, UnitPlacement Placement, bool Space, string? Prefix, string? Suffix);

    private static string Build(SensorReading? reading, SensorUnit unit, Format format)
    {
        string number = Number(reading, unit, format.Decimals);
        string symbol = format.ShowUnit ? unit.Symbol() : "";
        string separator = format.Space ? " " : "";

        string body = symbol.Length == 0
            ? number
            : format.Placement == UnitPlacement.Prefix
                ? symbol + separator + number
                : number + separator + symbol;

        return format.Prefix + body + format.Suffix;
    }

    private static string Number(SensorReading? reading, SensorUnit unit, int decimals) =>
        SensorUnits.Shown(reading, unit) is { } shown
            ? shown.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
            : MissingValue;
}
