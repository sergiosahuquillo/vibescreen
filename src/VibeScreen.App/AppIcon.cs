using System.Buffers.Binary;
using Avalonia.Controls;
using SkiaSharp;
using VibeScreen.App.Theme;

namespace VibeScreen.App;

/// <summary>
/// El icono de la aplicación, dibujado con Skia en lugar de guardado como fichero.
/// </summary>
/// <remarks>
/// <para>
/// Es la marca de la barra de título, trazo por trazo y del mismo realce: la ventana, la barra de
/// tareas y la bandeja tienen que enseñar el mismo dibujo. Por eso no hay un binario en el
/// repositorio, que se quedaría con el color de fábrica y se olvidaría de actualizar al primer cambio.
/// </para>
/// <para>
/// Sale como un <c>.ico</c> con varios tamaños y no como un PNG suelto: con un PNG, Avalonia hace el
/// icono a su tamaño tal cual y Windows lo encoge a 16 o a 24 px a su manera, que a una línea de
/// píxel y medio la deja deshecha. Con el <c>.ico</c>, Avalonia escoge el tamaño que toca para el DPI
/// de la pantalla —16 por la escala en la bandeja, 24 por la escala en la barra de tareas— y cada
/// uno está dibujado a su medida.
/// </para>
/// </remarks>
public static class AppIcon
{
    // Los de la bandeja (16 por escala) y los de la barra de tareas (24 por escala) de 100 % a 300 %,
    // más los grandes para quien los pida, como Alt+Tab.
    private static readonly int[] Sizes = [16, 20, 24, 28, 30, 32, 36, 40, 42, 48, 60, 64, 72, 96, 128, 256];

    /// <summary>El icono con el realce que está puesto ahora.</summary>
    public static WindowIcon Current()
    {
        var accent = Palette.Accent;
        return new WindowIcon(new MemoryStream(Ico(new SKColor(accent.R, accent.G, accent.B, accent.A))));
    }

    /// <summary>El <c>.ico</c> entero, con todos los tamaños, en el color que se pida.</summary>
    /// <remarks>
    /// Lo usa también <c>VibeScreen.IconExport</c> para el icono de los ficheros —el del ejecutable,
    /// el acceso directo y el instalador—, que sale de aquí con el realce de fábrica: un fichero no
    /// puede seguir el realce elegido, pero sí tiene que ser el mismo dibujo que la ventana.
    /// </remarks>
    public static byte[] Ico(SKColor color) => Pack(Sizes.Select(size => Draw(size, color)).ToArray());

    /// <summary>La marca sola en un PNG, para quien necesite una imagen y no un icono.</summary>
    public static byte[] Png(int size, SKColor color) => Draw(size, color);

    /// <summary>La marca a un tamaño: el mismo lienzo de 24 y el mismo trazo de 1,5 que la barra de título.</summary>
    private static byte[] Draw(int size, SKColor color)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(size / 24f);

            using var mark = SKPath.ParseSvgPathData(Icons.BrandmarkData);
            using var stroke = new SKPaint
            {
                Color = color,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
            };
            canvas.DrawPath(mark, stroke);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Mete los PNG en un <c>.ico</c>: la cabecera, una entrada por tamaño y las imágenes detrás.
    /// </summary>
    /// <remarks>
    /// PNG dentro del <c>.ico</c> y no mapa de bits: Windows los entiende desde Vista, y Avalonia le
    /// pasa cada entrada a <c>CreateIconFromResourceEx</c> sin mirar qué lleva dentro.
    /// </remarks>
    private static byte[] Pack(byte[][] images)
    {
        const int header = 6, entry = 16;

        var offset = header + entry * images.Length;
        var ico = new byte[offset + images.Sum(image => image.Length)];
        var span = ico.AsSpan();

        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], (ushort)images.Length);

        for (var i = 0; i < images.Length; i++)
        {
            var at = span[(header + entry * i)..];

            // 256 no cabe en un byte: el formato lo escribe como 0.
            at[0] = at[1] = (byte)(Sizes[i] % 256);
            BinaryPrimitives.WriteUInt16LittleEndian(at[4..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(at[6..], 32);
            BinaryPrimitives.WriteUInt32LittleEndian(at[8..], (uint)images[i].Length);
            BinaryPrimitives.WriteUInt32LittleEndian(at[12..], (uint)offset);

            images[i].CopyTo(span[offset..]);
            offset += images[i].Length;
        }

        return ico;
    }
}
