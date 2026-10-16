namespace VibeScreen.Core.Rendering;

/// <summary>
/// Con qué letra se escribe cuando la que se pide puede no estar instalada.
/// </summary>
/// <remarks>
/// Es puro a propósito: recibe la lista de las instaladas en vez de preguntarle al sistema, así que
/// se puede comprobar sin depender de las fuentes que tenga la máquina donde corran las pruebas.
/// Quien lo llama le pasa la lista de Skia, que es quien acaba pintando tanto la ventana como el panel.
/// </remarks>
public static class FontChoice
{
    /// <summary>
    /// Las monoespaciadas que se prueban, por orden.
    /// </summary>
    /// <remarks>
    /// Vim no trae fuente propia: usa la del terminal. En Windows eso es hoy Cascadia Mono, y antes
    /// era Consolas, así que ese es el orden. Detrás van las habituales de Linux y de macOS, y al
    /// final Courier New, que está en todas partes y es el último cartucho antes de rendirse.
    /// </remarks>
    public static readonly IReadOnlyList<string> Monospace =
    [
        "Cascadia Mono", "Cascadia Code", "Consolas",
        "JetBrains Mono", "Fira Code", "Source Code Pro", "IBM Plex Mono",
        "DejaVu Sans Mono", "Liberation Mono", "Noto Sans Mono", "Ubuntu Mono",
        "SF Mono", "Menlo", "Monaco",
        "Courier New",
    ];

    /// <summary>
    /// La primera de <paramref name="wanted"/> que esté instalada, o null si no hay ninguna.
    /// </summary>
    /// <remarks>
    /// Sin distinguir mayúsculas: las listas de fuentes de Windows y de fontconfig no se ponen de
    /// acuerdo en eso, y un tema traído de otra máquina trae el nombre como lo escribiera aquella.
    /// </remarks>
    public static string? FirstInstalled(IEnumerable<string?> wanted, IEnumerable<string> installed)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        ArgumentNullException.ThrowIfNull(installed);

        var available = new HashSet<string>(installed, StringComparer.OrdinalIgnoreCase);
        return wanted.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name) && available.Contains(name));
    }

    /// <summary>
    /// La letra de la ventana: la elegida si existe y, si no, la primera monoespaciada que esté.
    /// Null significa que no hay ninguna y decide el sistema, que es mejor que dibujar cuadrados.
    /// </summary>
    /// <param name="chosen">Lo que el usuario haya elegido, o null para no pedir nada en concreto.</param>
    public static string? Resolve(string? chosen, IEnumerable<string> installed) =>
        FirstInstalled(chosen is null ? Monospace : [chosen, .. Monospace], installed);
}
