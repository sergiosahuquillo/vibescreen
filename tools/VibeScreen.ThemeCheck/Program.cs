using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using VibeScreen.App;
using VibeScreen.App.Localization;
using VibeScreen.App.Views;
using VibeScreen.ThemeCheck;

// Verificador del aspecto de la ventana. Hace las dos comparaciones que hacían falta y que hasta
// ahora solo se hacían a mano, mirando capturas:
//
//   * contra el diseño: design/app.css es la especificación, y aquí se le pregunta medida a medida;
//   * contra Fluent:    todo estado que no se vista se queda con el color de Fluent, así que se
//                       recorre la tabla de claves buscando los que nadie ha pintado.
//
// No necesita pantalla ni hardware: Avalonia corre sin ventana y los controles se miden igual.

int checks = 0, failures = 0;

void Check(bool condition, string what)
{
    checks++;
    if (condition) return;
    failures++;
    Console.WriteLine($"  FALLO  {what}");
}

void Header(string title)
{
    Console.WriteLine();
    Console.WriteLine(title);
    Console.WriteLine(new string('-', title.Length));
}

// Lo mismo que en los otros verificadores: una sección que se rompe cuenta como un fallo y la tanda
// sigue, porque justo cuando algo se rompe es cuando más falta hace ver el resto.
void Broke(string title, Exception e)
{
    checks++;
    failures++;
    Console.WriteLine($"  FALLO  la sección «{title}» se ha roto: {e.GetType().Name}: {e.Message}");
}

Color? Of(IBrush? brush) => (brush as ISolidColorBrush)?.Color;

void Same(Color? design, Color? actual, string what)
{
    if (design is null)
    {
        checks++;
        failures++;
        Console.WriteLine($"  FALLO  {what}: el diseño no dice nada de eso (¿ha cambiado el nombre de la regla?)");
        return;
    }

    Check(actual == design, $"{what}: el diseño pide {design} y la ventana pinta {actual?.ToString() ?? "nada"}");
}

void Near(double design, double actual, string what) =>
    Check(Math.Abs(design - actual) < 0.51, $"{what}: el diseño pide {design} y la ventana da {actual}");

// A qué altura cae el centro del número de un contador, contado desde su canto de arriba. El texto se
// pinta arriba del todo de su presentador, así que el centro es el borde de arriba de este más la
// mitad de lo que pide, no de lo que mide: estirado, el presentador mide el alto entero, su centro
// coincide con el del contador y el número se ve arriba igual.
double Middle(NumericUpDown counter)
{
    var text = Bench.Part<TextPresenter>(counter, "PART_TextPresenter");
    return (text.TranslatePoint(default, counter)?.Y ?? double.NaN) + (text.DesiredSize.Height / 2);
}

var css = new Css(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "design", "app.css")));
string js = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "design", "app.js"));

// El realce no está en el CSS: el prototipo lo escribe con JavaScript desde su tabla de ocho, así
// que --accent sale de ahí. Sin esto, todo lo que lo nombra -el realce rebajado, la selección, el
// resplandor- se compararía contra el tono oscuro también en la paleta clara.
//   { id: 'rojo', name: 'Rojo', hex: '#ff6f61', light: '#cf3a2b' }
string? AccentHex(string id, bool light)
{
    int at = js.IndexOf($"id: '{id}'", StringComparison.Ordinal);
    if (at < 0) return null;

    string line = js[at..js.IndexOf('}', at)];
    string key = light ? "light: '" : "hex: '";
    int from = line.IndexOf(key, StringComparison.Ordinal);
    return from < 0 ? null : line[(from + key.Length)..].Split('\'')[0];
}

// Con qué arranca el prototipo, que tampoco está en el CSS sino en el estado de app.js:
//   grid: true, gridStep: 8, snap: true, zoom: 1.5,
string? Initial(string name)
{
    int at = js.IndexOf($" {name}: ", StringComparison.Ordinal);
    if (at < 0) return null;

    string rest = js[(at + name.Length + 3)..];
    int end = rest.IndexOfAny([',', '\n', '}']);
    return (end < 0 ? rest : rest[..end]).Trim();
}

Dictionary<string, string> Variables(bool light)
{
    var table = css.Variables(light);
    if (AccentHex(Palette.DefaultAccent, light) is { } hex) table["--accent"] = hex;
    return table;
}

Bench.Start();

// ── 1. los colores de la paleta ───────────────────────────────────────────────
//
// Cada variable del :root del diseño tiene un pincel en Palette que hace su papel. Esto es lo que
// sostiene todo lo demás: si un token se va, cada regla que lo nombra se va con él.

