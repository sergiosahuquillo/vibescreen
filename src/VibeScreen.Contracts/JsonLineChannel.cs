using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace VibeScreen.Contracts;

/// <summary>
/// Un mensaje JSON por línea sobre una tubería. Sirve de trama porque JSON escapa los saltos de línea
/// del contenido: un mensaje nunca contiene un <c>\n</c> literal, así que la primera línea es el
/// mensaje entero y no hay que contar bytes ni inventar cabeceras.
/// </summary>
public sealed class JsonLineChannel : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Lo más largo que puede ser un mensaje, en caracteres.
    /// </summary>
    /// <remarks>
    /// Las órdenes de verdad caben en unos pocos kilobytes; el megabyte es holgura, no un objetivo.
    /// El tope existe porque en Windows el servicio corre como SYSTEM y la tubería está abierta a
    /// todos los usuarios locales a propósito: sin él, un proceso cualquiera puede escribir sin
    /// mandar nunca un salto de línea y hacer crecer al servicio hasta quedarse sin memoria, con la
    /// máquina entera sufriéndolo por el camino.
    /// </remarks>
    public const int MaxLineChars = 1024 * 1024;

    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;

    // La lectura va por trozos y con su propio resto: StreamReader.ReadLineAsync acumula sin tope
    // hasta encontrar el salto de línea, y es justo el tope lo que hace falta.
    private readonly char[] _chunk = new char[4096];
    private int _chunkStart;
    private int _chunkLength;

    public JsonLineChannel(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        _reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        // Salto de línea fijo: el de Windows metería un \r que el otro lado tendría que quitar.
        _writer = new StreamWriter(stream, Utf8, bufferSize: 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
    }

    public async Task WriteAsync<T>(T value, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
    {
        string line = JsonSerializer.Serialize(value, typeInfo);
        await _writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>El siguiente mensaje, o null si el otro extremo ha cerrado.</summary>
    public async Task<T?> ReadAsync<T>(JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
    {
        string? line;
        do
        {
            line = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) return default;
        }
        while (line.Length == 0); // Una línea en blanco no es un mensaje ni es el fin: se salta.

        return JsonSerializer.Deserialize(line, typeInfo);
    }

    /// <summary>
    /// La siguiente línea, o null si el otro extremo ha cerrado.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// La línea pasa de <see cref="MaxLineChars"/>. Quien escribe así no está hablando el protocolo.
    /// </exception>
    private async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var line = new StringBuilder();

        while (true)
        {
            if (_chunkStart >= _chunkLength)
            {
                _chunkStart = 0;
                _chunkLength = await _reader.ReadAsync(_chunk, cancellationToken).ConfigureAwait(false);

                // Fin del otro lado: lo que hubiera suelto sin salto de línea es una línea igual.
                if (_chunkLength == 0) return line.Length > 0 ? Trim(line) : null;
            }

            var pending = _chunk.AsSpan(_chunkStart, _chunkLength - _chunkStart);
            int newline = pending.IndexOf('\n');

            if (newline < 0)
            {
                Grow(line, pending);
                _chunkStart = _chunkLength;
                continue;
            }

            Grow(line, pending[..newline]);
            _chunkStart += newline + 1;
            return Trim(line);
        }
    }

    private static void Grow(StringBuilder line, ReadOnlySpan<char> more)
    {
        if (line.Length + more.Length > MaxLineChars)
            throw new InvalidDataException(Notice.Of("ipc.line-too-long", MaxLineChars).ToString());

        line.Append(more);
    }

    // El que escribe usa «\n», pero quitar un «\r» de más no cuesta nada y admite a quien no.
    private static string Trim(StringBuilder line)
    {
        if (line.Length > 0 && line[^1] == '\r') line.Length--;
        return line.ToString();
    }

    public void Dispose()
    {
        // Cerrar el escritor vacía lo que quede pendiente, y eso falla si el otro extremo ya se ha
        // ido. Que el otro extremo se vaya es lo normal —el servicio se reinicia, el cliente se
        // cierra—, así que cerrar el canal no puede ser motivo para tirar a nadie.
        try
        {
            _writer.Dispose();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
        }

        _reader.Dispose();
    }
}
