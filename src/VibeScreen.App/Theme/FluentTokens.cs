using Avalonia;
using Avalonia.Media;

namespace VibeScreen.App.Theme;

/// <summary>
/// Los colores de los controles de serie, apuntados a la paleta.
/// </summary>
/// <remarks>
/// <para>
/// El tema Fluent no se sustituye: se le reviste. Eso quiere decir que debajo sigue habiendo una
/// tabla completa de colores por control y por estado —reposo, el ratón encima, pulsado, apagado, con
/// el foco, marcado—, y que **todo estado que no se vista se queda con el color de Fluent**. Que no
/// es un color cualquiera: en oscuro son blancos translúcidos sobre un gris medio, y esta ventana es
/// de grises azulados macizos sobre casi negro. Puestos uno al lado del otro, cantan.
/// </para>
/// <para>
/// <b>Esta es la forma barata de cubrirlos todos.</b> Cada color de Fluent sale de una clave con
/// nombre, y las claves son parte de su API. Apuntarlas a los pinceles de la paleta deja una sola
/// fuente para cada color —pinta Fluent, con el color de aquí— y de regalo cubre estados que nadie
/// ha enumerado: apagado, con el foco, indeterminado. Lo otro, un estilo por pieza, obliga a
/// adivinar nombres de piezas de plantilla y a acordarse de cada estado.
/// </para>
/// <para>
/// <b>Qué color va en cada estado lo dice el diseño</b>, no el gusto: <c>assets/app.css</c> del
/// export. Y lo que más sorprende de él es que **no define color de pulsado en ningún control** —el
/// único <c>:active</c> es el del botón, y solo lo hunde un píxel—, así que aquí todos los
/// <c>Pressed</c> repiten el estado anterior en vez de inventarse uno. Igual con el cuadro de texto y
/// el desplegable, que solo cambian de borde: el fondo es el mismo en los cuatro estados.
/// </para>
/// <para>
/// <b>Dos avisos para quien añada claves.</b> El primero: no todas quieren un <c>IBrush</c>; hay
/// diecisiete que quieren un <c>Color</c>, casi todas del Expander, y **no se distinguen por el
/// nombre** —<c>ExpanderHeaderBackgroundDisabled</c> es Color y <c>ExpanderHeaderBorderBrushDisabled</c>
/// es pincel—. Si se equivoca el tipo, el color no se aplica y no avisa nadie. Van por <see cref="Tint"/>.
/// El segundo: que una clave exista no se supone, se comprueba preguntándole a Fluent en marcha; las
/// 291 de esta tabla se verificaron así antes de escribirlas.
/// </para>
/// <para>
/// No hay nada del <c>ProgressBar</c> porque no tiene ni una clave de color: enlaza su pista y su
/// relleno con el <c>Background</c> y el <c>Foreground</c> del propio control, que ya se ponen en
/// <see cref="ControlThemes"/>. Ni del deslizador ni del interruptor, que no se usan: hay un
/// <see cref="Switch"/> y un <see cref="Range"/> propios.
/// </para>
/// </remarks>
public static class FluentTokens
{
    /// <summary>
    /// Vuelca la paleta sobre las claves del tema.
    /// </summary>
    /// <remarks>
    /// Se llama desde <c>Palette.Apply</c> y se vuelca entera cada vez. Con los pinceles bastaría una
    /// vez —son mutables y compartidos, así que el color viaja solo—, pero las claves que quieren un
    /// <c>Color</c> guardan un valor y no una referencia: esas hay que volver a escribirlas al cambiar
    /// de paleta. Repetirlo todo sale más barato de leer que recordar cuáles son cuáles.
    /// </remarks>
    public static void Paint()
    {
        if (Application.Current is not { } app) return;

        Buttons(app);
        Inputs(app);
        DropDown(app);
        Sections(app);
        Scrolling(app);
        Floating(app);
    }

