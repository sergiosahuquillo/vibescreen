using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

// Con System.IO en los usings implicitos, Path a secas es ambiguo.
using Path = Avalonia.Controls.Shapes.Path;
namespace VibeScreen.App.Theme;

/// <summary>Los iconos de la ventana, portados del diseno.</summary>
/// <remarks>
/// <para>
/// Son de trazo y no de relleno: cada uno se dibuja sobre un lienzo de 24x24 con una linea de 1,5
/// de grosor, remates redondos y sin relleno. Eso es lo que hace que el mismo dibujo valga a 13 px
/// dentro de una pastilla y a 22 en la barra de titulo sin tener que rehacerlo.
/// </para>
/// <para>
/// Cada icono es <b>una sola</b> cadena de path aunque el original tuviera varios trazos, un
/// rectangulo y un circulo sueltos: Avalonia no entiende <c>rect</c> ni <c>circle</c>, y como todos
/// los trozos comparten pincel y grosor, fundirlos no cambia el dibujo y ahorra un control por
/// trozo. Al fundirlos hay un detalle que no se ve venir: en SVG el primer <c>moveto</c> de un path
/// es absoluto aunque se escriba en minuscula, pero sus pares siguientes son <c>lineto</c>
/// relativos; pegado detras de otro trazo, ese <c>m</c> pasaria a contarse desde donde acabo el
/// anterior. Por eso los que empezaban asi van partidos en <c>M x y</c> y un <c>l</c> explicito.
/// </para>
/// <para>
/// Las geometrias son estaticas y se comparten: un icono puede salir cien veces en la ventana
/// —cada fila del explorador, cada boton— y analizar la cadena en cada una seria trabajo tirado.
/// Una <see cref="StreamGeometry"/> es inmutable en cuanto se analiza, asi que compartirla no tiene
/// pega.
/// </para>
/// </remarks>
public static class Icons
{
    // 29 iconos
    public static StreamGeometry Alert { get; } = Parse(
        "M12 4.5 2.9 20h18.2L12 4.5Z M12 10v4.25M12 17.2v.05");

    public static StreamGeometry Bell { get; } = Parse(
        "M6.75 10a5.25 5.25 0 0 1 10.5 0c0 3.3.8 5 1.75 6H5c.95-1 1.75-2.7 1.75-6Z M10.25 19.25a2 2 0 0 0 3.5 0");

    public static StreamGeometry Brandmark { get; } = Parse(BrandmarkData);

    /// <summary>La marca en crudo, porque también la dibuja Skia: es el icono de la barra de tareas.</summary>
    public const string BrandmarkData =
        "M5 5H19A2.5 2.5 0 0 1 21.5 7.5V16.5A2.5 2.5 0 0 1 19 19H5A2.5 2.5 0 0 1 2.5 16.5V7.5A2.5 2.5 0 0 1 5 5Z M6.5 15.2 9 10.4l2.6 4.8 2.4-7 2.5 7";

    public static StreamGeometry Check { get; } = Parse(
        "M4.75 12.5 l5 5 9.5-11");

    public static StreamGeometry Chevron { get; } = Parse(
        "M6 9.5 l6 6 6-6");

    public static StreamGeometry Copy { get; } = Parse(
        "M10.75 8.75H18.25A2 2 0 0 1 20.25 10.75V18.25A2 2 0 0 1 18.25 20.25H10.75A2 2 0 0 1 8.75 18.25V10.75A2 2 0 0 1 10.75 8.75Z M15.25 5.75a2 2 0 0 0-2-2h-7.5a2 2 0 0 0-2 2v7.5a2 2 0 0 0 2 2");

    public static StreamGeometry Editor { get; } = Parse(
        "M5 4.75H19A2.25 2.25 0 0 1 21.25 7V17A2.25 2.25 0 0 1 19 19.25H5A2.25 2.25 0 0 1 2.75 17V7A2.25 2.25 0 0 1 5 4.75Z M9.25 4.75v14.5M2.75 12h6.5 M13 8.5h5M13 12h3");