foreach (bool light in new[] { false, true })
{
    string which = light ? "claro" : "oscuro";
    Header($"La paleta contra las variables del diseño ({which})");

    try
    {
        Palette.Apply(light ? AppTheme.Light : AppTheme.Dark, Palette.DefaultAccent);
        var vars = Variables(light);

        Color? Design(string name) => Css.Colour($"var({name})", vars);

        (string Name, Color Actual)[] table =
        [
            ("--bg", Palette.BackgroundBrush.Color),
            ("--surface", Palette.SurfaceBrush.Color),
            ("--surface-2", Palette.Surface2Brush.Color),
            ("--surface-3", Palette.Surface3Brush.Color),
            ("--line", Palette.LineBrush.Color),
            ("--line-strong", Palette.LineStrongBrush.Color),
            ("--line-hover", Palette.LineHoverBrush.Color),
            ("--line-soft", Palette.LineSoftBrush.Color),
            ("--text", Palette.TextBrush.Color),
            ("--text-2", Palette.MutedBrush.Color),
            ("--text-3", Palette.FaintBrush.Color),
            ("--ok", Palette.GoodBrush.Color),
            ("--warn", Palette.WarnBrush.Color),
            ("--danger", Palette.BadBrush.Color),
            ("--on-accent", Palette.OnAccentBrush.Color),
            ("--accent-12", Palette.AccentSoftBrush.Color),
            ("--accent-18", Palette.AccentMediumBrush.Color),
            ("--accent-hover", Palette.AccentHoverBrush.Color),
            ("--sel-bg", Palette.SelectionBrush.Color),
            ("--glow", Palette.GlowBrush.Color),
            ("--wash", Palette.WashBrush.Color),
            ("--bezel-1", Palette.Bezel1Brush.Color),
            ("--bezel-2", Palette.Bezel2Brush.Color),
            ("--thumb-off", Palette.ThumbOffBrush.Color),
            ("--thumb-ring", Palette.ThumbRingBrush.Color),
            ("--scroll", Palette.ScrollBrush.Color),
            ("--scroll-hover", Palette.ScrollHoverBrush.Color),
            ("--swatch-edge", Palette.SwatchEdgeBrush.Color),
        ];

        foreach (var (name, actual) in table) Same(Design(name), actual, name);

        // El inspector es el único sitio donde el diseño pide la superficie a medias.
        Same(Css.Colour(css.Value(".inspector", "background"), vars), Palette.SurfaceSoftBrush.Color, ".inspector background");

        // Los ocho realces, que en el diseño son una tabla de JavaScript y aquí un registro.
        foreach (var choice in Palette.Accents)
            Same(Css.Colour(AccentHex(choice.Id, light), vars), choice.For(light), $"realce «{choice.Id}»");
    }
    catch (Exception e)
    {
        Broke($"paleta {which}", e);
    }
}

Palette.Apply(AppTheme.Dark, Palette.DefaultAccent);

// ── 2. la sección plegable ────────────────────────────────────────────────────
//
// .prop-sub y su cabecera. Es la que más se ha torcido: el diseño la describe entera -recorte,
// fondo, raya al abrirla, alto y color del título- y nada comparaba lo escrito con lo pintado.

Header("La sección plegable contra .prop-sub");

try
{
    var dark = Variables(light: false);
    var box = css.Of(".prop-sub");
    var summary = css.Of(".prop-sub > summary");
    var open = css.Of(".prop-sub[open] > summary");
    var body = css.Of(".prop-sub-body");

    var cerrada = new FormSection(Loc.L("props.value"));
    var abierta = new FormSection(Loc.L("props.box"), open: true);
    abierta.Fields.Add(Loc.L("props.decimals"), Forms.Number(0, 3));
    Bench.Mount(new StackPanel { Spacing = 10, Width = 348, Children = { abierta, cerrada } });

    double radius = Css.Number(box["border-radius"]);
    Color? line = Css.Colour(box["border"].Split(' ')[^1], dark);
    Color? surface2 = Css.Colour(box["background"], dark);

    foreach (var (section, name) in new[] { (cerrada, "cerrada"), (abierta, "abierta") })
    {
        var head = Bench.Part<ToggleButton>(section, "ExpanderHeader");
        var plate = Bench.Part<Border>(head, "ToggleButtonBackground");

        Same(surface2, Of(plate.Background), $"la cabecera {name}: fondo");
        Same(line, Of(plate.BorderBrush), $"la cabecera {name}: borde");
        Near(1, plate.BorderThickness.Left, $"la cabecera {name}: grosor del borde");

        // El marco de la caja son las dos mitades, así que la cabecera **tiene que** llevar borde
        // por los cuatro lados: arriba y los costados son el marco, y abajo es la raya al abrirla.
        Check(
            plate.BorderThickness is { Top: 1, Left: 1, Right: 1, Bottom: 1 },
            $"la cabecera {name}: el borde no está por los cuatro lados, está en {plate.BorderThickness}");

        Near(Css.Number(summary["min-height"]), head.Bounds.Height, $"la cabecera {name}: alto");
        Near(Css.Numbers(summary["padding"])[1], head.Padding.Left, $"la cabecera {name}: hueco por la izquierda");
        Near(radius, plate.CornerRadius.TopLeft, $"la cabecera {name}: redondeo de arriba");
    }

    // Cerrada, la caja es solo la cabecera: redondea por los cuatro lados.
    var sola = Bench.Part<Border>(Bench.Part<ToggleButton>(cerrada, "ExpanderHeader"), "ToggleButtonBackground");
    Near(radius, sola.CornerRadius.BottomLeft, "la cabecera cerrada: redondeo de abajo");

    // Abierta, el cuerpo es la otra mitad del marco: sin borde arriba -ahí está la raya- y con los
    // otros tres, redondeado por abajo y recortando lo que pinte dentro, que es el overflow:hidden.
    var content = Bench.Part<Border>(abierta, "ExpanderContent");
    Same(surface2, Of(content.Background), "el cuerpo: fondo");
    Same(line, Of(content.BorderBrush), "el cuerpo: borde");
    Check(
        content.BorderThickness is { Top: 0, Left: 1, Right: 1, Bottom: 1 },
        $"el cuerpo: el borde debería ir por tres lados y está en {content.BorderThickness}");
    Near(radius, content.CornerRadius.BottomLeft, "el cuerpo: redondeo de abajo");
    Check(content.ClipToBounds, "el cuerpo no recorta: el diseño pide overflow:hidden y la curva se come lo de dentro");
    Near(Css.Number(body["padding"]), content.Padding.Left, "el cuerpo: hueco");

    // La raya bajo el título al abrirla no es una pieza aparte: es el borde de abajo de la cabecera.
    Same(Css.Colour(open["border-bottom"].Split(' ')[^1], dark), line, "la raya bajo el título abierto");

    // El título: baja de tono al cerrarla, que es lo que dice cuál se está tocando.
    TextBlock Title(FormSection section) => Bench.First<TextBlock>(Bench.Part<ToggleButton>(section, "ExpanderHeader"));

    Same(Css.Colour(summary["color"], dark), Of(Title(cerrada).Foreground), "el título cerrado");
    Same(Css.Colour(open["color"], dark), Of(Title(abierta).Foreground), "el título abierto");
    Near(Css.Number(summary["font-size"]), Title(abierta).FontSize, "el título: cuerpo de letra");
    Check(Title(abierta).FontWeight >= FontWeight.SemiBold, "el título: el diseño lo pide en seminegrita");

    // El color con el ratón encima no se puede provocar sin ratón, así que se comprueba la clave de
    // la que sale, que es lo mismo: es el único sitio de donde Fluent lo lee.
    Same(
        Css.Colour(css.Value(".prop-sub > summary:hover", "background"), dark),
        Bench.App.TryFindResource("ExpanderHeaderBackgroundPointerOver", ThemeVariant.Dark, out object? hover) ? hover as Color? : null,
        "la cabecera con el ratón encima");
}
catch (Exception e)
{
    Broke("la sección plegable", e);
}

