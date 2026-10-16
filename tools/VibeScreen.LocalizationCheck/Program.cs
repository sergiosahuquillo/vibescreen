using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VibeScreen.App;
using VibeScreen.App.Localization;
using VibeScreen.App.Views;
using VibeScreen.Contracts;
using VibeScreen.Core.Layout;

// Verificador de los idiomas. Tres cosas, de la más barata a la más cara:
//
//   * los catálogos: los dos idiomas con las mismas claves, las mismas llaves en cada texto y los
//     plurales completos, y cada clave que se usa en el código existe y no sobra ninguna;
//   * cómo se escriben: los textos con argumentos, los plurales y las cifras de cada idioma;
//   * el cambio en caliente: la ventana entera, sin pantalla, cambiando de idioma con las cuatro
//     secciones montadas, y buscando en lo que se ve cualquier resto del idioma de antes.
//
// No necesita hardware ni servicio. Si hay un servicio en marcha se habla con él, y la ventana
// enseña más cosas que mirar; sin él, enseña las de «servicio parado».

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

void Broke(string title, Exception e)
{
    checks++;
    failures++;
    Console.WriteLine($"  FALLO  la sección «{title}» se ha roto: {e.GetType().Name}: {e.Message}");
}

string root = Root();

// Los catálogos que hay, cada uno con sus dos idiomas y dónde se usan sus claves.
var catalogs = new List<Catalog>
{
    new("la ventana", Path.Combine(root, "src", "VibeScreen.App", "Localization"),
        [Path.Combine(root, "src", "VibeScreen.App"), Path.Combine(root, "tools")],
        new Regex(@"\bLoc\.(L|S|Plural)\(\s*""([^""]+)""", RegexOptions.Compiled)),

    // Los avisos de Core y del servicio. Sus códigos no siempre van dentro de Notice.Of —el de un
    // plazo vencido se elige antes de saber cuál ha sido—, así que cuenta cualquier cadena con la
    // forma de un código. Los verificadores que comparan códigos también cuentan: así se caza un
    // código mal escrito en una comprobación, que si no pasaría siempre.
    new("los avisos", Path.Combine(root, "src", "VibeScreen.Contracts", "Notices"),
        [
            Path.Combine(root, "src", "VibeScreen.Contracts"),
            Path.Combine(root, "src", "VibeScreen.Core"),
            Path.Combine(root, "src", "VibeScreen.Service"),
            Path.Combine(root, "tools", "VibeScreen.CoreCheck"),
            Path.Combine(root, "tools", "VibeScreen.ServiceCheck"),
        ],
        // Sin los nombres de fichero, que tienen la misma forma: «layout.json» no es un código.
        new Regex(@"()""((?:brightness|error|ipc|layout|panel|sensors|service)\.[a-z0-9-]+(?:\.[a-z0-9-]+)*)(?<!\.json)""", RegexOptions.Compiled)),
};

var placeholder = new Regex(@"(?<!\{)\{(\d+)(?:[,:][^}]*)?\}", RegexOptions.Compiled);
var keyShape = new Regex(@"^[a-z][a-z0-9-]*(\.[a-z0-9-]+)+$", RegexOptions.Compiled);

foreach (var catalog in catalogs)
{
    Header($"Catálogo de {catalog.Name}");
    try
    {
        var es = Read(Path.Combine(catalog.Folder, "es.json"));
        var en = Read(Path.Combine(catalog.Folder, "en.json"));
        catalog.Es = es;
        catalog.En = en;

        foreach (string key in es.Keys.Except(en.Keys)) Check(false, $"«{key}» está en español y falta en inglés");
        foreach (string key in en.Keys.Except(es.Keys)) Check(false, $"«{key}» está en inglés y no en español");

        foreach (var (key, text) in es)
        {
            Check(text.Trim().Length > 0, $"«{key}» está vacía en español");
            if (!en.TryGetValue(key, out string? other)) continue;
            Check(other.Trim().Length > 0, $"«{key}» está vacía en inglés");

            // Un singular puede no escribir el número —«1 sensor»—, así que ahí no cuenta el {0}.
            var mine = Holes(text, key);
            var theirs = Holes(other, key);
            Check(mine.SetEquals(theirs),
                $"«{key}» no lleva los mismos huecos: {{{string.Join(",", mine)}}} en español y {{{string.Join(",", theirs)}}} en inglés");

            Check(Formats(text) && Formats(other), $"«{key}» tiene las llaves mal y no se puede rellenar");
        }

        foreach (string key in es.Keys.Where(k => k.EndsWith(".one", StringComparison.Ordinal)))
            Check(es.ContainsKey(key[..^4] + ".other"), $"«{key}» no tiene su «.other»");
        foreach (string key in es.Keys.Where(k => k.EndsWith(".other", StringComparison.Ordinal)))
            Check(es.ContainsKey(key[..^6] + ".one"), $"«{key}» no tiene su «.one»");

        Console.WriteLine($"  {es.Count} claves en cada idioma");
    }
    catch (Exception e)
    {
        Broke($"Catálogo de {catalog.Name}", e);
    }
}

