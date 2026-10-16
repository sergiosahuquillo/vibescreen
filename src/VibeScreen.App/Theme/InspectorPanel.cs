using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using VibeScreen.App.Localization;
using Avalonia.Media.Transformation;

namespace VibeScreen.App.Theme;

/// <summary>
/// Una de las cajas de la columna del editor —Pantalla, Añadir, Widgets, Propiedades—: el
/// <c>.panel</c> del diseño.
/// </summary>
/// <remarks>
/// <para>
/// Hecha a mano, como <see cref="Switch"/> y <see cref="Segmented{T}"/>, y no un <see cref="Expander"/>
/// revestido. Los colores del Expander salen de claves de Fluent, y una clave es por tipo de control:
/// las secciones de dentro de Propiedades son Expanders con el otro aspecto del diseño —el de
/// <c>.prop-sub</c>, un escalón más claras y con la cabecera más baja—, así que esta caja tendría que
/// pisar esas claves ejemplar a ejemplar y volver a pisarlas con cada cambio de paleta, porque las del
/// Expander guardan un color y no un pincel. Aquí no hay plantilla ajena: los pinceles son los de
/// <see cref="Palette"/>, que cambian solos.
/// </para>
/// <para>
/// Fija o plegable. La fija lleva a la derecha del título una nota —cuántos widgets hay, cuál está
/// elegido—; la plegable lleva la flecha, se abre con un clic o con Espacio e Intro, y avisa al
/// abrirse con <see cref="OpeningEvent"/>, que es lo que escucha <c>Accordion</c> para dejar una sola
/// abierta en toda la columna.
/// </para>
/// <para>
/// Sus piezas llevan el nombre que les da el diseño —<c>panel-head</c>, <c>panel-body</c>,
/// <c>panel-note</c>— para que el verificador del tema las encuentre y las mida.
/// </para>
/// </remarks>
public sealed class InspectorPanel : Border
{
    /// <summary>El hueco del cuerpo de una caja, el de <c>.panel-body</c>.</summary>
    public static readonly Thickness BodyPadding = new(14, 4, 14, 16);

    /// <summary>
    /// El hueco entre las piezas de dentro de una caja: el <c>gap</c> de <c>.panel-body</c>. Lo
    /// aplica quien llena la caja, que es quien sabe cuáles son sus piezas.
    /// </summary>
    public const double BodySpacing = 14;

    /// <summary>Salta al abrirse una plegable, y sube por el árbol.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> OpeningEvent =
        RoutedEvent.Register<InspectorPanel, RoutedEventArgs>("Opening", RoutingStrategies.Bubble);

    private readonly Border _body;
    private readonly TextBlock _note;
    private readonly Viewbox? _chevron;
    private bool _expanded;

    /// <param name="title">El título, en minúsculas: se sube al pintarlo.</param>
    /// <param name="content">Lo de dentro.</param>
    /// <param name="padding">
    /// El hueco alrededor de lo de dentro: <see cref="BodyPadding"/> casi siempre, y ninguno para la
    /// lista de widgets, que en el diseño va pegada a la caja y lleva el suyo por dentro de lo que se
    /// desplaza.
    /// </param>
    /// <param name="collapsible">Si se pliega. Las fijas no tienen flecha ni cambian al apuntarlas.</param>
    /// <param name="open">Si una plegable nace abierta. Las fijas lo están siempre.</param>
    public InspectorPanel(Text title, Control content, Thickness padding, bool collapsible = false, bool open = false)
    {
        ArgumentNullException.ThrowIfNull(content);

        Collapsible = collapsible;
        _expanded = !collapsible || open;

        Background = Palette.SurfaceBrush;
        BorderBrush = Palette.LineBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);

        // El overflow:hidden del diseño: la cabecera, al apuntarla, pinta hasta la curva del marco.
        ClipToBounds = true;

        _note = AppFonts.Mono(new TextBlock
        {
            Name = "panel-note",
            FontSize = 12,
            Foreground = Palette.FaintBrush,
            MaxWidth = 190,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        });