    public static StreamGeometry Grid { get; } = Parse(
        "M5.25 3.75H18.75A1.5 1.5 0 0 1 20.25 5.25V18.75A1.5 1.5 0 0 1 18.75 20.25H5.25A1.5 1.5 0 0 1 3.75 18.75V5.25A1.5 1.5 0 0 1 5.25 3.75Z M9.25 3.75v16.5M14.75 3.75v16.5M3.75 9.25h16.5M3.75 14.75h16.5");

    public static StreamGeometry Image { get; } = Parse(
        "M5.75 4.75H18.25A2 2 0 0 1 20.25 6.75V17.25A2 2 0 0 1 18.25 19.25H5.75A2 2 0 0 1 3.75 17.25V6.75A2 2 0 0 1 5.75 4.75Z M7.4 10A1.6 1.6 0 1 0 10.6 10A1.6 1.6 0 1 0 7.4 10Z M4.5 17.5 l4.6-4.6 3.4 3.4 2.6-2.4 4.4 4");

    public static StreamGeometry Import { get; } = Parse(
        "M12 3.75v10.5 M8.25 10.75 l3.75 3.5 3.75-3.5 M4.75 16.5v1.75a2 2 0 0 0 2 2h10.5a2 2 0 0 0 2-2V16.5");

    public static StreamGeometry Link { get; } = Parse(
        "M4.75 12a3.25 3.25 0 0 1 3.25-3.25h2.5M19.25 12a3.25 3.25 0 0 0-3.25-3.25h-2.5 M4.75 12a3.25 3.25 0 0 0 3.25 3.25h2.5M19.25 12a3.25 3.25 0 0 1-3.25 3.25h-2.5 M9 12h6");

    public static StreamGeometry Max { get; } = Parse(
        "M6.75 5.25H17.25A1.5 1.5 0 0 1 18.75 6.75V17.25A1.5 1.5 0 0 1 17.25 18.75H6.75A1.5 1.5 0 0 1 5.25 17.25V6.75A1.5 1.5 0 0 1 6.75 5.25Z");

    public static StreamGeometry Min { get; } = Parse(
        "M5 12h14");

    public static StreamGeometry Monitor { get; } = Parse(
        "M5 4.75H19A2 2 0 0 1 21 6.75V14.75A2 2 0 0 1 19 16.75H5A2 2 0 0 1 3 14.75V6.75A2 2 0 0 1 5 4.75Z M9 20.25h6M12 16.75v3.5");

    public static StreamGeometry Moon { get; } = Parse(
        "M20 14.1A8.25 8.25 0 0 1 9.9 4a8.25 8.25 0 1 0 10.1 10.1Z");

    public static StreamGeometry Panel { get; } = Parse(
        "M5 4.75H19A2.25 2.25 0 0 1 21.25 7V17A2.25 2.25 0 0 1 19 19.25H5A2.25 2.25 0 0 1 2.75 17V7A2.25 2.25 0 0 1 5 4.75Z M6.5 15.5a5.5 5.5 0 0 1 11 0 M12 15.5 15 11");

    public static StreamGeometry Plus { get; } = Parse(
        "M12 5v14M5 12h14");

    public static StreamGeometry Power { get; } = Parse(
        "M12 3.5v7.5 M17.3 6.4a7.25 7.25 0 1 1-10.6 0");

    public static StreamGeometry Refresh { get; } = Parse(
        "M20 12a8 8 0 1 1-2.4-5.7 M20.25 4v3.6h-3.6");

    public static StreamGeometry Restore { get; } = Parse(
        "M12 3.75a8.25 8.25 0 1 0 8.25 8.25 M20.25 3.75v4.9h-4.9");

