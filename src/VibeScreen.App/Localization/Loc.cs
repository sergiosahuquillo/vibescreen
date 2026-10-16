using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;

namespace VibeScreen.App.Localization;

/// <summary>
/// El idioma de la ventana: los catálogos, cuál está puesto y cómo se le pasa a los controles.
/// </summary>
/// <remarks>
/// <para>
/// Sin biblioteca. Con español e inglés, un plural son dos claves —<c>.one</c> y <c>.other</c>, la
/// regla de los dos: uno es singular y todo lo demás plural—, y el resto es buscar una cadena y
/// rellenarla. Si algún día entra un idioma con plurales de verdad complicados, es el momento de
/// mirar MessageFormat.
/// </para>
/// <para>
/// El cambio en caliente es el mismo truco que la letra de los datos en <see cref="AppFonts"/>: una
/// sola clave en los recursos de la aplicación que cambia al cambiar de idioma, y cada texto enlazado
/// a ella. Una notificación por cambio, y ningún evento estático: el enlace muere con el control, y
/// las filas que se rehacen —sensores, widgets— no se quedan enganchadas a nada.
/// </para>
/// <para>
/// El español es la fuente: una clave que falte en otro idioma sale en español, y una que falte en
/// español sale tal cual, que se ve a la legua. La <c>LocalizationCheck</c> se asegura de que no
/// pase ninguna de las dos.
/// </para>
/// </remarks>
public static class Loc
{
    /// <summary>La clave de los recursos que cambia con el idioma. Lo que se enlaza a ella se vuelve a pintar.</summary>
    public const string ResourceKey = "VibeScreenLanguage";

    /// <summary>El idioma en el que está escrito todo, y al que se cae lo que falte en los demás.</summary>
    public const string Source = "es";

    /// <summary>
    /// Los idiomas que hay, cada uno con su nombre en su propia lengua: quien no entiende el idioma
    /// puesto tiene que poder encontrar el suyo.
    /// </summary>
    public static IReadOnlyList<(string Code, string Name)> Languages { get; } =
    [
        ("es", "Español"),
        ("en", "English"),
    ];

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Catalogs = new(StringComparer.Ordinal);

    private static readonly ConditionalWeakTable<AvaloniaObject, Dictionary<AvaloniaProperty, Bound>> Links = new();

    private static CultureInfo _systemUi = CultureInfo.CurrentUICulture;
    private static CultureInfo _systemFormat = CultureInfo.CurrentCulture;
    private static IReadOnlyDictionary<string, string>? _current;
    private static IReadOnlyDictionary<string, string>? _source;

    /// <summary>El idioma puesto: <c>es</c> o <c>en</c>.</summary>
    public static string Current { get; private set; } = Source;

    /// <summary>
    /// Con qué se escriben las cifras de la interfaz.
    /// </summary>
    /// <remarks>
    /// Se pide siempre a mano y nunca se deja en la cultura del hilo, que se queda invariante a
    /// propósito: así nada cambia de formato sin que alguien lo haya pedido, y el panel y su vista
    /// previa —que pinta Core— siguen escribiendo igual en todas las máquinas. Si la configuración
    /// regional del sistema es del mismo idioma, se usa esa —un inglés británico sigue viendo sus
    /// separadores—; si no, la del idioma a secas.
    /// </remarks>
    public static CultureInfo Culture { get; private set; } = CultureInfo.InvariantCulture;

    /// <summary>El idioma del sistema, si es uno de los que hay; si no, inglés, que lo entiende más gente.</summary>
    public static string System =>
        Languages.Any(l => l.Code == _systemUi.TwoLetterISOLanguageName) ? _systemUi.TwoLetterISOLanguageName : "en";

