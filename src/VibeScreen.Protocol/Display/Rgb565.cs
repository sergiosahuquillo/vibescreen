using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace VibeScreen.Protocol.Display;

/// <summary>Conversión a RGB565, el único formato que aceptan estos paneles.</summary>
public static class Rgb565
{
    /// <summary>
    /// Convierte un bloque BGRA de 8 bits por canal (el orden nativo de SkiaSharp en
    /// máquinas little-endian) a RGB565 little-endian, que es lo que espera la revisión A.
    /// </summary>
    /// <param name="source">Píxeles de origen, 4 bytes por píxel.</param>
    /// <param name="sourceStride">Bytes por fila en el origen, que puede ser mayor que <paramref name="width"/> * 4.</param>
    /// <param name="destination">Destino, 2 bytes por píxel y sin relleno entre filas.</param>
    public static void FromBgra8888(
        ReadOnlySpan<byte> source, int sourceStride, int width, int height,
        Span<byte> destination, bool bigEndian = false)
    {
        long required = (long)width * height * 2;
        if (destination.Length < required)
            throw new ArgumentException($"El destino necesita {required} bytes.", nameof(destination));
        if (sourceStride < width * 4)
            throw new ArgumentOutOfRangeException(nameof(sourceStride), sourceStride, "El paso de fila es menor que la fila.");
        if (source.Length < (long)sourceStride * (height - 1) + (long)width * 4)
            throw new ArgumentException("El origen es más corto de lo que indican ancho, alto y paso.", nameof(source));

        // El camino corriente: destino little-endian en una máquina little-endian, que es todo lo que
        // hay hoy. Ahí un ushort escrito tal cual ya sale en el orden que espera el panel, así que se
        // recorre el destino como ushort y desaparecen los dos Slice con comprobación de límites por
        // píxel. Son 153.600 píxeles por fotograma completo, y este es el único camino de verdad
        // caliente del programa: por aquí pasa todo lo que se envía.
        if (!bigEndian && BitConverter.IsLittleEndian)
        {
            Span<ushort> output = MemoryMarshal.Cast<byte, ushort>(destination);
            int at = 0;
            for (int row = 0; row < height; row++)
            {
                ReadOnlySpan<byte> line = source.Slice(row * sourceStride, width * 4);
                for (int col = 0; col < width; col++)
                {
                    int i = col * 4;
                    output[at++] = Pack(line[i + 2], line[i + 1], line[i]); // B G R A -> R G B
                }
            }
            return;
        }

        // Y el camino explícito, que no da por hecho el orden de bytes de la máquina.
        int outIndex = 0;
        for (int row = 0; row < height; row++)
        {
            ReadOnlySpan<byte> line = source.Slice(row * sourceStride, width * 4);
            for (int col = 0; col < width; col++)
            {
                int i = col * 4;
                ushort packed = Pack(line[i + 2], line[i + 1], line[i]); // B G R A -> R G B
                if (bigEndian)
                    BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(outIndex, 2), packed);
                else
                    BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(outIndex, 2), packed);
                outIndex += 2;
            }
        }
    }

    /// <summary>Empaqueta un color de 24 bits en RGB565: 5 bits de rojo, 6 de verde y 5 de azul.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort Pack(byte r, byte g, byte b) =>
        (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));

    /// <summary>Desempaqueta a los ocho bits por canal más cercanos. No es exacto: RGB565 pierde información.</summary>
    public static (byte R, byte G, byte B) Unpack(ushort value)
    {
        int r = (value >> 11) & 0x1F;
        int g = (value >> 5) & 0x3F;
        int b = value & 0x1F;
        // Replicar los bits altos en los bajos da un reparto uniforme de 0 a 255.
        return ((byte)((r << 3) | (r >> 2)), (byte)((g << 2) | (g >> 4)), (byte)((b << 3) | (b >> 2)));
    }

    /// <summary>
    /// Bytes que ocupa un rectángulo en el cable.
    /// </summary>
    /// <remarks>
    /// La cuenta va en <c>long</c> y se convierte con <c>checked</c>. El resultado cabe en un
    /// <c>int</c> para cualquier panel que <c>LayoutLoader</c> acepte, y de ahí el tipo de vuelta:
    /// es lo que quieren el búfer de envío y el recuento del planificador. Pero el área por dos
    /// desbordaba en silencio y devolvía un tamaño <b>negativo</b>, que aguas abajo es un
    /// <c>new byte[]</c> imposible o un rectángulo que nadie sabe interpretar; así, si alguna vez
    /// llega un tamaño que no cabe, se entera en el sitio.
    /// </remarks>
    /// <exception cref="OverflowException">El rectángulo ocupa más de <see cref="int.MaxValue"/> bytes.</exception>
    public static int ByteCount(in PixelRect rect) => checked((int)(rect.Area * 2));
}
