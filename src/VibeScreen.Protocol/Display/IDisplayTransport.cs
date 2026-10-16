namespace VibeScreen.Protocol.Display;

/// <summary>
/// Canal de bytes hacia el panel. Se separa del driver para que la lógica del protocolo
/// sea verificable sin hardware: en las pruebas se sustituye por un transporte que graba.
/// </summary>
public interface IDisplayTransport : IAsyncDisposable
{
    bool IsOpen { get; }

    /// <summary>Nombre del puerto, para diagnóstico.</summary>
    string Name { get; }

    ValueTask OpenAsync(CancellationToken cancellationToken = default);
    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>Bloquea hasta que el sistema ha vaciado el búfer de salida.</summary>
    ValueTask FlushAsync(CancellationToken cancellationToken = default);

    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
    ValueTask CloseAsync();
}
