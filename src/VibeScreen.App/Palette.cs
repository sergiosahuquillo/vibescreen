using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using VibeScreen.App.Localization;

namespace VibeScreen.App;

/// <summary>Con qué paleta se pinta la ventana.</summary>
/// <remarks>
/// Es de la ventana y no del panel: lo que se está pintando en la pantalla lo decide el tema del
/// servicio y no cambia porque aquí se ponga el modo claro.
/// </remarks>
public enum AppTheme
{
    Dark,
    Light,
    System,
}

/// <summary>Uno de los realces elegibles, con su color para cada paleta.</summary>
/// <remarks>
/// Dos tonos y no uno aclarado: el oscuro tiene que leerse sobre casi negro y el claro sobre blanco,
/// y aclarar el mismo color no da las dos cosas. Los dos pasan de 4,5:1 en su fondo.
/// </remarks>
/// <param name="Id">Lo que se guarda en los ajustes. No es un color: es cuál de los ocho.</param>
/// <param name="Name">Cómo se lee en Ajustes.</param>
/// <param name="OnDark">El tono para la paleta oscura.</param>
/// <param name="OnLight">El tono para la paleta clara.</param>
public sealed record AccentChoice(string Id, Text Name, string OnDark, string OnLight)
{
    /// <summary>El tono que toca según la paleta que esté puesta.</summary>
    public Color For(bool light) => Color.Parse(light ? OnLight : OnDark);
}

/// <summary>Los colores de la aplicación. Los del panel los pone cada tema, estos son los de la ventana.</summary>
/// <remarks>
/// <para>
/// Casi negro —o casi blanco—, texto y un solo color de realce. El realce no decora: marca el título
/// de cada tarjeta, la sección activa, la fila seleccionada y el foco, y es lo único que el usuario
/// elige. El verde y el ámbar de los avisos se quedan fuera de esa elección, porque «va bien» y
/// «cuidado» no pueden ser los dos del mismo color que todo lo demás.
/// </para>
/// <para>
/// <b>Todos los pinceles son mutables y compartidos a propósito.</b> Quien los usa se queda con esta
/// misma instancia, así que cambiar de paleta o de realce repinta la ventana entera sin reiniciar y
/// sin que ninguna vista tenga que enterarse. Es lo que ya hacía el realce, extendido al resto: la
/// alternativa —un <c>new SolidColorBrush(...)</c> en cada sitio de uso— obligaría a reconstruir
/// todas las vistas para cambiar de modo.
/// </para>
/// </remarks>
public static class Palette
{
    /// <summary>El realce de fábrica, hasta que el usuario diga otra cosa.</summary>
    /// <remarks>
    /// Es un identificador y no un <c>#RRGGBB</c>: con paleta clara y oscura, un realce ya no es un
    /// color sino una pareja, y lo que se guarda tiene que poder resolverse a las dos.
    /// </remarks>
    public const string DefaultAccent = "rojo";

    /// <summary>El tono oscuro del realce de fábrica.</summary>
    /// <remarks>
    /// Aparte porque lo necesita también el icono de los ficheros —el del ejecutable, el acceso
    /// directo y el instalador—, que no puede seguir el realce elegido como hace el de la ventana y
    /// se queda con el de fábrica. Constante y no la lista: quien lo exporta no tiene Avalonia en pie
    /// y leer <see cref="Accents"/> dispararía el constructor estático, que pinta la paleta.
    /// </remarks>
    public const string DefaultAccentOnDark = "#FF6F61";

    /// <summary>Los ocho realces, que abarcan la rueda sin pasarse.</summary>
    public static readonly IReadOnlyList<AccentChoice> Accents =
    [
        new("rojo", Loc.L("accent.red"), DefaultAccentOnDark, "#CF3A2B"),
        new("naranja", Loc.L("accent.orange"), "#F59E5B", "#B25A08"),
        new("amarillo", Loc.L("accent.yellow"), "#E8C55A", "#856800"),
        new("verde", Loc.L("accent.green"), "#63D08A", "#0F7A4F"),
        new("turquesa", Loc.L("accent.turquoise"), "#4ED8C4", "#0D7A72"),
        new("azul", Loc.L("accent.blue"), "#6BA6FF", "#1F5FD6"),
        new("violeta", Loc.L("accent.violet"), "#A98BFF", "#6741CF"),
        new("rosa", Loc.L("accent.pink"), "#FF7FB6", "#B8317A"),
    ];

