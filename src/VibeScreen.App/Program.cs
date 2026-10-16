using Avalonia;
using VibeScreen.App.Localization;
using VibeScreen.Core.Rendering;

namespace VibeScreen.App;

internal static class Program
{
    /// <summary>Con qué se ha arrancado esta vez, y si está a prueba. Lo lee la pestaña de ajustes.</summary>
    public static RenderStartup Render { get; private set; }

    /// <summary>
    /// Si la ventana ha llegado a abrirse alguna vez en este arranque. Lo que falle después de eso no
    /// es culpa del modo de render, y no tiene por qué cambiarlo.
    /// </summary>
    public static bool WindowShown { get; set; }

    /// <summary>El sitio de esta ventana como la única del usuario. Lo escucha la aplicación para ponerse delante.</summary>
    public static SingleInstance? Instance { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        // Lo primero de todo, antes de leer los ajustes: una segunda ventana que llegara a leerlos
        // borraría el modo de render que la primera tiene a prueba, más abajo.
        using var instance = SingleInstance.Acquire(restarting: args.Contains(SingleInstance.RestartArgument));
        if (instance is null)
        {
            SingleInstance.WakeExisting();
            return 0;
        }

        Instance = instance;

        // Antes que nada: lo que se lea o se formatee a partir de aquí ya va con la cultura del proceso
        // invariante, y el idioma del sistema queda apuntado para «Idioma: el del sistema».
        Loc.CaptureSystem();

        var settings = AppSettingsStore.Load();
        Loc.Use(settings.Language);
        Render = GuiRenderChoice.Start(settings.Render, settings.RenderTrying, AppRender.Forced());

        // El intento se borra ANTES de arriesgarse a usarlo. Si la ventana no llega a verse, el
        // arranque siguiente ya no lo encuentra y vuelve al modo que se sabe bueno: no hay que detectar
        // la avería ni esperar a ningún temporizador, basta con que este proceso no vuelva a pasar por
        // aquí. Confirmarlo es cosa de la ventana, cuando el usuario diga que se ve.
        if (settings.RenderTrying is not null)
        {
            settings.RenderTrying = null;
            AppSettingsStore.Save(settings);
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e) when (!WindowShown && Render.Mode != RenderMode.Software && AppRender.Forced() is null)
        {
            // No ha llegado ni a abrir. Sea lo que sea, con software no depende del controlador de
            // vídeo, así que la próxima vez arranca con eso y el usuario puede volver a elegir.
            settings.Render = RenderMode.Software;
            settings.RenderTrying = null;
            AppSettingsStore.Save(settings);

            Console.Error.WriteLine(Loc.S("startup.failed", AppRender.Label(Render.Mode), e.Message));
            Console.Error.WriteLine(Loc.S("startup.next-software"));
            return 1;
        }
    }

    /// <summary>Lo usa también el diseñador de Avalonia, así que tiene que ser público y sin argumentos.</summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<VibeScreenApp>()
            .UsePlatformDetect()
            .LogToTrace();

        // Hay máquinas donde el compositor de Windows parpadea con según qué controlador de vídeo, así
        // que con qué se pinta la ventana se elige en Ajustes. VIBESCREEN_RENDER manda sobre el ajuste
        // y es lo que queda cuando la ventana ni siquiera abre para poder cambiarlo.
        return AppRender.With(builder, Render.Mode);
    }
}
