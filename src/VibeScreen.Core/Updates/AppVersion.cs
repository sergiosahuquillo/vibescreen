using System.Globalization;

namespace VibeScreen.Core.Updates;

/// <summary>Una versión publicada: <c>MAJOR.MINOR.PATCH</c> y nada más.</summary>
/// <remarks>
/// Solo las de una release. Lo que se compila fuera de <c>main</c> lleva sufijo —<c>1.0.0-dev</c>,
/// <c>1.0.3-dev.42</c>— y no se puede leer como una de estas a propósito: una compilación de
/// desarrollo no tiene con qué compararse, y ofrecerle «actualizar» a la última release sería
/// ofrecerle irse hacia atrás.
/// </remarks>
public readonly record struct AppVersion(int Major, int Minor, int Patch) : IComparable<AppVersion>
{
    /// <summary>
    /// Lee <c>1.0.2</c>, con <c>v</c> delante o sin ella —es como van los tags— y sin lo que el SDK
    /// añade detrás de un «+», que es el hash de git. Con cualquier otro sufijo, no.
    /// </summary>
    public static bool TryParse(string? text, out AppVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var span = text.AsSpan().Trim();
        if (span.Length > 0 && (span[0] == 'v' || span[0] == 'V')) span = span[1..];

        int plus = span.IndexOf('+');
        if (plus >= 0) span = span[..plus];

        Span<Range> parts = stackalloc Range[4];
        if (span.Split(parts, '.') != 3) return false;

        if (!Part(span[parts[0]], out int major) || !Part(span[parts[1]], out int minor) ||
            !Part(span[parts[2]], out int patch))
            return false;

        version = new AppVersion(major, minor, patch);
        return true;
    }

    // Solo dígitos: int.TryParse admite signos y espacios, y «1.-0.2» no es una versión.
    private static bool Part(ReadOnlySpan<char> text, out int value)
    {
        value = 0;
        return text.Length > 0 && !text.ContainsAnyExceptInRange('0', '9') &&
               int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    public int CompareTo(AppVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major)
        : Minor != other.Minor ? Minor.CompareTo(other.Minor)
        : Patch.CompareTo(other.Patch);

    public static bool operator <(AppVersion left, AppVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(AppVersion left, AppVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(AppVersion left, AppVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(AppVersion left, AppVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
}
