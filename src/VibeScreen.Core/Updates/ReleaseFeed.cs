using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace VibeScreen.Core.Updates;

/// <summary>Una release que se puede instalar: qué versión es, de dónde se baja y qué tiene que medir.</summary>
/// <param name="Version">La del tag.</param>
/// <param name="FileName">El nombre del instalador, que es también con el que se guarda.</param>
/// <param name="Download">De dónde se baja el instalador.</param>
/// <param name="Size">Lo que mide según GitHub, para el progreso cuando el servidor no lo diga.</param>
/// <param name="Sha256">El hash del instalador, en hexadecimal y en minúsculas.</param>
/// <param name="Page">La página de la release, con sus novedades.</param>
public sealed record ReleaseInfo(
    AppVersion Version, string FileName, Uri Download, long Size, string Sha256, Uri? Page);

/// <summary>Por qué una release no se ofrece.</summary>
public enum ReleaseProblem
{
    None,

    /// <summary>La respuesta no es la de una release, o le falta el tag.</summary>
    Malformed,

    /// <summary>La release no trae el instalador de esa misma versión.</summary>
    NoInstaller,

    /// <summary>
    /// GitHub no da el hash del instalador. Sin él no hay forma de saber que lo que se ha bajado es lo
    /// que se publicó, y no se instala.
    /// </summary>
    NoDigest,
}

/// <summary>
/// Lee la respuesta de <c>GET /repos/{dueño}/{repo}/releases/latest</c> del API de GitHub.
/// </summary>
/// <remarks>
/// <para>
/// Esa ruta nunca devuelve borradores ni versiones previas: es la última publicada. El CI publica una
/// sola cosa por release, <c>VibeScreen-X.Y.Z-setup.exe</c>, y aquí se exige que sea la del mismo
/// número que el tag: un instalador con otro número sería un error al publicar, no una actualización.
/// </para>
/// <para>
/// El hash sale del campo <c>digest</c> que GitHub calcula él mismo al subir cada fichero
/// (<c>sha256:…</c>). Es lo que permite no publicar ningún fichero de sumas aparte.
/// </para>
/// </remarks>
public static partial class ReleaseFeed
{
    /// <summary>La última release de VibeScreen.</summary>
    public static Uri Latest { get; } = new("https://api.github.com/repos/sergiosahuquillo/vibescreen/releases/latest");

    public static bool TryParse(string json, [NotNullWhen(true)] out ReleaseInfo? release, out ReleaseProblem problem)
    {
        ArgumentNullException.ThrowIfNull(json);
        release = null;

        GitHubRelease? raw;
        try
        {
            raw = JsonSerializer.Deserialize(json, ReleaseJsonContext.Default.GitHubRelease);
        }
        catch (JsonException)
        {
            raw = null;
        }

        if (raw is null || !AppVersion.TryParse(raw.TagName, out var version))
        {
            problem = ReleaseProblem.Malformed;
            return false;
        }

        string expected = $"VibeScreen-{version}-setup.exe";
        var asset = raw.Assets?.FirstOrDefault(a => string.Equals(a.Name, expected, StringComparison.Ordinal));
        if (asset is null || !Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var download))
        {
            problem = ReleaseProblem.NoInstaller;
            return false;
        }

        var digest = asset.Digest is null ? null : Sha256Digest().Match(asset.Digest);
        if (digest is not { Success: true })
        {
            problem = ReleaseProblem.NoDigest;
            return false;
        }

        Uri.TryCreate(raw.HtmlUrl, UriKind.Absolute, out var page);
        release = new ReleaseInfo(
            version, expected, download, asset.Size, digest.Groups[1].Value.ToLowerInvariant(), page);
        problem = ReleaseProblem.None;
        return true;
    }

    [GeneratedRegex("^sha256:([0-9a-fA-F]{64})$")]
    private static partial Regex Sha256Digest();
}

// Solo lo que se usa. GitHub añade campos cuando quiere, así que aquí lo desconocido se ignora, al
// revés que en los ficheros que escribe la propia aplicación.
internal sealed record GitHubRelease(
    string? TagName, string? HtmlUrl, IReadOnlyList<GitHubAsset>? Assets);

internal sealed record GitHubAsset(
    string? Name, long Size, string? BrowserDownloadUrl, string? Digest);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(GitHubRelease))]
internal sealed partial class ReleaseJsonContext : JsonSerializerContext;
