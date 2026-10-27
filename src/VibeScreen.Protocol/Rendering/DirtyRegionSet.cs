using VibeScreen.Protocol.Display;

namespace VibeScreen.Protocol.Rendering;

/// <summary>
/// Conjunto de rectángulos sucios.
/// </summary>
/// <remarks>
/// <para>
/// Deliberadamente <b>no fusiona</b> rectángulos cercanos. La medición de la Fase 0 sobre
/// una Turing 3.5" rev. A dio un coste fijo por comando indistinguible de cero (la regresión
/// sobre ocho tamaños, de 1 KB a 300 KB, da −0,18 ms) con un caudal constante de unos
/// 261.600 B/s. Con coste por comando nulo, fusionar dos rectángulos separados solo añade
/// los bytes del hueco sin ahorrar nada.
/// </para>
/// <para>
/// Lo único que sí conviene es descartar rectángulos ya contenidos en otro, que es ahorro puro.
/// </para>
/// </remarks>
public static class DirtyRegionSet
{
    /// <summary>Elimina los rectángulos contenidos por completo en otro del conjunto.</summary>
    public static List<PixelRect> DropContained(IEnumerable<PixelRect> rects)
    {
        // Ordenar de mayor a menor área hace que el contenedor se examine siempre antes que el contenido.
        var ordered = rects.Where(r => !r.IsEmpty).OrderByDescending(r => r.Area).ToList();
        var kept = new List<PixelRect>(ordered.Count);

        foreach (var candidate in ordered)
        {
            bool covered = false;
            foreach (var existing in kept)
            {
                if (existing.Contains(candidate)) { covered = true; break; }
            }
            if (!covered) kept.Add(candidate);
        }

        return kept;
    }

    /// <summary>Bytes totales que costaría enviar el conjunto tal cual.</summary>
    public static int TotalBytes(IEnumerable<PixelRect> rects, PixelFormat format = PixelFormat.Rgb565LittleEndian) =>
        rects.Sum(r => format.ByteCount(r));
}
