using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VibeScreen.Service;

/// <summary>Los dos bucles del servicio: el que mantiene el panel y el que atiende la tubería.</summary>
/// <remarks>
/// <para>
/// Los dos solo terminan cuando se para el servicio, así que <b>que uno vuelva ya es la avería</b>.
/// Esperarlos con <c>Task.WhenAll</c> sería lo natural de leer y lo peor de ejecutar: <c>WhenAll</c>
/// no completa hasta que completan los dos, de modo que si uno falla, el otro sigue vivo, la espera
/// nunca termina y <see cref="BackgroundService"/> jamás llega a observar el fallo. El servicio se
/// queda en pie, el gestor de servicios lo da por bueno, y el panel enseña una imagen congelada
/// mientras la tubería contesta un estado que ya no es verdad.
/// </para>
/// <para>
/// Con <c>WhenAny</c> el primero que vuelve manda: se le pide al otro que pare y se propaga lo que
/// haya fallado, que es lo que hace que el servicio termine y que quien lo supervisa pueda
/// reiniciarlo.
/// </para>
/// </remarks>
public sealed class PanelWorker(PanelHost panel, IpcServer ipc, ILogger<PanelWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("VibeScreen en marcha. Configuración en {Ruta}.", ServicePaths.ConfigFile);

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var loops = new[] { panel.RunAsync(stopping.Token), ipc.RunAsync(stopping.Token) };

        await Task.WhenAny(loops).ConfigureAwait(false);

        if (!stoppingToken.IsCancellationRequested)
            logger.LogError("Uno de los dos bucles del servicio ha terminado por su cuenta. Se para el otro.");

        await stopping.CancelAsync().ConfigureAwait(false);

        try
        {
            // Los dos, para no dejar tareas sueltas, y propagando lo que haya fallado.
            await Task.WhenAll(loops).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // El que seguía vivo ha parado porque se lo hemos pedido.
        }
    }
}
