using VibeScreen.Contracts;
using VibeScreen.Core.Layout;

namespace VibeScreen.Service;

/// <summary>
/// Qué rutas acepta el servicio cuando le mandan un tema.
/// </summary>
/// <remarks>
/// <para>
/// En Windows el servicio corre como LocalSystem —lo necesita para el acceso ring0 de los
/// sensores— y su tubería está abierta a todos los usuarios locales a propósito, porque la GUI no
/// va elevada. El resultado es que cualquier proceso del usuario, por poco privilegiado que sea,
/// puede pedirle a SYSTEM que abra un fichero. Eso obliga a mirar lo que se pide:
/// </para>
/// <list type="bullet">
/// <item>
/// Una ruta de red hace que SYSTEM abra una conexión saliente y se autentique con la cuenta de
/// máquina contra quien esté al otro lado. Es la primitiva clásica de escalada por relay.
/// </item>
/// <item>
/// Un fichero enorme se carga entero en memoria antes de mirar siquiera si es JSON.
/// </item>
/// </list>
/// <para>
/// Vive aparte de <see cref="PanelHost"/> para poder comprobarlo sin montar un servicio entero.
/// </para>
/// </remarks>
public static class LayoutPathRule
{
    /// <summary>Lo más que puede ocupar un tema. Los de verdad no llegan ni a la centésima parte.</summary>
    public const long MaxBytes = 4L * 1024 * 1024;

    /// <summary>
    /// El problema de esa ruta, o null si se puede usar. <paramref name="full"/> sale absoluta.
    /// </summary>
    /// <param name="root">
    /// La carpeta en la que tienen que estar los temas. Por omisión la de verdad; las comprobaciones
    /// pasan la suya para no escribir en la del usuario.
    /// </param>
    public static Notice? Problem(string? path, out string full, string? root = null)
    {
        full = "";
        if (string.IsNullOrWhiteSpace(path)) return Notice.Of("layout.path.missing");

        // Tal como llega, antes de normalizar: fuera de Windows, GetFullPath convierte una UNC en un
        // nombre relativo perfectamente inocente y dejaría de parecer lo que es.
        if (IsRemote(path))
            return Notice.Of("layout.path.remote");

        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Notice.Of("layout.path.invalid", e.Message);
        }

        if (IsRemote(full))
            return Notice.Of("layout.path.remote");

        if (!string.Equals(Path.GetExtension(full), ".json", StringComparison.OrdinalIgnoreCase))
            return Notice.Of("layout.path.not-json");

        string folder = root ?? LayoutPaths.Root;
        if (!Inside(full, folder))
            return Notice.Of("layout.path.outside", folder);

        // Y lo último, porque cuesta disco: a dónde apunta de verdad. Las dos comprobaciones de
        // arriba miran la ruta tal como se escribe, y GetFullPath no resuelve puntos de reanálisis,
        // así que un C:\temas que sea un enlace a \\servidor\temas las pasa las
        // dos con pinta de local.
        if (Resolved(full) is { } target && IsRemote(target))
            return Notice.Of("layout.path.link-remote");

        return null;
    }

    /// <summary>Lo mismo para un recurso que viene dentro del tema, como la imagen de fondo.</summary>
    public static Notice? ResourceProblem(string? path) =>
        !string.IsNullOrWhiteSpace(path) && IsRemote(path)
            ? Notice.Of("layout.path.resource-remote", path)
            : null;

    /// <summary>Lo que ocupa de más, o null si cabe. Se mira antes de leer, no después.</summary>
    public static Notice? SizeProblem(string full)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(full);

        try
        {
            var file = new FileInfo(full);
            return file.Exists && file.Length > MaxBytes
                ? Notice.Of("layout.path.too-big", file.Length, MaxBytes)
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Si no se puede ni mirar, que lo diga quien lo abra: aquí no hay nada que decidir.
            return null;
        }
    }

    /// <summary>
    /// Si una ruta cuelga de una carpeta.
    /// </summary>
    /// <remarks>
    /// Con el separador al final de la carpeta, que sin él «…/layouts-de-otro» empezaría igual que
    /// «…/layouts» y pasaría por dentro. Las dos rutas llegan ya normalizadas por GetFullPath.
    /// </remarks>
    private static bool Inside(string full, string folder)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A dónde apunta de verdad la ruta, siguiendo los enlaces, o null si no hay ninguno o no se
    /// puede saber.
    /// </summary>
    /// <remarks>
    /// Se mira el fichero y también sus carpetas, subiendo, porque el enlace puede estar en cualquier
    /// tramo de la ruta y basta uno para que lo que parece local no lo sea. <c>returnFinalTarget</c>
    /// sigue la cadena entera, que es lo que interesa: da igual por cuántos enlaces se pase si el
    /// último está en un servidor.
    /// </remarks>
    private static string? Resolved(string full)
    {
        try
        {
            for (string? at = full; !string.IsNullOrEmpty(at); at = Path.GetDirectoryName(at))
            {
                FileSystemInfo entry = Directory.Exists(at) ? new DirectoryInfo(at) : new FileInfo(at);
                if (entry.ResolveLinkTarget(returnFinalTarget: true) is { } target) return target.FullName;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Si no se puede ni mirar, que lo diga quien lo abra: aquí no hay nada que decidir.
        }

        return null;
    }

    /// <summary>
    /// Rutas que no son de un disco de esta máquina: UNC (<c>\\servidor\recurso</c>), espacios de
    /// nombres de dispositivo (<c>\\.\</c>, <c>\\?\</c>) y las que traen esquema, como <c>http:</c>.
    /// </summary>
    private static bool IsRemote(string path)
    {
        string normalized = path.Replace('/', '\\');
        if (normalized.StartsWith(@"\\", StringComparison.Ordinal)) return true;

        return Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile;
    }
}
