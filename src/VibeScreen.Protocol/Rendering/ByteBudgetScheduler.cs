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
        public required PixelFormat Format { get; init; }
        public int Bytes => Format.ByteCount(Rect);
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
    /// el editor lo habría pintado en rojo. Con 0,6 entraba al 49 % y quedaba libre el 40 % del
    /// enlace para que una orden o un widget recién sucio no esperaran detrás de una cola.
    /// </para>
    /// <para>
    /// Y subió a 0,75 cuando llegaron los GIF, que son los primeros widgets que de verdad llenan el
    /// enlace: un Mangekyō de 64 px a 10 fotogramas por segundo son 82 KB/s, más de la mitad del
    /// presupuesto de 0,6. El caudal, medido otra vez el 26 de octubre de 2026, sigue en 259.600 B/s,
    /// así que un cuarto de margen son 65 KB/s. Si el enlace fuera más despacio que eso no se rompe
    /// nada: el planificador cobra por tiempo transcurrido, los envíos tardan más y los widgets se
    /// saltan fotogramas en vez de dejar cola. Lo que crece es lo que espera una orden de la ventana,
    /// que va detrás del envío en curso: en el peor caso, tras una pausa, de 0,3 s a 0,375 s.
    /// </para>
    /// <para>
    /// Se probó ese mismo día al 0,8 en la Turing 3.5" rev. A, con un tema que pedía el 139 % de
    /// aquel presupuesto —cuatro GIF, barras, históricos, temperaturas, un reloj con segundos y textos
    /// fijos—, así que el enlace iba lleno todo el rato: ni un resto en barras, históricos o números
    /// al cambiar, y el brillo y el cambio de tema respondiendo al instante. Lo único que se resentía
    /// eran los GIF, que es lo que toca cuando se pide más de lo que cabe. Se deja en 0,75 y no en lo
    /// probado para quedarse un poco por debajo: el panel ya ha enseñado que no siempre se queda con
    /// todo lo que recibe.
    /// </para>
    /// </remarks>
    public const double DefaultUtilization = 0.75;

    private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);

    /// <summary>El orden de salida de este ciclo. Se reutiliza: esto corre veinte veces por segundo.</summary>
    private readonly List<KeyValuePair<string, Pending>> _order = [];

    private readonly double _bytesPerTickSecond;
    private readonly double _burstCapBytes;
    private readonly PixelFormat _format;
    private double _available;
    private long _sequence;

    /// <param name="bytesPerSecond">Caudal del panel. Sale de <see cref="DisplayCapabilities.ThroughputBytesPerSecond"/>.</param>
    /// <param name="utilization">Fracción del enlace que se permite usar, de 0 a 1. Dejar margen mantiene el panel receptivo.</param>
    /// <param name="maxBurstSeconds">Cuánto presupuesto sin gastar se puede acumular.</param>
    /// <param name="format">
    /// Cómo viajan los píxeles, que es lo que dice cuántos bytes cuesta cada rectángulo. Sale de
    /// <see cref="DisplayCapabilities.PixelFormat"/>.
    /// </param>
    public ByteBudgetScheduler(int bytesPerSecond, double utilization = DefaultUtilization, double maxBurstSeconds = 0.5,
                               PixelFormat format = PixelFormat.Rgb565LittleEndian)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bytesPerSecond, 0);
        if (utilization is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(utilization), utilization, "La utilización va de 0 (excluido) a 1.");
        ArgumentOutOfRangeException.ThrowIfNegative(maxBurstSeconds);

        BytesPerSecond = bytesPerSecond;
        Utilization = utilization;
        _bytesPerTickSecond = bytesPerSecond * utilization;
        _burstCapBytes = _bytesPerTickSecond * maxBurstSeconds;
        _format = format;
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
            _pending[widgetId] = new Pending { Rect = rect, Priority = priority, Sequence = _sequence++, Format = _format };
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
