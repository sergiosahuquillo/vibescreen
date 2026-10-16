using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using SkiaSharp;
using VibeScreen.Core.Rendering;

namespace VibeScreen.App;

/// <summary>Las tipografías de la ventana: las que van dentro y las que hay en la máquina.</summary>
/// <remarks>
/// <para>
/// Tres papeles y no uno. <see cref="Ui"/> escribe lo que se lee —etiquetas, botones, ayudas—,
/// <see cref="Display"/> los títulos, y la monoespaciada queda para lo que es un <b>dato</b>: cifras,
/// unidades e identificadores de sensor, donde las columnas tienen que cuadrar y un 0 no puede
/// confundirse con una O.
/// </para>
/// <para>
/// Las tres van empotradas en el ejecutable. La ventana no puede depender de que estén instaladas:
/// una máquina sin ellas dejaría la interfaz con la tipografía que el sistema tenga a mano y el
/// rediseño se caería solo. Son OFL, así que se redistribuyen con su licencia al lado, en
/// <c>Assets/Fonts</c>.
/// </para>
/// <para>
/// La monoespaciada es la excepción: se puede cambiar por otra de la máquina, porque es la única
/// elección tipográfica que el usuario tiene en Ajustes y hay quien tiene la suya. La empotrada es
/// la de fábrica y la primera de la lista, así que <b>la elección nunca se cae a cuadrados</b>:
/// haya lo que haya instalado, siempre queda una que existe seguro.
/// </para>
/// </remarks>
public static class AppFonts
{
    /// <summary>
    /// De dónde salen las empotradas.
    /// </summary>
    /// <remarks>
    /// Es el <b>nombre del ensamblado</b> y no el del espacio de nombres, que aquí no coinciden: el
    /// proyecto se compila como <c>vibescreen-app</c>. Con <c>VibeScreen.App</c> el recurso no
    /// aparece y Avalonia tira un «Could not create glyphTypeface» al medir el primer texto, o sea
    /// antes de que llegue a verse la ventana.
    /// </remarks>
    private const string Embedded = "avares://vibescreen-app/Assets/Fonts";

    /// <summary>La de la interfaz: lo que se lee de corrido.</summary>
    public static FontFamily Ui { get; } = new($"{Embedded}#Archivo");

    /// <summary>La de los títulos y las cifras grandes.</summary>
    public static FontFamily Display { get; } = new($"{Embedded}#Space Grotesk");

    /// <summary>La de los datos, de fábrica.</summary>
    public static FontFamily DataDefault { get; } = new($"{Embedded}#JetBrains Mono");

    /// <summary>Cómo se llama la empotrada en el desplegable de Ajustes.</summary>
    public const string DataDefaultName = "JetBrains Mono";

    /// <summary>Dónde vive en los recursos de la aplicación la de los datos que se está usando.</summary>
    private const string DataKey = "VibeScreenDataFont";

    /// <summary>La de los datos que se está usando: la empotrada, o la que se eligió en Ajustes.</summary>
    /// <remarks>
    /// Para lo que se dibuja a mano con un <see cref="Typeface"/>. Lo que es un control la recibe con
    /// <see cref="Mono"/> y se repinta solo al cambiarla.
    /// </remarks>
    public static FontFamily DataCurrent { get; private set; } = DataDefault;

    private static readonly Lazy<string[]> InstalledFamilies = new(() => SKFontManager.Default.GetFontFamilies());

