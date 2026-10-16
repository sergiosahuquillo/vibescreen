using System.IO.Pipes;

namespace VibeScreen.Contracts;

/// <summary>
/// El lado cliente del IPC: lo usan la CLI, la bandeja y el editor. Una conexión atiende una orden
/// detrás de otra; para una ráfaga, abre varias.
/// </summary>
public sealed class ServiceClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly JsonLineChannel _channel;
    private int _nextId;

    private ServiceClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
        _channel = new JsonLineChannel(pipe);
    }

    /// <exception cref="ServiceUnavailableException">No hay nadie escuchando.</exception>
    public static async Task<ServiceClient> ConnectAsync(string pipeName = IpcEndpoint.PipeName, CancellationToken cancellationToken = default)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync((int)IpcEndpoint.ConnectTimeout.TotalMilliseconds, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException e)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);

            // No es que no esté: es que no se deja hablar con él. Distinguirlo importa porque el
            // arreglo es otro, y con el mensaje de «no está en marcha» se pierden horas buscando un
            // servicio que estaba en marcha todo el tiempo.
            throw new ServiceUnavailableException(Notice.Of("service.pipe-denied", pipeName, e.Message), e);
        }
        catch (Exception e) when (e is TimeoutException or IOException)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw new ServiceUnavailableException(innerException: e);
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new ServiceClient(pipe);
    }

    public async Task<ServiceResponse> SendAsync(ServiceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.Id = Interlocked.Increment(ref _nextId);
        await _channel.WriteAsync<ServiceRequest>(request, ServiceJsonContext.Default.ServiceRequest, cancellationToken).ConfigureAwait(false);

        return await _channel.ReadAsync(ServiceJsonContext.Default.ServiceResponse, cancellationToken).ConfigureAwait(false)
               ?? throw new NoticeIOException(Notice.Of("service.closed"));
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Dispose();
        await _pipe.DisposeAsync().ConfigureAwait(false);
    }
}
