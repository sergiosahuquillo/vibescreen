using System.Globalization;
using System.Text;

namespace VibeScreen.Core.Layout;

/// <summary>
/// El formato de fecha y hora de los temas: un patrón con marcas, como <c>HH:mm</c> o
/// <c>dddd d 'de' MMMM</c>.
/// </summary>
/// <remarks>
/// <para>
/// Las marcas son las de toda la vida, pero las convierte esta clase y no <c>DateTime.ToString</c>.
/// El servicio compila con <c>InvariantGlobalization</c>, así que las plantillas de .NET dirían
/// «Monday» y «September» en cualquier máquina; con una tabla propia el panel dice lo mismo en todas
/// y el instalador no tiene que arrastrar ICU, que es justo lo que no queríamos pedirle al usuario.
/// </para>
/// <para>
/// Una tabla por idioma, y el idioma lo dice quien pinta: el servicio, el que le ha dicho la ventana;
/// la vista previa, el de la ventana. Lo que el tema escribe entre comillas —el «de» de «d 'de'
/// MMMM»— es del tema y sale tal cual en cualquier idioma.
/// </para>
/// </remarks>
public static class ClockFormat
{
    /// <summary>Lo que muestra un reloj recién puesto.</summary>
    public const string Default = "HH:mm";

    /// <summary>El idioma de los nombres cuando no se dice otro: el de siempre.</summary>
    public const string DefaultLanguage = "es";

    /// <summary>
    /// Los nombres de cada idioma: los meses de enero a diciembre, y los días en el orden de
    /// <see cref="DayOfWeek"/>, que empieza en domingo.
    /// </summary>
    private static readonly Dictionary<string, (string[] Months, string[] Days)> Names = new(StringComparer.Ordinal)
    {
        ["es"] = (
            ["enero", "febrero", "marzo", "abril", "mayo", "junio",
             "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"],
            ["domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado"]),
        ["en"] = (
            ["January", "February", "March", "April", "May", "June",
             "July", "August", "September", "October", "November", "December"],
            ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"]),
    };

    /// <summary>Los idiomas en los que sabe escribir los nombres.</summary>
    public static IReadOnlyCollection<string> Languages => Names.Keys;

    /// <summary>
    /// Las marcas, y dentro de cada letra la más larga primero: al leer <c>dddd</c> hay que
    /// reconocer el día de la semana y no cuatro veces el día del mes.
    /// </summary>
    private static readonly string[] Marks =
    [
        "yyyy", "yy",
        "MMMM", "MMM", "MM", "M",
        "dddd", "ddd", "dd", "d",
        "HH", "H", "hh", "h",
        "mm", "m",
        "ss", "s",
        "tt", "t",
    ];

    /// <summary>El texto del reloj en ese instante. Lo que va entre comillas simples sale tal cual.</summary>
    /// <param name="language">El idioma de los nombres de días y meses. Uno que no se conoce, en español.</param>
    public static string Compose(string format, DateTimeOffset when, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(format);

        var names = Names.TryGetValue(language ?? DefaultLanguage, out var known) ? known : Names[DefaultLanguage];
        var text = new StringBuilder(format.Length + 8);
        foreach (var (mark, literal) in Parts(format)) text.Append(mark is null ? literal : Render(mark, when, names));
        return text.ToString();
    }

    /// <summary>
    /// Cada cuánto puede cambiar el texto de ese patrón.
    /// </summary>
    /// <remarks>
    /// Es lo que separa un reloj de un sensor a efectos de enlace: un «HH:mm» mirado cada segundo no
    /// cambia más de una vez por minuto, así que cobrarle un refresco por segundo en el medidor del
    /// editor pintaría de rojo un tema que cabe de sobra.
    /// </remarks>
    public static TimeSpan Period(string format)
    {
        ArgumentNullException.ThrowIfNull(format);

        bool second = false, minute = false, hour = false;
        foreach (var (mark, _) in Parts(format))
        {
            switch (mark?[0])
            {
                case 's': second = true; break;
                case 'm': minute = true; break;
                case 'H' or 'h' or 't': hour = true; break;
                default: break;
            }
        }

        if (second) return TimeSpan.FromSeconds(1);
        if (minute) return TimeSpan.FromMinutes(1);
        return hour ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1);
    }

    /// <summary>El patrón troceado en marcas y texto literal, que es lo único que hay en él.</summary>
    private static IEnumerable<(string? Mark, string Literal)> Parts(string format)
    {
        var literal = new StringBuilder();
        int i = 0;

        while (i < format.Length)
        {
            if (format[i] == '\'')
            {
                // Entre comillas va texto tal cual, para escribir «de» sin que la «d» sea el día.
                i++;
                while (i < format.Length && format[i] != '\'') literal.Append(format[i++]);
                if (i < format.Length) i++;
                continue;
            }

            if (Mark(format, i) is not { } mark)
            {
                literal.Append(format[i++]);
                continue;
            }

            if (literal.Length > 0)
            {
                yield return (null, literal.ToString());
                literal.Clear();
            }

            yield return (mark, mark);
            i += mark.Length;
        }

        if (literal.Length > 0) yield return (null, literal.ToString());
    }

    private static string? Mark(string format, int at) =>
        Marks.FirstOrDefault(mark => string.CompareOrdinal(format, at, mark, 0, mark.Length) == 0
                                     && at + mark.Length <= format.Length);

    private static string Render(string mark, DateTimeOffset when, (string[] Months, string[] Days) names) => mark switch
    {
        "yyyy" => Digits(when.Year, 4),
        "yy" => Digits(when.Year % 100, 2),
        "MMMM" => names.Months[when.Month - 1],
        "MMM" => names.Months[when.Month - 1][..3],
        "MM" => Digits(when.Month, 2),
        "M" => Digits(when.Month, 1),
        "dddd" => names.Days[(int)when.DayOfWeek],
        "ddd" => names.Days[(int)when.DayOfWeek][..3],
        "dd" => Digits(when.Day, 2),
        "d" => Digits(when.Day, 1),
        "HH" => Digits(when.Hour, 2),
        "H" => Digits(when.Hour, 1),
        "hh" => Digits(Hour12(when), 2),
        "h" => Digits(Hour12(when), 1),
        "mm" => Digits(when.Minute, 2),
        "m" => Digits(when.Minute, 1),
        "ss" => Digits(when.Second, 2),
        "s" => Digits(when.Second, 1),
        "tt" => when.Hour < 12 ? "AM" : "PM",
        _ => when.Hour < 12 ? "a" : "p",
    };

    /// <summary>Las doce en punto son las 12 y no las 0, que es lo único raro del reloj de 12 horas.</summary>
    private static int Hour12(DateTimeOffset when) => when.Hour % 12 == 0 ? 12 : when.Hour % 12;

    private static string Digits(int value, int count) =>
        value.ToString("D" + count.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
}