    private static readonly Lazy<string[]> Sorted =
        new(() => [.. InstalledFamilies.Value.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)]);

    /// <summary>
    /// Todas las familias de esta máquina, ordenadas.
    /// </summary>
    /// <remarks>
    /// Para el desplegable de letra de cada widget, que se construye en cada cambio de selección —o
    /// sea, en cada clic en el lienzo—. Pedírsela a Skia y ordenarla cada vez, con unos cientos de
    /// tipografías instaladas, se nota en el clic.
    /// </remarks>
    public static IReadOnlyList<string> Installed => Sorted.Value;

    /// <summary>
    /// Las monoespaciadas que se pueden elegir, en orden de preferencia.
    /// </summary>
    /// <remarks>
    /// La empotrada va primera y siempre está, que es lo que hace que la lista no pueda quedar
    /// vacía. Detrás, las conocidas que esta máquina tenga de verdad.
    /// </remarks>
    public static IReadOnlyList<string> Available =>
    [
        DataDefaultName,
        .. FontChoice.Monospace
            .Where(family => !string.Equals(family, DataDefaultName, StringComparison.OrdinalIgnoreCase))
            .Where(family => FontChoice.FirstInstalled([family], InstalledFamilies.Value) is not null),
    ];

    /// <summary>El nombre de la monoespaciada que se va a usar de verdad.</summary>
    /// <remarks>
    /// Nunca null, al revés que antes: ahora siempre hay una, porque una de ellas viaja dentro. En
    /// Ajustes esto es lo que sale marcado, que es la respuesta a la única pregunta que tiene quien
    /// abre ese desplegable: no cuál se pidió, sino cuál se está usando.
    /// </remarks>
    public static string Resolve(string? chosen) =>
        chosen is null || string.Equals(chosen, DataDefaultName, StringComparison.OrdinalIgnoreCase)
            ? DataDefaultName
            : FontChoice.Resolve(chosen, InstalledFamilies.Value) ?? DataDefaultName;

    /// <summary>La monoespaciada para Avalonia: la empotrada, o la que el usuario haya elegido.</summary>
    public static FontFamily Data(string? chosen) =>
        Resolve(chosen) is var family && string.Equals(family, DataDefaultName, StringComparison.Ordinal)
            ? DataDefault
            : new FontFamily(family);

    /// <summary>
    /// Pone la monoespaciada de los datos, y la cambia en todo lo que la lleve.
    /// </summary>
    /// <remarks>
    /// Va a los recursos de la aplicación, como los tonos del realce, y no a la ventana: desde el
    /// rediseño cada vista se pone encima la letra de interfaz, así que lo que la ventana heredaba ya
    /// no le llegaba a ningún dato. Elegir otra en Ajustes se guardaba y no cambiaba nada, ni al
    /// reiniciar.
    /// </remarks>
    public static void UseData(string? chosen)
    {
        DataCurrent = Data(chosen);
        if (Application.Current is { } app) app.Resources[DataKey] = DataCurrent;
    }

    /// <summary>
    /// Escribe un control en la monoespaciada de los datos, y lo deja siguiendo a la de Ajustes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Un enlace al recurso y no un evento: con un evento estático, cada fila que se rehace —la lista
    /// de sensores, la de widgets— se quedaría enganchada a él para siempre, y la ventana vive días en
    /// la bandeja. El recurso lo busca el propio control en su árbol y muere con él.
    /// </para>
    /// <para>
    /// Mientras el control no está en ningún árbol, o donde nadie ha puesto el recurso —la ThemeCheck—,
    /// lleva la que haya ahora.
    /// </para>
    /// </remarks>
    public static T Mono<T>(T control)
        where T : Control
    {
        FollowData(control);
        return control;
    }

    /// <summary>
    /// Lo mismo que <see cref="Mono"/>, devolviendo el enlace para quien tenga que soltarlo.
    /// </summary>
    /// <remarks>
    /// Para el control que unas veces es dato y otras no. Ponerle a mano la letra de interfaz no basta:
    /// el enlace sigue vivo, y la siguiente letra elegida en Ajustes la pisaba. Tampoco lo para un
    /// <c>ClearValue</c>; hay que soltarlo.
    /// </remarks>
    public static IDisposable FollowData(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.Bind(TextElement.FontFamilyProperty, control.GetResourceObservable(DataKey, v => v as FontFamily ?? DataCurrent));
    }
}