        // El título entero y la nota con lo que quede, pegada a la derecha. Al revés —la nota primero—
        // un identificador largo se comía el título en la columna estrecha: en el diseño la que
        // encoge es la nota, con sus puntos suspensivos, y el título no baja nunca de lo que mide.
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        row.Children.Add(Blocks.SectionTitle(title));

        Control trailing = _note;
        if (collapsible)
        {
            // A 24 el trazo de la flecha mide los 12 px de ancho de la del diseño, que es un ángulo de
            // dos rayas de 9 girado: el dibujo ocupa la mitad del lienzo del icono.
            _chevron = Icons.Make(Icons.Chevron, Palette.FaintBrush, 24);
            _chevron.RenderTransform = Turn(_expanded);
            _chevron.Transitions =
            [
                new TransformOperationsTransition
                {
                    Property = RenderTransformProperty,
                    Duration = Palette.Quick,
                    Easing = Palette.Ease,
                },
            ];
            trailing = _chevron;
        }

        trailing.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(trailing, 1);
        row.Children.Add(trailing);

        var head = new Border
        {
            Name = "panel-head",
            MinHeight = 42,
            Padding = new Thickness(14, 0),
            Child = row,
        };
        if (collapsible) Foldable(head, title);

        _body = new Border { Name = "panel-body", Padding = padding, Child = content, IsVisible = _expanded };

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(_body, 1);
        layout.Children.Add(head);
        layout.Children.Add(_body);
        Child = layout;
    }

    /// <summary>Si se pliega. Las fijas no las cierra nadie, tampoco <c>Accordion</c>.</summary>
    public bool Collapsible { get; }

    /// <summary>Si se ve lo de dentro. En una fija, siempre.</summary>
    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            if (!Collapsible || _expanded == value) return;

            _expanded = value;
            _body.IsVisible = value;
            if (_chevron is not null) _chevron.RenderTransform = Turn(value);

            // Al abrirse, venga de donde venga: también cuando el editor abre Añadir porque ya no hay
            // nada elegido. Es lo que hacía el Expander, y lo que espera Accordion.
            if (value) RaiseEvent(new RoutedEventArgs(OpeningEvent, this));
        }
    }

    /// <summary>Lo que se lee a la derecha del título de una fija: un recuento, un identificador.</summary>
    public string? Note
    {
        get => _note.Text;
        set => _note.Text = value;
    }

    /// <summary>La flecha hacia abajo cerrada y hacia arriba abierta, girando entre las dos.</summary>
    private static TransformOperations Turn(bool open) =>
        TransformOperations.Parse(open ? "rotate(180deg)" : "rotate(0deg)");

    /// <summary>
    /// Lo que hace de la cabecera un botón: el ratón encima, el clic y el teclado.
    /// </summary>
    /// <remarks>
    /// En reposo va del color de la caja y no transparente, que a la vista es lo mismo pero no al
    /// animarlo: de transparente a un color macizo, el paso intermedio destella. Mírese
    /// <c>ControlThemes.Hover()</c>.
    /// </remarks>
    private void Foldable(Border head, Text title)
    {
        head.Background = Palette.SurfaceBrush;
        head.Transitions =
        [
            new BrushTransition { Property = BackgroundProperty, Duration = Palette.Quick, Easing = Palette.Ease },
        ];
        head.Cursor = new Cursor(StandardCursorType.Hand);
        head.Focusable = true;
        Loc.Set(head, AutomationProperties.NameProperty, title);

        head.PointerEntered += (_, _) => head.Background = Palette.Surface2Brush;
        head.PointerExited += (_, _) => head.Background = Palette.SurfaceBrush;
        head.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(head).Properties.IsLeftButtonPressed) return;
            IsExpanded = !IsExpanded;
        };
        head.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Space or Key.Enter)) return;
            e.Handled = true;
            IsExpanded = !IsExpanded;
        };
    }
}
