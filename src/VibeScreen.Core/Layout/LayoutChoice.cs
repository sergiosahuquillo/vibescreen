namespace VibeScreen.Core.Layout;

/// <summary>
/// Qué layout pintar cuando lo elige la máquina y no el usuario.
/// </summary>
/// <remarks>
/// Vive aquí, y no en el servicio, porque es una decisión y no una tarea: así se comprueba con una
/// lista de nombres y unos dados cargados, sin carpetas, sin panel y sin esperar a un arranque.
/// </remarks>
public static class LayoutChoice
{
    /// <summary>
    /// Uno al azar, evitando el de la última vez.
    /// </summary>
    /// <remarks>
    /// Evitarlo importa más cuanto menos hay: con dos layouts, el azar puro repite la mitad de los
    /// arranques y la impresión que da es que la opción no funciona. Con uno solo no hay nada que
    /// evitar, y ese es el único caso en que puede salir el mismo dos veces seguidas.
    /// </remarks>
    /// <param name="names">Los layouts que hay, por nombre. Vacío si no hay ninguno.</param>
    /// <param name="last">El que se pintó la última vez, si se sabe.</param>
    /// <param name="random">Los dados. Por omisión los compartidos; se pasan otros para comprobarlo.</param>
    /// <returns>El elegido, o null si no hay entre qué elegir.</returns>
    public static string? AtRandom(IReadOnlyList<string> names, string? last = null, Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Count == 0) return null;
        if (names.Count == 1) return names[0];

        random ??= Random.Shared;

        // Sin el último, que es justo el que no queremos repetir. Como los nombres son carpetas y no
        // se repiten entre sí, quitarlo nunca deja la lista vacía habiendo dos o más.
        var pool = names.Where(name => !string.Equals(name, last, StringComparison.OrdinalIgnoreCase)).ToList();
        return pool[random.Next(pool.Count)];
    }
}
