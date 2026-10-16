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
