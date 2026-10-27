using System.Diagnostics;
using VibeScreen.Contracts;
using VibeScreen.Core.Layout;
using VibeScreen.Core.Sensors;
using VibeScreen.Protocol.Display;
using VibeScreen.Protocol.Rendering;

namespace VibeScreen.Core.Rendering;

/// <summary>
/// Lleva un layout al panel: un fotograma completo al empezar y, después, solo los widgets que
/// cambian, repartidos por <see cref="ByteBudgetScheduler"/>.
/// </summary>
public sealed class LayoutRunner : IDisposable
{
    /// <summary>Cada cuánto se mira si hay algo que refrescar o enviar.</summary>
    public static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(50);

    private readonly IDisplayDriver _driver;
    private readonly ISensorProvider _sensors;
    private readonly ByteBudgetScheduler _scheduler;
    private readonly PixelFormat _format;

    /// <summary>
    /// De dónde salen los píxeles de cada envío.
    /// </summary>
    /// <remarks>
    /// Uno solo, del tamaño del fotograma completo, y no uno nuevo por rectángulo: un reloj con
    /// segundos y una gráfica pequeña reservaban veintitantos kilobytes por segundo, todos basura
    /// inmediata, y el fotograma completo del arranque iba derecho al montón de objetos grandes.
    /// </remarks>
    private readonly byte[] _pixels;

    private TimeSpan _lastTick;

    /// <param name="language">En qué idioma escriben los relojes los nombres de los días y los meses.</param>
    public LayoutRunner(IDisplayDriver driver, LayoutDocument layout, ISensorProvider sensors,
                        double utilization = ByteBudgetScheduler.DefaultUtilization, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(sensors);

        var display = layout.Display;
        if (display.Width != driver.Width || display.Height != driver.Height || display.Orientation != driver.Orientation)
            throw new NoticeArgumentException(Notice.Of("layout.panel-mismatch",
                display.Width, display.Height, display.Orientation, driver.Width, driver.Height, driver.Orientation));

        _driver = driver;
        _sensors = sensors;
        Renderer = new LayoutRenderer(layout, language: language);
        // El formato, del panel y no supuesto: es lo que dice cuántos bytes cuesta cada envío y en qué
        // orden van, y un panel que lo pida distinto no tiene que notarse aquí.
        _format = driver.Capabilities.PixelFormat;
        _scheduler = new ByteBudgetScheduler(driver.Capabilities.ThroughputBytesPerSecond, utilization, format: _format);
        _pixels = new byte[_format.ByteCount(Renderer.Surface.Bounds)];
    }

    public LayoutRenderer Renderer { get; }

    public long BlitCount { get; private set; }

    public long BytesSent { get; private set; }

    /// <summary>
    /// Pinta todo y lo envía de una vez, fuera del presupuesto: sin este fotograma el panel seguiría
    /// mostrando lo que dejara el programa anterior.
    /// </summary>
    public async ValueTask StartAsync(TimeSpan now, CancellationToken cancellationToken = default)
    {
        Renderer.RenderAll(_sensors, now);
        _scheduler.Clear();
        await SendAsync(Renderer.Surface.Bounds, cancellationToken).ConfigureAwait(false);
        _lastTick = now;
    }

    /// <summary>Un ciclo: refresca lo que toca y envía lo que quepa en el presupuesto acumulado.</summary>
    public async ValueTask<IReadOnlyList<BlitRequest>> TickAsync(TimeSpan now, CancellationToken cancellationToken = default)
    {
        foreach (var dirty in Renderer.Update(_sensors, now))
            _scheduler.MarkDirty(dirty.WidgetId, dirty.Rect, dirty.Priority);

        var blits = _scheduler.Drain(now - _lastTick);
        _lastTick = now;

        // Los píxeles se extraen al enviar y no al marcar: si un widget cambió dos veces mientras
        // esperaba turno, sale directamente su último aspecto.
        foreach (var blit in blits)
            await SendAsync(blit.Rect, cancellationToken).ConfigureAwait(false);

        return blits;
    }

    /// <summary>Arranca y sigue hasta que se cancele.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        await StartAsync(clock.Elapsed, cancellationToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(Tick);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            await TickAsync(clock.Elapsed, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask SendAsync(PixelRect rect, CancellationToken cancellationToken)
    {
        int bytes = _format.ByteCount(rect);
        var pixels = _pixels.AsMemory(0, bytes);
        Renderer.Surface.Extract(rect, _format, pixels.Span);

        await _driver.BlitAsync(pixels, rect, cancellationToken).ConfigureAwait(false);
        BlitCount++;
        BytesSent += bytes;
    }

    public void Dispose() => Renderer.Dispose();
}
