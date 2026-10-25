using System.Text.Json;
using System.Text.Json.Serialization;
using VibeScreen.App.Localization;
using VibeScreen.Core.Rendering;
using VibeScreen.Core.Storage;

namespace VibeScreen.App;

/// <summary>Lo que la ventana recuerda entre arranques. No es del panel: es de quien la mira.</summary>
/// <remarks>
/// Aparte de la configuración del servicio a propósito. Aquella la escribe un proceso que en Windows
/// corre como SYSTEM y vale para toda la máquina; esto es el gusto de un usuario, y va a su perfil.
/// </remarks>
public sealed record AppSettings
{
    /// <summary>
    /// Con qué paleta se pinta la ventana.
    /// </summary>
    /// <remarks>
    /// De la ventana y no del panel: lo que se está pintando en la pantalla lo decide el tema del
    /// servicio y no cambia porque aquí se ponga el modo claro. De fábrica, oscuro, que es como
    /// nació la aplicación; quien quiera seguir al sistema lo dice.
    /// </remarks>
    [JsonConverter(typeof(JsonStringEnumConverter<AppTheme>))]
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>
    /// Cuál de los realces está elegido.
    /// </summary>
    /// <remarks>
    /// Es el identificador de uno de los ocho —«rojo», «azul»— y no un <c>#RRGGBB</c>: con dos
    /// paletas, un realce es una pareja de tonos y no un color, y guardar el ya resuelto dejaría el
    /// de fondo negro encima de fondo blanco al cambiar de modo. Los ficheros de antes traen el hex
    /// y se traducen al leerlos.
    /// </remarks>
    public string Accent { get; set; } = Palette.DefaultAccent;

    /// <summary>
    /// Familia tipográfica de la ventana. Sin ella, la primera monoespaciada que esté instalada.
    /// Si la que dice no está en esta máquina, se cae a esa misma lista en vez de quedarse en blanco.
    /// </summary>
    public string? Font { get; set; }

    /// <summary>
    /// En qué idioma habla la ventana: <c>es</c>, <c>en</c>, o null para seguir al del sistema.
    /// </summary>
    /// <remarks>
    /// De fábrica, el del sistema: quien abre la aplicación por primera vez tiene que poder leer
    /// Ajustes para encontrar dónde se cambia. Uno que no esté entre los que hay se toma como null.
    /// </remarks>
    public string? Language { get; set; }

    /// <summary>Con qué se pinta la ventana. El que se sabe bueno: con este ya se ha visto algo.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<RenderMode>))]
    public RenderMode Render { get; set; } = RenderMode.Auto;

    /// <summary>
    /// Un modo pedido y todavía sin probar. El arranque lo borra <b>antes</b> de usarlo: si la ventana
    /// no llega a verse, el siguiente ya no lo encuentra y vuelve a <see cref="Render"/>.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<RenderMode>))]
    public RenderMode? RenderTrying { get; set; }

    /// <summary>Si la ventana busca versiones nuevas por su cuenta, solo cuando se le pide o nunca.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<UpdateMode>))]
    public UpdateMode Updates { get; set; } = UpdateMode.Notify;
}

/// <summary>Qué hace la ventana con las versiones nuevas.</summary>
/// <remarks>
/// Instalar es siempre a mano, en los tres: cada actualización cierra la ventana, apaga el panel unos
/// segundos y pide permiso de administrador, y eso no puede pasar sin que nadie lo haya pedido.
/// </remarks>
public enum UpdateMode
{
    /// <summary>Busca al abrirse y una vez al día, y avisa si hay una nueva.</summary>
    Notify,

    /// <summary>Solo busca cuando se pulsa «Buscar ahora».</summary>
    Manual,

    /// <summary>No actualizar: ni busca ni se conecta a nada.</summary>
    Off,
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
public sealed partial class AppSettingsJsonContext : JsonSerializerContext;

/// <summary>Dónde guarda la ventana lo suyo y cómo lo lee sin poder fallar.</summary>
public static class AppSettingsStore
{
    /// <summary>
    /// El perfil del usuario: <c>%APPDATA%</c> en Windows y <c>~/.config</c> en Linux, que es lo que
    /// devuelve <see cref="Environment.SpecialFolder.ApplicationData"/> en cada uno.
    /// </summary>
    public static string File => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VibeScreen", "app.json");

    /// <summary>Lo guardado, o los valores por defecto. Nunca lanza: un gusto no tira una ventana.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (!System.IO.File.Exists(File)) return new AppSettings();

            var settings =
                JsonSerializer.Deserialize(System.IO.File.ReadAllText(File), AppSettingsJsonContext.Default.AppSettings)
                ?? new AppSettings();

            // Al leer y no al guardar: un fichero de antes tiene el color del realce y no cuál de los
            // ocho era, y lo que se haga con él tiene que valer también para el arranque en el que
            // nadie toca los ajustes y por tanto nadie guarda.
            settings.Accent = Palette.MigrateAccent(settings.Accent) ?? Palette.DefaultAccent;
            return settings;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Guarda. Devuelve el problema si lo hubo, para poder contarlo en la propia pestaña.</summary>
    public static Text? Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            // Entero o nada. Esto se guarda muy a menudo —cada vez que se toca un ajuste—, así que
            // la ventana en la que un corte deja el fichero a medias no es pequeña.
            AtomicFile.WriteAllText(File, JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings));
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Loc.L("settings.save-failed", File, e.Message);
        }
    }
}