    /// <summary>La paleta que está puesta ahora mismo. <see cref="AppTheme.System"/> ya viene resuelto.</summary>
    public static bool IsLight { get; private set; }

    // Lo elegido, para poder rehacer el realce cuando cambie la paleta: el mismo realce es otro color
    // en claro que en oscuro, así que no basta con guardar el color ya resuelto.
    private static string _accent = DefaultAccent;

    // ── los pinceles ──────────────────────────────────────────────────────────
    //
    // Todos nacen transparentes y los rellena el constructor estático llamando a Apply: los
    // inicializadores de campo corren en orden textual, así que darles aquí su color obligaría a
    // declarar las dos paletas antes que ellos y a no volver a moverlas nunca. El constructor
    // estático corre después de todos los inicializadores, de modo que ahí las paletas ya existen,
    // y termina antes de que nadie pueda leer un pincel: tocar cualquier miembro de la clase es lo
    // que dispara la inicialización del tipo.

    public static readonly SolidColorBrush BackgroundBrush = new(Colors.Transparent);
    public static readonly SolidColorBrush SurfaceBrush = new(Colors.Transparent);

    /// <summary>La superficie de lo que se levanta sobre una tarjeta: botones, casillas, cabeceras.</summary>
    public static readonly SolidColorBrush Surface2Brush = new(Colors.Transparent);

    /// <summary>Un escalón más arriba: el botón con el ratón encima, el hueco de una barra.</summary>
    public static readonly SolidColorBrush Surface3Brush = new(Colors.Transparent);

    public static readonly SolidColorBrush LineBrush = new(Colors.Transparent);

    /// <summary>La línea que tiene que verse: el borde de un control, un separador.</summary>
    public static readonly SolidColorBrush LineStrongBrush = new(Colors.Transparent);

    /// <summary>El borde con el ratón encima.</summary>
    public static readonly SolidColorBrush LineHoverBrush = new(Colors.Transparent);

    /// <summary>La línea que casi no está: entre filas de una tabla larga.</summary>
    public static readonly SolidColorBrush LineSoftBrush = new(Colors.Transparent);

    public static readonly SolidColorBrush TextBrush = new(Colors.Transparent);

    /// <summary>El texto de segunda: etiquetas, unidades, lo que acompaña al dato.</summary>
    public static readonly SolidColorBrush MutedBrush = new(Colors.Transparent);

    /// <summary>El texto de tercera: ayudas, versalitas de sección, las marcas de una escala.</summary>
    public static readonly SolidColorBrush FaintBrush = new(Colors.Transparent);

    public static readonly SolidColorBrush GoodBrush = new(Colors.Transparent);
    public static readonly SolidColorBrush WarnBrush = new(Colors.Transparent);
    public static readonly SolidColorBrush BadBrush = new(Colors.Transparent);

    /// <summary>El velo que separa el banco de trabajo del resto.</summary>
    public static readonly SolidColorBrush WashBrush = new(Colors.Transparent);

    /// <summary>La superficie a medio hacer del inspector, que deja ver el fondo por detrás.</summary>
    /// <remarks>
    /// Translúcida y no un gris calculado: el diseño la pide así —«el 60 % de la superficie»— y es
    /// lo que separa la columna del lienzo sin convertirla en otra tarjeta. Encima de ella, las
    /// secciones van macizas, y ese escalón es lo que hace que se vean encajadas y no pegadas.
    /// </remarks>
    public static readonly SolidColorBrush SurfaceSoftBrush = new(Colors.Transparent);

    /// <summary>La superficie más rala, al 45 %: la barra y el pie del editor.</summary>
    /// <remarks>
    /// Más que la del inspector, y a propósito: con la columna de inspección a todo el alto, la barra
    /// queda pegada a ella, y el diseño quiere la columna un punto más clara que lo que tiene al lado.
    /// Macizas, el escalón salía al revés.
    /// </remarks>
    public static readonly SolidColorBrush SurfaceThinBrush = new(Colors.Transparent);

    /// <summary>El bisel del marco que rodea la previsualización del panel.</summary>
    public static readonly SolidColorBrush Bezel1Brush = new(Colors.Transparent);

    /// <summary>El otro extremo del bisel: el marco es un degradado entre los dos.</summary>
    public static readonly SolidColorBrush Bezel2Brush = new(Colors.Transparent);

