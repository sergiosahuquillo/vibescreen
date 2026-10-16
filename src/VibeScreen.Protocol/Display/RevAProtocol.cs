namespace VibeScreen.Protocol.Display;

/// <summary>
/// Codificador del protocolo de la revisión A. Puro: no abre puertos ni conoce el transporte,
/// solo produce los bytes que hay que escribir. Eso lo hace verificable sin hardware.
/// </summary>
public static class RevAProtocol
{
    /// <summary>Longitud de un comando normal.</summary>
    public const int CommandLength = 6;

    /// <summary>Longitud de la trama de orientación, que lleva carga extra.</summary>
    public const int OrientationFrameLength = 16;

    /// <summary>Valor máximo de cada coordenada: los cuatro campos son de 10 bits.</summary>
    public const int MaxCoordinate = 1023;

    /// <summary>
    /// Empaqueta un comando con su rectángulo. Los cuatro campos ocupan 10 bits cada uno,
    /// repartidos a caballo entre bytes: 40 bits de coordenadas más 8 de comando.
    /// </summary>
    public static void WriteCommand(Span<byte> destination, RevACommand command, int x, int y, int endX, int endY)
    {
        if (destination.Length < CommandLength)
            throw new ArgumentException($"Se necesitan al menos {CommandLength} bytes.", nameof(destination));

        ThrowIfOutOfRange(x, nameof(x));
        ThrowIfOutOfRange(y, nameof(y));
        ThrowIfOutOfRange(endX, nameof(endX));
        ThrowIfOutOfRange(endY, nameof(endY));

        destination[0] = (byte)(x >> 2);
        destination[1] = (byte)(((x & 3) << 6) + (y >> 4));
        destination[2] = (byte)(((y & 15) << 4) + (endX >> 6));
        destination[3] = (byte)(((endX & 63) << 2) + (endY >> 8));
        destination[4] = (byte)(endY & 0xFF);
        destination[5] = (byte)command;
    }

    public static byte[] Command(RevACommand command, int x = 0, int y = 0, int endX = 0, int endY = 0)
    {
        var buffer = new byte[CommandLength];
        WriteCommand(buffer, command, x, y, endX, endY);
        return buffer;
    }

    /// <summary>Cabecera que precede a los píxeles de un blit. El rectángulo se expresa con extremos inclusivos.</summary>
    public static byte[] DisplayBitmapHeader(in PixelRect rect)
    {
        if (rect.IsEmpty)
            throw new ArgumentException("El rectángulo no puede estar vacío.", nameof(rect));
        return Command(RevACommand.DisplayBitmap, rect.X, rect.Y, rect.Right, rect.Bottom);
    }

    /// <summary>
    /// Brillo. El panel escala al revés que la interfaz: 0 es el máximo y 255 el mínimo,
    /// así que aquí se invierte una sola vez y el resto del código habla siempre en porcentaje.
    /// </summary>
    public static byte[] SetBrightness(int percent) =>
        Command(RevACommand.SetBrightness, BrightnessRaw(percent));

    /// <summary>Valor absoluto que espera el panel, de 0 (máximo) a 255 (mínimo). Viaja en el campo X del comando.</summary>
    public static byte BrightnessRaw(int percent) => (byte)(255 - (int)(CheckedBrightness(percent) / 100.0 * 255));

    /// <summary>
    /// Devuelve el brillo si es un porcentaje y si no se planta. Aparte de la trama porque el brillo
    /// se puede guardar para más tarde, y un valor imposible tiene que fallar cuando se pide, no cuando
    /// le toque salir.
    /// </summary>
    public static int CheckedBrightness(int percent) =>
        percent is < 0 or > 100
            ? throw new ArgumentOutOfRangeException(nameof(percent), percent, "El brillo va de 0 a 100.")
            : percent;

    /// <summary>Convierte el valor absoluto del panel de vuelta a porcentaje. Inversa exacta de <see cref="SetBrightness"/>.</summary>
    public static int BrightnessPercentFromRaw(byte raw) => (int)Math.Round((255 - raw) / 255.0 * 100);

    /// <summary>
    /// Trama de orientación. Lleva la orientación desplazada 100 y las dimensiones ya orientadas,
    /// es decir con ancho y alto intercambiados cuando el panel va apaisado.
    /// </summary>
    public static byte[] SetOrientation(Orientation orientation, int nativeWidth, int nativeHeight)
    {
        if (!Enum.IsDefined(orientation))
            throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "Solo hay cuatro orientaciones.");

        var buffer = new byte[OrientationFrameLength];
        WriteCommand(buffer, RevACommand.SetOrientation, 0, 0, 0, 0);

        int width = orientation.IsRotated() ? nativeHeight : nativeWidth;
        int height = orientation.IsRotated() ? nativeWidth : nativeHeight;

        buffer[6] = (byte)((int)orientation + 100);
        buffer[7] = (byte)(width >> 8);
        buffer[8] = (byte)(width & 0xFF);
        buffer[9] = (byte)(height >> 8);
        buffer[10] = (byte)(height & 0xFF);
        return buffer;
    }

    /// <summary>Saludo de identificación: seis veces el mismo byte. El panel contesta con seis bytes o con nada.</summary>
    public static byte[] Hello()
    {
        var buffer = new byte[CommandLength];
        buffer.AsSpan().Fill((byte)RevACommand.Hello);
        return buffer;
    }

    /// <summary>Interpreta la respuesta al saludo. Devuelve null si el panel no contestó (Turing 3.5" original).</summary>
    public static (int Width, int Height)? ParseHelloResponse(ReadOnlySpan<byte> response)
    {
        if (response.Length < 6) return null;
        byte first = response[0];
        for (int i = 1; i < 6; i++)
            if (response[i] != first) return null;

        return first switch
        {
            0x01 => (320, 480),
            0x02 => (480, 800),
            0x03 => (600, 1024),
            _ => null,
        };
    }

    private static void ThrowIfOutOfRange(int value, string name)
    {
        if (value is < 0 or > MaxCoordinate)
            throw new ArgumentOutOfRangeException(name, value, $"Las coordenadas del protocolo van de 0 a {MaxCoordinate}.");
    }
}
