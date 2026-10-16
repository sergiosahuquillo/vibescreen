using System.Text.Json;
using Microsoft.Extensions.Logging;
using VibeScreen.Core.Storage;

namespace VibeScreen.Service;

public interface IConfigStore
{
    ServiceConfig Load();

    void Save(ServiceConfig config);
}

/// <summary>Dónde guarda sus cosas el servicio.</summary>
public static class ServicePaths
{
    /// <summary>La carpeta compartida de la máquina. La define <see cref="AppPaths"/>, que es de todos.</summary>
    public static string ConfigDirectory => AppPaths.Shared;

    public static string ConfigFile => Path.Combine(ConfigDirectory, "config.json");
}

/// <param name="path">El fichero de configuración.</param>
/// <param name="logger">Donde contar que no se ha podido leer o guardar.</param>
/// <param name="firstRunLayout">
/// El tema con el que arrancar cuando todavía no hay configuración, que es lo que pasa la primera vez
/// después de instalar. Solo entonces: con el fichero ya escrito manda lo que diga, aunque sea «ningún
/// tema», y uno ilegible tampoco lo usa, porque eso no es un primer arranque sino una avería.
/// </param>
public sealed class FileConfigStore(string path, ILogger logger, string? firstRunLayout = null) : IConfigStore
{
    public string Path { get; } = path;

    public ServiceConfig Load()
    {
        try
        {
            if (!File.Exists(Path)) return new ServiceConfig { LayoutPath = firstRunLayout };

            return JsonSerializer.Deserialize(File.ReadAllText(Path), ServiceConfigJsonContext.Default.ServiceConfig)
                   ?? new ServiceConfig();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Una configuración ilegible no puede dejar el panel a oscuras: se avisa y se sigue con
            // los valores por defecto, que el usuario puede corregir desde la GUI.
            logger.LogError("No se ha podido leer {Ruta}: {Mensaje}. Se usan los valores por defecto.", Path, e.Message);
            return new ServiceConfig();
        }
    }

    public void Save(ServiceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        try
        {
            // Entero o nada: un corte a mitad de escritura dejaría al servicio sin la ruta del tema,
            // sin el brillo y sin el puerto, y sin forma de saber qué había.
            AtomicFile.WriteAllText(Path, JsonSerializer.Serialize(config, ServiceConfigJsonContext.Default.ServiceConfig));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogError("No se ha podido guardar {Ruta}: {Mensaje}.", Path, e.Message);
        }
    }
}