    // ── botones ───────────────────────────────────────────────────────────────
    //
    // Las variantes del diseño —primario, borrar, los de la barra de título— van por clase, y una
    // clave es por tipo de control: eso se queda en ControlThemes. Aquí va el botón de serie.

    private static void Buttons(Application app)
    {
        Brush(app, Palette.Surface2Brush, "ButtonBackground", "ButtonBackgroundDisabled");
        Brush(app, Palette.Surface3Brush, "ButtonBackgroundPointerOver", "ButtonBackgroundPressed");
        Brush(app, Palette.LineStrongBrush, "ButtonBorderBrush", "ButtonBorderBrushDisabled");
        Brush(app, Palette.LineHoverBrush, "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed");
        Brush(app, Palette.TextBrush, "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed");
        Brush(app, Palette.MutedBrush, "ButtonForegroundDisabled");

        // Los dos del contador no pintan en reposo a propósito: el hueco donde van tiene las esquinas
        // redondeadas del control que los contiene, y un botón macizo se come esa curva. Mírese el
        // recorte del ButtonSpinner en ControlThemes.
        Brush(app, Brushes.Transparent, "RepeatButtonBackground", "RepeatButtonBackgroundDisabled");
        Brush(app, Palette.Surface3Brush, "RepeatButtonBackgroundPointerOver", "RepeatButtonBackgroundPressed");
        Brush(app, Brushes.Transparent,
            "RepeatButtonBorderBrush", "RepeatButtonBorderBrushPointerOver",
            "RepeatButtonBorderBrushPressed", "RepeatButtonBorderBrushDisabled");
        Brush(app, Palette.MutedBrush,
            "RepeatButtonForeground", "RepeatButtonForegroundPointerOver",
            "RepeatButtonForegroundPressed", "RepeatButtonForegroundDisabled");
    }

    // ── lo que se escribe y lo que se marca ───────────────────────────────────

