using VibeScreen.Protocol.Display;

namespace VibeScreen.Protocol.Rendering;

/// <summary>
/// Reparte el ancho de banda del panel entre los widgets sucios.
/// </summary>
/// <remarks>
/// <para>
/// El panel es una tubería de bytes de caudal constante: en la Fase 0, unos 261.600 B/s con
/// coste por comando indistinguible de cero. Eso permite planificar por presupuesto en lugar
/// de dejar un bucle libre que encole trabajo sin control: cada ciclo acumula
/// <c>bytesPerSecond × utilization × segundos</c> y se emiten rectángulos hasta agotarlo.
/// </para>
/// <para>
/// El acumulador se limita a <c>maxBurstSeconds</c> para que una pausa larga no provoque una
/// ráfaga que sature el enlace de golpe, con una excepción: si el rectángulo pendiente más grande
/// no cabe en ese tope, el acumulador crece hasta él. Sin esa excepción un widget grande
/// (un fotograma completo son 307.200 B) no se enviaría jamás.
/// </para>
/// </remarks>
public sealed class ByteBudgetScheduler
{
    private sealed class Pending
    {
        public required PixelRect Rect { get; set; }
        public required int Priority { get; set; }
        public required long Sequence { get; init; }
        public int Bytes => Rgb565.ByteCount(Rect);
    }

    /// <summary>
    /// Fracción del enlace que se usa por omisión.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es una constante pública porque no es solo cosa del planificador: el medidor del editor tiene
    /// que medir contra <b>este</b> presupuesto y no contra el caudal íntegro del panel, o enseñaría
    /// en verde un tema que en marcha va a ir a tirones. Un solo sitio donde cambiarla.
    /// </para>
    /// <para>
    /// Estuvo en un cuarto, que era una intuición sin medir detrás —«dejar margen mantiene el panel
    /// receptivo»—, y resultó ser demasiado prudente: un tema de diez textos, ocho barras y dos
    /// rótulos, que es lo que mueve sin despeinarse la implementación de referencia en Python, pide
    /// 76.550 B/s. Eso es el 29 % del caudal del panel, pero se salía del presupuesto de un cuarto y
    /// el editor lo habría pintado en rojo. Con 0,6 entra al 49 % y aún queda libre el 40 % del
    /// enlace para que una orden o un widget recién sucio no esperen detrás de una cola.
    /// </para>
    /// </remarks>
    public const double DefaultUtilization = 0.6;

    private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);

    /// <summary>El orden de salida de este ciclo. Se reutiliza: esto corre veinte veces por segundo.</summary>
    private readonly List<KeyValuePair<string, Pending>> _order = [];

    private readonly double _bytesPerTickSecond;
    private readonly double _burstCapBytes;
    private double _available;
    private long _sequence;

    /// <param name="bytesPerSecond">Caudal del panel. Sale de <see cref="DisplayCapabilities.ThroughputBytesPerSecond"/>.</param>
    /// <param name="utilization">Fracción del enlace que se permite usar, de 0 a 1. Dejar margen mantiene el panel receptivo.</param>
    /// <param name="maxBurstSeconds">Cuánto presupuesto sin gastar se puede acumular.</param>
    public ByteBudgetScheduler(int bytesPerSecond, double utilization = DefaultUtilization, double maxBurstSeconds = 0.5)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bytesPerSecond, 0);
        if (utilization is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(utilization), utilization, "La utilización va de 0 (excluido) a 1.");
        ArgumentOutOfRangeException.ThrowIfNegative(maxBurstSeconds);

        BytesPerSecond = bytesPerSecond;
        Utilization = utilization;
        _bytesPerTickSecond = bytesPerSecond * utilization;
        _burstCapBytes = _bytesPerTickSecond * maxBurstSeconds;
    }

    public int BytesPerSecond { get; }
    public double Utilization { get; }

    /// <summary>Presupuesto acumulado y aún sin gastar, en bytes.</summary>
    public double AvailableBytes => _available;

    public int PendingCount => _pending.Count;

    /// <summary>Bytes que costaría enviar ahora mismo todo lo pendiente.</summary>
    public int PendingBytes => _pending.Values.Sum(p => p.Bytes);

    /// <summary>
    /// Marca sucia la zona de un widget. Si ya estaba pendiente se une con la anterior: es la
    /// misma superficie, así que unir no añade huecos. Entre widgets distintos nunca se une.
    /// </summary>
    public void MarkDirty(string widgetId, in PixelRect rect, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(widgetId);
        if (rect.IsEmpty) return;

        if (_pending.TryGetValue(widgetId, out var existing))
        {
            existing.Rect = existing.Rect.Union(rect);
            existing.Priority = Math.Max(existing.Priority, priority);
        }
        else
        {
            _pending[widgetId] = new Pending { Rect = rect, Priority = priority, Sequence = _sequence++ };
        }
    }

    /// <summary>
    /// Acredita el presupuesto del tiempo transcurrido y devuelve lo que cabe enviar, de mayor
    /// prioridad a menor y, a igual prioridad, lo que lleve más tiempo esperando.
    /// </summary>
    public IReadOnlyList<BlitRequest> Drain(TimeSpan elapsed)
    {
        if (elapsed > TimeSpan.Zero)
            _available += _bytesPerTickSecond * elapsed.TotalSeconds;

        // El tope del acumulador nunca impide enviar el rectángulo más grande que haya pendiente.
        int largestPending = 0;
        foreach (var item in _pending.Values)
            if (item.Bytes > largestPending) largestPending = item.Bytes;

        double cap = Math.Max(_burstCapBytes, largestPending);
        if (_available > cap) _available = cap;

        if (_pending.Count == 0) return [];

        _order.Clear();
        foreach (var entry in _pending) _order.Add(entry);
        _order.Sort(static (a, b) => a.Value.Priority != b.Value.Priority
            ? b.Value.Priority.CompareTo(a.Value.Priority)
            : a.Value.Sequence.CompareTo(b.Value.Sequence));

        List<BlitRequest>? emitted = null;
        foreach (var (id, item) in _order)
        {
            int bytes = item.Bytes;

            // Se para, no se salta. Saltar dejaba que un rectángulo pequeño de prioridad baja
            // adelantara a uno grande de prioridad alta y le comiera el presupuesto, que es
            // exactamente lo contrario de lo que promete el orden.
            if (bytes > _available) break;

            _available -= bytes;
            (emitted ??= []).Add(new BlitRequest(id, item.Rect, bytes, item.Priority));
            _pending.Remove(id);
        }

        // Lo normal en un ciclo es no enviar nada: sin lista, ni una reserva.
        return emitted ?? (IReadOnlyList<BlitRequest>)[];
    }

    /// <summary>Cuánto tarda el panel en tragar esos bytes a caudal pleno.</summary>
    public TimeSpan TransferTime(int bytes) => TimeSpan.FromSeconds((double)bytes / BytesPerSecond);

    public void Clear()
    {
        _pending.Clear();
        _available = 0;
    }
}