Header("Claves usadas en el código");
try
{
    foreach (var catalog in catalogs)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in catalog.Sources.SelectMany(Sources))
        {
            string code = File.ReadAllText(file);
            foreach (Match match in catalog.Usage.Matches(code))
            {
                string key = match.Groups[2].Value;
                bool plural = match.Groups[1].Value == "Plural";
                string where = $"{Path.GetRelativePath(root, file)}:{Line(code, match.Index)}";

                if (plural)
                {
                    used.Add(key + ".one");
                    used.Add(key + ".other");
                    Check(catalog.Es.ContainsKey(key + ".one") && catalog.Es.ContainsKey(key + ".other"),
                        $"{where}: el plural «{key}» no tiene «.one» y «.other» en el catálogo");
                }
                else
                {
                    used.Add(key);
                    Check(catalog.Es.ContainsKey(key), $"{where}: la clave «{key}» no está en el catálogo");
                }
            }

            // Una clave hecha a trozos no la encuentra nadie: ni esto, ni quien la busque a mano.
            foreach (Match dynamic in Regex.Matches(code, @"\b(Loc\.(L|S|Plural)|Notice\.Of)\(\s*\$"))
                Check(false, $"{Path.GetRelativePath(root, file)}:{Line(code, dynamic.Index)}: clave compuesta a trozos");
        }

        foreach (string orphan in catalog.Es.Keys.Where(k => !used.Contains(k)).Order(StringComparer.Ordinal))
            Check(false, $"«{orphan}» está en el catálogo de {catalog.Name} y no se usa en ningún sitio");

        Console.WriteLine($"  {used.Count} claves en uso en {catalog.Name}");
    }
}
catch (Exception e)
{
    Broke("Claves usadas en el código", e);
}

