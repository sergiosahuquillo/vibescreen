using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

// Con System.IO en los usings implicitos, Path a secas es ambiguo.
using Path = Avalonia.Controls.Shapes.Path;

namespace VibeScreen.App.Theme;

/// <summary>El aspecto de los controles de serie, llevado al del rediseño.</summary>
/// <remarks>
/// <para>
/// <b>Se reviste la plantilla de Fluent en vez de sustituirla.</b> Un <see cref="ControlTheme"/>
/// propio obliga a escribir el <c>ControlTemplate</c> entero, y en controles como el deslizador o el
/// cuadro de texto eso es rehacer la navegación con el teclado, la selección, el cursor y el
/// desplazamiento —cosas que ya funcionan y que no tienen nada que ver con el color—. Aquí solo se
/// repintan las piezas.
/// </para>
/// <para>
/// Los nombres de las partes no están adivinados: salen de volcar el árbol visual de un ejemplar de
/// cada control con esta misma versión de Avalonia. Si algún día una actualización los cambia, lo
/// que se pierde es el color, no el funcionamiento: el control sigue siendo el de Fluent.
/// </para>
/// <para>
/// Y el orden importa: esto se añade a <c>Application.Styles</c> <b>después</b> del tema Fluent, que
/// es lo que hace que gane cuando los dos dicen algo del mismo sitio.
/// </para>
/// </remarks>
public static class ControlThemes
{
    /// <summary>El rojo del botón de cerrar.</summary>
    private static readonly IBrush CloseHover = new SolidColorBrush(Color.Parse("#B3261E"));

    /// <summary>Lo que dura un cambio de fondo o de borde al pasar el ratón.</summary>
    /// <remarks>
    /// <para>
    /// <b>Ninguno de los dos extremos de esto puede ser <c>Brushes.Transparent</c>.</b> El
    /// transparente de Avalonia no es «nada»: es blanco con el alfa a cero, <c>#00FFFFFF</c>. Y el
    /// color se interpola canal a canal, con el alfa aparte del RGB, así que ir de ahí a un color
    /// macizo pasa a mitad de camino por un gris claro medio translúcido. Encima de la paleta oscura
    /// eso es un destello; encima de la clara no se distingue, que es por lo que el fallo solo se
    /// veía en una de las dos. Es cosa de Avalonia —AvaloniaUI/Avalonia#11238 y #15419— y no se
    /// arregla desde fuera: <c>DoTransition</c> es interno, de modo que no hay forma de escribir una
    /// transición propia que interpole bien.
    /// </para>
    /// <para>
    /// Así que lo que se anima son dos colores macizos, o dos que comparten el RGB y solo cambian de
    /// alfa —un realce rebajado y el mismo un poco menos, o dos blancos translúcidos—. Donde el
    /// diseño pide que en reposo no se vea nada, se pinta el color del fondo que hay debajo en vez
    /// de transparente: se ve igual y no queda nada que interpolar mal.
    /// </para>
    /// </remarks>
    private static Transitions Hover() =>
    [
        new BrushTransition { Property = Border.BackgroundProperty, Duration = Palette.Quick, Easing = Palette.Ease },
        new BrushTransition { Property = Border.BorderBrushProperty, Duration = Palette.Quick, Easing = Palette.Ease },
    ];

    public static Styles Styles() =>
    [
        .. Buttons(),
        .. Inputs(),
        .. Toggles(),
        .. Bars(),
        .. Panels(),
        .. WindowChrome(),
    ];

    // ── botones ───────────────────────────────────────────────────────────────

