using VibeScreen.Protocol.Display;

namespace VibeScreen.Protocol.Rendering;

/// <summary>Un widget visto como consumidor de ancho de banda: su caja por su frecuencia de refresco.</summary>
public readonly record struct WidgetBandwidth(string WidgetId, PixelRect Rect, double RefreshHz)
{
    /// <summary>El coste de un widget es su caja contenedora, no su contenido.</summary>
    public double BytesPerSecond => Rgb565.ByteCount(Rect) * RefreshHz;
}

/// <summary>Reparto del enlace entre los widgets de un layout, ordenado de mayor a menor consumo.</summary>
public sealed record BandwidthReport
{
    public required double RequiredBytesPerSecond { get; init; }

    /// <summary>El caudal íntegro del panel. Es el techo físico, no lo que se puede gastar.</summary>
    public required int PanelBytesPerSecond { get; init; }

    /// <summary>
    /// Lo que de verdad se puede gastar: el caudal del panel por la fracción que usa el planificador.
    /// </summary>
    /// <remarks>
    /// Medir contra el caudal íntegro daba un número cuatro veces optimista: un tema que pedía el
    /// 95 % del presupuesto salía como «24 % del panel», en verde, y en marcha iba a tirones.
    /// </remarks>
    public required double BudgetBytesPerSecond { get; init; }

    public required IReadOnlyList<WidgetBandwidth> Consumers { get; init; }

    /// <summary>Fracción del presupuesto que pide el layout. Por encima de 1 no cabe.</summary>
    public double Utilization => RequiredBytesPerSecond / BudgetBytesPerSecond;

    public bool Oversubscribed => Utilization > 1.0;

    /// <summary>Por encima de este punto el panel responde con retraso visible aunque técnicamente quepa.</summary>
    public bool NearLimit => Utilization > 0.6;
}

/// <summary>
/// Calcula lo que un layout le pide al panel. Alimenta el medidor del editor, para que el
/// usuario vea que su tema no cabe antes de instalarlo en lugar de descubrirlo por los tirones.
/// </summary>
public static class BandwidthBudget
{
    /// <param name="utilization">
    /// La misma fracción con la que corre <see cref="ByteBudgetScheduler"/>. Por omisión,
    /// <see cref="ByteBudgetScheduler.DefaultUtilization"/>: el informe y el planificador tienen que
    /// hablar del mismo presupuesto o el aviso llega tarde.
    /// </param>
    public static BandwidthReport Analyze(
        IEnumerable<WidgetBandwidth> widgets,
        DisplayCapabilities capabilities,
        double utilization = ByteBudgetScheduler.DefaultUtilization)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        if (utilization is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(utilization), utilization, "La utilización va de 0 (excluido) a 1.");

        var ordered = widgets.OrderByDescending(w => w.BytesPerSecond).ToList();
        return new BandwidthReport
        {
            RequiredBytesPerSecond = ordered.Sum(w => w.BytesPerSecond),
            PanelBytesPerSecond = capabilities.ThroughputBytesPerSecond,
            BudgetBytesPerSecond = capabilities.ThroughputBytesPerSecond * utilization,
            Consumers = ordered,
        };
    }
}