    /// <summary>
    /// Apunta la cultura del sistema y deja la del proceso invariante.
    /// </summary>
    /// <remarks>
    /// Lo primero del arranque. La aplicación compila sin <c>InvariantGlobalization</c> para poder
    /// saber en qué idioma está el sistema, y eso pone la cultura del proceso en la del usuario: sin
    /// esto, cada cifra formateada sin decir con qué, y el contador de decimales de Avalonia, pasarían
    /// a escribir «12,5» sin que nadie lo hubiera pedido.
    /// </remarks>
    public static void CaptureSystem()
    {
        _systemUi = CultureInfo.CurrentUICulture;
        _systemFormat = CultureInfo.CurrentCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    /// <summary>
    /// Pone un idioma, o el del sistema con null, y se lo dice a todo lo que esté enlazado.
    /// </summary>
    /// <remarks>
    /// Se puede llamar antes de que exista la aplicación —el arranque lo hace para tener los textos
    /// de la consola—; entonces solo carga el catálogo, y la siguiente llamada con la aplicación ya en
    /// pie es la que avisa a los controles.
    /// </remarks>
    public static void Use(string? language)
    {
        string code = language is not null && Languages.Any(l => l.Code == language) ? language : System;

        Current = code;
        _source = Catalog(Source);
        _current = Catalog(code);
        Culture = CultureFor(code);

        if (Application.Current is { } app) app.Resources[ResourceKey] = code;
    }

    /// <summary>Un texto del catálogo, sin resolver: se pinta en el idioma que haya cuando se pinte.</summary>
    public static Text L(string key, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Text.FromKey(key, args);
    }

    /// <summary>
    /// Un texto con plural: busca <c>clave.one</c> o <c>clave.other</c> según el número, que va
    /// como <c>{0}</c> y empuja los demás argumentos un sitio.
    /// </summary>
    public static Text Plural(string key, long count, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Text.FromPlural(key, count, args);
    }

    /// <summary>Un dato que no se traduce. Lo mismo que <see cref="Text.Raw"/>, para donde <c>Text</c> es otra cosa.</summary>
    public static Text Raw(string? value) => Text.Raw(value);

    /// <summary>Un texto compuesto con código al pintarlo. Lo mismo que <see cref="Text.Of"/>.</summary>
    public static Text Of(Func<string> compose) => Text.Of(compose);

    /// <summary>Nada que decir.</summary>
    public static Text Empty => Text.Empty;

    /// <summary>Un aviso del servicio o de Core, en el idioma de la ventana.</summary>
    public static Text Notice(VibeScreen.Contracts.Notice notice) => Text.From(notice);

    /// <summary>Varios avisos, uno por renglón y con su viñeta: los problemas de un tema, los avisos del inspector.</summary>
    public static Text Notices(IEnumerable<VibeScreen.Contracts.Notice> notices, string bullet = "• ")
    {
        ArgumentNullException.ThrowIfNull(notices);
        var list = notices.ToList();
        return Text.Of(() => string.Join(Environment.NewLine,
            list.Select(n => bullet + VibeScreen.Contracts.Notices.Render(n, Current))));
    }

    /// <summary>
    /// El texto ya resuelto, en el idioma de ahora. Para lo que se enseña y desaparece —un aviso, un
    /// diálogo— y no tiene que cambiar con el idioma.
    /// </summary>
    public static string S(string key, params object?[] args) => L(key, args).ToString();

    /// <summary>Una cifra con el formato del idioma.</summary>
    public static string Number(double value, string format = "N0") => value.ToString(format, Culture);

    /// <summary>
    /// Pone un texto en una propiedad y lo deja siguiendo al idioma.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La única forma de escribir texto para el usuario en un control. Un dato (<see cref="Text.Raw"/>)
    /// se pone tal cual; lo demás se enlaza a <see cref="ResourceKey"/> y se vuelve a resolver en cada
    /// cambio de idioma. Volver a ponerlo suelta el enlace de antes —un enlace no se para poniendo un
    /// valor a mano, lo dice <see cref="AppFonts.FollowData"/>—, y ponerlo igual que ya estaba no hace
    /// nada, que es lo que deja al sondeo escribir lo mismo cada segundo sin rehacer nada.
    /// </para>
    /// <para>
    /// Lo que no es un control, como una entrada del menú de la bandeja, sigue a la aplicación.
    /// </para>
    /// </remarks>
    public static T Set<T>(T target, AvaloniaProperty property, Text text)
        where T : AvaloniaObject
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);

        var links = Links.GetOrCreateValue(target);
        if (links.TryGetValue(property, out var old))
        {
            if (old.Text.Equals(text)) return target;
            old.Link.Dispose();
            links.Remove(property);
        }

        if (text.IsFixed || Host(target) is not { } host)
        {
            target.SetValue(property, text.ToString());
            return target;
        }

        links[property] = new Bound(text, target.Bind(property, host.GetResourceObservable(ResourceKey, _ => text.ToString())));
        return target;
    }

    /// <summary>
    /// Deja una propiedad que no es texto siguiendo al idioma, como el formato de un contador.
    /// </summary>
    public static IDisposable Follow(Control control, AvaloniaProperty property, Func<object?> value)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(value);
        return control.Bind(property, control.GetResourceObservable(ResourceKey, _ => value()));
    }

    /// <summary>La plantilla de una clave en el idioma puesto, o en español si falta, o la clave si falta en los dos.</summary>
    internal static string Template(string key)
    {
        if (_current is null) Use(null);
        if (_current!.TryGetValue(key, out string? text)) return text;
        if (_source!.TryGetValue(key, out text)) return text;
        return key;
    }

    /// <summary>Rellena una plantilla. Sin argumentos no la toca: una llave suelta en el texto no es un error.</summary>
    internal static string Format(string template, object?[]? args)
    {
        if (args is not { Length: > 0 }) return template;

        var resolved = new object?[args.Length];
        for (int i = 0; i < args.Length; i++) resolved[i] = args[i] is Text text ? text.ToString() : args[i];

        try
        {
            return string.Format(Culture, template, resolved);
        }
        catch (FormatException)
        {
            // Una traducción con las llaves mal no puede tirar la ventana. La LocalizationCheck la
            // caza antes; si se escapa, se ve la plantilla sin rellenar y no una excepción.
            return template;
        }
    }

    private static IResourceHost? Host(AvaloniaObject target) => target switch
    {
        IResourceHost host => host,
        _ => Application.Current,
    };

    private static IReadOnlyDictionary<string, string> Catalog(string code)
    {
        lock (Catalogs)
        {
            if (Catalogs.TryGetValue(code, out var catalog)) return catalog;

            catalog = Read(code);
            Catalogs[code] = catalog;
            return catalog;
        }
    }

    private static IReadOnlyDictionary<string, string> Read(string code)
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream($"VibeScreen.Localization.{code}.json");
        if (stream is null) return new Dictionary<string, string>(StringComparer.Ordinal);
        return JsonSerializer.Deserialize(stream, LocJsonContext.Default.DictionaryStringString)
               ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static CultureInfo CultureFor(string code)
    {
        if (string.Equals(_systemFormat.TwoLetterISOLanguageName, code, StringComparison.Ordinal)) return _systemFormat;

        try
        {
            return CultureInfo.GetCultureInfo(code);
        }
        catch (CultureNotFoundException)
        {
            // Donde el proceso corre con InvariantGlobalization —los verificadores— no hay culturas
            // que pedir. Los textos se resuelven igual; las cifras, en invariante.
            return CultureInfo.InvariantCulture;
        }
    }

    private sealed record Bound(Text Text, IDisposable Link);
}

[JsonSourceGenerationOptions(ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class LocJsonContext : JsonSerializerContext;
