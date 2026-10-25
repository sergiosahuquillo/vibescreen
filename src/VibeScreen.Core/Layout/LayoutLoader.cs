using System.Text.Json;
using SkiaSharp;
using VibeScreen.Contracts;
using VibeScreen.Core.Storage;

namespace VibeScreen.Core.Layout;

/// <summary>Un layout que no se puede usar, con todos sus problemas a la vez en lugar de uno por intento.</summary>
/// <remarks>
/// Los problemas son avisos y no frases: la ventana los enseña en su idioma, y el mensaje de la
/// excepción —el de la CLI y los logs— los junta en español.
/// </remarks>
public sealed class LayoutException(string source, IReadOnlyList<Notice> problems, Exception? innerException = null)
    : Exception($"{source}: {string.Join(" ", problems)}", innerException), INoticed
{
    public IReadOnlyList<Notice> Problems { get; } = problems;

    /// <summary>
    /// Lo que se cuenta del layout en una línea: cuál es y su primer problema, con cuántos más hay.
    /// </summary>
    /// <remarks>Para donde no cabe la lista entera: un aviso del panel, el detalle de una orden.</remarks>
    public Notice Notice { get; } = problems.Count switch
    {
        0 => Notice.Of("layout.invalid", source),
        1 => Notice.Of("layout.invalid.single", source).With(problems[0]),
        _ => Notice.Of("layout.invalid.several", source, problems.Count - 1).With(problems[0]),
    };
}

public static class LayoutLoader
{
    public const int CurrentSchema = 2;

    /// <summary>Por debajo de esto no hay sensor que cambie, y solo se gasta CPU en leerlo.</summary>
    public const int MinRefreshMs = 100;

    /// <summary>Menos de dos muestras no son un histórico.</summary>
    public const int MinChartSamples = 2;

    /// <summary>
    /// Muestras máximas de un histórico.
    /// </summary>
    /// <remarks>
    /// De aquí sale un <c>float[]</c> del tamaño que diga el fichero, y el fichero llega por una
    /// tubería abierta a todos los usuarios locales que atiende un proceso que en Windows corre como
    /// SYSTEM: sin cota, un JSON de doscientos bytes pide gigabytes. Y son de sobra para lo que se
    /// puede ver, que es un panel de unos cientos de píxeles de ancho.
    /// </remarks>
    public const int MaxChartSamples = 4096;

    /// <summary>
    /// Lado máximo del panel, en píxeles.
    /// </summary>
    /// <remarks>
    /// Por lo mismo que <see cref="MaxChartSamples"/>: de aquí salen dos mapas de bits y el búfer de
    /// envío, los tres del tamaño que diga el fichero. Además deja las cuentas de bytes —el área por
    /// dos— dentro de un <c>int</c>, que es donde viven. El editor ya no ofrece más de 2048, así que
    /// el tope no quita nada de lo que hoy se puede hacer.
    /// </remarks>
    public const int MaxDisplaySide = 4096;

    public static LayoutDocument Load(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LayoutException(path, [Notice.Of("layout.unreadable", e.Message)], e);
        }