// ── 3. las filas de propiedades ───────────────────────────────────────────────

Header("Las filas contra .prop-row y .input-num");

try
{
    var row = css.Of(".prop-row");
    var num = css.Of(".input-num");

    var section = new FormSection(Loc.L("props.box"), open: true);
    section.Fields.AddFields(Loc.L("props.position"), Forms.Number(0, 480), Forms.Number(0, 480));
    Bench.Mount(new StackPanel { Width = 348, Children = { section } });

    double[] columns = Css.Numbers(row["grid-template-columns"]);
    Near(columns[0], section.Fields.ColumnDefinitions[0].Width.Value, "la columna de las etiquetas");
    Near(Css.Number(row["gap"]), section.Fields.ColumnSpacing, "el hueco entre etiqueta y campo");
    Near(Css.Number(row["gap"]), section.Fields.RowSpacing, "el hueco entre filas");

    var fields = section.Fields.Children.OfType<StackPanel>().Single();
    Check(fields.Children.Count == 2, "la fila de «Posición» debería llevar los dos campos");

    foreach (var control in fields.Children.OfType<NumericUpDown>())
    {
        Near(Css.Number(num["width"]), control.Width, "el campo estrecho: ancho");
        Check(
            control.HorizontalContentAlignment == HorizontalAlignment.Center,
            "el campo estrecho: el diseño lo pide centrado");
        Check(
            control.FontFamily == AppFonts.DataDefault,
            "el campo estrecho: el diseño lo pide en monoespaciada (clase «mono»)");
        Check(!control.ShowButtonSpinner, "el campo estrecho: en 66 px no caben las flechas");
        Near(Css.Numbers(num["padding"])[1], control.Padding.Left, "el campo estrecho: hueco a los lados");
        Near(control.Bounds.Height / 2, Middle(control), "el campo estrecho: el número centrado en alto");
    }
}
catch (Exception e)
{
    Broke("las filas", e);
}

// ── 4. los controles de serie ─────────────────────────────────────────────────
//
// Lo que el diseño describe como .btn, .input, .check-box, .witem y .card. Se mide el control ya
// montado, no el estilo: lo que importa es lo que queda después de que Fluent y lo nuestro peleen.

Header("Los controles contra el CSS del diseño");