    /// <summary>El degradado del marco, de <see cref="Bezel1Brush"/> arriba a <see cref="Bezel2Brush"/> abajo.</summary>
    /// <remarks>
    /// Uno compartido y repintado en <see cref="Apply(AppTheme, string?)"/>, como los demás pinceles. Cada marco
    /// hacía el suyo copiando los dos colores al crearse, y al cambiar de modo con la ventana abierta
    /// se quedaba con el bisel del modo anterior: gris claro alrededor de un panel en modo oscuro.
    /// </remarks>
    public static readonly LinearGradientBrush BezelBrush = new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Transparent, 1) },
    };

    /// <summary>El pulgar de un interruptor apagado.</summary>
    public static readonly SolidColorBrush ThumbOffBrush = new(Colors.Transparent);

    /// <summary>El aro que despega el pulgar de un deslizador de su propia pista.</summary>
    public static readonly SolidColorBrush ThumbRingBrush = new(Colors.Transparent);

    public static readonly SolidColorBrush ScrollBrush = new(Colors.Transparent);
    public static readonly SolidColorBrush ScrollHoverBrush = new(Colors.Transparent);

    /// <summary>El canto de una muestra de color, para que el blanco no se pierda sobre blanco.</summary>
    public static readonly SolidColorBrush SwatchEdgeBrush = new(Colors.Transparent);

    /// <summary>La rejilla del lienzo del editor. No es el realce: se dibuja encima del tema.</summary>
    public static readonly SolidColorBrush GridBrush = new(Colors.Transparent);

    /// <summary>El realce.</summary>
    public static readonly SolidColorBrush AccentBrush = new(Colors.Transparent);

    /// <summary>Lo que se escribe encima del realce: casi negro en oscuro, blanco en claro.</summary>
    public static readonly SolidColorBrush OnAccentBrush = new(Colors.Transparent);

    /// <summary>El realce con el ratón encima.</summary>
    public static readonly SolidColorBrush AccentHoverBrush = new(Colors.Transparent);

    /// <summary>El realce muy rebajado: el fondo de la sección activa, el de una fila elegida.</summary>
    public static readonly SolidColorBrush AccentSoftBrush = new(Colors.Transparent);

    /// <summary>Un punto más: el botón de un segmentado que está puesto.</summary>
    public static readonly SolidColorBrush AccentMediumBrush = new(Colors.Transparent);

    /// <summary>El realce apagado contra el fondo, para poder escribir encima de lo seleccionado.</summary>
    public static readonly SolidColorBrush SelectionBrush = new(Colors.Transparent);

    /// <summary>El resplandor de la esquina de la ventana. Casi no se ve, y sin él la ventana es plana.</summary>
    public static readonly SolidColorBrush GlowBrush = new(Colors.Transparent);

    // ── los tres estados, rebajados ───────────────────────────────────────────
    //
    // Una pastilla de «va bien» o un botón de borrar no se pintan del color plano: llevan ese color
    // en la letra, un borde a media tinta y un fondo que apenas se insinúa. Son translúcidos por lo
    // mismo que el realce rebajado —valen encima de la ventana y encima de una tarjeta—, y van aquí
    // y no calculados en cada sitio porque el verde y el ámbar cambian con la paleta.

    /// <summary>El fondo de algo que va bien.</summary>
    public static readonly SolidColorBrush GoodSoftBrush = new(Colors.Transparent);

    /// <summary>Su borde.</summary>
    public static readonly SolidColorBrush GoodEdgeBrush = new(Colors.Transparent);

    /// <summary>El fondo de un aviso.</summary>
    public static readonly SolidColorBrush WarnSoftBrush = new(Colors.Transparent);

    /// <summary>Su borde.</summary>
    public static readonly SolidColorBrush WarnEdgeBrush = new(Colors.Transparent);

    /// <summary>El fondo de lo que no tiene vuelta atrás.</summary>
    public static readonly SolidColorBrush BadSoftBrush = new(Colors.Transparent);

    /// <summary>Su fondo con el ratón encima, que es cuando hay que dudar.</summary>
    public static readonly SolidColorBrush BadSoftHoverBrush = new(Colors.Transparent);

    /// <summary>Su borde.</summary>
    public static readonly SolidColorBrush BadEdgeBrush = new(Colors.Transparent);

    /// <summary>Su borde con el ratón encima.</summary>
    public static readonly SolidColorBrush BadEdgeHoverBrush = new(Colors.Transparent);

    // ── los colores, por si hace falta el valor y no el pincel ────────────────

    public static Color Background => BackgroundBrush.Color;
    public static Color Surface => SurfaceBrush.Color;
    public static Color Line => LineBrush.Color;
    public static Color Text => TextBrush.Color;
    public static Color Muted => MutedBrush.Color;
    public static Color Good => GoodBrush.Color;
    public static Color Warn => WarnBrush.Color;
    public static Color Bad => BadBrush.Color;
    public static Color Accent => AccentBrush.Color;

    // ── geometría y movimiento ────────────────────────────────────────────────

    /// <summary>El redondeo de una tarjeta.</summary>
    public static readonly CornerRadius CardRadius = new(12);

    /// <summary>El redondeo de un control: botón, casilla, desplegable.</summary>
    public static readonly CornerRadius ControlRadius = new(8);

    /// <summary>El redondeo de lo que tiene que parecer una pastilla: barras, chips, pistas.</summary>
    public static readonly CornerRadius PillRadius = new(999);

    /// <summary>Lo que tarda un cambio de estado —el ratón encima, el foco— en verse.</summary>
    public static readonly TimeSpan Quick = TimeSpan.FromMilliseconds(180);

    /// <summary>Lo que tarda algo que se mueve de sitio o cambia de tamaño.</summary>
    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(240);

    /// <summary>La curva de todo lo que se mueve: sale deprisa y llega frenando.</summary>
    public static readonly Easing Ease = new SplineEasing(0.2, 0.7, 0.3, 1);

    /// <summary>La sombra de una tarjeta: una luz arriba por dentro y una sombra corta debajo.</summary>
    public static BoxShadows CardShadow { get; private set; }

    /// <summary>La sombra de lo que flota: un aviso, un desplegable abierto.</summary>
    public static BoxShadows PopShadow { get; private set; }

    static Palette() => Apply(AppTheme.Dark, DefaultAccent);

    /// <summary>
    /// Pone la paleta y el realce de una vez.
    /// </summary>
    /// <remarks>
    /// Juntos y no en dos llamadas porque no son independientes: el mismo realce es un color en
    /// oscuro y otro en claro, así que cambiar de paleta sin rehacer el realce dejaría un rojo
    /// pensado para fondo negro encima de fondo blanco.
    /// </remarks>
    /// <param name="theme">La paleta pedida. <see cref="AppTheme.System"/> se resuelve aquí.</param>
    /// <param name="accent">El identificador del realce, o un <c>#RRGGBB</c> a medida. Null lo deja como está.</param>
    public static void Apply(AppTheme theme, string? accent)
    {
        IsLight = theme switch
        {
            AppTheme.Light => true,
            AppTheme.Dark => false,
            _ => SystemIsLight(),
        };

        var tokens = IsLight ? LightVariant : DarkVariant;

        BackgroundBrush.Color = tokens.Background;
        SurfaceBrush.Color = tokens.Surface;
        Surface2Brush.Color = tokens.Surface2;
        Surface3Brush.Color = tokens.Surface3;
        LineBrush.Color = tokens.Line;
        LineStrongBrush.Color = tokens.LineStrong;
        LineHoverBrush.Color = tokens.LineHover;
        LineSoftBrush.Color = tokens.LineSoft;
        TextBrush.Color = tokens.Text;
        MutedBrush.Color = tokens.Text2;
        FaintBrush.Color = tokens.Text3;
        GoodBrush.Color = tokens.Good;
        WarnBrush.Color = tokens.Warn;
        BadBrush.Color = tokens.Bad;
        WashBrush.Color = tokens.Wash;
        Bezel1Brush.Color = tokens.Bezel1;
        Bezel2Brush.Color = tokens.Bezel2;
        BezelBrush.GradientStops[0].Color = tokens.Bezel1;
        BezelBrush.GradientStops[1].Color = tokens.Bezel2;
        ThumbOffBrush.Color = tokens.ThumbOff;
        ThumbRingBrush.Color = tokens.ThumbRing;
        ScrollBrush.Color = tokens.Scroll;
        ScrollHoverBrush.Color = tokens.ScrollHover;
        SwatchEdgeBrush.Color = tokens.SwatchEdge;
        GridBrush.Color = tokens.Grid;
        OnAccentBrush.Color = tokens.OnAccent;

        SurfaceSoftBrush.Color = Fade(tokens.Surface, 0.60);
        SurfaceThinBrush.Color = Fade(tokens.Surface, 0.45);

        GoodSoftBrush.Color = Fade(tokens.Good, 0.10);
        GoodEdgeBrush.Color = Fade(tokens.Good, 0.32);
        WarnSoftBrush.Color = Fade(tokens.Warn, 0.09);
        WarnEdgeBrush.Color = Fade(tokens.Warn, 0.26);
        BadSoftBrush.Color = Fade(tokens.Bad, 0.08);
        BadSoftHoverBrush.Color = Fade(tokens.Bad, 0.16);
        BadEdgeBrush.Color = Fade(tokens.Bad, 0.38);
        BadEdgeHoverBrush.Color = Fade(tokens.Bad, 0.55);

        CardShadow = tokens.CardShadow;
        PopShadow = tokens.PopShadow;

        if (accent is not null) _accent = accent;
        SetAccent(ParseAccent(_accent));

        // Y con los pinceles ya puestos, volcarlos sobre los colores de los controles de serie. Aquí
        // al final porque Theme.FluentTokens lee de ellos: antes de SetAccent escribiría el realce
        // viejo. Mírese ahí por qué el color de un control de Fluent se pone por su clave y no con
        // un estilo.
        Theme.FluentTokens.Paint();

        if (Application.Current is { } app)
            app.RequestedThemeVariant = IsLight ? ThemeVariant.Light : ThemeVariant.Dark;
    }

    /// <summary>Cambia solo la paleta, conservando el realce elegido.</summary>
    public static void Apply(AppTheme theme) => Apply(theme, null);

    /// <summary>
    /// Cambia el color de realce de toda la aplicación.
    /// </summary>
    /// <remarks>
    /// Además de los pinceles propios toca los recursos del tema Fluent, que es de donde salen la
    /// fila seleccionada de una lista, el aro del foco, las casillas marcadas y el pulgar del
    /// deslizador. Sin eso, la mitad de la ventana seguiría azul.
    /// </remarks>
    public static void SetAccent(Color color)
    {
        AccentBrush.Color = color;
        AccentHoverBrush.Color = Mix(color, IsLight ? Colors.Black : Colors.White, 0.86);
        AccentSoftBrush.Color = Fade(color, IsLight ? 0.10 : 0.12);
        AccentMediumBrush.Color = Fade(color, IsLight ? 0.15 : 0.18);
        SelectionBrush.Color = Fade(color, IsLight ? 0.22 : 0.40);
        GlowBrush.Color = Fade(color, IsLight ? 0.09 : 0.07);

        if (Application.Current is not { } app) return;

        app.Resources["SystemAccentColor"] = color;
        app.Resources["SystemAccentColorLight1"] = Mix(color, Colors.White, 0.8);
        app.Resources["SystemAccentColorLight2"] = Mix(color, Colors.White, 0.6);
        app.Resources["SystemAccentColorLight3"] = Mix(color, Colors.White, 0.4);
        app.Resources["SystemAccentColorDark1"] = Mix(color, Colors.Black, 0.8);
        app.Resources["SystemAccentColorDark2"] = Mix(color, Colors.Black, 0.6);
        app.Resources["SystemAccentColorDark3"] = Mix(color, Colors.Black, 0.4);
    }

    /// <summary>
    /// Resuelve lo que está guardado en los ajustes al color que toca en la paleta de ahora.
    /// </summary>
    /// <remarks>
    /// Acepta las dos formas: el identificador de uno de los ocho —lo que se guarda desde que hay
    /// dos paletas— y un <c>#RRGGBB</c> suelto, que es lo que hay en los ficheros de antes. Lo que
    /// no sea ni una cosa ni la otra se queda con el de fábrica en vez de fallar: un gusto mal
    /// escrito no puede dejar la ventana sin realce.
    /// </remarks>
    public static Color ParseAccent(string? value)
    {
        foreach (var choice in Accents)
            if (string.Equals(choice.Id, value, StringComparison.OrdinalIgnoreCase))
                return choice.For(IsLight);

        return Color.TryParse(value, out var custom) ? custom : Accents[0].For(IsLight);
    }

    /// <summary>
    /// Traduce lo que hay en un fichero de ajustes de antes de que los realces fueran parejas.
    /// </summary>
    /// <remarks>
    /// Hasta ahora se guardaba el <c>#RRGGBB</c> del realce, y los ocho que se ofrecían eran estos.
    /// Dejarlos pasar como «color a medida» sería mentir dos veces: ninguna muestra saldría marcada
    /// —el usuario eligió una, y sigue queriendo esa— y el tono no cambiaría al pasar a modo claro,
    /// que es justo para lo que existe la pareja. Un color que no sea de esta lista sí es a medida y
    /// se queda como está.
    /// </remarks>
    public static string? MigrateAccent(string? stored) => stored?.ToUpperInvariant() switch
    {
        "#E5483D" => "rojo",
        "#E07A3C" => "naranja",
        "#D8A13A" => "amarillo",
        "#5FA96A" => "verde",
        "#3FA3A0" => "turquesa",
        "#5B8DD9" => "azul",
        "#8E7BD4" => "violeta",
        "#CF6A9B" => "rosa",
        _ => stored,
    };

    /// <summary>
    /// Si el sistema pide claro.
    /// </summary>
    /// <remarks>
    /// Se le pregunta a la plataforma y no al tema de Avalonia, que es justo lo que estamos a punto
    /// de fijar: preguntarle a él sería leer nuestra propia respuesta. Sin plataforma —antes de que
    /// la aplicación esté en pie— se contesta oscuro, que es el de fábrica.
    /// </remarks>
    private static bool SystemIsLight() =>
        Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Light;

    /// <summary>
    /// <paramref name="proportion"/> de <paramref name="color"/> y el resto de <paramref name="over"/>.
    /// </summary>
    private static Color Mix(Color color, Color over, double proportion)
    {
        byte Blend(byte a, byte b) => (byte)Math.Round((a * proportion) + (b * (1 - proportion)));
        return Color.FromRgb(Blend(color.R, over.R), Blend(color.G, over.G), Blend(color.B, over.B));
    }

    /// <summary>
    /// El mismo color, translúcido.
    /// </summary>
    /// <remarks>
    /// Translúcido y no mezclado contra el fondo: el mismo realce rebajado se usa encima de la
    /// ventana y encima de una tarjeta, que no son del mismo color, y mezclado contra uno se vería
    /// como una mancha sobre el otro.
    /// </remarks>
    private static Color Fade(Color color, double alpha) =>
        Color.FromArgb((byte)Math.Round(alpha * 255), color.R, color.G, color.B);

    private static Color Rgba(byte r, byte g, byte b, double alpha) =>
        Color.FromArgb((byte)Math.Round(alpha * 255), r, g, b);

    /// <summary>Estilos que se repiten por toda la ventana, por clase.</summary>
    public static Styles Styles() =>
    [
        new Style(x => x.OfType<TextBlock>().Class("h2"))
        {
            Setters =
            {
                // En versalitas y espaciado: un título en mayúsculas separa las tarjetas mejor que un
                // cuerpo de letra mayor, y deja la cifra de dentro como lo más grande de la caja.
                new Setter(TextBlock.FontSizeProperty, 12.0),
                new Setter(TextBlock.LetterSpacingProperty, 1.2),
                new Setter(TextBlock.ForegroundProperty, AccentBrush),
            },
        },
        new Style(x => x.OfType<Border>().Class("tarjeta"))
        {
            Setters =
            {
                new Setter(Border.BackgroundProperty, SurfaceBrush),
                new Setter(Border.BorderBrushProperty, LineBrush),
                new Setter(Border.BorderThicknessProperty, new Thickness(1)),
                new Setter(Border.CornerRadiusProperty, CardRadius),
                new Setter(Border.PaddingProperty, new Thickness(20, 18, 20, 20)),
            },
        },
    ];

    /// <summary>Los veintitantos colores de una paleta. Se mide aparte, no se aclara desde la otra.</summary>
    private sealed record Variant
    {
        public required Color Background { get; init; }
        public required Color Surface { get; init; }
        public required Color Surface2 { get; init; }
        public required Color Surface3 { get; init; }
        public required Color Line { get; init; }
        public required Color LineStrong { get; init; }
        public required Color LineHover { get; init; }
        public required Color LineSoft { get; init; }
        public required Color Text { get; init; }
        public required Color Text2 { get; init; }
        public required Color Text3 { get; init; }
        public required Color Good { get; init; }
        public required Color Warn { get; init; }
        public required Color Bad { get; init; }
        public required Color Wash { get; init; }
        public required Color Bezel1 { get; init; }
        public required Color Bezel2 { get; init; }
        public required Color ThumbOff { get; init; }
        public required Color ThumbRing { get; init; }
        public required Color Scroll { get; init; }
        public required Color ScrollHover { get; init; }
        public required Color SwatchEdge { get; init; }
        public required Color Grid { get; init; }
        public required Color OnAccent { get; init; }
        public required BoxShadows CardShadow { get; init; }
        public required BoxShadows PopShadow { get; init; }
    }

    private static readonly Variant DarkVariant = new()
    {
        Background = Color.Parse("#0A0C10"),
        Surface = Color.Parse("#12151B"),
        Surface2 = Color.Parse("#191D25"),
        Surface3 = Color.Parse("#222732"),
        Line = Rgba(255, 255, 255, 0.07),
        LineStrong = Rgba(255, 255, 255, 0.13),
        LineHover = Rgba(255, 255, 255, 0.22),
        LineSoft = Rgba(255, 255, 255, 0.035),
        Text = Color.Parse("#E6E9EF"),
        Text2 = Color.Parse("#9AA3B2"),
        Text3 = Color.Parse("#828C9E"),
        Good = Color.Parse("#3FBF7F"),
        Warn = Color.Parse("#E8A33D"),
        Bad = Color.Parse("#FF6F61"),
        Wash = Rgba(255, 255, 255, 0.035),
        Bezel1 = Color.Parse("#1C2029"),
        Bezel2 = Color.Parse("#0E1116"),
        ThumbOff = Color.Parse("#79828F"),
        ThumbRing = Color.Parse("#0E1116"),
        Scroll = Color.Parse("#222732"),
        ScrollHover = Color.Parse("#2D3341"),
        SwatchEdge = Rgba(255, 255, 255, 0.18),
        Grid = Rgba(255, 255, 255, 0.10),
        OnAccent = Color.Parse("#0A0C10"),
        CardShadow = new BoxShadows(
            new BoxShadow { OffsetY = 1, Blur = 2, Color = Rgba(0, 0, 0, 0.40) },
            [new BoxShadow { OffsetY = 1, Color = Rgba(255, 255, 255, 0.035), IsInset = true }]),
        PopShadow = new BoxShadows(new BoxShadow { OffsetY = 12, Blur = 30, Color = Rgba(0, 0, 0, 0.50) }),
    };

    private static readonly Variant LightVariant = new()
    {
        Background = Color.Parse("#EEF1F6"),
        Surface = Color.Parse("#FFFFFF"),
        Surface2 = Color.Parse("#F2F5F9"),
        Surface3 = Color.Parse("#E4E9F1"),
        Line = Rgba(13, 20, 33, 0.10),
        LineStrong = Rgba(13, 20, 33, 0.16),
        LineHover = Rgba(13, 20, 33, 0.30),
        LineSoft = Rgba(13, 20, 33, 0.06),
        Text = Color.Parse("#111722"),
        Text2 = Color.Parse("#4D5768"),
        Text3 = Color.Parse("#5F6A7D"),
        Good = Color.Parse("#12855A"),
        Warn = Color.Parse("#8F6206"),
        Bad = Color.Parse("#C0392B"),
        Wash = Rgba(13, 20, 33, 0.04),
        Bezel1 = Color.Parse("#DFE4EC"),
        Bezel2 = Color.Parse("#B9C1CD"),
        ThumbOff = Color.Parse("#97A1B0"),
        ThumbRing = Color.Parse("#FFFFFF"),
        Scroll = Color.Parse("#CCD4E0"),
        ScrollHover = Color.Parse("#B6C0D0"),
        SwatchEdge = Rgba(13, 20, 33, 0.18),

        // La rejilla se dibuja encima del tema del panel, que es oscuro pase lo que pase en la
        // ventana: en claro tiene que seguir siendo blanca o desaparecería sobre el fondo del tema.
        Grid = Rgba(255, 255, 255, 0.10),
        OnAccent = Color.Parse("#FFFFFF"),
        CardShadow = new BoxShadows(
            new BoxShadow { OffsetY = 1, Blur = 2, Color = Rgba(15, 23, 42, 0.09) },
            [new BoxShadow { OffsetY = 1, Color = Rgba(255, 255, 255, 0.70), IsInset = true }]),
        PopShadow = new BoxShadows(new BoxShadow { OffsetY = 12, Blur = 30, Color = Rgba(15, 23, 42, 0.18) }),
    };
}
