using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeScreen.Contracts;

/// <summary>
/// Un mensaje para una persona, sin redactar: qué ha pasado, con un código, y los datos para contarlo.
/// </summary>
/// <remarks>
/// <para>
/// Core y el servicio no escriben frases: dicen <c>panel.not-found</c> y, si hace falta, con qué —un
/// puerto, unos segundos—. Quien lo enseña lo redacta en su idioma: la ventana en el que tenga puesto,
/// y la CLI y los logs en español, que es lo que da <see cref="ToString"/>. Así el servicio, que corre
/// como SYSTEM y no sabe quién mira, no tiene que saberlo.
/// </para>
/// <para>
/// Los argumentos van ya escritos, en invariante: son datos —una ruta, un número, el mensaje de una
/// excepción del sistema— y no se traducen. Lo que sí se traduce va en <see cref="Inner"/>, que es
/// otro aviso: «{causa} Siguiente intento en 5 s.» o «{widget «cpu»}: falta el id.» se redactan
/// enteros en el idioma de quien los lee.
/// </para>
/// </remarks>
public sealed record Notice
{
    /// <summary>Qué ha pasado: la clave de su texto en el catálogo de <see cref="Notices"/>.</summary>
    public required string Code { get; init; }

    /// <summary>Los datos del texto, por orden: <c>{0}</c>, <c>{1}</c>…</summary>
    public IReadOnlyList<string> Args { get; init; } = [];

    /// <summary>
    /// Otro aviso dentro de este: lo que lo ha provocado, o de qué se habla. Va detrás de los
    /// argumentos: con dos argumentos, es <c>{2}</c>.
    /// </summary>
    public Notice? Inner { get; init; }

    /// <summary>Un aviso con sus datos. Las cifras se escriben en invariante.</summary>
    public static Notice Of(string code, params object?[] args)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(args);

        return new Notice
        {
            Code = code,
            Args = [.. args.Select(arg => arg switch
            {
                null => "",
                string text => text,
                IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
                _ => arg.ToString() ?? "",
            })],
        };
    }

    /// <summary>El mismo aviso, con otro dentro: qué lo ha provocado, o de qué se habla.</summary>
    public Notice With(Notice? inner) => this with { Inner = inner };

    /// <summary>
    /// Lo que cuenta una excepción.
    /// </summary>
    /// <remarks>
    /// Las de aquí llevan su aviso. Las demás —una de E/S del sistema, una de System.Text.Json— traen
    /// su propio mensaje, en el idioma que sea, y viajan tal cual como dato.
    /// </remarks>
    public static Notice From(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is INoticed noticed ? noticed.Notice : Of("error.raw", exception.Message);
    }

    /// <summary>Redactado en español: para la CLI, los logs y el mensaje de las excepciones.</summary>
    public override string ToString() => Notices.Render(this);

    /// <summary>Iguales si dicen lo mismo: el mismo código con los mismos datos.</summary>
    public bool Equals(Notice? other) =>
        other is not null
        && string.Equals(Code, other.Code, StringComparison.Ordinal)
        && Args.SequenceEqual(other.Args, StringComparer.Ordinal)
        && Equals(Inner, other.Inner);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Code, StringComparer.Ordinal);
        foreach (string arg in Args) hash.Add(arg, StringComparer.Ordinal);
        hash.Add(Inner);
        return hash.ToHashCode();
    }
}

/// <summary>Una excepción que sabe contarse con un <see cref="Notice"/>.</summary>
public interface INoticed
{
    Notice Notice { get; }
}

/// <summary>
/// Un fallo con su aviso. El mensaje de la excepción es el aviso en español, para los logs.
/// </summary>
/// <remarks>
/// Hay uno por cada tipo que alguien captura por su tipo —una avería del panel se reconoce por ser
/// de E/S o de operación no válida, y una orden mal hecha por ser de argumento—: con un tipo propio
/// suelto, esas capturas dejarían de reconocerlo.
/// </remarks>
public class NoticeException(Notice notice, Exception? innerException = null)
    : Exception(notice?.ToString(), innerException), INoticed
{
    public Notice Notice { get; } = notice ?? throw new ArgumentNullException(nameof(notice));
}