    private static Styles Buttons() =>
    [
        new Style(x => x.OfType<Button>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, Palette.Surface2Brush),
                new Setter(TemplatedControl.BorderBrushProperty, Palette.LineStrongBrush),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.CornerRadiusProperty, Palette.ControlRadius),
                new Setter(TemplatedControl.ForegroundProperty, Palette.TextBrush),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(14, 0)),
                new Setter(TemplatedControl.FontSizeProperty, 14.0),
                new Setter(TemplatedControl.FontWeightProperty, FontWeight.Medium),
                new Setter(Layoutable.MinHeightProperty, 36.0),
                new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
                new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
            },
        },

        // Fluent pinta el fondo del botón en su presentador y no en el propio botón, así que los
        // estados hay que ponérselos ahí o los pisa él.
        Part<Button, ContentPresenter>("PART_ContentPresenter", null,
            (ContentPresenter.BackgroundProperty, Palette.Surface2Brush),
            (ContentPresenter.BorderBrushProperty, Palette.LineStrongBrush),
            (ContentPresenter.CornerRadiusProperty, Palette.ControlRadius),
            (Visual.TransitionsProperty, Hover())),

        Part<Button, ContentPresenter>("PART_ContentPresenter", ":pointerover",
            (ContentPresenter.BackgroundProperty, Palette.Surface3Brush),
            (ContentPresenter.BorderBrushProperty, Palette.LineHoverBrush)),

        Part<Button, ContentPresenter>("PART_ContentPresenter", ":pressed",
            (ContentPresenter.BackgroundProperty, Palette.Surface3Brush),
            (ContentPresenter.BorderBrushProperty, Palette.LineHoverBrush)),

        // Hundirlo un píxel al pulsarlo. Es lo único que dice que el clic ha llegado en un botón que
        // no cambia de color lo bastante como para notarse.
        new Style(x => x.OfType<Button>().Class(":pressed"))
        {
            Setters = { new Setter(Visual.RenderTransformProperty, new TranslateTransform(0, 1)) },
        },

        new Style(x => x.OfType<Button>().Class(":disabled"))
        {
            Setters = { new Setter(Visual.OpacityProperty, 0.45) },
        },

        // El realce, para la acción principal de cada pantalla. Una por pantalla: si hay dos, no hay
        // ninguna.
        Part<Button, ContentPresenter>("PART_ContentPresenter", null, "primary",
            (ContentPresenter.BackgroundProperty, Palette.AccentBrush),
            (ContentPresenter.BorderBrushProperty, Palette.AccentBrush)),

        Part<Button, ContentPresenter>("PART_ContentPresenter", ":pointerover", "primary",
            (ContentPresenter.BackgroundProperty, Palette.AccentHoverBrush),
            (ContentPresenter.BorderBrushProperty, Palette.AccentHoverBrush)),

        Part<Button, ContentPresenter>("PART_ContentPresenter", ":pressed", "primary",
            (ContentPresenter.BackgroundProperty, Palette.AccentHoverBrush),
            (ContentPresenter.BorderBrushProperty, Palette.AccentHoverBrush)),

        new Style(x => x.OfType<Button>().Class("primary"))
        {
            Setters =
            {
                new Setter(TemplatedControl.ForegroundProperty, Palette.OnAccentBrush),
                new Setter(TemplatedControl.FontWeightProperty, FontWeight.SemiBold),
            },
        },

        // Lo que no tiene vuelta atrás. No es rojo del todo: un botón rojo macizo al lado de los
        // demás parece el importante, y este es justo el que no hay que pulsar sin pensar.
        Part<Button, ContentPresenter>("PART_ContentPresenter", null, "danger",
            (ContentPresenter.BackgroundProperty, Palette.BadSoftBrush),
            (ContentPresenter.BorderBrushProperty, Palette.BadEdgeBrush)),

        Part<Button, ContentPresenter>("PART_ContentPresenter", ":pointerover", "danger",
            (ContentPresenter.BackgroundProperty, Palette.BadSoftHoverBrush),
            (ContentPresenter.BorderBrushProperty, Palette.BadEdgeHoverBrush)),

        new Style(x => x.OfType<Button>().Class("danger"))
        {
            Setters = { new Setter(TemplatedControl.ForegroundProperty, Palette.BadBrush) },
        },

        new Style(x => x.OfType<Button>().Class("sm"))
        {
            Setters =
            {
                new Setter(Layoutable.MinHeightProperty, 30.0),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(11, 0)),
                new Setter(TemplatedControl.FontSizeProperty, 13.0),
            },
        },

        // Los de la barra de título: cuadrados, sin borde y sin redondeo, porque van pegados al
        // canto de la ventana y un botón redondeado ahí deja una esquina de fondo a la vista.
        //
        // En reposo van del color de la barra y no transparentes, que es lo mismo a la vista pero no
        // lo mismo al animarlo: mírese Hover(). El borde sí se queda transparente en los tres
        // estados, y por eso en él no hay nada que interpolar.
        Part<Button, ContentPresenter>("PART_ContentPresenter", null, "winbtn",
            (ContentPresenter.BackgroundProperty, Palette.SurfaceBrush),
            (ContentPresenter.BorderBrushProperty, Brushes.Transparent),
            (ContentPresenter.CornerRadiusProperty, new CornerRadius(0))),

        Part<Button, ContentPresenter>("PART_ContentPresenter", ":pointerover", "winbtn",
            (ContentPresenter.BackgroundProperty, Palette.Surface2Brush),
            (ContentPresenter.BorderBrushProperty, Brushes.Transparent)),

        Part<Button, ContentPresenter>("PART_ContentPresenter", ":pressed", "winbtn",
            (ContentPresenter.BackgroundProperty, Palette.Surface3Brush),
            (ContentPresenter.BorderBrushProperty, Brushes.Transparent)),

        // El de cerrar, detrás del anterior para poder pisarle el rojo. No sale de la paleta: es el
        // mismo en claro y en oscuro, porque lo que dice no depende del gusto de nadie.
        Part<Button, ContentPresenter>("PART_ContentPresenter", ":pointerover", "winbtn-close",
            (ContentPresenter.BackgroundProperty, CloseHover),
            (ContentPresenter.BorderBrushProperty, Brushes.Transparent)),

        Part<Button, ContentPresenter>("PART_ContentPresenter", ":pressed", "winbtn-close",
            (ContentPresenter.BackgroundProperty, CloseHover),
            (ContentPresenter.BorderBrushProperty, Brushes.Transparent)),

        new Style(x => x.OfType<Button>().Class("icon"))
        {
            Setters =
            {
                new Setter(Layoutable.WidthProperty, 36.0),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
            },
        },
    ];

    // ── casillas y desplegables ───────────────────────────────────────────────

    private static Styles Inputs() =>
    [
        new Style(x => x.OfType<TextBox>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.CornerRadiusProperty, Palette.ControlRadius),
                new Setter(TemplatedControl.FontSizeProperty, 14.0),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(12, 0)),
                new Setter(Layoutable.MinHeightProperty, 36.0),
                new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center),
            },
        },

        // El borde y la letra los pone Fluent con las claves de FluentTokens. Lo de aquí es la forma,
        // el movimiento y **una cosa que parece color y no lo es**: el fondo se reenvía al del propio
        // control, que es lo que la plantilla ya quería decir con su TemplateBinding y que Fluent
        // pisa con sus estados. Sigue habiendo una sola fuente —la clave— y este reenvío la respeta:
        // un cuadro suelto saca de ahí su Surface2, y el del contador saca el transparente que su
        // plantilla le escribe.
        //
        // Hace falta porque esa plantilla además lo saca un píxel por los cuatro lados con un margen
        // de -1, justo encima del borde del control: con fondo macizo se lo come, y sin fondo propio
        // no. Comprobado las dos veces midiendo el borde píxel a píxel.
        PartIdle<TextBox, Border>("PART_BorderElement",
            (Border.BackgroundProperty, FromControl("Background")),
            (Border.CornerRadiusProperty, Palette.ControlRadius),
            (Visual.TransitionsProperty, Hover())),

        Part<TextBox, Border>("PART_BorderElement", ":pointerover",
            (Border.BackgroundProperty, FromControl("Background"))),

        Part<TextBox, Border>("PART_BorderElement", ":focus",
            (Border.BackgroundProperty, FromControl("Background"))),

        // Y apagado, que faltaba. Ahí Fluent le pone su fondo de lo apagado, macizo, y el cuadro del
        // contador se comía el marco en todo el tramo del número: con «Cambiar de color según el
        // valor» sin marcar, los dos cortes salían sin borde y las flechas con él. A un cuadro suelto
        // no le cambia nada, porque su fondo y el de lo apagado son el mismo Surface2.
        Part<TextBox, Border>("PART_BorderElement", ":disabled",
            (Border.BackgroundProperty, FromControl("Background"))),

        // El número se escribe, y la caja además se arrastra en el lienzo: el contador va estrecho y
        // centrado, como en el diseño.
        //
        // El color se le pone **al control de fuera** y no a la caja de dentro. Su plantilla baja el
        // fondo, el borde y el redondeo al ButtonSpinner y de ahí al borde que de verdad se pinta, y
        // aquí arriba no hay ninguna plantilla ajena con más prioridad que un estilo de la
        // aplicación. Por el mismo camino llegan al cuadro de texto de dentro, que Fluent monta con
        // el fondo y el borde transparentes: hasta ahora el fondo y el borde del contador los pintaba
        // él, y el número salía de su paleta y no de esta.
        new Style(x => x.OfType<NumericUpDown>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.CornerRadiusProperty, Palette.ControlRadius),
                new Setter(TemplatedControl.FontSizeProperty, 14.0),
                new Setter(Layoutable.MinHeightProperty, 36.0),
                new Setter(Visual.TransitionsProperty, Hover()),

                // El número centrado en alto, como en cualquier cuadro. La plantilla le pasa al
                // cuadro de dentro la alineación del contador, y Fluent la deja estirada: el número
                // se pintaba arriba, pegado a los 5 px de su relleno, tres o cuatro por encima del
                // centro. Y el relleno, el de .input: nada arriba ni abajo, que con los 5 y 6 de
                // Fluent el centro tampoco caía en su sitio, y 12 a los lados, como el texto de un
                // cuadro, para que en una columna de campos el número empiece donde empieza el texto.
                new Setter(NumericUpDown.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(12, 0)),
            },
        },

        // Estos dos sí se quedan: el contador no tiene ni un estado propio en el tema de Fluent
        // —comprobado—, así que su borde al apuntarlo y al enfocarlo no lo pinta nadie más.
        new Style(x => x.OfType<NumericUpDown>().Class(":pointerover"))
        {
            Setters = { new Setter(TemplatedControl.BorderBrushProperty, Palette.LineHoverBrush) },
        },

        // Con «el foco dentro» y no con «el foco»: quien lo recibe es el cuadro de texto de dentro,
        // así que el control de fuera no está enfocado nunca y el aro de realce no llegaría a salir.
        new Style(x => x.OfType<NumericUpDown>().Class(":focus-within"))
        {
            Setters = { new Setter(TemplatedControl.BorderBrushProperty, Palette.AccentBrush) },
        },

        // Y la caja de dentro sin borde, que es lo que la plantilla de Fluent quiere decir al
        // dejárselo transparente: el borde lo pinta el spinner. Sin esto queda una raya de más entre
        // el número y los botones, porque el borde del cuadro cae justo encima del del spinner. Va
        // después del estilo del cuadro de texto a propósito: los dos valen lo mismo, y con eso gana
        // el último.
        //
        // Al enfocarlo, Fluent le devuelve el grosor desde su propio estado -una pseudoclase pesa
        // más que esto- y la raya vuelve, ya con el realce. Se queda así: marca el cuadro donde se
        // escribe dentro del control enfocado.
        //
        // El margen de -1 de la plantilla se queda: probado quitarlo y es peor, porque entonces el
        // borde del cuadro deja de caer encima del del spinner y al enfocar se ven los dos.
        Part<NumericUpDown, TextBox>("PART_TextBox", null,
            (TemplatedControl.BorderThicknessProperty, new Thickness(0))),

        // Las dos flechas, que salían del color del texto porque lo heredan del contador. Van en un
        // estilo suelto y no con el ayudante de piezas porque en la plantilla no tienen nombre; con
        // un solo salto se llega, que es lo que hace falta. El fondo que se les pone al posarse
        // encima no está aquí: ese vive dos plantillas más adentro y se pone por recurso, en
        // Resources().
        new Style(x => x.OfType<ButtonSpinner>().Template().OfType<PathIcon>())
        {
            Setters = { new Setter(TemplatedControl.ForegroundProperty, Palette.MutedBrush) },
        },

        // Y lo de dentro, recortado a la curva. El hueco donde va el contenido es un rectángulo de
        // esquinas rectas, así que el redondeo del marco le entra por dentro y lo que se pinte ahí
        // se sale por las dos esquinas de la derecha. Un Border de Avalonia recorta con su propio
        // redondeo —declara ClipToBoundsRadius—, de modo que basta con decírselo.
        new Style(x => x.OfType<ButtonSpinner>().Template().OfType<Border>())
        {
            Setters = { new Setter(Visual.ClipToBoundsProperty, true) },
        },

        // Pero el recorte solo arregla la forma, no el tapado: recorta por el canto de fuera, así
        // que un botón macizo sigue pintando encima de la raya del borde en todo el tramo curvo.
        // **Comprobado píxel a píxel**: con el botón relleno, la esquina de la derecha salía sin
        // raya, y la de la izquierda —donde no hay botón— salía entera.
        //
        // Así que en reposo no pintan nada y se ve el marco del contador por debajo, que es de lo
        // que va todo esto. Su raya separadora no depende de esto: esa la pinta su propio borde.
        //
        // Con el ayudante del reposo y no con un estilo a secas, porque su fondo se lo pasa la
        // plantilla del spinner y eso le gana a un estilo de la aplicación: comprobado en marcha
        // pintándolos de un color chillón, que sin la pseudoclase no salía. Y encaja con lo otro que
        // hace falta: apagados no entra aquí, y de su fondo se encarga el recurso de Resources().
        //
        // Aquí sí hay un transparente pegado a un color macizo —el de posarse encima—, que es lo que
        // Hover() prohíbe. Vale porque **estos botones no se animan**: el tema de Fluent no les pone
        // transición y la nuestra no les llega, porque va por tipo y un RepeatButton no es un Button.
        // Si algún día se les pusiera una, habría que volver aquí.
        PartIdle<ButtonSpinner, RepeatButton>("PART_IncreaseButton",
            (TemplatedControl.BackgroundProperty, Brushes.Transparent)),

        PartIdle<ButtonSpinner, RepeatButton>("PART_DecreaseButton",
            (TemplatedControl.BackgroundProperty, Brushes.Transparent)),

        // El que no se puede pulsar —el de bajar con el valor ya en su mínimo— se apaga en vez de
        // cambiar de color, que es lo que hace el resto de la ventana con lo que no se deja pulsar.
        // Su fondo lo quita el recurso de Resources(); esto es lo que apaga el dibujo.
        new Style(x => x.OfType<ButtonSpinner>().Template().OfType<RepeatButton>().Class(":disabled"))
        {
            Setters = { new Setter(Visual.OpacityProperty, 0.45) },
        },

        new Style(x => x.OfType<ComboBox>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.CornerRadiusProperty, Palette.ControlRadius),
                new Setter(TemplatedControl.FontSizeProperty, 14.0),
                // Solo a la izquierda. A la derecha no hace falta hueco para la flecha: Fluent la pone
                // en su propia columna de la rejilla, así que reservarlo aquí se lo quita dos veces al
                // texto. Estuvo escrito como «12 y 0», que en Avalonia es 12 a cada lado y no 12 a la
                // izquierda: el texto se cortaba 12 px antes de llegar a la flecha.
                new Setter(TemplatedControl.PaddingProperty, new Thickness(12, 0, 0, 0)),
                new Setter(Layoutable.MinHeightProperty, 36.0),
                new Setter(ComboBox.VerticalContentAlignmentProperty, VerticalAlignment.Center),
            },
        },

        // El pequeño, el del zoom de la barra del editor. Fluent trae 32 de alto, pero lo pone con un
        // setter de su ControlTheme —un estilo de la aplicación le gana— y su plantilla no fija alto
        // por dentro, así que con esto basta para llegar a los 30 del diseño.
        //
        // El relleno, solo a la izquierda: los 30 de la derecha de .select-sm son el sitio de la
        // flecha, y Fluent ya se lo da en su propia columna. Con 10 a cada lado, que es lo que estaba
        // escrito, al texto le quedaban 18 px y «1.5x» perdía la «x».
        new Style(x => x.OfType<ComboBox>().Class("sm"))
        {
            Setters =
            {
                new Setter(Layoutable.MinHeightProperty, 30.0),
                new Setter(TemplatedControl.FontSizeProperty, 13.0),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(10, 0, 0, 0)),
            },
        },

        Part<ComboBox, Border>("Background", null,
            (Border.CornerRadiusProperty, Palette.ControlRadius),
            (Visual.TransitionsProperty, Hover())),

        // Fluent pone un segundo borde encima para el resaltado y una pastilla detrás de la flecha.
        // Los dos sobran —el borde ya lo dice— y no tienen clave que apagar, así que se apagan aquí.
        Part<ComboBox, Border>("HighlightBackground", null,
            (Border.BackgroundProperty, Brushes.Transparent),
            (Border.BorderBrushProperty, Brushes.Transparent)),

        Part<ComboBox, Border>("DropDownOverlay", null,
            (Border.BackgroundProperty, Brushes.Transparent)),

        new Style(x => x.OfType<ComboBoxItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(6)),
                new Setter(TemplatedControl.FontSizeProperty, 14.0),
                new Setter(Layoutable.MinHeightProperty, 32.0),
            },
        },

        Part<ListBoxItem, ContentPresenter>("PART_ContentPresenter", ":selected",
            (ContentPresenter.BackgroundProperty, Palette.AccentMediumBrush)),

        Part<ListBoxItem, ContentPresenter>("PART_ContentPresenter", ":pointerover",
            (ContentPresenter.BackgroundProperty, Palette.Surface2Brush)),

        new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(7)),
                new Setter(TemplatedControl.FontSizeProperty, 13.0),
                new Setter(Layoutable.MinHeightProperty, 34.0),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(10, 0)),
            },
        },

        // Las dos columnas de una fila de la lista de widgets, que cambian de color con la fila: el
        // identificador en gris que se aclara al apuntarla o elegirla, y el tipo en el realce cuando
        // está elegida. Por clase y con el estado de la fila delante, porque la plantilla de la fila
        // no sabe si está elegida. Y sin color propio en la plantilla: uno puesto ahí ganaría a esto.
        new Style(x => x.OfType<TextBlock>().Class("witem-id"))
        {
            Setters = { new Setter(TextBlock.ForegroundProperty, Palette.MutedBrush) },
        },

        new Style(x => x.OfType<ListBoxItem>().Class(":pointerover").Descendant().OfType<TextBlock>().Class("witem-id"))
        {
            Setters = { new Setter(TextBlock.ForegroundProperty, Palette.TextBrush) },
        },

        new Style(x => x.OfType<ListBoxItem>().Class(":selected").Descendant().OfType<TextBlock>().Class("witem-id"))
        {
            Setters = { new Setter(TextBlock.ForegroundProperty, Palette.TextBrush) },
        },

        new Style(x => x.OfType<TextBlock>().Class("witem-kind"))
        {
            Setters = { new Setter(TextBlock.ForegroundProperty, Palette.FaintBrush) },
        },

        new Style(x => x.OfType<ListBoxItem>().Class(":selected").Descendant().OfType<TextBlock>().Class("witem-kind"))
        {
            Setters = { new Setter(TextBlock.ForegroundProperty, Palette.AccentBrush) },
        },
    ];

    // ── lo que se enciende y se apaga ─────────────────────────────────────────

    private static Styles Toggles() =>
    [
        new Style(x => x.OfType<CheckBox>())
        {
            Setters =
            {
                new Setter(TemplatedControl.FontSizeProperty, 14.0),
                new Setter(Layoutable.MinHeightProperty, 0.0),

                // El redondeo se le pone **al control** y no a la caja: la plantilla se lo baja con
                // un TemplateBinding, y eso pesa más que un estilo de la aplicación. Puesto en la
                // caja no llegaba, y salía con los 3 de Fluent en vez de con los 5 del diseño.
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(5)),
            },
        },

        // La caja: 18 px y esquinas de 5, que es lo que la distingue de un botón pequeño. Solo la
        // forma; el color de la caja, de su borde y de la marca sale de las claves CheckBox* de
        // FluentTokens, que llevan los seis estados —marcada, sin marcar, indeterminada, y las tres
        // con el ratón encima, pulsada o apagada—. Enumerar eso a mano aquí eran doce estilos.
        // El tamaño **sí** va aquí, y por PartIdle: la plantilla de Fluent le escribe un 20 a pelo, y
        // un valor escrito en una plantilla pesa más que un estilo de la aplicación. Con la
        // pseudoclase delante se le gana, que es para lo que existe ese ayudante. Lo dijo el
        // verificador del tema midiendo la casilla: 20 px y esquinas de 3 donde el diseño pide 18 y 5.
        PartIdle<CheckBox, Border>("NormalRectangle",
            (Border.BorderThicknessProperty, new Thickness(1)),
            (Layoutable.WidthProperty, 18.0),
            (Layoutable.HeightProperty, 18.0),
            (Visual.TransitionsProperty, Hover())),
    ];

    // ── barras ────────────────────────────────────────────────────────────────

    private static Styles Bars() =>
    [
        new Style(x => x.OfType<ProgressBar>())
        {
            Setters =
            {
                new Setter(TemplatedControl.ForegroundProperty, Palette.AccentBrush),
                new Setter(TemplatedControl.BackgroundProperty, Palette.Surface3Brush),
                new Setter(Layoutable.MinHeightProperty, 8.0),
            },
        },

        Part<ProgressBar, Border>("ProgressBarRoot", null,
            (Border.BackgroundProperty, Palette.Surface3Brush),
            (Border.BorderThicknessProperty, new Thickness(0)),
            (Border.CornerRadiusProperty, Palette.PillRadius)),

        Part<ProgressBar, Border>("PART_Indicator", null,
            (Border.CornerRadiusProperty, Palette.PillRadius)),

        // El deslizador y el interruptor no están aquí, y no por olvido: comprobado en marcha que
        // sus colores no se dejan cambiar desde fuera. Fluent los fija en el ControlTheme de cada
        // uno, que se aplica con prioridad de plantilla y gana a cualquier estilo de la aplicación
        // —al contrario que el botón, la casilla, el cuadro de texto o el desplegable, donde lo
        // mismo sí funciona—. Los dos salen en un solo sitio, el brillo y el encendido de la pestaña
        // Panel, y el diseño los quiere con otra forma además de otro color, así que se rehacen ahí
        // como controles propios en vez de pelearse con los recursos del tema.

        // La barra de desplazamiento del diseño es una raya fina sin botones en los extremos: los
        // dos triangulitos de Fluent no se usan nunca y roban alto a la pista.
        new Style(x => x.OfType<ScrollBar>().Template().OfType<Rectangle>().Name("TrackRect"))
        {
            Setters =
            {
                new Setter(Shape.FillProperty, Brushes.Transparent),
                new Setter(Shape.StrokeProperty, Brushes.Transparent),
            },
        },

        new Style(x => x.OfType<ScrollBar>().Template().OfType<RepeatButton>().Name("PART_LineUpButton"))
        {
            Setters = { new Setter(Visual.IsVisibleProperty, false) },
        },

        new Style(x => x.OfType<ScrollBar>().Template().OfType<RepeatButton>().Name("PART_LineDownButton"))
        {
            Setters = { new Setter(Visual.IsVisibleProperty, false) },
        },

        // Un solo salto hasta el pulgar y no dos hasta su borde: la pieza de dentro de la plantilla
        // de otro control no se alcanza desde aquí. El pulgar enlaza su fondo con el propio control,
        // así que basta con pintárselo a él.
        new Style(x => x.OfType<ScrollBar>().Template().OfType<Thumb>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, Palette.ScrollBrush),
                new Setter(TemplatedControl.CornerRadiusProperty, Palette.PillRadius),
                new Setter(Visual.TransitionsProperty, Hover()),
            },
        },

        // El reposo del pulgar sí se queda aquí: de toda la familia ScrollBar* la única que no
        // existe es la del pulgar en reposo —comprobado preguntándole a Fluent—, porque su plantilla
        // lo enlaza con el Background del control. Los otros dos estados sí van por clave.
        new Style(x => x.OfType<ScrollBar>().Class(":pointerover").Template().OfType<Thumb>())
        {
            Setters = { new Setter(TemplatedControl.BackgroundProperty, Palette.ScrollHoverBrush) },
        },

    ];

    // ── secciones y flotantes ─────────────────────────────────────────────────

    private static Styles Panels() =>
    [
        // **La caja de una sección no la pinta una sola pieza.** La plantilla de Fluent no tiene un
        // borde que rodee al Expander entero: su raíz es un DockPanel y debajo cuelgan dos hermanos,
        // la cabecera y el cuerpo. Lo que se ve como una caja son esas dos mitades con el mismo
        // fondo y el mismo borde, y de ahí sale sola la raya bajo el título que pide el diseño:
        // cerrada, la cabecera redondea por los cuatro lados; abierta, Fluent le quita al cuerpo el
        // borde de arriba —se queda en 1,0,1,1— y el de abajo de la cabecera hace de separador.
        //
        // Por eso el borde de la cabecera **tiene que verse**. Con su clave en transparente, que es
        // como estaba, al desplegar la sección desaparecían el borde de arriba y los dos costados de
        // la cabecera, y la caja se quedaba abierta por la parte de arriba. Comprobado volcando el
        // árbol con sus valores, que es lo que dijo que el borde no estaba oculto: no se pintaba.
        //
        // Aquí solo va la forma. El fondo y el borde salen de las claves Expander* de FluentTokens
        // —ojo si se vuelve aquí: casi todas quieren un Color y no un pincel, y no se distingue por
        // el nombre—, y el redondeo se reparte entre las dos mitades con los conversores de Fluent.
        new Style(x => x.OfType<Expander>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(9)),

                // El alto de la cabecera, que Fluent deja en 48 con su ExpanderMinHeight. Se le pone
                // al Expander porque su plantilla se lo pasa al botón de la cabecera.
                new Setter(Layoutable.MinHeightProperty, 36.0),
            },
        },

        // El hueco de la cabecera. Fluent lo deja en 16 por la izquierda; el diseño pide 12.
        //
        // Un solo salto de plantilla y llega, porque el botón de la cabecera es pieza del Expander.
        // Dentro de **su** plantilla ya no: el borde que pinta su fondo cuelga del botón y no del
        // Expander, así que un Part<Expander, Border>("ToggleButtonBackground", ...) no casa con
        // nada. Estuvo escrito y no hacía nada; de ahí que la cabecera siguiera midiendo 48.
        Part<Expander, ToggleButton>("ExpanderHeader", null,
            (TemplatedControl.PaddingProperty, new Thickness(12, 0, 0, 0))),

        // Y el cuerpo recortado a su propia curva, que es el overflow:hidden del diseño: el hueco
        // donde va el contenido es un rectángulo de esquinas rectas, de modo que el redondeo del
        // marco le entra por dentro y lo que se pinte ahí se sale por las dos esquinas de abajo. Un
        // Border de Avalonia recorta con su propio redondeo -declara ClipToBoundsRadius-, así que
        // basta con decírselo. Es el mismo arreglo que el del contador con sus flechas.
        Part<Expander, Border>("ExpanderContent", null,
            (Visual.ClipToBoundsProperty, true)),

        new Style(x => x.OfType<ToolTip>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(10)),
                new Setter(TemplatedControl.FontSizeProperty, 13.0),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(10, 6)),
            },
        },
    ];

    // ── la decoración que dibuja Avalonia ─────────────────────────────────────

    /// <summary>
    /// Viste la capa de decoración que Avalonia pinta por encima de la ventana.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Con el área de cliente extendida, Avalonia 12 dibuja su propia barra en una capa aparte —el
    /// título y cuatro botones— <b>encima</b> de lo que pinte la aplicación. Esa capa no se puede
    /// quitar desde fuera: vive en el anfitrión del <c>TopLevel</c>, que es interno.
    /// </para>
    /// <para>
    /// Y tampoco interesa quitarla. Sus botones no son un dibujo que se parece a los de Windows:
    /// llevan el rol de cada uno, así que el sistema los reconoce y de ahí salen los Snap Layouts
    /// al posarse en el de maximizar, que a mano no hay forma de imitar. Lo que sobra es el título
    /// —la barra de la aplicación ya lleva la marca, y se veían los dos encimados— y el botón de
    /// pantalla completa, que en esta ventana no lleva a ninguna parte.
    /// </para>
    /// </remarks>
    private static Styles WindowChrome() =>
    [
        // Con opacidad y no con IsVisible: la plantilla de la decoración fija la visibilidad de este
        // panel según el estado de la ventana, y eso gana a un estilo de la aplicación. La opacidad
        // no la toca nadie.
        //
        // Y con IsHitTestVisible en falso, que es lo que no se ve venir: **la opacidad no quita el
        // impacto del ratón**. Este panel ocupa todo el ancho de la barra, así que invisible pero
        // aún «tocable» se comía los clics de la barra propia que hay debajo —ni arrastre, ni doble
        // clic para maximizar, ni el conmutador de tema—.
        new Style(x => x.OfType<Panel>().Name("PART_TitleTextPanel"))
        {
            Setters =
            {
                new Setter(Visual.OpacityProperty, 0.0),
                new Setter(InputElement.IsHitTestVisibleProperty, false),
            },
        },

        new Style(x => x.OfType<Button>().Name("PART_FullScreenButton"))
        {
            Setters = { new Setter(Visual.IsVisibleProperty, false) },
        },

        .. CaptionButton("PART_MinimizeButton"),
        .. CaptionButton("PART_MaximizeButton"),
        .. CaptionButton("PART_CloseButton", close: true),
    ];

    private static Styles CaptionButton(string name, bool close = false) =>
    [
        new Style(x => x.OfType<Button>().Name(name))
        {
            Setters =
            {
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(0)),
                new Setter(TemplatedControl.ForegroundProperty, Palette.MutedBrush),
                new Setter(Layoutable.WidthProperty, 46.0),
                new Setter(Layoutable.HeightProperty, 40.0),
            },
        },

        // Estos tres no se animan, y es lo único que se puede hacer con ellos.
        //
        // Su reposo no se deja cambiar: la plantilla de Fluent le escribe al presentador un
        // Background="Transparent" **literal**, y un literal de plantilla sí le gana a un estilo de
        // la aplicación —al contrario que un TemplateBinding, que es lo que llevan los botones
        // normales y por lo que a ellos sí se les puede poner el color—. El estado de
        // posarse encima, en cambio, sí se cambia, porque una pseudoclase pesa más. Entre los dos
        // queda justo el par que destella —mírese Hover()— y no hay forma de cerrarlo por el lado
        // del reposo sin rehacerles la plantilla entera.
        //
        // Así que se les quita la transición y el color entra de golpe, que además es lo que hacen
        // los botones de la barra de título de Windows.
        Named<Button, ContentPresenter>(name, "PART_ContentPresenter",
            (ContentPresenter.CornerRadiusProperty, new CornerRadius(0)),
            (Visual.TransitionsProperty, new Transitions())),

        new Style(x => x.OfType<Button>().Name(name).Class(":pointerover")
            .Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters =
            {
                new Setter(ContentPresenter.BackgroundProperty, close ? CloseHover : Palette.Surface2Brush),
                new Setter(ContentPresenter.BorderBrushProperty, Brushes.Transparent),
            },
        },

        // Y el de pulsar, que faltaba: sin él, al pulsarlos entra el blanco translúcido de Fluent y
        // el par vuelve a quedar abierto por ese lado.
        new Style(x => x.OfType<Button>().Name(name).Class(":pressed")
            .Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters =
            {
                new Setter(ContentPresenter.BackgroundProperty, close ? CloseHover : Palette.Surface3Brush),
                new Setter(ContentPresenter.BorderBrushProperty, Brushes.Transparent),
            },
        },
    ];

    /// <summary>Lo que valga esa propiedad en el control del que esta pieza es la plantilla.</summary>
    /// <remarks>
    /// Es el <c>TemplateBinding</c> de toda la vida, escrito en C#. Hace falta cuando una pieza tiene
    /// que seguir al control en vez de llevar un color fijo, y el estilo que se lo dice ha de ganarle
    /// a Fluent: mírese <see cref="PartIdle{TControl,TPart}"/>.
    /// </remarks>
    private static Binding FromControl(string property) =>
        new(property) { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) };

    /// <summary>
    /// El reposo de una pieza cuyo color fija Fluent desde dentro de su propio <c>ControlTheme</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Un estilo sin pseudoclase pierde contra eso; uno con pseudoclase gana. De ahí el
    /// <c>:not(:disabled)</c>, que además dice algo: vale mientras el control se pueda usar, que es
    /// justo cuando queremos nuestro color, y deja el gris de deshabilitado en manos de Fluent, que
    /// ya lo tiene resuelto.
    /// </para>
    /// <para>
    /// Sin esto, el reposo lo pintaba Fluent y el ratón encima lo pintábamos nosotros, y entre los
    /// dos quedaba el par que destella: mírese <see cref="Hover"/>. Comprobado en marcha midiendo el
    /// color del contador píxel a píxel —su cuadro salía casi negro en reposo y macizo al apuntarlo—.
    /// </para>
    /// </remarks>
    private static Style PartIdle<TControl, TPart>(
        string part, params (AvaloniaProperty Property, object? Value)[] setters)
        where TControl : Control
        where TPart : Control
    {
        var style = new Style(x => x.OfType<TControl>()
            .Not(y => y.Class(":disabled"))
            .Template().OfType<TPart>().Name(part));

        foreach ((var property, object? value) in setters) style.Setters.Add(new Setter(property, value));
        return style;
    }

    /// <summary>Un estilo para una pieza de dentro de la plantilla de un control con nombre.</summary>
    private static Style Named<TControl, TPart>(
        string control, string part, params (AvaloniaProperty Property, object? Value)[] setters)
        where TControl : Control
        where TPart : Control
    {
        var style = new Style(x => x.OfType<TControl>().Name(control).Template().OfType<TPart>().Name(part));
        foreach ((var property, object? value) in setters) style.Setters.Add(new Setter(property, value));
        return style;
    }

    /// <summary>
    /// Un estilo para una pieza con nombre de dentro de la plantilla de un control.
    /// </summary>
    /// <remarks>
    /// Existe porque el mismo patrón —entrar en la plantilla, buscar la pieza por su nombre y
    /// ponerle dos o tres colores— se repite cuarenta veces, y escrito a mano son cinco líneas cada
    /// una en las que lo único que cambia son los valores.
    /// </remarks>
    /// <param name="part">El nombre de la pieza dentro de la plantilla.</param>
    /// <param name="state">La pseudoclase del control, como <c>:pointerover</c>, o null para siempre.</param>
    private static Style Part<TControl, TPart>(
        string part, string? state, params (AvaloniaProperty Property, object? Value)[] setters)
        where TControl : Control
        where TPart : Control
        => Part<TControl, TPart>(part, state, null, setters);

    /// <param name="cls">Una clase de estilo del control, o null si vale para todos.</param>
    private static Style Part<TControl, TPart>(
        string part, string? state, string? cls, params (AvaloniaProperty Property, object? Value)[] setters)
        where TControl : Control
        where TPart : Control
    {
        var style = new Style(x =>
        {
            var selector = x.OfType<TControl>();
            if (cls is not null) selector = selector.Class(cls);
            if (state is not null) selector = selector.Class(state);
            return selector.Template().OfType<TPart>().Name(part);
        });

        foreach ((var property, object? value) in setters) style.Setters.Add(new Setter(property, value));
        return style;
    }
}
