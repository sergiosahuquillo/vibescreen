using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using VibeScreen.App.Localization;
using VibeScreen.Core.Rendering;

namespace VibeScreen.App;

/// <summary>Traduce el modo de render elegido a lo que entiende cada plataforma de Avalonia.</summary>
public static class AppRender
{
    /// <summary>La variable de entorno que manda sobre el ajuste. Es la salida cuando la ventana no abre.</summary>
    public const string Variable = "VIBESCREEN_RENDER";

    /// <summary>
    /// Los modos que se pueden elegir en esta máquina.
    /// </summary>
    /// <remarks>
    /// Direct3D es de Windows —ANGLE traduce OpenGL ES a Direct3D— y EGL, de Linux; en Windows no se
    /// ofrece porque el EGL que hay es el de ANGLE, que ya está como Direct3D.
    /// </remarks>
    public static IReadOnlyList<RenderMode> Available => OperatingSystem.IsWindows()
        ? [RenderMode.Auto, RenderMode.Direct3D, RenderMode.OpenGl, RenderMode.Vulkan, RenderMode.Software]
        : [RenderMode.Auto, RenderMode.OpenGl, RenderMode.Egl, RenderMode.Vulkan, RenderMode.Software];

    /// <summary>Lo que dice la variable de entorno, o null si no dice nada que se entienda.</summary>
    public static RenderMode? Forced() => GuiRenderChoice.Parse(Environment.GetEnvironmentVariable(Variable));

    public static Text Label(RenderMode mode) => mode switch
    {
        RenderMode.Direct3D => Loc.L("render.mode.direct3d"),
        RenderMode.OpenGl => OperatingSystem.IsWindows() ? Loc.L("render.mode.wgl") : Loc.L("render.mode.glx"),
        RenderMode.Egl => Loc.L("render.mode.egl"),
        RenderMode.Vulkan => Loc.L("render.mode.vulkan"),
        RenderMode.Software => Loc.L("render.mode.software"),
        _ => Loc.L("render.mode.auto"),
    };

    /// <summary>El nombre a secas, para cuando ya se sabe de qué se está hablando.</summary>
    public static Text Short(RenderMode mode) => mode switch
    {
        RenderMode.Direct3D => Text.Raw("Direct3D"),
        RenderMode.OpenGl => Text.Raw(OperatingSystem.IsWindows() ? "WGL" : "GLX"),
        RenderMode.Egl => Text.Raw("EGL"),
        RenderMode.Vulkan => Text.Raw("Vulkan"),
        RenderMode.Software => Loc.L("render.short.software"),
        _ => Loc.L("render.short.auto"),
    };

    /// <summary>Qué hace el automático en esta plataforma, que es una lista y no una elección.</summary>
    public static Text Chain => OperatingSystem.IsWindows()
        ? Loc.L("render.chain.windows")
        : Loc.L("render.chain.linux");

    /// <summary>
    /// Con qué se está pintando de verdad, o null si no se ha podido averiguar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// En automático el modo elegido no lo dice: Avalonia recorre su lista —en Windows ANGLE y luego
    /// software, en Linux GLX y luego software— y se queda con el primero que consiga inicializar.
    /// </para>
    /// <para>
    /// Se mira qué bibliotecas nativas ha acabado cargando el proceso, porque Avalonia 12 no lo cuenta
    /// por ninguna vía pública: <c>AvaloniaLocator</c> ya no existe e <c>IPlatformGraphics</c> no es
    /// público. Las tres son inconfundibles: <c>av_libglesv2</c> es el ANGLE que trae Avalonia y no lo
    /// carga nadie más, <c>vulkan-1</c> es el cargador de Vulkan y <c>opengl32</c> solo aparece con
    /// WGL. Si no está ninguna es que no hay GPU de por medio y se está pintando por CPU.
    /// </para>
    /// <para>
    /// Es diagnóstico: si no se puede mirar la lista de módulos se contesta null y se enseña la cadena
    /// del automático, que siempre es cierta, en lugar de arriesgar una respuesta inventada.
    /// </para>
    /// </remarks>
    public static RenderMode? Effective()
    {
        List<string> loaded;
        try
        {
            loaded = [.. Process.GetCurrentProcess().Modules
                .OfType<ProcessModule>()
                .Select(m => m.ModuleName ?? "")];
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            return null;
        }

        if (loaded.Count == 0) return null;
        bool Loaded(string library) => loaded.Exists(m => m.StartsWith(library, StringComparison.OrdinalIgnoreCase));

        if (Loaded("vulkan-1")) return RenderMode.Vulkan;

        // ANGLE antes que nada: por dentro es EGL, y mirado al revés se confundiría con el EGL de Linux.
        if (Loaded("av_libglesv2") || Loaded("libGLESv2"))
            return OperatingSystem.IsWindows() ? RenderMode.Direct3D : RenderMode.Egl;

        if (Loaded("opengl32") || Loaded("libGLX") || Loaded("libGL.so")) return RenderMode.OpenGl;
        return RenderMode.Software;
    }

    /// <summary>
    /// Le pone el modo al constructor de la aplicación.
    /// </summary>
    /// <remarks>
    /// En automático no se toca nada: la lista que trae Avalonia ya prueba la GPU y se cae a software
    /// por su cuenta, y escribirla a mano sería quedarse con la de hoy para siempre. Un modo que esta
    /// plataforma no tiene —Direct3D fuera de Windows, EGL dentro— se queda también en automático, que
    /// es lo único que no puede fallar.
    /// </remarks>
    public static AppBuilder With(AppBuilder builder, RenderMode mode)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (mode == RenderMode.Auto) return builder;

        if (OperatingSystem.IsWindows())
        {
            Win32RenderingMode[]? windows = mode switch
            {
                RenderMode.Direct3D => [Win32RenderingMode.AngleEgl],
                RenderMode.OpenGl => [Win32RenderingMode.Wgl],
                RenderMode.Vulkan => [Win32RenderingMode.Vulkan],
                RenderMode.Software => [Win32RenderingMode.Software],
                _ => null,
            };

            return windows is null ? builder : builder.With(new Win32PlatformOptions { RenderingMode = windows });
        }

        if (!OperatingSystem.IsLinux()) return builder;

        X11RenderingMode[]? linux = mode switch
        {
            RenderMode.OpenGl => [X11RenderingMode.Glx],
            RenderMode.Egl => [X11RenderingMode.Egl],
            RenderMode.Vulkan => [X11RenderingMode.Vulkan],
            RenderMode.Software => [X11RenderingMode.Software],
            _ => null,
        };

        return linux is null ? builder : builder.With(new X11PlatformOptions { RenderingMode = linux });
    }
}