try
{
    var vars = Variables(light: false);

    Color? Design(string selector, string property) => Css.Colour(css.Value(selector, property), vars);
    double Size(string selector, string property) => Css.Number(css.Value(selector, property) ?? "");

    // El botón. Fluent pinta su fondo en el presentador y no en el propio botón, de modo que es ahí
    // donde hay que mirar: preguntarle al botón daría el color que nadie llega a ver.
    var button = Bench.Mount(new Button { Content = "Guardar" });
    var face = Bench.Part<ContentPresenter>(button, "PART_ContentPresenter");

    Same(Design(".btn", "background"), Of(face.Background), ".btn background");
    Same(Css.Colour(css.Value(".btn", "border")?.Split(' ')[^1], vars), Of(face.BorderBrush), ".btn border-color");
    Near(Size(".btn", "min-height"), button.Bounds.Height, ".btn min-height");
    Near(Css.Numbers(css.Value(".btn", "padding")!)[1], button.Padding.Left, ".btn padding");
    Near(Size(".btn", "font-size"), button.FontSize, ".btn font-size");
    Near(Css.Number(vars["--r-ctl"]), face.CornerRadius.TopLeft, ".btn border-radius");
    Same(Design(".btn:hover", "background"), Bench.App.TryFindResource("ButtonBackgroundPointerOver", ThemeVariant.Dark, out object? bh) ? Of(bh as IBrush) : null, ".btn:hover background");
    Same(Css.Colour(css.Value(".btn:hover", "border-color"), vars), Bench.App.TryFindResource("ButtonBorderBrushPointerOver", ThemeVariant.Dark, out object? bb) ? Of(bb as IBrush) : null, ".btn:hover border-color");

    // El diseño no da color de pulsado a nada: el único :active es el del botón, y solo lo hunde un
    // píxel. Eso es una regla del diseño y se comprueba como tal.
    Check(
        css.Value(".btn:active", "background") is null && css.Value(".btn:active", "transform") is not null,
        "el diseño ha cambiado: ahora .btn:active sí dice un color, y la tabla de claves repite el hover");

    // Las variantes, que van por clase y no por clave: una clave de Fluent es por tipo de control, y
    // el primario y el de borrar son el mismo Button con otra ropa. Aquí es donde se ve que el de
    // borrar **no** es rojo macizo, que es lo que el diseño quiere decir con un 8 % de fondo: el que
    // no tiene vuelta atrás no puede parecer el importante.
    var primary = Bench.Mount(new Button { Content = "Aplicar", Classes = { "primary" } });
    var primaryFace = Bench.Part<ContentPresenter>(primary, "PART_ContentPresenter");

    Same(Design(".btn-primary", "background"), Of(primaryFace.Background), ".btn-primary background");
    Same(Css.Colour(css.Value(".btn-primary", "border-color"), vars), Of(primaryFace.BorderBrush), ".btn-primary border-color");
    Same(Design(".btn-primary", "color"), Of(primary.Foreground), ".btn-primary color");
    Same(Design(".btn-primary:hover", "background"), Palette.AccentHoverBrush.Color, ".btn-primary:hover background");

    var danger = Bench.Mount(new Button { Content = "Eliminar", Classes = { "danger" } });
    var dangerFace = Bench.Part<ContentPresenter>(danger, "PART_ContentPresenter");

    Same(Design(".btn-danger", "background"), Of(dangerFace.Background), ".btn-danger background");
    Same(Css.Colour(css.Value(".btn-danger", "border-color"), vars), Of(dangerFace.BorderBrush), ".btn-danger border-color");
    Same(Design(".btn-danger", "color"), Of(danger.Foreground), ".btn-danger color");
    Same(Design(".btn-danger:hover", "background"), Palette.BadSoftHoverBrush.Color, ".btn-danger:hover background");
    Same(Css.Colour(css.Value(".btn-danger:hover", "border-color"), vars), Palette.BadEdgeHoverBrush.Color, ".btn-danger:hover border-color");

    var small = Bench.Mount(new Button { Content = "Nuevo", Classes = { "sm" } });

    Near(Size(".btn-sm", "min-height"), small.Bounds.Height, ".btn-sm min-height");
    Near(Css.Numbers(css.Value(".btn-sm", "padding")!)[1], small.Padding.Left, ".btn-sm padding");
    Near(Size(".btn-sm", "font-size"), small.FontSize, ".btn-sm font-size");
    Near(Size(".btn-icon", "width"), Bench.Mount(new Button { Classes = { "icon" } }).Width, ".btn-icon width");

    // El cuadro de texto.
    var input = Bench.Mount(new TextBox { Text = "Escrito" });
    var frame = Bench.Part<Border>(input, "PART_BorderElement");

    Same(Design(".input", "background"), Of(input.Background), ".input background");
    Same(Css.Colour(css.Value(".input", "border")?.Split(' ')[^1], vars), Of(frame.BorderBrush), ".input border-color");
    Near(Size(".input", "min-height"), input.Bounds.Height, ".input min-height");
    Near(Css.Numbers(css.Value(".input", "padding")!)[1], input.Padding.Left, ".input padding");
    Near(Css.Number(vars["--r-ctl"]), frame.CornerRadius.TopLeft, ".input border-radius");

    // El contador, encendido y apagado. Su cuadro de texto va un píxel por fuera de todo -la plantilla
    // le pone un margen de -1, justo encima del marco- y no puede pintar fondo propio en ningún
    // estado: macizo, se come el borde en todo el tramo del número y la casilla parece no tenerlo.
    foreach (bool on in new[] { true, false })
    {
        var counter = Bench.Mount(new NumericUpDown { Value = 40, IsEnabled = on });
        var inner = Bench.Part<Border>(Bench.Part<TextBox>(counter, "PART_TextBox"), "PART_BorderElement");
        Check(
            (Of(inner.Background)?.A ?? 0) == 0,
            $"el contador {(on ? "encendido" : "apagado")}: su cuadro de texto pinta {Of(inner.Background)} encima del marco");
    }

    // Y el número dentro de él, como en cualquier .input: centrado en alto y con el mismo hueco a la
    // izquierda que el texto de un cuadro, para que en una columna de campos empiecen todos a la vez.
    var wide = Bench.Mount(new NumericUpDown { Value = 40, Width = 200 });
    Near(Css.Numbers(css.Value(".input", "padding")!)[1], wide.Padding.Left, "el contador: hueco por la izquierda");
    Near(wide.Bounds.Height / 2, Middle(wide), "el contador: el número centrado en alto");

    // La casilla.
    var check = Bench.Mount(new CheckBox { Content = "Rejilla" });
    var tick = Bench.Part<Border>(check, "NormalRectangle");

    Near(Size(".check-box", "width"), tick.Bounds.Width, ".check-box width");
    Near(Size(".check-box", "height"), tick.Bounds.Height, ".check-box height");
    Near(Size(".check-box", "border-radius"), tick.CornerRadius.TopLeft, ".check-box border-radius");
    Same(Design(".check-box", "background"), Of(tick.Background), ".check-box background");
    Same(Css.Colour(css.Value(".check-box", "border")?.Split(' ')[^1], vars), Of(tick.BorderBrush), ".check-box border-color");
    Near(Css.Number(css.Value(".check-box", "border")!), tick.BorderThickness.Left, ".check-box border-width");

    // La fila de la lista de widgets.
    var list = Bench.Mount(new ListBox { Items = { "Reloj", "Texto" } });
    var item = Bench.First<ListBoxItem>(list);

    Near(Size(".witem", "min-height"), item.Bounds.Height, ".witem min-height");
    Near(Css.Numbers(css.Value(".witem", "padding")!)[1], item.Padding.Left, ".witem padding");
    Near(Size(".witem", "border-radius"), item.CornerRadius.TopLeft, ".witem border-radius");
    Near(Size(".witem", "font-size"), item.FontSize, ".witem font-size");
    Same(Design(".witem.is-sel", "background"), Palette.AccentMediumBrush.Color, ".witem.is-sel background");
    Same(Design(".witem:hover", "background"), Palette.Surface2Brush.Color, ".witem:hover background");

    // Las dos columnas de la fila, que cambian de color con ella: el identificador en gris que se
    // aclara al elegirla, y el tipo en el realce. Van por estilo y no en la plantilla de la fila, así
    // que se comprueba con la fila elegida de verdad.
    var kind = new TextBlock { Text = "Arco", Classes = { "witem-kind" } };
    var id = new TextBlock { Text = "cpu-0-load", Classes = { "witem-id" } };
    var rows = Bench.Mount(new ListBox { Items = { new ListBoxItem { Content = new StackPanel { Children = { kind, id } } } } });

    Same(Design(".witem-kind", "color"), Of(kind.Foreground), ".witem-kind color");
    Same(Design(".witem", "color"), Of(id.Foreground), ".witem color");
    rows.SelectedIndex = 0;
    Bench.Settle();
    Same(Design(".witem.is-sel .witem-kind", "color"), Of(kind.Foreground), ".witem.is-sel .witem-kind color");
    Same(Design(".witem.is-sel", "color"), Of(id.Foreground), ".witem.is-sel color");

    // El desplegable pequeño, el del zoom.
    var zoom = Bench.Mount(new ComboBox { Classes = { "sm" }, Items = { "1.5x" }, SelectedIndex = 0 });

    Near(Size(".select-sm", "min-height"), zoom.Bounds.Height, ".select-sm min-height");
    Near(Size(".select-sm", "font-size"), zoom.FontSize, ".select-sm font-size");

    // Los dos desplegables, el normal y el pequeño: el relleno de la izquierda es el del diseño, y el
    // texto llega hasta la columna de la flecha. El padding-right de .select y de .select-sm es el
    // sitio de la flecha, y Fluent ya se lo da en su propia columna: un relleno a la derecha encima de
    // eso era sitio vacío, y el texto se cortaba 12 px antes de tiempo.
    var select = new ComboBox { Width = 180, Items = { "Llenar el panel (recorta)" }, SelectedIndex = 0 };
    var selectSm = new ComboBox { Width = 70, Classes = { "sm" }, Items = { "1.5x" }, SelectedIndex = 0 };

    foreach (var (box, left, name) in new[]
    {
        (select, Css.Numbers(css.Value(".select", "padding")!)[1], ".select"),
        (selectSm, Css.Numbers(css.Value(".select-sm", "padding")!)[^1], ".select-sm"),
    })
    {
        Bench.Mount(box);
        Near(left, box.Padding.Left, $"{name} padding a la izquierda");

        var lanes = Bench.First<Grid>(box);
        var text = Bench.Part<ContentControl>(box, "ContentPresenter");
        double end = text.TranslatePoint(new Point(text.Bounds.Width, 0), lanes)?.X ?? double.NaN;
        Near(lanes.ColumnDefinitions[0].ActualWidth, end, $"{name}: el texto llega hasta la columna de la flecha");
    }

    // La tarjeta.
    var card = Bench.Mount(VibeScreen.App.Theme.Blocks.Card(Loc.L("editor.panel.widgets"), null, new TextBlock { Text = "nada" }));

    Same(Design(".card", "background"), Of(card.Background), ".card background");
    Same(Css.Colour(css.Value(".card", "border")?.Split(' ')[^1], vars), Of(card.BorderBrush), ".card border-color");
    Near(Css.Number(vars["--r-card"]), card.CornerRadius.TopLeft, ".card border-radius");

    double[] padding = Css.Numbers(css.Value(".card", "padding")!);
    Near(padding[0], card.Padding.Top, ".card padding arriba");
    Near(padding[1], card.Padding.Left, ".card padding a los lados");
    Near(padding[2], card.Padding.Bottom, ".card padding abajo");
}
catch (Exception e)
{
    Broke("los controles", e);
}