Header("Cómo se escribe cada idioma");
try
{
    Loc.CaptureSystem();
    var traffic = ValueColors.Traffic(0, 100);

    Loc.Use("es");
    Check(Labels.Range(traffic, 0, "°C").ToString() == "menos de 40 °C", "el tramo bajo se lee «menos de 40 °C»");
    Check(Labels.Range(traffic, 1, "°C").ToString() == "40 a 80 °C", "el medio, «40 a 80 °C»");
    Check(Labels.Range(traffic, 2, "°C").ToString() == "80 °C o más", "y el alto, «80 °C o más»");
    Check(Labels.Range(traffic, 0).ToString() == "menos de 40", "sin unidad, solo el número");
    Check(Labels.Range(new ColorLevels { Low = "#000000", Medium = "#000000", High = "#000000", MediumFrom = 12.5f, HighFrom = 20 }, 0).ToString()
          == "menos de 12,5", "una cifra con decimales lleva coma en español");
    Check(Loc.Number(1234567) == "1.234.567", $"los miles con punto en español: {Loc.Number(1234567)}");
    Check(Loc.Plural("sensors.count", 1).ToString() == "1 sensor", "el singular, en singular");
    Check(Loc.Plural("sensors.count", 3).ToString() == "3 sensores", "el plural, en plural");
    Check(Loc.Plural("sensors.count", 0).ToString() == "0 sensores", "el cero, en plural");
    Check(Loc.L("startup.failed", AppRender.Label(VibeScreen.Core.Rendering.RenderMode.Software), "x").ToString()
          == "VibeScreen no ha podido arrancar con Software (la CPU): x", "un texto dentro de otro se traduce con él");
    Check(Loc.L("rail.device.header").Upper().ToString() == "PANEL CONECTADO", "las versalitas se ponen al resolver");

    // Lo que ya está enlazado no se queda con el idioma en el que nació: se resuelve al pintarlo.
    var later = Loc.L("nav.settings");
    Loc.Use("en");
    Check(later.ToString() == "Settings", "un texto creado en español sale en inglés si se pinta en inglés");
    Check(Labels.Range(traffic, 0, "°C").ToString() == "below 40 °C", "the low band reads «below 40 °C»");
    Check(Labels.Range(traffic, 1, "°C").ToString() == "40 to 80 °C", "the middle one, «40 to 80 °C»");
    Check(Labels.Range(traffic, 2, "°C").ToString() == "80 °C or more", "and the high one, «80 °C or more»");
    Check(Loc.Number(1234567) == "1,234,567", $"thousands with a comma in English: {Loc.Number(1234567)}");
    Check(Loc.Plural("sensors.count", 1).ToString() == "1 sensor", "singular in English");
    Check(Loc.Plural("sensors.count", 2).ToString() == "2 sensors", "plural in English");

    // Compuesta a trozos a propósito, para que la búsqueda de claves de arriba no la cuente como una
    // que falta en el catálogo: es justo la que no tiene que estar.
    string missing = string.Concat("no.such", ".key");
    Check(Loc.L(missing).ToString() == missing, "una clave que no existe se ve tal cual, que se nota");

    // Los avisos del servicio: con otro dentro, y en los dos idiomas.
    var noId = Notice.Of("layout.widget.no-id").With(Notice.Of("layout.where.widget", "cpu"));
    Check(noId.ToString() == "widget «cpu»: falta el id.", $"un aviso con otro dentro, en español: {noId}");
    Check(Notices.Render(noId, "en") == "widget “cpu”: the id is missing.", $"and in English: {Notices.Render(noId, "en")}");
    var retry = Notice.Of("panel.retry", 5).With(Notice.Of("panel.not-found"));
    Check(Notices.Render(retry, "en") == "No compatible panel was found. Next attempt in 5 s.",
          $"la causa va dentro y también se traduce: {Notices.Render(retry, "en")}");
    Check(Notice.From(new InvalidOperationException("x")) == Notice.Of("error.raw", "x"),
          "una excepción ajena viaja con su mensaje tal cual");
    Loc.Use("en");
    Check(Loc.Notice(retry).ToString() == Notices.Render(retry, "en"), "la ventana pinta los avisos en su idioma");
}
catch (Exception e)
{
    Broke("Cómo se escribe cada idioma", e);
}

Header("En caliente: la ventana entera");
try
{
    AppBuilder.Configure<VibeScreenApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
        .SetupWithoutStarting();

    var es = catalogs[0].Es;
    var onlySpanish = catalogs.SelectMany(c => Distinct(c.Es, c.En)).ToList();
    var onlyEnglish = catalogs.SelectMany(c => Distinct(c.En, c.Es)).ToList();

    using var service = new ServiceLink();
    var window = new MainWindow(service, new AppSettings(), () => { });
    var applySettings = typeof(MainWindow).GetMethod("ApplySettings", BindingFlags.NonPublic | BindingFlags.Instance)!;
    var show = typeof(MainWindow).GetMethod("Show", BindingFlags.NonPublic | BindingFlags.Instance, [typeof(AppView)])!;

    Loc.Use("es");
    applySettings.Invoke(window, null);
    window.Show();
    Settle(window, TimeSpan.FromSeconds(3));

    // De ida y vuelta, y otra vez: lo que se rompe al segundo cambio —un enlace que se suelta, una
    // fila rehecha que se queda con el idioma de antes— no se ve en el primero.
    foreach (string language in new[] { "en", "es", "en" })
    {
        // Cada vista se monta antes de cambiar de idioma y se vuelve a mirar después: así se ven
        // tanto las que estaban delante como las que estaban escondidas al cambiar.
        foreach (var view in Enum.GetValues<AppView>())
        {
            show.Invoke(window, [view]);
            Settle(window, TimeSpan.FromMilliseconds(200));
        }

        Loc.Use(language);
        applySettings.Invoke(window, null);

        foreach (var view in Enum.GetValues<AppView>())
        {
            show.Invoke(window, [view]);
            Settle(window, TimeSpan.FromMilliseconds(400));

            var shown = Shown(window).ToList();
            var stale = language == "en" ? onlySpanish : onlyEnglish;

            foreach (var (text, where) in shown)
            {
                foreach (string old in stale.Where(o => Mentions(text, o)))
                    Check(false, $"{language} · {view}: se ve «{text}», que es del otro idioma («{old}»), en {Trace(where)}");

                if (keyShape.IsMatch(text) && es.ContainsKey(text))
                    Check(false, $"{language} · {view}: se ve la clave «{text}» en vez de su texto, en {Trace(where)}");
            }

            Check(shown.Count > 10, $"{language} · {view}: hay algo que mirar ({shown.Count} textos)");
        }
    }

    window.Dispose();
}
catch (Exception e)
{
    Broke("En caliente: la ventana entera", e);
}

