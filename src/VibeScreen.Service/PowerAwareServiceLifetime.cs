#if WINDOWS
using System.ServiceProcess;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VibeScreen.Service;

/// <summary>
/// El ciclo de vida de servicio de Windows de siempre, que además se entera de cuándo vuelve el sistema
/// de suspensión o de hibernación y se lo dice al panel.
/// </summary>
/// <remarks>
/// <para>
/// Hace falta porque un servicio no vuelve a arrancar en ninguno de esos casos, y tampoco al encender
/// después de «Apagar» si el inicio rápido de Windows está puesto, que viene así de serie: ese
/// «Apagar» hiberna los servicios con el núcleo y los despierta tal cual al encender. El panel, en
/// cambio, sí se queda sin corriente. Ver <see cref="PanelHost.PanelMayHaveReset"/>.
/// </para>
/// <para>
/// Solo se usa cuando el proceso corre como servicio. Lanzado a mano desde una consola no hay gestor
/// de servicios que mande el aviso, y se queda el ciclo de vida de consola de siempre.
/// </para>
/// </remarks>
public sealed class PowerAwareServiceLifetime : WindowsServiceLifetime
{
    private readonly PanelHost _panel;

    public PowerAwareServiceLifetime(
        IHostEnvironment environment,
        IHostApplicationLifetime applicationLifetime,
        ILoggerFactory loggerFactory,
        IOptions<HostOptions> optionsAccessor,
        IOptions<WindowsServiceLifetimeOptions> windowsServiceOptionsAccessor,
        PanelHost panel)
        : base(environment, applicationLifetime, loggerFactory, optionsAccessor, windowsServiceOptionsAccessor)
    {
        ArgumentNullException.ThrowIfNull(panel);
        _panel = panel;

        // Aquí y no más tarde: el gestor de servicios solo pregunta qué avisos se aceptan al arrancar.
        CanHandlePowerEvent = true;
    }

    protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
    {
        // Pueden llegar varios por la misma vuelta: el automático siempre, y el de suspensión si
        // además hay alguien delante. Da igual, porque el aviso es una marca.
        if (powerStatus is PowerBroadcastStatus.ResumeAutomatic
            or PowerBroadcastStatus.ResumeSuspend
            or PowerBroadcastStatus.ResumeCritical)
        {
            _panel.PanelMayHaveReset();
        }

        return base.OnPowerEvent(powerStatus);
    }
}
#endif