    private static void Inputs(Application app)
    {
        // El fondo es el mismo en los cuatro estados: el diseño solo mueve el borde. Y además es lo
        // que cierra el destello por este lado, porque con un solo valor no hay nada que interpolar.
        Brush(app, Palette.Surface2Brush,
            "TextControlBackground", "TextControlBackgroundPointerOver",
            "TextControlBackgroundFocused", "TextControlBackgroundDisabled");
        Brush(app, Palette.LineStrongBrush, "TextControlBorderBrush", "TextControlBorderBrushDisabled");
        Brush(app, Palette.LineHoverBrush, "TextControlBorderBrushPointerOver");
        Brush(app, Palette.AccentBrush, "TextControlBorderBrushFocused");
        Brush(app, Palette.TextBrush,
            "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused");
        Brush(app, Palette.MutedBrush, "TextControlForegroundDisabled");
        Brush(app, Palette.FaintBrush,
            "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver",
            "TextControlPlaceholderForegroundFocused", "TextControlPlaceholderForegroundDisabled");
        Tint(app, Palette.SelectionBrush.Color, "TextControlSelectionHighlightColor");

        // El botoncito de limpiar que Fluent mete dentro del cuadro. Sin esto sale el azul de Windows.
        Brush(app, Brushes.Transparent,
            "TextControlButtonBackground", "TextControlButtonBorderBrush",
            "TextControlButtonBorderBrushPointerOver", "TextControlButtonBorderBrushPressed");
        Brush(app, Palette.Surface3Brush, "TextControlButtonBackgroundPointerOver", "TextControlButtonBackgroundPressed");
        Brush(app, Palette.MutedBrush, "TextControlButtonForeground");
        Brush(app, Palette.TextBrush, "TextControlButtonForegroundPointerOver", "TextControlButtonForegroundPressed");

        // La casilla. El control de fuera no pinta nada: lo que se ve es la caja, y esa es la familia
        // «CheckBackgroundFill». Su borde es «CheckBackgroundStroke» y la marca, «CheckGlyphForeground».
        Brush(app, Brushes.Transparent,
            "CheckBoxBackgroundUnchecked", "CheckBoxBackgroundUncheckedPointerOver",
            "CheckBoxBackgroundUncheckedPressed", "CheckBoxBackgroundUncheckedDisabled",
            "CheckBoxBackgroundChecked", "CheckBoxBackgroundCheckedPointerOver",
            "CheckBoxBackgroundCheckedPressed", "CheckBoxBackgroundCheckedDisabled",
            "CheckBoxBackgroundIndeterminate", "CheckBoxBackgroundIndeterminatePointerOver",
            "CheckBoxBackgroundIndeterminatePressed", "CheckBoxBackgroundIndeterminateDisabled",
            "CheckBoxBorderBrushUnchecked", "CheckBoxBorderBrushUncheckedPointerOver",
            "CheckBoxBorderBrushUncheckedPressed", "CheckBoxBorderBrushUncheckedDisabled",
            "CheckBoxBorderBrushChecked", "CheckBoxBorderBrushCheckedPointerOver",
            "CheckBoxBorderBrushCheckedPressed", "CheckBoxBorderBrushCheckedDisabled",
            "CheckBoxBorderBrushIndeterminate", "CheckBoxBorderBrushIndeterminatePointerOver",
            "CheckBoxBorderBrushIndeterminatePressed", "CheckBoxBorderBrushIndeterminateDisabled");

        Brush(app, Palette.Surface2Brush,
            "CheckBoxCheckBackgroundFillUnchecked", "CheckBoxCheckBackgroundFillUncheckedPointerOver",
            "CheckBoxCheckBackgroundFillUncheckedPressed", "CheckBoxCheckBackgroundFillUncheckedDisabled");
        Brush(app, Palette.LineStrongBrush,
            "CheckBoxCheckBackgroundStrokeUnchecked", "CheckBoxCheckBackgroundStrokeUncheckedDisabled");
        Brush(app, Palette.LineHoverBrush,
            "CheckBoxCheckBackgroundStrokeUncheckedPointerOver", "CheckBoxCheckBackgroundStrokeUncheckedPressed");

        // Marcada e indeterminada van del mismo realce. La indeterminada no se usa, pero cubrirla
        // cuesta tres líneas y evita que asome el azul de Fluent el día que se use.
        Brush(app, Palette.AccentBrush,
            "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundFillCheckedPressed",
            "CheckBoxCheckBackgroundFillCheckedDisabled", "CheckBoxCheckBackgroundStrokeChecked",
            "CheckBoxCheckBackgroundStrokeCheckedPressed", "CheckBoxCheckBackgroundStrokeCheckedDisabled",
            "CheckBoxCheckBackgroundFillIndeterminate", "CheckBoxCheckBackgroundFillIndeterminatePressed",
            "CheckBoxCheckBackgroundFillIndeterminateDisabled", "CheckBoxCheckBackgroundStrokeIndeterminate",
            "CheckBoxCheckBackgroundStrokeIndeterminatePressed", "CheckBoxCheckBackgroundStrokeIndeterminateDisabled");
        Brush(app, Palette.AccentHoverBrush,
            "CheckBoxCheckBackgroundFillCheckedPointerOver", "CheckBoxCheckBackgroundStrokeCheckedPointerOver",
            "CheckBoxCheckBackgroundFillIndeterminatePointerOver", "CheckBoxCheckBackgroundStrokeIndeterminatePointerOver");

        // La marca, del color de «encima del realce». Sin esto sale clara y el diseño la quiere casi
        // negra en oscuro, que es lo que hace legible una casilla marcada.
        Brush(app, Palette.OnAccentBrush,
            "CheckBoxCheckGlyphForegroundChecked", "CheckBoxCheckGlyphForegroundCheckedPointerOver",
            "CheckBoxCheckGlyphForegroundCheckedPressed", "CheckBoxCheckGlyphForegroundCheckedDisabled",
            "CheckBoxCheckGlyphForegroundIndeterminate", "CheckBoxCheckGlyphForegroundIndeterminatePointerOver",
            "CheckBoxCheckGlyphForegroundIndeterminatePressed", "CheckBoxCheckGlyphForegroundIndeterminateDisabled");
        Brush(app, Brushes.Transparent,
            "CheckBoxCheckGlyphForegroundUnchecked", "CheckBoxCheckGlyphForegroundUncheckedPointerOver",
            "CheckBoxCheckGlyphForegroundUncheckedPressed", "CheckBoxCheckGlyphForegroundUncheckedDisabled");

        Brush(app, Palette.TextBrush,
            "CheckBoxForegroundUnchecked", "CheckBoxForegroundUncheckedPointerOver",
            "CheckBoxForegroundUncheckedPressed", "CheckBoxForegroundChecked",
            "CheckBoxForegroundCheckedPointerOver", "CheckBoxForegroundCheckedPressed",
            "CheckBoxForegroundIndeterminate", "CheckBoxForegroundIndeterminatePointerOver",
            "CheckBoxForegroundIndeterminatePressed");
        Brush(app, Palette.MutedBrush,
            "CheckBoxForegroundUncheckedDisabled", "CheckBoxForegroundCheckedDisabled",
            "CheckBoxForegroundIndeterminateDisabled");
    }