// ── 5. los estados que Fluent se quedaría ─────────────────────────────────────
//
// Esta es la otra mitad. Fluent trae una tabla completa de colores por control y por estado, y todo
// el que no se vista se queda con el suyo: blancos translúcidos sobre gris medio, en una ventana de
// grises azulados macizos. La forma de encontrarlos es recorrer los que sí se visten, probar cada
// estado hermano y preguntar si Fluent lo contesta: si lo contesta y no es nuestro, está sin vestir.

Header("Los estados sin vestir de Fluent");

try
{
    string[] states =
    [
        "PointerOver", "Pressed", "Disabled", "Focused", "PointerFocused",
        "Selected", "SelectedPointerOver", "SelectedPressed", "SelectedDisabled", "SelectedUnfocused",
        "Checked", "CheckedPointerOver", "CheckedPressed", "CheckedDisabled",
        "Unchecked", "UncheckedPointerOver", "UncheckedPressed", "UncheckedDisabled",
        "Indeterminate", "IndeterminatePointerOver", "IndeterminatePressed", "IndeterminateDisabled",
    ];

    // Las nuestras son las que hay sueltas en los recursos de la aplicación: ahí solo escribe
    // FluentTokens, con el realce de Palette.
    var ours = Bench.App.Resources.Keys.OfType<string>().ToHashSet(StringComparer.Ordinal);

    // El realce no es una clave de control: son los siete tonos que Fluent reparte por su cuenta.
    ours.RemoveWhere(key => key.StartsWith("SystemAccentColor", StringComparison.Ordinal));

    var families = new SortedSet<string>(StringComparer.Ordinal);
    foreach (string key in ours)
    {
        string family = key;
        foreach (string state in states.OrderByDescending(s => s.Length))
        {
            if (!key.EndsWith(state, StringComparison.Ordinal)) continue;
            family = key[..^state.Length];
            break;
        }

        if (family.Length > 0) families.Add(family);
    }

    int missing = 0;
    foreach (string family in families)
    {
        foreach (string state in states)
        {
            string key = family + state;
            if (ours.Contains(key)) continue;

            // Sin la clave nuestra delante, esto va directo al tema: si contesta, el estado existe
            // en Fluent y lo pintaría él.
            if (!Bench.App.TryFindResource(key, ThemeVariant.Dark, out _)) continue;

            missing++;
            Console.WriteLine($"  FALLO  «{key}» existe en Fluent y no la pinta nadie: ese estado sale con su color");
        }
    }

    checks += families.Count;
    failures += missing;
    Console.WriteLine($"  {families.Count} familias de claves vestidas, {ours.Count} claves en total");
}
catch (Exception e)
{
    Broke("los estados de Fluent", e);
}

