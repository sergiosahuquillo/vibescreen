using System.IO.Ports;
using VibeScreen.Contracts;
using VibeScreen.Protocol.Display;

namespace VibeScreen.Core.Transport;

/// <summary>Transporte real sobre un puerto serie.</summary>
/// <remarks>
/// <para>
/// El panel es un CDC-ACM sobre USB: los 115200 baudios son nominales y no describen el caudal
/// real, que la Fase 0 midió en unos 261.600 B/s. Se mantienen porque el firmware espera que se
/// negocien, pero nadie debería deducir el rendimiento de esa cifra.
/// </para>
/// <para>
/// El control de flujo por RTS/CTS es el que usa la implementación de referencia y es lo que
/// evita desbordar el búfer del microcontrolador.
/// </para>
/// </remarks>
public sealed class SerialDisplayTransport : IDisplayTransport
{
    public const int NominalBaudRate = 115200;

    private readonly SerialPort _port;
    private Stream? _stream;

    public SerialDisplayTransport(string portName, TimeSpan? writeTimeout = null, TimeSpan? readTimeout = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(portName);

        _port = new SerialPort(portName, NominalBaudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.RequestToSend,
            WriteTimeout = (int)(writeTimeout ?? TimeSpan.FromSeconds(30)).TotalMilliseconds,
            ReadTimeout = (int)(readTimeout ?? TimeSpan.FromSeconds(1)).TotalMilliseconds,
            DtrEnable = true,
            RtsEnable = true,
        };
    }

    public string Name => _port.PortName;
    public bool IsOpen => _port.IsOpen;

    public ValueTask OpenAsync(CancellationToken cancellationToken = default)
    {
        if (_port.IsOpen) return ValueTask.CompletedTask;

        try
        {
            _port.Open();
        }
        catch (UnauthorizedAccessException e)
        {
            throw new NoticeIOException(Notice.Of("panel.port-busy", _port.PortName), e);
        }

        _stream = _port.BaseStream;
        return ValueTask.CompletedTask;
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        Stream stream = _stream ?? throw new NoticeInvalidOperationException(Notice.Of("panel.port-closed"));
        await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Espera a que el búfer de salida se vacíe de verdad. Sin esto, cualquier medición de
    /// tiempo mide la velocidad a la que el sistema acepta bytes, no la del panel.
    /// </summary>
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        Stream stream = _stream ?? throw new NoticeInvalidOperationException(Notice.Of("panel.port-closed"));
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Stream stream = _stream ?? throw new NoticeInvalidOperationException(Notice.Of("panel.port-closed"));
        try
        {
            return await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return 0; // El panel original no contesta al saludo: silencio no es error.
        }
    }

    public ValueTask CloseAsync()
    {
        if (_port.IsOpen) _port.Close();
        _stream = null;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _port.Dispose();
        return ValueTask.CompletedTask;
    }
}
