using System.Text.Json.Serialization;

namespace VibeScreen.Service;

/// <summary>Lo que el servicio recuerda entre arranques.</summary>
/// <remarks>
/// Con <c>init</c> en lugar de <c>set</c>, una propiedad que falte en el JSON recibiría el valor por
/// defecto de su tipo y pisaría el inicializador: un fichero sin <c>brightness</c> dejaría el panel a 0.
/// </remarks>
public sealed record ServiceConfig
{
    /// <summary>Ruta del layout vigente. Sin ella, el servicio se conecta al panel y lo deja limpio.</summary>
    public string? LayoutPath { get; set; }

    /// <summary>
    /// Elegir layout al azar en cada arranque en vez de repetir siempre el mismo.
    /// </summary>
    /// <remarks>
    /// <see cref="LayoutPath"/> sigue siendo el que se está pintando, no un deseo: al arrancar se
    /// elige uno y se escribe ahí. Así el estado, la ventana y la CLI dicen la verdad sin tener que
    /// entender el modo, y el elegido de este arranque es lo que la próxima vez se evita repetir.
    /// </remarks>
    public bool RandomLayout { get; set; }

    public int Brightness { get; set; } = 50;

    public bool ScreenOn { get; set; } = true;

    /// <summary>Puerto forzado a mano. Sin él, el primer panel que se encuentre.</summary>
    public string? PortName { get; set; }

    /// <summary>
    /// Revisión forzada a mano, por su nombre: <c>RevA</c>. Sin ella, la que se detecte.
    /// </summary>
    /// <remarks>
    /// Para el panel que la detección no sabe reconocer, o que reconoce mal: con un puerto forzado, sin
    /// esto se suponía la revisión A. Va como texto y no como enumerado para que un nombre que no
    /// existe no deje el servicio sin arrancar: lo lee <see cref="PanelHost"/> al conectar, y un nombre
    /// malo sale en el estado como cualquier otro fallo del panel.
    /// <para>
    /// Sin valor no se escribe. El fichero rechaza lo que no conoce, así que un <c>"revision": null</c>
    /// escrito por esta versión dejaba a la anterior sin poder leerlo, y la anterior arrancaba con los
    /// valores por defecto: sin tema. Así, quien no fuerza nada sigue con un fichero que leen las dos.
    /// </para>
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Revision { get; set; }

    /// <summary>
    /// En qué idioma escriben los relojes del panel los nombres de los días y los meses.
    /// </summary>
    /// <remarks>
    /// Lo pone la ventana con <c>set-language</c>, y se guarda para que el panel lo siga diciendo igual
    /// después de un reinicio sin ventana abierta. Sin él, español, que es como salía siempre.
    /// </remarks>
    public string? Language { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    WriteIndented = true)]
[JsonSerializable(typeof(ServiceConfig))]
public sealed partial class ServiceConfigJsonContext : JsonSerializerContext;
