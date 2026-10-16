using VibeScreen.Contracts;
using VibeScreen.Core.Sensors;
using VibeScreen.Protocol.Display;
using VibeScreen.Protocol.Rendering;

namespace VibeScreen.Core.Layout;

/// <summary>
/// Lo que se puede decir de un layout mirando la máquina en la que va a correr: sensores que pide y no
/// están, unidades que no pegan con su sensor y lo que le va a pedir al enlace del panel.
/// </summary>
/// <remarks>
/// Nada de esto es un error de carga. Un tema hecho en otro equipo tiene que poder abrirse para
/// arreglarlo, así que son avisos: el servicio los publica en su estado y el editor los enseña al lado
/// del widget que los provoca.
/// </remarks>
public static class LayoutInspector
{
    /// <summary>Unidad elegida en el widget, o <see cref="SensorUnit.Auto"/> si el widget no muestra valores.</summary>
    public static SensorUnit UnitOf(LayoutWidget widget) => widget switch
    {
        TextWidget text => text.Unit,
        GraphWidget graph => graph.Unit,
        _ => SensorUnit.Auto,
    };

    /// <param name="exists">
    /// Cómo se comprueba que un fichero está. Por omisión, el disco. Se puede dar otra porque estos
    /// avisos se recalculan con cada tecla del editor, y preguntarle al disco por una imagen que
    /// vive en una unidad de red caída congela la ventana letra a letra: quien llama tan a menudo
    /// pasa una versión que recuerda la respuesta.
    /// </param>
    public static IReadOnlyList<Notice> Warnings(
        LayoutDocument layout, ISensorProvider sensors, Func<string, bool>? exists = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(sensors);

        var warnings = new List<Notice>();
        var reported = new HashSet<string>(StringComparer.Ordinal);

        exists ??= File.Exists;

        if (layout.Background is ImageBackground image && !exists(layout.Resolve(image.Path)))
            warnings.Add(Notice.Of("layout.warn.background-missing", image.Path));

        // Un widget que se sale no impide editar, pero sí guardar: Validate lo rechaza. Sin avisarlo
        // aquí, cambiar el tamaño del panel deja el tema imposible de guardar y no se descubre hasta
        // pulsar «Guardar», con todo el trabajo hecho: la vista previa no lo delata porque el
        // renderizador recorta contra el lienzo.
        int outside = layout.Widgets.Count(w => w is not null && !Fits(w.Rect, layout.Display));
        if (outside > 0)
            warnings.Add(outside == 1
                ? Notice.Of("layout.warn.outside.one", outside, layout.Display.Width, layout.Display.Height)
                : Notice.Of("layout.warn.outside.other", outside, layout.Display.Width, layout.Display.Height));

        // El color sale del valor y no del dibujo, así que un corte fuera del recorrido sí cambia el
        // color: lo que pasa es que la barra ya está llena, o vacía, cuando lo hace. Uno por widget y
        // no uno por corte, que el cuadro de avisos del editor mide dos renglones.
        foreach (var graph in layout.Widgets.OfType<GraphWidget>())
        {
            if (graph.Levels is not { } levels) continue;

            if (Outside(levels.MediumFrom, graph) || Outside(levels.HighFrom, graph))
                warnings.Add(Notice.Of("layout.warn.levels-outside", graph.Id, graph.Min, graph.Max));
        }

        foreach (var widget in layout.Widgets)
        {
            if (widget?.SensorId is not { } id) continue;

            var reading = sensors.Read(id);
            if (reading is null)
            {
                if (reported.Add(id))
                    warnings.Add(Notice.Of("layout.warn.missing-sensor", id));
                continue;
            }

            var unit = UnitOf(widget);
            if (unit != SensorUnit.Auto && !SensorUnits.CanConvert(reading.Kind.NativeUnit(), unit))
                warnings.Add(Notice.Of("layout.warn.unit-mismatch", widget.Id, id, unit.Symbol(), reading.Unit));
        }

        return warnings;
    }

    /// <summary>
    /// Lo que cuesta cada widget en el peor caso: su caja entera en cada refresco. Es la cota alta,
    /// porque un widget que no cambia no se envía, y es la que sirve para avisar de que un tema no cabe.
    /// </summary>
    public static IReadOnlyList<WidgetBandwidth> Consumers(LayoutDocument layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        return layout.Widgets
            .Where(w => w is not null)
            .Select(w => new WidgetBandwidth(w.Id, w.Rect, RefreshHz(w)))
            .ToList();
    }

    /// <summary>
    /// Si la caja cabe entera en el panel. Es la misma regla que aplica <see cref="LayoutLoader"/> al
    /// validar, con los extremos inclusivos de <see cref="PixelRect"/>.
    /// </summary>
    private static bool Fits(PixelRect rect, LayoutDisplay display) =>
        !rect.IsEmpty && rect.X >= 0 && rect.Y >= 0 && rect.Right < display.Width && rect.Bottom < display.Height;

    /// <summary>Si un corte de color cae fuera del recorrido que dibuja la gráfica.</summary>
    private static bool Outside(float cut, GraphWidget graph) => cut < graph.Min || cut > graph.Max;

    /// <summary>
    /// Cuántas veces por segundo puede cambiar de aspecto un widget.
    /// </summary>
    /// <remarks>
    /// Normalmente es su refresco, porque un sensor puede dar un valor distinto cada vez que se lee.
    /// El reloj no: un «HH:mm» mirado cada segundo no cambia más de una vez por minuto, y cobrárselo
    /// como sesenta pintaría de rojo un tema que cabe de sobra.
    /// </remarks>
    private static double RefreshHz(LayoutWidget widget)
    {
        // El cargador exige al menos MinRefreshMs, pero esto también se llama sobre documentos en
        // memoria: el medidor del editor se recalcula mientras el usuario teclea el número, y un 0 a
        // medio escribir daba infinito y un negativo restaba consumo del total. Lo más rápido que
        // el formato admite es una cota honrada para algo que todavía se está escribiendo.
        double refresh = 1000.0 / Math.Max(widget.RefreshMs, LayoutLoader.MinRefreshMs);
        return widget is ClockWidget clock
            ? Math.Min(refresh, 1.0 / ClockFormat.Period(clock.Format).TotalSeconds)
            : refresh;
    }
}