/// <inheritdoc cref="NoticeException"/>
public sealed class NoticeIOException(Notice notice, Exception? innerException = null)
    : IOException(notice?.ToString(), innerException), INoticed
{
    public Notice Notice { get; } = notice ?? throw new ArgumentNullException(nameof(notice));
}

/// <inheritdoc cref="NoticeException"/>
public sealed class NoticeInvalidOperationException(Notice notice, Exception? innerException = null)
    : InvalidOperationException(notice?.ToString(), innerException), INoticed
{
    public Notice Notice { get; } = notice ?? throw new ArgumentNullException(nameof(notice));
}

/// <summary>
/// Una orden o un dato que no vale, con su aviso.
/// </summary>
/// <remarks>
/// Sin nombre de parámetro a propósito: estos mensajes los acaba leyendo una persona, y el
/// «(Parameter 'ids')» que pega detrás <see cref="ArgumentException"/> parece que el programa se ha roto.
/// </remarks>
public sealed class NoticeArgumentException(Notice notice, Exception? innerException = null)
    : ArgumentException(notice?.ToString(), innerException), INoticed
{
    public Notice Notice { get; } = notice ?? throw new ArgumentNullException(nameof(notice));
}

/// <inheritdoc cref="NoticeException"/>
public sealed class NoticeTimeoutException(Notice notice, Exception? innerException = null)
    : TimeoutException(notice?.ToString(), innerException), INoticed
{
    public Notice Notice { get; } = notice ?? throw new ArgumentNullException(nameof(notice));
}

/// <summary>
/// Los textos de los avisos, en cada idioma.
/// </summary>
/// <remarks>
/// Viven aquí, junto al contrato, porque los necesitan todos los que lo usan: la ventana los pinta en
/// su idioma, y la CLI y los logs del servicio, en español. El español es la fuente: lo que falte en
/// otro idioma sale en español, y lo que falte en los dos sale con su código, que se ve a la legua.
/// </remarks>
public static class Notices
{
    /// <summary>El idioma en el que está escrito todo, y al que se cae lo que falte.</summary>
    public const string Source = "es";

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Catalogs = new(StringComparer.Ordinal);

    /// <summary>El texto de un código, todavía con sus huecos.</summary>
    public static string Template(string code, string language = Source)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (Catalog(language).TryGetValue(code, out string? text)) return text;
        if (Catalog(Source).TryGetValue(code, out text)) return text;
        return code;
    }

    /// <summary>El aviso redactado en un idioma, con el de dentro si lo tiene.</summary>
    public static string Render(Notice notice, string language = Source)
    {
        ArgumentNullException.ThrowIfNull(notice);

        string template = Template(notice.Code, language);
        var args = new object?[notice.Args.Count + (notice.Inner is null ? 0 : 1)];
        for (int i = 0; i < notice.Args.Count; i++) args[i] = notice.Args[i];
        if (notice.Inner is { } inner) args[^1] = Render(inner, language);

        if (args.Length == 0) return template;

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, args);
        }
        catch (FormatException)
        {
            // Una traducción con las llaves mal no puede tirar a quien la enseña: se ve sin rellenar.
            return template;
        }
    }

    private static IReadOnlyDictionary<string, string> Catalog(string language)
    {
        lock (Catalogs)
        {
            if (Catalogs.TryGetValue(language, out var catalog)) return catalog;

            using var stream = typeof(Notices).Assembly.GetManifestResourceStream($"VibeScreen.Notices.{language}.json");
            catalog = stream is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : JsonSerializer.Deserialize(stream, NoticeJsonContext.Default.DictionaryStringString)
                  ?? new Dictionary<string, string>(StringComparer.Ordinal);

            Catalogs[language] = catalog;
            return catalog;
        }
    }
}

[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class NoticeJsonContext : JsonSerializerContext;