Console.WriteLine();
Console.WriteLine(new string('=', 48));
Console.WriteLine(failures == 0
    ? $"CORRECTO: {checks} comprobaciones"
    : $"FALLOS: {failures} de {checks} comprobaciones");
Console.WriteLine(new string('=', 48));
return failures == 0 ? 0 : 1;

// ---- ayudantes ----

// La raíz del repositorio, subiendo desde donde se esté hasta dar con la solución.
static string Root()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "VibeScreen.slnx"))) return dir.FullName;

    for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "VibeScreen.slnx"))) return dir.FullName;

    throw new DirectoryNotFoundException("no se encuentra VibeScreen.slnx subiendo desde aquí");
}

// Un catálogo, leído a mano para poder decir si una clave está dos veces: el lector de diccionarios
// se queda con la última sin avisar, y la otra traducción desaparecería sin que nadie lo viera.
Dictionary<string, string> Read(string path)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    var reader = new Utf8JsonReader(File.ReadAllBytes(path), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });

    reader.Read();
    if (reader.TokenType != JsonTokenType.StartObject) throw new InvalidDataException($"{path}: no es un objeto");

    while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
    {
        string key = reader.GetString()!;
        reader.Read();
        if (reader.TokenType != JsonTokenType.String)
        {
            Check(false, $"{Path.GetFileName(path)}: «{key}» no es un texto");
            reader.Skip();
            continue;
        }

        Check(!result.ContainsKey(key), $"{Path.GetFileName(path)}: «{key}» está dos veces");
        result[key] = reader.GetString()!;
    }

    return result;
}

// Los huecos de un texto: {0}, {1:N0}… Las llaves dobles son una llave escrita, no un hueco.
HashSet<int> Holes(string text, string key)
{
    var holes = placeholder.Matches(text).Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
    if (key.EndsWith(".one", StringComparison.Ordinal)) holes.Remove(0);
    return holes;
}

// Si string.Format sabe rellenarlo con argumentos de sobra.
static bool Formats(string text)
{
    try
    {
        _ = string.Format(System.Globalization.CultureInfo.InvariantCulture, text, new object?[10]);
        return true;
    }
    catch (FormatException)
    {
        return false;
    }
}

static IEnumerable<string> Sources(string folder) =>
    Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
        .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part is "obj" or "bin"));

static int Line(string code, int index) => code.AsSpan(0, index).Count('\n') + 1;