// ── 6. el editor ──────────────────────────────────────────────────────────────
//
// Lo de arriba mide controles sueltos, y el diseño también tiene reglas que solo existen entre
// piezas: la columna de inspección que va de arriba abajo, Propiedades quedándose con el sitio que
// sobra, el campo del tema creciendo hasta su máximo. El export del 6 de octubre solo cambió eso, y
// las cinco secciones de arriba lo dejaban pasar en verde sin tocar una línea. Así que aquí se monta
// el editor entero, del tamaño de la ventana, y se le pregunta dónde ha quedado cada cosa.
//
// Las piezas se buscan por su nombre, que es el del diseño: la clase o el id con que el CSS las llama.

Header("El editor contra .view-editor");

try
{
    var dark = Variables(light: false);

    using var link = new ServiceLink();
    using var sensors = new RemoteSensorProvider();
    using var editor = new EditorView(link, sensors);
    Bench.Fill(editor);

    Control Piece(string name) => Bench.Part<Control>(editor, name);

    // Dónde ha quedado una pieza, contado desde la esquina del editor y no desde la de su padre.
    Rect Where(Control piece) => new(piece.TranslatePoint(default, editor) ?? default, piece.Bounds.Size);

    var inspector = Where(Piece("inspector"));
    var bar = (Border)Piece("editor-bar");
    var foot = (Border)Piece("editor-foot");

    // La rejilla de dos columnas: la de inspección mide lo que dice el diseño y va de arriba abajo.
    double column = Css.Numbers(css.Value(".view-editor.is-active", "grid-template-columns")!)[^1];
    Near(column, inspector.Width, ".view-editor: el ancho de la columna de inspección");
    Check(
        css.Value(".inspector", "grid-row") == "1 / -1",
        "el diseño ha cambiado: .inspector ya no va de la primera fila de la rejilla a la última");
    Near(0, inspector.Top, ".inspector: empieza arriba del todo");
    Near(editor.Bounds.Height, inspector.Height, ".inspector: mide todo el alto del editor");

    // La barra y el pie se quedan en la columna del lienzo: no pasan por encima de la otra.
    Near(editor.Bounds.Width - inspector.Width, Where(bar).Width, ".editor-bar: solo encima del lienzo");
    Near(editor.Bounds.Width - inspector.Width, Where(foot).Width, ".editor-foot: solo debajo del lienzo");

    // La lista de widgets crece con la ventana. El vh del prototipo es la ventana entera -su .win
    // mide 100dvh-, no el editor.
    double tall = TopLevel.GetTopLevel(editor)?.ClientSize.Height ?? 0;
    Check(tall > 0, "la ventana del banco no dice su alto: la lista de widgets no tiene contra qué medirse");
    var list = (ListBox)Piece("wlist");
    double[] clamp = Css.Numbers(css.Value(".wlist", "max-height")!);
    Near(Math.Clamp(tall * clamp[1] / 100, clamp[0], clamp[2]), list.MaxHeight, ".wlist max-height");

    // Su relleno va por dentro de lo que se desplaza, y entre fila y fila hay un hueco de 2.
    double[] room = Css.Numbers(css.Value(".wlist", "padding")!);
    Near(room[0], list.Padding.Top, ".wlist padding arriba");
    Near(room[1], list.Padding.Left, ".wlist padding a los lados");
    Near(room[2], list.Padding.Bottom, ".wlist padding abajo");
    Near(Css.Number(css.Value(".wlist", "gap")!), (list.ItemsPanelRoot as StackPanel)?.Spacing ?? double.NaN, ".wlist gap");

    // flex: 1 0 auto. Sin nada elegido, Propiedades llega al fondo de la columna: lo que sobra es suyo
    // y no un hueco vacío debajo.
    double padding = Css.Number(css.Value(".inspector", "padding")!);
    Near(inspector.Bottom - padding, Where(Piece("props-panel")).Bottom, "#props-panel: se queda con el sitio que sobra");

    // La barra. Con el ancho de esta ventana sobra sitio, así que el campo del tema crece hasta el tope.
    var theme = Piece("bar-theme");
    Near(Css.Number(css.Value(".bar-theme", "max-width")!), theme.Bounds.Width, ".bar-theme: con sitio, crece hasta su max-width");
    Near(Css.Number(css.Value(".bar-theme", "min-width")!), theme.MinWidth, ".bar-theme min-width");
    Near(Css.Number(css.Value(".save-state", "min-height")!), Piece("save-state").Bounds.Height, ".save-state min-height");

    Same(Css.Colour(css.Value(".editor-bar", "background"), dark), Of(bar.Background), ".editor-bar background");
    Same(Css.Colour(css.Value(".editor-foot", "background"), dark), Of(foot.Background), ".editor-foot background");

    // El grupo de aplicar y eliminar, empujado al canto derecho de su línea. Va el último porque es
    // la única pieza que el editor no tenía antes de este export: si falta, lo demás ya está dicho.
    double[] inside = Css.Numbers(css.Value(".editor-bar", "padding")!);
    Near(Where(bar).Right - inside[1], Where(Piece("bar-right")).Right, ".bar-right margin-left: auto: al canto derecho");

    // El aumento de partida, el del prototipo, y que la opción elegida se lea entera. El ancho que el
    // desplegable le deja al texto se compara con el que pide ese mismo texto medido suelto: con 70 px
    // se comía la «x» de «1.5x».
    var zoom = (ComboBox)Piece("zoom");
    string start = $"{Initial("zoom")}x";
    Check(zoom.SelectedItem as string == start, $"el zoom de partida: el diseño pide {start} y el editor abre en {zoom.SelectedItem}");

    var shown = Bench.First<TextBlock>(Bench.Part<ContentControl>(zoom, "ContentPresenter"));
    var whole = new TextBlock { Text = shown.Text, FontFamily = shown.FontFamily, FontSize = shown.FontSize, FontWeight = shown.FontWeight };
    whole.Measure(Size.Infinity);
    Check(
        whole.DesiredSize.Width <= shown.Bounds.Width + 0.5,
        $"el zoom: «{shown.Text}» pide {whole.DesiredSize.Width:0.#} px y el desplegable le deja {shown.Bounds.Width:0.#}");
}
catch (Exception e)
{
    Broke("el editor", e);
}

