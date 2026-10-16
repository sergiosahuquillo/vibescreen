namespace VibeScreen.Service;

/// <summary>
/// Caudal de los últimos segundos, a partir de un contador que solo crece.
/// </summary>
/// <remarks>
/// <para>
/// La media desde que arrancó el layout no sirve para un medidor en vivo: el fotograma completo del
/// principio son 307.200 B y la deja disparada durante minutos, y después esconde los picos.
/// Con una ventana corta, lo que se ve es lo que está pasando ahora.
/// </para>
/// <para>
/// Escribe el bucle de pintado y lee quien pregunte el estado, sin cerrojo: las muestras solo las
/// toca <see cref="Add"/>, y el resultado se publica en un único <c>double</c>.
/// </para>
/// </remarks>
public sealed class RateWindow(TimeSpan window)
{
    private readonly Queue<(TimeSpan At, long Total)> _samples = new();
    private double _perSecond;

    /// <summary>Unidades por segundo en la ventana. Cero mientras no haya dos muestras separadas.</summary>
    public double PerSecond => Volatile.Read(ref _perSecond);

    /// <summary>Apunta el contador acumulado en ese instante.</summary>
    public void Add(TimeSpan at, long total)
    {
        _samples.Enqueue((at, total));
        while (_samples.Count > 1 && _samples.Peek().At < at - window) _samples.Dequeue();

        var oldest = _samples.Peek();
        double seconds = (at - oldest.At).TotalSeconds;
        Volatile.Write(ref _perSecond, seconds > 0 ? (total - oldest.Total) / seconds : 0);
    }

    /// <summary>Empieza de cero: el contador anterior ya no vale, por ejemplo al cambiar de layout.</summary>
    public void Reset()
    {
        _samples.Clear();
        Volatile.Write(ref _perSecond, 0);
    }
}
