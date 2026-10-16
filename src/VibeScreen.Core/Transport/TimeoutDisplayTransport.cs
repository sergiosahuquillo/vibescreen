using VibeScreen.Contracts;
using VibeScreen.Protocol.Display;

namespace VibeScreen.Core.Transport;

/// <summary>
/// Le pone plazo a cada operación con el panel.
/// </summary>
/// <remarks>
/// <para>
/// El panel es un CDC-ACM con control de flujo por RTS/CTS. Si deja de afirmar CTS y no vuelve, la
/// escritura se queda retenida por el control de flujo y el <c>WriteTimeout</c> del puerto no llega
/// a correr: la operación solapada queda pendiente para siempre. No falla, que sería recuperable;
/// es que no termina, y un <c>await</c> que no vuelve no lo recoge ningún <c>catch</c>.
/// </para>
/// <para>
/// De ahí que el plazo vaya aquí y no en el <see cref="System.IO.Ports.SerialPort"/>. Al vencer se
/// lanza <see cref="TimeoutException"/>, que es lo que el servicio ya trata como panel caído: cierra
/// el puerto —cerrarlo aborta la E/S pendiente— y vuelve a conectar. La operación se <b>abandona</b>
/// a propósito en vez de cancelarla: cancelar una escritura solapada no está garantizado, así que
/// esperar a que se entere sería volver a colgarse en el mismo sitio.
/// </para>
/// </remarks>
public sealed class TimeoutDisplayTransport : IDisplayTransport
{
    /// <summary>Lo que se le concede al panel para cada operación.</summary>
    /// <remarks>
    /// Holgado a propósito: un blit son unos pocos kilobytes y no se acerca ni de lejos, así que
    /// llegar aquí no es «va lento», es «no está». Apretarlo convertiría un panel cargado en
    /// reconexiones que no hacen falta.
    /// </remarks>
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(30);

    private readonly IDisplayTransport _inner;
    private readonly TimeSpan _deadline;

    public TimeoutDisplayTransport(IDisplayTransport inner, TimeSpan? deadline = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _deadline = deadline ?? Default;
    }

    public bool IsOpen => _inner.IsOpen;

    public string Name => _inner.Name;

    public ValueTask OpenAsync(CancellationToken cancellationToken = default) =>
        WithDeadline(_inner.OpenAsync(cancellationToken), "panel.timeout.open", cancellationToken);

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
        WithDeadline(_inner.WriteAsync(data, cancellationToken), "panel.timeout.write", cancellationToken);

    public ValueTask FlushAsync(CancellationToken cancellationToken = default) =>
        WithDeadline(_inner.FlushAsync(cancellationToken), "panel.timeout.flush", cancellationToken);

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        WithDeadline(_inner.ReadAsync(buffer, cancellationToken), "panel.timeout.read", cancellationToken);

    // Cerrar no lleva plazo: es lo que se hace para recuperarse de uno, y abandonarlo a medias
    // dejaría el puerto ocupado sin nadie que lo cierre después.
    public ValueTask CloseAsync() => _inner.CloseAsync();

    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    /// <summary>
    /// Le pone el plazo a una operación.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Con <see cref="Task.WaitAsync(TimeSpan, CancellationToken)"/>, que tiene exactamente esta
    /// semántica: <see cref="TimeoutException"/> al vencer, <see cref="OperationCanceledException"/>
    /// al cancelar, y la operación original <b>se abandona</b> en vez de cancelarla, que es lo que
    /// aquí hace falta. Antes esto montaba a mano un origen de cancelación enlazado, un
    /// <c>Task.Delay</c> y un <c>Task.WhenAny</c> en cada operación; un fotograma completo son 75
    /// escrituras más cabecera y vaciado, o sea 77 temporizadores por fotograma, y el servicio está
    /// en marcha durante días.
    /// </para>
    /// <para>
    /// La vía rápida de arriba se salta incluso el <c>AsTask</c>: abrir y cerrar contestan sin
    /// esperar, y convertir un <c>ValueTask</c> ya terminado en <c>Task</c> es reservar por nada.
    /// </para>
    /// </remarks>
    private async ValueTask WithDeadline(ValueTask operation, string what, CancellationToken cancellationToken)
    {
        if (operation.IsCompletedSuccessfully) return;

        var task = operation.AsTask();
        try
        {
            await task.WaitAsync(_deadline, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Abandon(task);
            throw Late(what);
        }
        catch (OperationCanceledException)
        {
            // Quien llama ha cancelado: eso es apagar el servicio, no un panel que no contesta.
            Abandon(task);
            throw;
        }
    }

    private async ValueTask<T> WithDeadline<T>(ValueTask<T> operation, string what, CancellationToken cancellationToken)
    {
        if (operation.IsCompletedSuccessfully) return operation.Result;

        var task = operation.AsTask();
        try
        {
            return await task.WaitAsync(_deadline, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Abandon(task);
            throw Late(what);
        }
        catch (OperationCanceledException)
        {
            Abandon(task);
            throw;
        }
    }

    /// <summary>
    /// Da por vista la excepción de una operación que se deja atrás.
    /// </summary>
    /// <remarks>
    /// Ya no la va a esperar nadie, así que sin esto una que falle más tarde sale por
    /// <see cref="TaskScheduler.UnobservedTaskException"/>.
    /// </remarks>
    private static void Abandon(Task operation) =>
        _ = operation.ContinueWith(static t => t.Exception, CancellationToken.None,
                                   TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    /// <param name="what">El aviso de la operación que no ha contestado: <c>panel.timeout.open</c>…</param>
    private NoticeTimeoutException Late(string what) =>
        new(Notice.Of(what, _inner.Name, Math.Round(_deadline.TotalSeconds)));
}
