using VibeScreen.Contracts;
using VibeScreen.Core.Storage;

namespace VibeScreen.Core.Layout;

/// <summary>
/// Dónde viven los layouts: <b>un layout es una carpeta</b>, no un fichero suelto.
/// </summary>
/// <remarks>
/// <para>
/// Dentro de esa carpeta va el <c>.json</c> y todo lo que necesite para pintarse, que hoy es la
/// imagen de fondo. Así el layout se sostiene solo: se puede copiar a otra máquina, comprimir y
/// mandárselo a alguien, o dejar de lado la foto original sin que el panel se entere.
/// </para>
/// <para>
/// Y la ubicación no se negocia con el usuario. Un diálogo de «guardar como» invita a dejar el
/// layout en Descargas, al lado de otro, o en una carpeta que mañana se renombra; de ahí salen
/// fondos que se pierden y rutas que dejan de existir. Aquí el usuario pone un nombre y del resto
/// se encarga el programa.
/// </para>
/// </remarks>
public static class LayoutPaths
{
    /// <summary>La carpeta de todos los layouts, compartida por la máquina como el propio servicio.</summary>
    public static string Root => Path.Combine(AppPaths.Shared, "layouts");

    /// <summary>El tema que trae el instalador, con el que arranca el panel la primera vez.</summary>
    /// <remarks>
    /// Sin él, una instalación limpia dejaba el panel en negro hasta que alguien abriera la ventana y
    /// eligiera algo. Se llama como la marca porque es un nombre que vale igual en los dos idiomas. Es
    /// un tema más: se edita, se renombra o se borra como cualquier otro, y el instalador solo lo deja
    /// en la primera instalación.
    /// </remarks>
    public const string Bundled = "VibeScreen";

    /// <summary>El fichero que le toca a un layout con ese nombre.</summary>
    public static string For(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Path.Combine(Root, name, name + ".json");
    }

    /// <summary>Si ya hay un layout guardado con ese nombre.</summary>
    public static bool Exists(string name) =>
        !string.IsNullOrWhiteSpace(name) && NameProblem(name) is null && File.Exists(For(name));

    /// <summary>
    /// Los layouts guardados, por nombre y en orden.
    /// </summary>
    /// <remarks>
    /// Una carpeta solo cuenta si dentro está su <c>.json</c>: así una carpeta a medias, o algo que
    /// alguien dejó ahí a mano, no aparece en el desplegable como si fuera un layout.
    /// </remarks>
    public static IReadOnlyList<string> All()
    {
        try
        {
            if (!Directory.Exists(Root)) return [];

            return
            [
                .. Directory.EnumerateDirectories(Root)
                    .Select(folder => Path.GetFileName(folder))
                    .OfType<string>()
                    .Where(Exists)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase),
            ];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Borra un layout entero. Devuelve false si no estaba.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Un layout <b>es una carpeta</b>, así que borrarlo es borrarla entera con lo que lleve dentro:
    /// el <c>.json</c> y la imagen de fondo. No hay vuelta atrás.
    /// </para>
    /// <para>
    /// Lo único que hay entre un nombre escrito en la GUI y un borrado recursivo es
    /// <see cref="NameProblem"/>, que rechaza los separadores de ruta, los nombres que acaban en
    /// punto —<c>..</c> entre ellos—, los reservados y el vacío. Por eso se comprueba aquí antes de
    /// tocar nada, y no solo al escribir el nombre.
    /// </para>
    /// <para>
    /// Que no esté devuelve false en vez de lanzar: entre mirar y borrar cabe que lo haya quitado el
    /// explorador de Windows, y eso no es una avería que contarle a nadie.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">El nombre no sirve como carpeta.</exception>
    public static bool Delete(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (NameProblem(name) is { } bad) throw new NoticeArgumentException(bad);

        string folder = Path.Combine(Root, name);
        if (!Directory.Exists(folder)) return false;

        Directory.Delete(folder, recursive: true);
        return true;
    }

    /// <summary>El nombre del layout de ese fichero, o null si no vive en la carpeta de layouts.</summary>
    public static string? NameOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
            string? name = Path.GetFileName(folder);
            if (string.IsNullOrEmpty(name)) return null;

            string parent = Path.GetDirectoryName(folder) ?? "";
            bool mine = string.Equals(Path.GetFullPath(parent), Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase);
            return mine && Exists(name) ? name : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// Lo que hace que un nombre no sirva, o null si sirve.
    /// </summary>
    /// <remarks>
    /// El nombre acaba siendo una carpeta y un fichero, así que tiene que valer como los dos. Se
    /// comprueba al escribirlo y no al guardar: enterarse de que «CPU: carga» no vale después de
    /// pulsar el botón, con el diálogo ya cerrado, es la peor forma de contarlo.
    /// </remarks>
    public static Notice? NameProblem(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Notice.Of("layout.name.empty");
        if (name != name.Trim()) return Notice.Of("layout.name.spaces");
        if (name.Length > 64) return Notice.Of("layout.name.too-long", 64);
        if (name.EndsWith('.')) return Notice.Of("layout.name.dot");

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return Notice.Of("layout.name.chars");

        // Windows no deja llamar así ni a un fichero ni a una carpeta, con extensión o sin ella, y lo
        // que cuenta es lo que va antes del primer punto: «CON.json» falla igual que «CON». Antes se
        // comparaba el nombre entero, así que ese pasaba de aquí y fallaba después, al crear la carpeta.
        int dot = name.IndexOf('.', StringComparison.Ordinal);
        string bare = dot < 0 ? name : name[..dot];
        if (Array.Exists(Reserved, r => string.Equals(r, bare, StringComparison.OrdinalIgnoreCase)))
            return Notice.Of("layout.name.reserved", name);

        return null;
    }

    private static readonly string[] Reserved =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];
}
