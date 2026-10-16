namespace VibeScreen.Core.Storage;

/// <summary>Dónde guarda VibeScreen lo suyo en esta máquina.</summary>
public static class AppPaths
{
    /// <summary>
    /// La carpeta de la máquina, no la de un usuario.
    /// </summary>
    /// <remarks>
    /// En Windows el servicio corre como SYSTEM —lo necesita para el ring0 de los sensores— así que
    /// lo que tenga que leer no puede vivir en el perfil de nadie: va a <c>%ProgramData%</c>. En
    /// Linux es una unidad de usuario y va a <c>~/.config</c>.
    /// </remarks>
    public static string Shared => Path.Combine(
        Environment.GetFolderPath(OperatingSystem.IsWindows()
            ? Environment.SpecialFolder.CommonApplicationData
            : Environment.SpecialFolder.ApplicationData),
        "VibeScreen");
}