        var layout = Parse(json, path);
        layout.Directory = Path.GetDirectoryName(Path.GetFullPath(path));
        return layout;
    }

    public static LayoutDocument Parse(string json, string source = "layout")
    {
        LayoutDocument? layout;
        try
        {
            layout = JsonSerializer.Deserialize(json, LayoutJsonContext.Default.LayoutDocument);
        }
        catch (JsonException e)
        {
            // Lo que dice System.Text.Json va tal cual, como dato y en su idioma. Lo nuestro —un
            // enumerado con un valor que no existe— lleva su aviso y se traduce.
            var what = Notice.From(e);
            var problem = e.Path is not null && !e.Message.Contains(e.Path, StringComparison.Ordinal)
                ? Notice.Of("layout.json.at", e.Path, (e.LineNumber ?? 0) + 1).With(what)
                : what;
            throw new LayoutException(source, [problem], e);
        }

        if (layout is null) throw new LayoutException(source, [Notice.Of("layout.empty")]);

        var problems = Validate(layout);
        if (problems.Count > 0) throw new LayoutException(source, problems);
        return layout;
    }

    /// <summary>El layout en JSON, tal como lo escribe el editor.</summary>
    /// <exception cref="LayoutException">El layout no es válido. No se serializa un tema que no se podría volver a leer.</exception>
    public static string Serialize(LayoutDocument layout)
    {
        var problems = Validate(layout);
        if (problems.Count > 0) throw new LayoutException("layout", problems);

        return JsonSerializer.Serialize(layout, LayoutJsonContext.Default.LayoutDocument);
    }

    /// <summary>Una copia independiente de un widget, con todo lo suyo.</summary>
    /// <remarks>
    /// <para>
    /// Por una ida y vuelta del serializador, y no con <c>with</c>, porque <c>with</c> copia por
    /// encima: un texto y una gráfica llevan <see cref="ColorLevels"/> y un arco lleva un
    /// <see cref="GaugeLabel"/>, el editor los cambia en sitio, y la copia se los quedaría
    /// compartidos con el original. Tocarle el color al duplicado se lo cambiaría al primero.
    /// </para>
    /// <para>
    /// Y no con un <c>switch</c> por tipo, que habría que acordarse de tocar el día que se añada un
    /// widget o un objeto anidado. Así es profunda por construcción y no por haberse acordado.
    /// </para>
    /// <para>
    /// Sale exacta porque ninguna propiedad anulable del modelo lleva inicializador: lo que se omite
    /// al escribir —los nulos— vuelve como nulo. Hay una comprobación que lo fija.
    /// </para>
    /// </remarks>
    public static LayoutWidget Copy(LayoutWidget widget)
    {
        ArgumentNullException.ThrowIfNull(widget);

        string json = JsonSerializer.Serialize(widget, LayoutJsonContext.Default.LayoutWidget);
        return JsonSerializer.Deserialize(json, LayoutJsonContext.Default.LayoutWidget)
               ?? throw new InvalidOperationException("La copia del widget ha salido vacía.");
    }

    /// <summary>Cómo se llama la imagen de fondo dentro de la carpeta del layout.</summary>
    /// <remarks>
    /// Nombre fijo y no el original: un layout tiene un fondo y solo uno, así que no hay nada que
    /// distinguir, y con un nombre fijo cambiar la imagen no deja la anterior criando polvo.
    /// </remarks>
    public const string BackgroundName = "fondo";

    /// <summary>
    /// Guarda el layout y lo que necesita para pintarse.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La imagen de fondo <b>se copia</b> a la carpeta del layout, no se apunta dónde estaba. Eso es
    /// lo que hace que el layout se sostenga solo: el usuario puede mover la foto, renombrarla,
    /// borrarla o vaciar la carpeta de descargas donde la tenía, y el panel sigue pintando. Y la
    /// carpeta entera se copia a otra máquina y funciona igual.
    /// </para>
    /// <para>
    /// Las imágenes de los widgets, igual, pero a <see cref="ImagesFolder"/> y con su nombre: puede
    /// haber varias. Lo que quede allí sin que ningún widget lo nombre se borra, ya con el JSON escrito.
    /// </para>
    /// <para>
    /// Primero se valida, después se toca el disco: así un layout que no se puede guardar no deja
    /// una copia de la imagen ni un documento a medio cambiar. El JSON se escribe entero o no se
    /// escribe, con <see cref="AtomicFile"/>.
    /// </para>
    /// </remarks>
    public static void Save(LayoutDocument layout, string path)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string full = Path.GetFullPath(path);
        string folder = Path.GetDirectoryName(full)!;

        // Antes de tocar nada: ni media copia de la imagen ni el documento cambiado si no se guarda.
        var problems = Validate(layout);
        if (problems.Count > 0) throw new LayoutException(full, problems);

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LayoutException(full, [Notice.Of("layout.cannot-create-folder", e.Message)], e);
        }

        // El documento no se queda con la ruta nueva hasta que el fichero está escrito: si falla el
        // disco, una relativa se resolvería contra la carpeta vieja y la imagen desaparecería de la
        // vista previa —y al guardar de vuelta allí, se perdería para siempre—.
        string? original = (layout.Background as ImageBackground)?.Path;
        if (layout.Background is ImageBackground image) image.Path = BringIn(image.Path, layout, folder);

        var pictures = layout.Widgets.OfType<ImageWidget>().Select(w => (Widget: w, Before: w.Path)).ToList();

        try
        {
            BringInImages(layout, folder);
            AtomicFile.WriteAllText(full, Serialize(layout));
        }
        catch (Exception e)
        {
            if (original is not null && layout.Background is ImageBackground failed) failed.Path = original;
            foreach (var (widget, before) in pictures) widget.Path = before;
            throw e is LayoutException
                ? e
                : new LayoutException(full, [Notice.Of("layout.cannot-save", e.Message)], e);
        }

        layout.Directory = folder;
        ForgetOldImages(layout, folder);
    }

    /// <summary>
    /// Guarda el layout con el nombre por el que se le conoce, que queda dentro del fichero además
    /// de dar nombre a su carpeta.
    /// </summary>
    /// <remarks>
    /// El nombre entra en el documento <b>antes</b> de escribir, porque el fichero es de donde lo
    /// leen la pestaña Panel y <c>service status</c>. Puesto después, en el disco se quedaba el del
    /// guardado anterior, y ni volviendo a guardar se arreglaba: al reabrir el tema se releía el
    /// viejo. Si no se llega a escribir, el documento se queda con el nombre que tenía.
    /// </remarks>
    public static void Save(LayoutDocument layout, string path, string name)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // Una copia y no el mismo LayoutMeta cambiado: así deshacer es devolver el de antes.
        var previous = layout.Meta;
        layout.Meta = (previous ?? new LayoutMeta()) with { Name = name };

        try
        {
            Save(layout, path);
        }
        catch
        {
            layout.Meta = previous;
            throw;
        }
    }

    /// <summary>
    /// Trae la imagen a la carpeta del layout y devuelve el nombre con el que queda dentro.
    /// </summary>
    /// <remarks>
    /// Si la original ya no está pero dentro hay una copia de un guardado anterior, esa es la que
    /// vale: es justo el caso para el que se copia. Y si no hay ni una ni otra, se deja la ruta como
    /// estaba y que lo cuente <c>LayoutInspector</c>; guardar no es el momento de discutirlo.
    /// </remarks>
    private static string BringIn(string path, LayoutDocument layout, string folder)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;

        string source = Path.GetFullPath(layout.Resolve(path));
        string target = Path.Combine(folder, BackgroundName + Path.GetExtension(source));

        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            return Path.GetFileName(target);

        try
        {
            if (!File.Exists(source))
                return File.Exists(target) ? Path.GetFileName(target) : path;

            File.Copy(source, target, overwrite: true);
            ForgetOldBackgrounds(folder, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LayoutException(folder, [Notice.Of("layout.cannot-copy-background", e.Message)], e);
        }

        return Path.GetFileName(target);
    }

    /// <summary>Subcarpeta del tema donde se copian las imágenes de los widgets.</summary>
    /// <remarks>
    /// Aparte del fondo porque son varias y conservan su nombre, y así limpiar lo que ya no se usa
    /// es mirar una carpeta que solo escribe esto, sin riesgo de llevarse nada más.
    /// </remarks>
    public const string ImagesFolder = "imagenes";

    /// <summary>
    /// Trae cada imagen de widget a <see cref="ImagesFolder"/> y deja su ruta relativa al tema.
    /// </summary>
    /// <remarks>
    /// Si ya hay allí otra distinta con el mismo nombre, la nueva se queda con «nombre-2», y así
    /// cambiar la imagen de un widget no pisa la de otro. Si la original ya no está pero dentro hay
    /// una copia, vale la copia, como con el fondo.
    /// </remarks>
    private static void BringInImages(LayoutDocument layout, string folder)
    {
        string images = Path.Combine(folder, ImagesFolder);

        // Qué fichero de dentro se ha quedado cada original en este guardado: dos widgets con la misma
        // imagen comparten copia, y dos imágenes distintas con el mismo nombre no.
        var taken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var widget in layout.Widgets.OfType<ImageWidget>())
        {
            if (string.IsNullOrWhiteSpace(widget.Path)) continue;

            string source = Path.GetFullPath(layout.Resolve(widget.Path));
            try
            {
                widget.Path = BringInImage(source, widget.Path, images, taken);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new LayoutException(folder, [Notice.Of("layout.cannot-copy-image", widget.Path, e.Message)], e);
            }
        }
    }

    private static string BringInImage(string source, string path, string images, Dictionary<string, string> taken)
    {
        string name = Path.GetFileName(source);

        // Ya está dentro: de un guardado anterior en esta misma carpeta.
        if (string.Equals(Path.GetDirectoryName(source), Path.TrimEndingDirectorySeparator(images), StringComparison.OrdinalIgnoreCase))
        {
            taken.TryAdd(source, source);
            return Relative(name);
        }

        if (!File.Exists(source))
            return File.Exists(Path.Combine(images, name)) ? Relative(name) : path;

        string stem = Path.GetFileNameWithoutExtension(name);
        string extension = Path.GetExtension(name);
        for (int n = 1; ; n++)
        {
            string candidate = n == 1 ? name : $"{stem}-{n}{extension}";
            string target = Path.Combine(images, candidate);

            if (taken.TryGetValue(target, out string? owner))
            {
                if (string.Equals(owner, source, StringComparison.OrdinalIgnoreCase)) return Relative(candidate);
                continue;
            }

            // Uno de un guardado anterior: si es la misma imagen se aprovecha, y si es otra no se pisa
            // todavía, que puede ser la de otro widget que viene detrás en la lista.
            if (File.Exists(target) && !SameContent(source, target)) continue;

            Directory.CreateDirectory(images);
            if (!File.Exists(target)) File.Copy(source, target);
            taken[target] = source;
            return Relative(candidate);
        }
    }

    /// <summary>La ruta con la que el tema nombra una imagen de dentro. Con «/», que vale en los dos sistemas.</summary>
    private static string Relative(string name) => ImagesFolder + "/" + name;

    private static bool SameContent(string a, string b) =>
        new FileInfo(a).Length == new FileInfo(b).Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));

    /// <summary>
    /// Borra de <see cref="ImagesFolder"/> lo que ya no nombra ningún widget. Con el JSON ya escrito:
    /// antes, un guardado que fallara dejaría al fichero viejo nombrando imágenes borradas.
    /// </summary>
    private static void ForgetOldImages(LayoutDocument layout, string folder)
    {
        string images = Path.Combine(folder, ImagesFolder);
        if (!Directory.Exists(images)) return;

        var used = layout.Widgets.OfType<ImageWidget>()
            .Select(w => Path.GetFullPath(layout.Resolve(w.Path)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (string file in Directory.EnumerateFiles(images))
                if (!used.Contains(Path.GetFullPath(file))) File.Delete(file);

            if (!Directory.EnumerateFileSystemEntries(images).Any()) Directory.Delete(images);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Queda alguna imagen de sobra en la carpeta. No estorba a nadie.
        }
    }

    /// <summary>Las imágenes que dejó un fondo anterior de otro formato. Solo las que puso esto.</summary>
    private static void ForgetOldBackgrounds(string folder, string keep)
    {
        try
        {
            foreach (string stale in Directory.EnumerateFiles(folder, BackgroundName + ".*"))
            {
                // En Windows el patrón «fondo.*» encaja también con un «fondo» a secas, sin extensión,
                // y ese no lo ha escrito esto: solo se borra lo que tiene la forma que esto escribe.
                if (Path.GetExtension(stale).Length == 0) continue;
                if (string.Equals(stale, keep, StringComparison.OrdinalIgnoreCase)) continue;
                File.Delete(stale);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Queda una imagen de sobra en la carpeta. No estorba a nadie.
        }
    }

    /// <summary>Lo que el formato JSON no puede comprobar por sí solo: coherencia entre campos.</summary>
    public static IReadOnlyList<Notice> Validate(LayoutDocument layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var problems = new List<Notice>();

        if (layout.Schema != CurrentSchema)
            problems.Add(layout.Schema < CurrentSchema
                ? Notice.Of("layout.schema.old", layout.Schema, CurrentSchema)
                : Notice.Of("layout.schema.new", layout.Schema, CurrentSchema));

        var display = layout.Display;
        if (display.Width <= 0 || display.Height <= 0)
            problems.Add(Notice.Of("layout.display.invalid", display.Width, display.Height));
        else if (display.Width > MaxDisplaySide || display.Height > MaxDisplaySide)
            problems.Add(Notice.Of("layout.display.too-big", display.Width, display.Height, MaxDisplaySide));

        switch (layout.Background)
        {
            case ColorBackground background when !SKColor.TryParse(background.Color, out _):
                problems.Add(Notice.Of("layout.background.color", background.Color));
                break;

            case ImageBackground image:
                if (string.IsNullOrWhiteSpace(image.Path))
                    problems.Add(Notice.Of("layout.background.no-path"));

                if (!SKColor.TryParse(image.Color, out _))
                    problems.Add(Notice.Of("layout.background.fallback", image.Color));
                break;
        }

        // Que la imagen no esté donde dice el tema no impide abrirlo: es un aviso de
        // LayoutInspector, como un sensor que esta máquina no tiene.

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < layout.Widgets.Count; i++)
        {
            var widget = layout.Widgets[i];
            if (widget is null)
            {
                problems.Add(Notice.Of("layout.widget.empty").With(Notice.Of("layout.where.index", i)));
                continue;
            }

            // De qué widget se habla: por su id, o por su sitio en la lista si no lo tiene. Va dentro
            // de cada problema como aviso propio, porque hasta las comillas cambian con el idioma.
            var label = string.IsNullOrWhiteSpace(widget.Id)
                ? Notice.Of("layout.where.index", i)
                : Notice.Of("layout.where.widget", widget.Id);
            if (string.IsNullOrWhiteSpace(widget.Id))
                problems.Add(Notice.Of("layout.widget.no-id").With(label));
            else if (!ids.Add(widget.Id))
                problems.Add(Notice.Of("layout.widget.duplicate-id").With(label));

            var r = widget.Rect;
            if (r.IsEmpty || r.X < 0 || r.Y < 0 || r.Right >= display.Width || r.Bottom >= display.Height)
                problems.Add(Notice.Of("layout.widget.outside", r, display.Width, display.Height).With(label));

            if (widget.RefreshMs < MinRefreshMs)
                problems.Add(Notice.Of("layout.widget.refresh", MinRefreshMs).With(label));

            switch (widget)
            {
                case TextWidget text:
                    ValidateText(text, label, problems);
                    break;
                case ClockWidget clock:
                    ValidateLabel(clock, label, problems);
                    if (string.IsNullOrWhiteSpace(clock.Format))
                        problems.Add(Notice.Of("layout.clock.no-format", ClockFormat.Default).With(label));
                    break;
                case GraphWidget graph:
                    ValidateGraph(graph, label, problems);
                    break;
                case ImageWidget image when string.IsNullOrWhiteSpace(image.Path):
                    problems.Add(Notice.Of("layout.image.no-path").With(label));
                    break;
            }
        }

        return problems;
    }

    private static void ValidateText(TextWidget text, Notice label, List<Notice> problems)
    {
        ValidateLabel(text, label, problems);

        if ((text.Sensor is null) == (text.Text is null))
            problems.Add(Notice.Of("layout.text.sensor-or-text").With(label));

        if (text.Sensor is null && (text.Prefix is not null || text.Suffix is not null))
            problems.Add(Notice.Of("layout.text.affixes").With(label));

        if (text.Decimals is < 0 or > TextComposer.MaxDecimals)
            problems.Add(Notice.Of("layout.text.decimals", TextComposer.MaxDecimals).With(label));

        if (text.Sensor is null && text.Levels is not null)
            problems.Add(Notice.Of("layout.text.levels").With(label));

        if (text.Levels is { } levels) ValidateLevels(levels, label, problems);
    }

    /// <summary>
    /// La regla de color por valor. El reloj no necesita comprobación: no tiene la propiedad, así
    /// que un «levels» en un reloj lo rechaza la capa JSON por miembro desconocido.
    /// </summary>
    private static void ValidateLevels(ColorLevels levels, Notice label, List<Notice> problems)
    {
        foreach ((string color, string band) in new[]
                 { (levels.Low, "layout.levels.color.low"), (levels.Medium, "layout.levels.color.medium"), (levels.High, "layout.levels.color.high") })
            if (!SKColor.TryParse(color, out _))
                problems.Add(Notice.Of(band, color).With(label));

        if (!float.IsFinite(levels.MediumFrom) || !float.IsFinite(levels.HighFrom))
        {
            problems.Add(Notice.Of("layout.levels.not-numbers").With(label));
            return;
        }

        // Cruzados no hay tres tramos, hay dos y un hueco que nadie sabría de qué color pintar.
        if (levels.MediumFrom >= levels.HighFrom)
            problems.Add(Notice.Of("layout.levels.crossed", levels.MediumFrom, levels.HighFrom).With(label));
    }

    /// <summary>
    /// El valor escrito dentro del arco. No comparte comprobaciones con los widgets de texto porque
    /// no comparte campos: el sensor, la unidad y la caja son los del arco.
    /// </summary>
    private static void ValidateGaugeLabel(GaugeLabel inside, Notice label, List<Notice> problems)
    {
        if (inside.Size <= 0)
            problems.Add(Notice.Of("layout.gauge-label.size").With(label));

        if (inside.Color is { } color && !SKColor.TryParse(color, out _))
            problems.Add(Notice.Of("layout.gauge-label.color", color).With(label));

        if (inside.Decimals is < 0 or > TextComposer.MaxDecimals)
            problems.Add(Notice.Of("layout.gauge-label.decimals", TextComposer.MaxDecimals).With(label));
    }

    /// <summary>Lo que hay que comprobar en todo lo que se escribe, sea un sensor o la hora.</summary>
    private static void ValidateLabel(LabelWidget widget, Notice label, List<Notice> problems)
    {
        if (!SKColor.TryParse(widget.Color, out _))
            problems.Add(Notice.Of("layout.widget.color", widget.Color).With(label));

        if (widget.Size <= 0)
            problems.Add(Notice.Of("layout.widget.size").With(label));
    }

    private static void ValidateGraph(GraphWidget graph, Notice label, List<Notice> problems)
    {
        if (string.IsNullOrWhiteSpace(graph.Sensor))
            problems.Add(Notice.Of("layout.graph.no-sensor").With(label));

        if (graph.Max <= graph.Min)
            problems.Add(Notice.Of("layout.graph.range", graph.Max, graph.Min).With(label));

        if (!SKColor.TryParse(graph.Color, out _))
            problems.Add(Notice.Of("layout.widget.color", graph.Color).With(label));

        if (graph.Background is { } track && !SKColor.TryParse(track, out _))
            problems.Add(Notice.Of("layout.graph.background", track).With(label));

        if (graph.Outline is { } outline)
        {
            if (!SKColor.TryParse(outline, out _))
                problems.Add(Notice.Of("layout.graph.outline", outline).With(label));

            if (graph.OutlineWidth <= 0)
                problems.Add(Notice.Of("layout.graph.outline-width").With(label));
        }

        if (graph.Levels is { } levels) ValidateLevels(levels, label, problems);

        switch (graph)
        {
            case BarWidget bar when bar.Radius < 0:
                problems.Add(Notice.Of("layout.bar.radius").With(label));
                break;

            case GaugeWidget gauge:
                // El contorno va por fuera de la banda, así que ensancha el anillo y también cuenta.
                float band = gauge.Thickness + (gauge.Outline is null ? 0 : Math.Max(0, gauge.OutlineWidth));
                if (gauge.Thickness <= 0)
                    problems.Add(Notice.Of("layout.widget.thickness").With(label));
                else if (band * 2 > Math.Min(graph.Rect.Width, graph.Rect.Height))
                    problems.Add(Notice.Of("layout.gauge.ring", band, graph.Rect.Width, graph.Rect.Height).With(label));

                if (gauge.SweepAngle is <= 0 or > 360)
                    problems.Add(Notice.Of("layout.gauge.sweep").With(label));

                if (gauge.Label is { } inside) ValidateGaugeLabel(inside, label, problems);
                break;

            case ChartWidget chart:
                if (chart.Samples < MinChartSamples)
                    problems.Add(Notice.Of("layout.chart.samples.min", MinChartSamples).With(label));
                else if (chart.Samples > MaxChartSamples)
                    problems.Add(Notice.Of("layout.chart.samples.max", MaxChartSamples).With(label));

                if (chart.Thickness <= 0)
                    problems.Add(Notice.Of("layout.widget.thickness").With(label));
                else if (chart.Thickness >= graph.Rect.Height)
                    problems.Add(Notice.Of("layout.chart.thickness", chart.Thickness, graph.Rect.Height).With(label));

                if (chart.Fill is { } fill && !SKColor.TryParse(fill, out _))
                    problems.Add(Notice.Of("layout.chart.fill", fill).With(label));
                break;
        }
    }
}
