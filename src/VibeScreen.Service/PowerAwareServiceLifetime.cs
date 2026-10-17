#if WINDOWS
using System.ServiceProcess;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VibeScreen.Service;

/// <summary>
/// El ciclo de vida de servicio de Windows de siempre, que además se entera de cuándo se suspende el
/// sistema y de cuándo vuelve, y se lo dice al panel.
/// </summary>
/// <remarks>
/// <para>
/// Hace falta porque un servicio no vuelve a arrancar en ninguno de esos casos, y tampoco al encender
/// después de «Apagar» si el inicio rápido de Windows está puesto, que viene así de serie: ese
/// «Apagar» hiberna los servicios con el núcleo y los despierta tal cual al encender. El panel, en
/// cambio, sí se queda sin corriente. Ver <see cref="PanelHost.PanelMayHaveReset"/> y
/// <see cref="PanelHost.SuspendAsync"/>.
/// </para>
/// <para>
/// Solo se usa cuando el proceso corre como servicio. Lanzado a mano desde una consola no hay gestor
/// de servicios que mande el aviso, y se queda el ciclo de vida de consola de siempre.
/// </para>
/// </remarks>
public sealed class PowerAwareServiceLifetime : WindowsServiceLifetime
{
    /// <summary>Lo que se le da al panel para apagarse antes de que el sistema se suspenda.</summary>
    /// <remarks>
    /// Windows deja unos dos segundos para atender la suspensión. Un ciclo de pintado normal son unos
    /// pocos kilobytes, y el peor, un fotograma entero, está algo por encima del segundo.
    /// </remarks>
    private static readonly TimeSpan SuspendBudget = TimeSpan.FromSeconds(2);

    private readonly PanelHost _panel;
    private readonly ILogger _logger;

    public PowerAwareServiceLifetime(
        IHostEnvironment environment,
        IHostApplicationLifetime applicationLifetime,
        ILoggerFactory loggerFactory,
        IOptions<HostOptions> optionsAccessor,
        IOptions<WindowsServiceLifetimeOptions> windowsServiceOptionsAccessor,
        PanelHost panel)
        : base(environment, applicationLifetime, loggerFactory, optionsAccessor, windowsServiceOptionsAccessor)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(panel);
        _panel = panel;
        _logger = loggerFactory.CreateLogger<PowerAwareServiceLifetime>();

        // Aquí y no más tarde: el gestor de servicios solo pregunta qué avisos se aceptan al arrancar.
        CanHandlePowerEvent = true;
    }

    protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
    {
        switch (powerStatus)
        {
            case PowerBroadcastStatus.Suspend:
                Suspend();
                break;

            // Pueden llegar varios por la misma vuelta: el automático siempre, y el de suspensión si
            // además hay alguien delante. Da igual, porque el aviso es una marca.
            case PowerBroadcastStatus.ResumeAutomatic:
            case PowerBroadcastStatus.ResumeSuspend:
            case PowerBroadcastStatus.ResumeCritical:
                _panel.PanelMayHaveReset();
                break;
        }

        return base.OnPowerEvent(powerStatus);
    }

    /// <summary>Apaga el panel antes de volver, que es cuando el sistema sigue adelante con la suspensión.</summary>
    /// <remarks>
    /// Se espera aquí mismo, en el hilo del gestor de servicios, porque volver es dar permiso para
    /// suspender. Con plazo, eso sí, y sin dejar escapar nada: una excepción en este hilo tumbaría
    /// el proceso entero, y que el panel no se apague no es motivo para tanto.
    /// </remarks>
    private void Suspend()
    {
        using var budget = new CancellationTokenSource(SuspendBudget);
        try
        {
            _panel.SuspendAsync(budget.Token).GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            _logger.LogWarning("No se ha podido apagar el panel antes de suspender: {Mensaje}", e.Message);
        }
    }
}
#endif
