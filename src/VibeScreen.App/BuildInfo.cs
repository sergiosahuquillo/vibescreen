using System.Reflection;

namespace VibeScreen.App;

/// <summary>De qué compilación es este ejecutable.</summary>
public static class BuildInfo
{
    /// <summary>
    /// La versión, sin la <c>v</c>.
    /// </summary>
    /// <remarks>
    /// Del <c>InformationalVersion</c> y no del <c>Version</c> a secas: es el que lleva el sufijo de
    /// la compilación cuando lo hay, y el que se corresponde con lo que se publica. Se le quita lo
    /// que el SDK añade detrás de un «+», que es el hash de git y no cabe en la barra de estado.
    /// </remarks>
    public static string Version { get; } = Read();

    private static string Read()
    {
        string? raw = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (raw is null) return "";

        int plus = raw.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? raw : raw[..plus];
    }
}
