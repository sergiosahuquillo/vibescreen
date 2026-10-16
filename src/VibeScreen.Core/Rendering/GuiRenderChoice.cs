namespace VibeScreen.Core.Rendering;

/// <summary>Con qué pinta la GUI su ventana. No tiene nada que ver con lo que se envía al panel.</summary>
public enum RenderMode
{
    /// <summary>Lo que decida el sistema de ventanas, que ya prueba la GPU y se cae a software solo.</summary>
    Auto,

    /// <summary>GPU por Direct3D, a través de ANGLE. Solo en Windows.</summary>
    Direct3D,

    /// <summary>GPU por OpenGL nativo: WGL en Windows, GLX en Linux.</summary>
    OpenGl,

    /// <summary>GPU por OpenGL ES sobre EGL. Solo en Linux: el EGL de Windows es el de ANGLE.</summary>
    Egl,

    /// <summary>GPU por Vulkan. No lo prueba el automático, así que hay que pedirlo.</summary>
    Vulkan,

    /// <summary>Por CPU. Va más lenta, pero no depende del controlador de vídeo.</summary>
    Software,
}

/// <summary>Con qué modo arranca la ventana, y si ese arranque está a prueba.</summary>
/// <param name="Mode">El que se va a usar.</param>
/// <param name="Trying">El que se está probando, o null si el arranque es de los seguros.</param>
public readonly record struct RenderStartup(RenderMode Mode, RenderMode? Trying)
{
    /// <summary>Si hace falta que el usuario diga que se ve bien para quedarse con este modo.</summary>
    public bool OnTrial => Trying is not null;
}

/// <summary>
/// Qué modo de render usa la ventana al arrancar, y cómo se vuelve solo del que la deja inservible.
/// </summary>
/// <remarks>
/// <para>
/// El modo no se puede cambiar en caliente: se fija al construir la aplicación, antes de que haya
/// ninguna ventana. Por eso no vale la cuenta atrás de la resolución de Windows —si la pantalla sale
/// negra no hay dónde ponerla, y si el proceso se cae no hay quien la cuente—, y la vuelta atrás
/// tiene que sobrevivir a un reinicio.
/// </para>
/// <para>
/// La regla es apuntar el intento en disco y <b>borrarlo antes de arriesgarse</b>. Si la ventana llega
/// a verse, el usuario dice que sí y ese modo pasa a ser el bueno. Si no llega —pantalla negra, cuelgue
/// o caída—, el arranque siguiente ya no encuentra el intento y usa el último que se sabe bueno, sin
/// tener que detectar nada.
/// </para>
/// <para>
/// Vive aquí, y no en la GUI, para poder comprobarlo: no sabe nada de Avalonia ni de ventanas.
/// </para>
/// </remarks>
public static class GuiRenderChoice
{
    /// <summary>
    /// Con qué arrancar.
    /// </summary>
    /// <param name="known">El modo que se sabe bueno, el de la última vez que algo funcionó.</param>
    /// <param name="trying">El que el usuario ha pedido probar, si hay alguno.</param>
    /// <param name="forced">La salida de emergencia, que manda sobre todo lo demás y no se prueba.</param>
    public static RenderStartup Start(RenderMode known, RenderMode? trying, RenderMode? forced = null)
    {
        if (forced is { } hatch) return new RenderStartup(hatch, null);

        // Probar lo mismo que ya se usa no es probar nada, y dejaría una confirmación pendiente por
        // un arranque que no tiene ningún riesgo.
        return trying is { } candidate && candidate != known
            ? new RenderStartup(candidate, candidate)
            : new RenderStartup(known, null);
    }

    /// <summary>
    /// El modo que nombra una cadena, o null si no nombra ninguno.
    /// </summary>
    /// <remarks>
    /// Para la variable de entorno, que es lo que queda cuando ni siquiera se puede abrir la ventana
    /// para cambiarlo. Acepta «gpu» como sinónimo de Direct3D y «opengl» sin la mayúscula de en medio,
    /// porque nadie va a escribir «OpenGl» a mano en una consola.
    /// </remarks>
    public static RenderMode? Parse(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "auto" => RenderMode.Auto,
        "direct3d" or "d3d" or "angle" or "gpu" => RenderMode.Direct3D,
        "opengl" or "gl" or "wgl" or "glx" => RenderMode.OpenGl,
        "egl" or "gles" => RenderMode.Egl,
        "vulkan" or "vk" => RenderMode.Vulkan,
        "software" or "cpu" => RenderMode.Software,
        _ => null,
    };
}