// ── 7. las cajas de la columna ────────────────────────────────────────────────
//
// .panel y sus piezas. Las cajas no son un Expander revestido sino un control propio, así que aquí no
// hay claves de Fluent que repasar: se montan una fija y una plegable y se mide lo que pintan. Lo
// único que no se puede medir es el color con el ratón encima, que sin ratón no se provoca.

Header("Las cajas de la columna contra .panel");

try
{
    var dark = Variables(light: false);
    var box = css.Of(".panel");
    var head = css.Of(".panel-head");
    var title = css.Of(".panel-title");
    var note = css.Of(".panel-count");
    var body = css.Of(".panel-body");

    var fija = new VibeScreen.App.Theme.InspectorPanel(Loc.L("editor.panel.widgets"), new TextBlock { Text = "nada" },
        VibeScreen.App.Theme.InspectorPanel.BodyPadding) { Note = "8" };
    var plegable = new VibeScreen.App.Theme.InspectorPanel(Loc.L("editor.panel.display"), new TextBlock { Text = "nada" },
        VibeScreen.App.Theme.InspectorPanel.BodyPadding, collapsible: true, open: true);
    Bench.Mount(new StackPanel { Width = 348, Spacing = 10, Children = { fija, plegable } });

    foreach (var (caja, name) in new[] { (fija, "fija"), (plegable, "plegable") })
    {
        Same(Css.Colour(box["background"], dark), Of(caja.Background), $"la caja {name}: fondo");
        Same(Css.Colour(box["border"].Split(' ')[^1], dark), Of(caja.BorderBrush), $"la caja {name}: borde");
        Near(Css.Number(box["border"]), caja.BorderThickness.Left, $"la caja {name}: grosor del borde");
        Near(Css.Number(box["border-radius"]), caja.CornerRadius.TopLeft, $"la caja {name}: redondeo");
        Check(caja.ClipToBounds, $"la caja {name} no recorta: el diseño pide overflow:hidden");

        var cabecera = Bench.Part<Border>(caja, "panel-head");
        Near(Css.Number(head["min-height"]), cabecera.Bounds.Height, $"la cabecera {name}: alto");
        Near(Css.Numbers(head["padding"])[1], cabecera.Padding.Left, $"la cabecera {name}: hueco a los lados");

        // El título es lo primero que hay escrito en la cabecera.
        var titulo = Bench.First<TextBlock>(cabecera);
        Same(Css.Colour(title["color"], dark), Of(titulo.Foreground), $"el título {name}: color");
        Near(Css.Number(title["font-size"]), titulo.FontSize, $"el título {name}: cuerpo de letra");
        Near(Css.Number(title["letter-spacing"]) * titulo.FontSize, titulo.LetterSpacing, $"el título {name}: espaciado");
        Check(titulo.FontWeight >= FontWeight.SemiBold, $"el título {name}: el diseño lo pide en seminegrita");
        Check(titulo.FontFamily == AppFonts.Display, $"el título {name}: el diseño lo pide en la letra de los títulos");
        Check(titulo.Text == titulo.Text?.ToUpperInvariant(), $"el título {name}: el diseño lo pide en versalitas");

        var cuerpo = Bench.Part<Border>(caja, "panel-body");
        double[] hueco = Css.Numbers(body["padding"]);
        Near(hueco[0], cuerpo.Padding.Top, $"el cuerpo {name}: hueco arriba");
        Near(hueco[1], cuerpo.Padding.Left, $"el cuerpo {name}: hueco a los lados");
        Near(hueco[2], cuerpo.Padding.Bottom, $"el cuerpo {name}: hueco abajo");
    }

    // El hueco entre las piezas del cuerpo lo aplica quien llena la caja, con esta constante.
    Near(Css.Number(body["gap"]), VibeScreen.App.Theme.InspectorPanel.BodySpacing, ".panel-body gap");

    // La nota de la fija: el recuento de widgets o el identificador del elegido.
    var nota = Bench.Part<TextBlock>(fija, "panel-note");
    Near(Css.Number(note["font-size"]), nota.FontSize, ".panel-count: cuerpo de letra");
    Same(Css.Colour(note["color"], dark), Of(nota.Foreground), ".panel-count: color");
    Near(Css.Number(note["max-width"]), nota.MaxWidth, ".panel-count: ancho máximo");
    Check(nota.FontFamily == AppFonts.DataDefault, ".panel-count: el diseño la pide en la letra de los datos (clase «mono»)");
}
catch (Exception e)
{
    Broke("las cajas", e);
}