    // ── el desplegable, cerrado y abierto ─────────────────────────────────────

    private static void DropDown(Application app)
    {
        Brush(app, Palette.Surface2Brush,
            "ComboBoxBackground", "ComboBoxBackgroundPointerOver",
            "ComboBoxBackgroundPressed", "ComboBoxBackgroundDisabled", "ComboBoxBackgroundUnfocused");
        Brush(app, Palette.LineStrongBrush,
            "ComboBoxBorderBrush", "ComboBoxBorderBrushDisabled", "ComboBoxBackgroundBorderBrushUnfocused");
        Brush(app, Palette.LineHoverBrush, "ComboBoxBorderBrushPointerOver", "ComboBoxBorderBrushPressed");
        Brush(app, Palette.AccentBrush, "ComboBoxBackgroundBorderBrushFocused");
        Brush(app, Palette.TextBrush,
            "ComboBoxForeground", "ComboBoxForegroundFocused", "ComboBoxForegroundFocusedPressed");
        Brush(app, Palette.MutedBrush, "ComboBoxForegroundDisabled");
        Brush(app, Palette.MutedBrush,
            "ComboBoxDropDownGlyphForeground", "ComboBoxDropDownGlyphForegroundFocused",
            "ComboBoxDropDownGlyphForegroundFocusedPressed");
        Brush(app, Palette.FaintBrush, "ComboBoxDropDownGlyphForegroundDisabled");
        Brush(app, Palette.FaintBrush, "ComboBoxPlaceHolderForeground");

        // Lo que se despliega es un panel que flota: color de tarjeta y su borde, como todo lo demás
        // que se levanta sobre la ventana.
        Brush(app, Palette.SurfaceBrush, "ComboBoxDropDownBackground");
        Brush(app, Palette.LineStrongBrush, "ComboBoxDropDownBorderBrush", "ComboBoxDropdownBorderBrush");

        // Y sus opciones, como las filas de cualquier lista: nada en reposo, la superficie de encima
        // al apuntarlas y el realce rebajado en la elegida.
        Brush(app, Brushes.Transparent,
            "ComboBoxItemBackground", "ComboBoxItemBackgroundDisabled",
            "ComboBoxItemBorderBrushPointerOver", "ComboBoxItemBorderBrushPressed",
            "ComboBoxItemBorderBrushDisabled", "ComboBoxItemBorderBrushSelected",
            "ComboBoxItemBorderBrushSelectedPointerOver", "ComboBoxItemBorderBrushSelectedPressed",
            "ComboBoxItemBorderBrushSelectedDisabled");
        Brush(app, Palette.Surface2Brush, "ComboBoxItemBackgroundPointerOver", "ComboBoxItemBackgroundPressed");
        Brush(app, Palette.AccentMediumBrush,
            "ComboBoxItemBackgroundSelected", "ComboBoxItemBackgroundSelectedPointerOver",
            "ComboBoxItemBackgroundSelectedPressed", "ComboBoxItemBackgroundSelectedDisabled");
        Brush(app, Palette.TextBrush,
            "ComboBoxItemForeground", "ComboBoxItemForegroundPointerOver", "ComboBoxItemForegroundPressed",
            "ComboBoxItemForegroundSelected", "ComboBoxItemForegroundSelectedPointerOver",
            "ComboBoxItemForegroundSelectedPressed");
        Brush(app, Palette.MutedBrush, "ComboBoxItemForegroundDisabled", "ComboBoxItemForegroundSelectedDisabled");

        // El buscador de sensores despliega su propia lista.
        Brush(app, Palette.SurfaceBrush, "AutoCompleteBoxSuggestionsListBackground");
        Brush(app, Palette.LineStrongBrush, "AutoCompleteBoxSuggestionsListBorderBrush");
    }

