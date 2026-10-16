using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Media;

namespace VibeScreen.ThemeCheck;

/// <summary>
/// El CSS del diseño, leído lo justo para poder preguntarle medidas y colores.
/// </summary>
/// <remarks>
/// <para>
/// No es un navegador: no hay cascada por especificidad, ni herencia, ni unidades relativas. Lo
/// único que hace falta es «qué dice el diseño de <c>.prop-sub</c>», y para eso basta con juntar las
/// declaraciones de todas las reglas cuyo selector sea exactamente ese, en orden de aparición. Las
/// reglas de dentro de un <c>@media</c> se dejan fuera a propósito: describen la ventana estrecha, y
/// lo que se verifica aquí es la ancha.
/// </para>
/// <para>
/// De valores entiende cuatro formas —<c>#rgb</c>, <c>#rrggbb</c>, <c>rgba()</c> y
/// <c>color-mix(in srgb, X n%, Y)</c>— más <c>var(--loquesea)</c>, que es como el diseño escribe
/// casi todo. Con eso se resuelven los dos juegos de variables, el oscuro y el claro.
/// </para>
/// </remarks>
public sealed partial class Css
{
    private readonly List<(string Selector, string Property, string Value)> _rules = [];

    public Css(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        text = Comments().Replace(text, " ");

        int i = 0;
        while (i < text.Length)
        {
            int open = text.IndexOf('{', i);
            if (open < 0) break;

            string head = text[i..open].Trim();
            int close = Matching(text, open);
            if (close < 0) break;

            // Un @media o un @keyframes lleva reglas dentro: se salta entero, con su contenido.
            if (!head.StartsWith('@'))
            {
                string body = text[(open + 1)..close];
                foreach (string selector in head.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    foreach (string piece in body.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    {
                        int colon = piece.IndexOf(':', StringComparison.Ordinal);
                        if (colon <= 0) continue;
                        _rules.Add((selector, piece[..colon].Trim(), piece[(colon + 1)..].Trim()));
                    }
                }
            }

            i = close + 1;
        }
    }

    /// <summary>Lo que el diseño dice de un selector, con las reglas de después pisando a las de antes.</summary>
    public Dictionary<string, string> Of(string selector)
    {
        var declarations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (candidate, property, value) in _rules)
        {
            if (string.Equals(candidate, selector, StringComparison.Ordinal)) declarations[property] = value;
        }

        return declarations;
    }

    /// <summary>Una declaración suelta, o null si el diseño no dice nada de ella.</summary>
    public string? Value(string selector, string property) =>
        Of(selector).TryGetValue(property, out string? value) ? value : null;

    /// <summary>
    /// El juego de variables de una paleta.
    /// </summary>
    /// <remarks>
    /// El modo claro no repite la lista entera: solo lo que cambia. Así que se parte del
    /// <c>:root</c> y encima se vuelca el otro, que es lo que hace el navegador.
    /// </remarks>
    public Dictionary<string, string> Variables(bool light)
    {
        var table = Of(":root");
        if (light)
        {
            foreach (var (key, value) in Of(":root[data-theme=\"light\"]")) table[key] = value;
        }

        return table;
    }

    /// <summary>El número de delante de un valor: de <c>36px</c> saca 36, y de <c>0 12px</c> el 0.</summary>
    public static double Number(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var match = AnyNumber().Match(value);
        return match.Success ? double.Parse(match.Value, CultureInfo.InvariantCulture) : double.NaN;
    }

    /// <summary>Todos los números de un valor: de <c>0 12px</c> saca 0 y 12.</summary>
    public static double[] Numbers(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return [.. AnyNumber().Matches(value).Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture))];
    }

    /// <summary>
    /// El color de un valor, con las variables de una de las dos paletas ya puestas.
    /// </summary>
    /// <remarks>
    /// Devuelve null cuando el valor no es un color —<c>none</c>, un degradado, una sombra—, que es
    /// la forma de decir «de esto no se encarga esta comprobación» sin tener que mirarlo antes.
    /// </remarks>
    public static Color? Colour(string? value, IReadOnlyDictionary<string, string> variables, int depth = 0)
    {
        if (value is null || depth > 8) return null;
        ArgumentNullException.ThrowIfNull(variables);

        value = value.Trim();

        if (value.StartsWith("var(", StringComparison.Ordinal))
        {
            string name = value[4..value.IndexOf(')', StringComparison.Ordinal)].Trim();
            return variables.TryGetValue(name, out string? resolved) ? Colour(resolved, variables, depth + 1) : null;
        }

        if (value.StartsWith('#')) return Color.TryParse(value, out var parsed) ? parsed : null;

        if (value.StartsWith("rgba(", StringComparison.Ordinal) || value.StartsWith("rgb(", StringComparison.Ordinal))
        {
            double[] parts = Numbers(value);
            if (parts.Length < 3) return null;
            double alpha = parts.Length > 3 ? parts[3] : 1;
            return Color.FromArgb(Byte(alpha * 255), Byte(parts[0]), Byte(parts[1]), Byte(parts[2]));
        }

        // color-mix(in srgb, X n%, Y). El diseño solo lo usa con «transparent», «#fff» y «#000», que
        // son los tres casos que la paleta sabe hacer: rebajar el alfa, y aclarar u oscurecer.
        if (value.StartsWith("color-mix(", StringComparison.Ordinal))
        {
            string inside = value[10..value.LastIndexOf(')')];
            string[] pieces = SplitTop(inside);
            if (pieces.Length < 3) return null;

            string[] first = pieces[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (Colour(first[0], variables, depth + 1) is not { } tint) return null;

            double share = first.Length > 1 ? Number(first[^1]) / 100 : 0.5;
            string other = pieces[2].Trim();

            if (string.Equals(other, "transparent", StringComparison.Ordinal))
                return Color.FromArgb(Byte(share * 255), tint.R, tint.G, tint.B);

            if (Colour(other, variables, depth + 1) is not { } over) return null;
            return Color.FromRgb(
                Byte((tint.R * share) + (over.R * (1 - share))),
                Byte((tint.G * share) + (over.G * (1 - share))),
                Byte((tint.B * share) + (over.B * (1 - share))));
        }

        return null;
    }

    private static byte Byte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 255));

    /// <summary>Parte por comas sin meterse dentro de los paréntesis, que es donde van las otras.</summary>
    private static string[] SplitTop(string text)
    {
        var pieces = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')') depth--;
            else if (text[i] == ',' && depth == 0)
            {
                pieces.Add(text[start..i]);
                start = i + 1;
            }
        }

        pieces.Add(text[start..]);
        return [.. pieces];
    }

    /// <summary>La llave que cierra la que se abre en <paramref name="open"/>, contando las de dentro.</summary>
    private static int Matching(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return i;
        }

        return -1;
    }

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comments();

    // El CSS escribe los alfas sin el cero de delante -rgba(255,255,255,.07)-, así que la parte
    // decimal suelta tiene que entrar: sin ella ese .07 se leía como un 7 y todas las líneas de la
    // paleta salían blancas macizas.
    [GeneratedRegex(@"-?(\d+(\.\d+)?|\.\d+)")]
    private static partial Regex AnyNumber();
}