    public static StreamGeometry Save { get; } = Parse(
        "M4.75 6.75a2 2 0 0 1 2-2h8.6l3.9 3.9v8.6a2 2 0 0 1-2 2H6.75a2 2 0 0 1-2-2Z M8.5 4.75v4.5h6v-4.5M8.5 19.25v-4.75h7v4.75");

    public static StreamGeometry Search { get; } = Parse(
        "M4.75 10.75A6 6 0 1 0 16.75 10.75A6 6 0 1 0 4.75 10.75Z M15.2 15.2 l5 5");

    public static StreamGeometry Send { get; } = Parse(
        "M4 12.5 20 4l-4.2 16-4-6.6L4 12.5Z M11.8 13.4 l4.2-6.6");

    public static StreamGeometry Sensors { get; } = Parse(
        "M2.75 12.5h3.6L9 6.5l3.1 11 2.3-5h4.85");

    public static StreamGeometry Settings { get; } = Parse(
        "M4 7.5h10M18 7.5h2M4 16.5h4M12 16.5h8 M13.75 7.5A2.25 2.25 0 1 0 18.25 7.5A2.25 2.25 0 1 0 13.75 7.5Z M7.75 16.5A2.25 2.25 0 1 0 12.25 16.5A2.25 2.25 0 1 0 7.75 16.5Z");

    public static StreamGeometry Shuffle { get; } = Parse(
        "M3.25 6.75h3.4l3 4.6M3.25 17.25h3.4L16 6.75h4.75M20.75 6.75l-2.4-2.3M20.75 6.75l-2.4 2.3 M13.4 13.9l2.6 3.35h4.75M20.75 17.25l-2.4-2.3M20.75 17.25l-2.4 2.3");

    public static StreamGeometry Sun { get; } = Parse(
        "M8 12A4 4 0 1 0 16 12A4 4 0 1 0 8 12Z M12 2.75v2.1M12 19.15v2.1M21.25 12h-2.1M4.85 12h-2.1M18.55 5.45l-1.5 1.5M6.95 17.05l-1.5 1.5M18.55 18.55l-1.5-1.5M6.95 6.95l-1.5-1.5");

    public static StreamGeometry Trash { get; } = Parse(
        "M4.75 6.75h14.5M9.5 6.75V4.9h5v1.85M6.75 6.75l.8 12a1.6 1.6 0 0 0 1.6 1.5h5.7a1.6 1.6 0 0 0 1.6-1.5l.8-12");

    public static StreamGeometry X { get; } = Parse(
        "M6 6 l12 12M18 6 6 18");


    /// <summary>
    /// Un icono, del tamano y del color que se le pidan.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Va dentro de un <see cref="Viewbox"/> y no con <c>Stretch</c> en el propio <see cref="Path"/>
    /// porque el <see cref="Viewbox"/> escala <b>tambien el trazo</b>: a 18 px, la linea de 1,5 del
    /// lienzo de 24 se ve de 1,125, que es exactamente lo que hace el CSS del diseno. Estirando la
    /// geometria, el grosor se queda en 1,5 y los iconos pequenos salen gordos.
    /// </para>
    /// <para>
    /// El color se pasa y no se hereda: Avalonia no tiene <c>currentColor</c>, y un enlace al
    /// <c>Foreground</c> del padre seria el primer binding de una interfaz que no tiene ninguno a
    /// proposito. Quien necesite que el icono cambie con el estado del control —el rail, los botones
    /// de la ventana— le cambia el pincel, que para eso son compartidos y mutables.
    /// </para>
    /// </remarks>
    public static Viewbox Make(StreamGeometry icon, IBrush brush, double size = 18) => new()
    {
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new Path
        {
            Data = icon,
            Fill = null,
            Stroke = brush,
            StrokeThickness = 1.5,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = 24,
            Height = 24,
        },
    };

    private static StreamGeometry Parse(string data) => StreamGeometry.Parse(data);
}