    // ── secciones plegables ───────────────────────────────────────────────────
    //
    // Aquí está la trampa de los tipos: casi todas estas quieren un Color y no un pincel, y las que
    // no, no se distinguen por el nombre. Las que van por Tint son las que la sonda dijo.

    private static void Sections(Application app)
    {
        // Un escalón por encima de lo que hay debajo, que es lo que hace que la sección se vea
        // encajada y no pegada: el inspector va de superficie a medias y la sección, maciza y un
        // tono más clara. Pintarla del mismo color que la columna era lo que la dejaba plana.
        Tint(app, Palette.Surface2Brush.Color,
            "ExpanderHeaderBackground", "ExpanderHeaderBackgroundDisabled", "ExpanderContentBackground");
        Tint(app, Palette.Surface3Brush.Color, "ExpanderHeaderBackgroundPointerOver", "ExpanderHeaderBackgroundPressed");

        // El borde de la cabecera es **medio marco de la caja**, no un adorno del botón: mírese en
        // ControlThemes por qué la sección se queda sin la parte de arriba si esto va transparente.
        Tint(app, Palette.Line,
            "ExpanderHeaderBorderBrush", "ExpanderHeaderBorderBrushPointerOver",
            "ExpanderHeaderBorderBrushPressed", "ExpanderContentBorderBrush");
        Brush(app, Palette.LineBrush, "ExpanderHeaderBorderBrushDisabled");
        Tint(app, Palette.Text,
            "ExpanderHeaderForeground", "ExpanderHeaderForegroundPointerOver", "ExpanderHeaderForegroundPressed");
        Brush(app, Palette.MutedBrush, "ExpanderHeaderForegroundDisabled");

        Tint(app, Palette.FaintBrush.Color,
            "ExpanderChevronForeground", "ExpanderChevronForegroundPointerOver", "ExpanderChevronForegroundPressed");
        Brush(app, Palette.FaintBrush, "ExpanderChevronForegroundDisabled");
        Brush(app, Brushes.Transparent,
            "ExpanderChevronBackground", "ExpanderChevronBackgroundPointerOver",
            "ExpanderChevronBackgroundPressed", "ExpanderChevronBackgroundDisabled",
            "ExpanderChevronBorderBrush", "ExpanderChevronBorderBrushPointerOver",
            "ExpanderChevronBorderBrushPressed", "ExpanderChevronBorderBrushDisabled");
    }

    // ── la barra de desplazamiento ────────────────────────────────────────────