// Los textos de un idioma que delatan que se está viendo ese idioma: los que no dicen lo mismo en el
// otro y tienen algo de largo. «Panel» es igual en los dos y no delata nada. De los que llevan huecos
// cuentan los trozos fijos que sean frase —«Siguiente intento en»—, que es lo que se ve aunque los
// datos cambien.
static List<string> Distinct(Dictionary<string, string> mine, Dictionary<string, string> other)
{
    var hole = new Regex(@"\{\d+(?:[,:][^}]*)?\}");
    string theirs = string.Join("\n", other.Values);

    var found = new List<string>();
    foreach (var (key, value) in mine)
    {
        if (other.TryGetValue(key, out string? same) && string.Equals(same, value, StringComparison.OrdinalIgnoreCase)) continue;

        IEnumerable<string> pieces = hole.IsMatch(value)
            ? hole.Split(value)
                .Select(p => p.Trim(' ', '.', ',', ':', '·', '(', ')', '«', '»', '“', '”'))
                .Where(p => p.Contains(' ', StringComparison.Ordinal) && p.Length >= 10)
            : [value.Trim()];

        foreach (string piece in pieces.Where(p => p.Length >= 4 && !theirs.Contains(p, StringComparison.OrdinalIgnoreCase)))
            found.Add(piece);
    }

    return [.. found.Distinct(StringComparer.Ordinal)];
}

// Si un texto dice otro, sin mirar mayúsculas. Una frase basta con que aparezca entera, como
// palabras: «COM3 · sin servicio» dice «sin servicio». Una palabra suelta tiene que ser el texto
// entero: los nombres de los sensores vienen en inglés —«System Fan #1»— y una palabra del catálogo
// dentro de uno de ellos no es un resto del otro idioma, es un dato.
static bool Mentions(string text, string value) =>
    value.Contains(' ', StringComparison.Ordinal)
        ? Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(value)}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
        : string.Equals(text.Trim(), value, StringComparison.OrdinalIgnoreCase);

// Dónde está un control, para encontrarlo: su tipo y los de los padres que tengan nombre.
static string Trace(Visual where)
{
    var parts = new List<string>();
    for (Visual? at = where; at is not null && parts.Count < 6; at = at.GetVisualParent())
        parts.Add(at is Control { Name: { Length: > 0 } name } ? $"{at.GetType().Name}#{name}" : at.GetType().Name);
    return string.Join(" < ", parts);
}

// Deja que la ventana haga lo suyo: sondeos, respuestas del servicio, enlaces y medida.
static void Settle(Window window, TimeSpan time)
{
    var until = DateTime.UtcNow + time;
    do
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1456, 908));
        window.Arrange(new Rect(0, 0, 1456, 908));
        Thread.Sleep(15);
    }
    while (DateTime.UtcNow < until);
    Dispatcher.UIThread.RunJobs();
}

// Todo lo que se lee en la ventana: textos, etiquetas flotantes, nombres para el lector de pantalla,
// textos de las casillas vacías y los menús del botón derecho.
//
// Se salta lo que no es de la aplicación: las filas de sensores, que traen el nombre que les da el
// hardware —hay uno que se llama «System», a secas—, y los nombres para el lector de pantalla de las
// piezas de las plantillas de Avalonia, que vienen en inglés de fábrica: el pulgar de una barra de
// desplazamiento se llama «Position» en cualquier idioma.
static IEnumerable<(string Text, Visual Where)> Shown(Window window)
{
    foreach (var visual in window.GetVisualDescendants())
    {
        if (visual.GetVisualAncestors().Any(a => a.GetType().Name == "SensorRow")) continue;

        if (visual is TextBlock { Text: { Length: > 0 } text }) yield return (text, visual);
        if (visual is not Control control) continue;

        if (ToolTip.GetTip(control) is string { Length: > 0 } tip) yield return (tip, visual);
        if (control.TemplatedParent is null && AutomationProperties.GetName(control) is { Length: > 0 } name)
            yield return (name, visual);
        if (control is TextBox { PlaceholderText: { Length: > 0 } hint }) yield return (hint, visual);
        if (control is ComboBox { PlaceholderText: { Length: > 0 } choose }) yield return (choose, visual);
        if (control is AutoCompleteBox { PlaceholderText: { Length: > 0 } search }) yield return (search, visual);
        if (control is ContentControl { Content: string { Length: > 0 } content }) yield return (content, visual);
    }
}

/// <summary>Un catálogo: sus dos idiomas, dónde se usan sus claves y cómo se reconoce un uso.</summary>
internal sealed record Catalog(string Name, string Folder, string[] Sources, Regex Usage)
{
    public Dictionary<string, string> Es { get; set; } = [];

    public Dictionary<string, string> En { get; set; } = [];
}