// ── 8. la tabla de sensores vacía ─────────────────────────────────────────────
//
// .empty-table: lo que ocupa el sitio de las filas cuando no hay ninguna. Centrado y con aire por
// arriba, al contrario que el .empty de una tarjeta, que va pegado a la izquierda: en una tabla, pegado
// al canto, se leía como una fila más. Se monta la pestaña sin servicio, que es cuando sale.

Header("La tabla de sensores vacía contra .empty-table");

try
{
    var table = css.Of(".empty-table");

    using var link = new ServiceLink();
    using var sensors = new RemoteSensorProvider();
    using var editor = new EditorView(link, sensors);
    var view = Bench.Fill(new SensorsView(sensors, editor));

    var block = (StackPanel)Bench.Part<Control>(view, "sensor-offline");
    var area = (Control)block.Parent!;
    var at = block.TranslatePoint(default, area) ?? default;

    Check(block.IsVisible, "sin servicio, el aviso de la tabla no sale");
    Near(Css.Numbers(table["padding"])[0], at.Y, ".empty-table: el aire por arriba");
    Near(area.Bounds.Width / 2, at.X + (block.Bounds.Width / 2), ".empty-table: centrado a lo ancho");
    Near(Css.Number(table["gap"]), block.Spacing, ".empty-table gap");

    foreach (var line in block.Children.OfType<TextBlock>())
        Check(line.TextAlignment == TextAlignment.Center, $".empty-table: «{line.Text}» tiene que ir centrado");
}
catch (Exception e)
{
    Broke("la tabla de sensores vacía", e);
}

// ── el recuento ───────────────────────────────────────────────────────────────

Console.WriteLine();
Console.WriteLine(new string('=', 48));
Console.WriteLine(failures == 0 ? $"CORRECTO: {checks} comprobaciones" : $"FALLOS: {failures} de {checks} comprobaciones");
Console.WriteLine(new string('=', 48));
return failures == 0 ? 0 : 1;