    private static void Scrolling(Application app)
    {
        Brush(app, Brushes.Transparent,
            "ScrollBarBackground", "ScrollBarBackgroundPointerOver", "ScrollBarBorderBrush",
            "ScrollBarTrackFill", "ScrollBarTrackFillPointerOver",
            "ScrollBarTrackStroke", "ScrollBarTrackStrokePointerOver",
            "ScrollBarButtonBackground", "ScrollBarButtonBackgroundPointerOver", "ScrollBarButtonBackgroundPressed",
            "ScrollBarButtonBorderBrush", "ScrollBarButtonBorderBrushPointerOver",
            "ScrollBarButtonBorderBrushPressed", "ScrollBarButtonBorderBrushDisabled");

        Brush(app, Palette.ScrollBrush, "ScrollBarForeground", "ScrollBarThumbFillDisabled");
        Brush(app, Palette.ScrollHoverBrush, "ScrollBarThumbFillPointerOver", "ScrollBarThumbFillPressed");
        Tint(app, Palette.ScrollBrush.Color, "ScrollBarThumbBackgroundColor", "ScrollBarPanningThumbBackgroundColor");

        Brush(app, Palette.MutedBrush, "ScrollBarButtonArrowForeground", "ScrollBarButtonArrowForegroundPointerOver");
        Brush(app, Palette.TextBrush, "ScrollBarButtonArrowForegroundPressed");
        Brush(app, Palette.FaintBrush, "ScrollBarButtonArrowForegroundDisabled");
    }

    // ── lo que flota por encima de la ventana ─────────────────────────────────

    private static void Floating(Application app)
    {
        Brush(app, Palette.Surface3Brush, "ToolTipBackground");
        Brush(app, Palette.LineStrongBrush, "ToolTipBorderBrush");
        Brush(app, Palette.TextBrush, "ToolTipForeground");

        // El menú contextual de los widgets del editor, que hasta ahora salía entero de Fluent.
        Brush(app, Palette.SurfaceBrush, "MenuFlyoutPresenterBackground");
        Brush(app, Palette.LineStrongBrush, "MenuFlyoutPresenterBorderBrush");
        Brush(app, Brushes.Transparent, "MenuFlyoutItemBackground", "MenuFlyoutItemBackgroundDisabled");
        Brush(app, Palette.Surface2Brush, "MenuFlyoutItemBackgroundPointerOver", "MenuFlyoutItemBackgroundPressed");
        Brush(app, Palette.TextBrush,
            "MenuFlyoutItemForeground", "MenuFlyoutItemForegroundPointerOver", "MenuFlyoutItemForegroundPressed");
        Brush(app, Palette.MutedBrush, "MenuFlyoutItemForegroundDisabled");
        Brush(app, Palette.FaintBrush,
            "MenuFlyoutItemKeyboardAcceleratorTextForeground",
            "MenuFlyoutItemKeyboardAcceleratorTextForegroundPointerOver",
            "MenuFlyoutItemKeyboardAcceleratorTextForegroundPressed",
            "MenuFlyoutItemKeyboardAcceleratorTextForegroundDisabled");
        Brush(app, Palette.MutedBrush,
            "MenuFlyoutSubItemChevron", "MenuFlyoutSubItemChevronPointerOver",
            "MenuFlyoutSubItemChevronPressed", "MenuFlyoutSubItemChevronSubMenuOpened");
        Brush(app, Palette.FaintBrush, "MenuFlyoutSubItemChevronDisabled");
    }

    /// <summary>Las claves que quieren un pincel, que son casi todas.</summary>
    private static void Brush(Application app, IBrush value, params string[] keys)
    {
        foreach (string key in keys) app.Resources[key] = value;
    }

    /// <summary>
    /// Las diecisiete que quieren un <see cref="Avalonia.Media.Color"/> suelto.
    /// </summary>
    /// <remarks>
    /// Escribirles un pincel no da error: simplemente no se aplican, y el control se queda con el
    /// color de Fluent como si nadie hubiera tocado nada. Por eso están separadas.
    /// </remarks>
    private static void Tint(Application app, Color value, params string[] keys)
    {
        foreach (string key in keys) app.Resources[key] = value;
    }
}
