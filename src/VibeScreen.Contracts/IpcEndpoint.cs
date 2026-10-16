namespace VibeScreen.Contracts;

/// <summary>Dónde escucha el servicio.</summary>
public static class IpcEndpoint
{
    /// <summary>
    /// Nombre de la tubería, uno por máquina: el servicio es único. En Windows es
    /// <c>\.\pipe\vibescreen</c>; en Linux, un socket de dominio Unix que crea el propio runtime.
    /// </summary>
    public const string PipeName = "vibescreen";

    /// <summary>Lo que se espera a conectar antes de dar por hecho que el servicio no está en marcha.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
}

/// <summary>No se puede hablar con el servicio: no está instalado, está parado, o no hay permisos.</summary>
public sealed class ServiceUnavailableException(Notice? notice = null, Exception? innerException = null)
    : NoticeException(notice ?? Notice.Of("service.not-running"), innerException);
