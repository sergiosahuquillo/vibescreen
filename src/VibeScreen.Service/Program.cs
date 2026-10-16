using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VibeScreen.Contracts;
using VibeScreen.Core.Layout;
using VibeScreen.Core.Sensors;
using VibeScreen.Service;

var builder = Host.CreateApplicationBuilder(args);

#if WINDOWS
// Con esto el proceso sabe hablar con el SCM. Lanzado a mano desde una consola no cambia nada, así
// que el mismo binario sirve para depurar y para el servicio de verdad.
builder.Services.AddWindowsService(options => options.ServiceName = "VibeScreen");
#else
builder.Services.AddSystemd();
#endif

// El tema de serie, si el instalador lo ha dejado: es con el que arranca el panel la primera vez, en
// lugar de quedarse en negro hasta que alguien abra la ventana.
builder.Services.AddSingleton<IConfigStore>(sp =>
    new FileConfigStore(ServicePaths.ConfigFile, sp.GetRequiredService<ILogger<FileConfigStore>>(),
        LayoutPaths.Exists(LayoutPaths.Bundled) ? LayoutPaths.For(LayoutPaths.Bundled) : null));
builder.Services.AddSingleton<IPanelConnector>(_ => new SerialPanelConnector());
builder.Services.AddSingleton(sp => CreateSensors(sp.GetRequiredService<ILogger<PanelHost>>()));
builder.Services.AddSingleton<PanelHost>();
builder.Services.AddSingleton<IpcServer>();
builder.Services.AddHostedService<PanelWorker>();

await builder.Build().RunAsync();

static ISensorProvider CreateSensors(ILogger logger)
{
    try
    {
        return SensorProviders.CreateDefault();
    }
    catch (PlatformNotSupportedException e)
    {
        // Sin sensores el servicio sigue siendo útil: mantiene el panel y contesta al IPC.
        logger.LogError("Sin sensores: {Mensaje}", e.Message);
        return new EmptySensorProvider(Notice.From(e));
    }
}
