using System.Text.Json.Serialization;

namespace VibeScreen.Contracts;

/// <summary>En qué punto está el servicio respecto al panel.</summary>
public enum ServiceState
{
    /// <summary>Arrancando, todavía sin buscar.</summary>
    Starting,

    /// <summary>Sin panel: no se ha encontrado, o se ha perdido y se está reintentando.</summary>
    Searching,

    /// <summary>Panel abierto, pero sin layout que pintar.</summary>
    Connected,

    /// <summary>Panel abierto y pintando un layout.</summary>
    Painting,

    /// <summary>Parando.</summary>
    Stopping,
}

/// <summary>
/// Una orden para el servicio. El discriminador es <c>command</c>, así que el JSON de la tubería se
/// lee a simple vista: <c>{"command":"set-brightness","id":3,"percent":40}</c>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "command")]
[JsonDerivedType(typeof(StatusRequest), "status")]
[JsonDerivedType(typeof(SensorsRequest), "sensors")]
[JsonDerivedType(typeof(SetLayoutRequest), "set-layout")]
[JsonDerivedType(typeof(SetRandomLayoutRequest), "set-random-layout")]
[JsonDerivedType(typeof(ReloadLayoutRequest), "reload-layout")]
[JsonDerivedType(typeof(SetBrightnessRequest), "set-brightness")]
[JsonDerivedType(typeof(SetScreenPowerRequest), "set-screen-power")]
[JsonDerivedType(typeof(SetLanguageRequest), "set-language")]
public abstract record ServiceRequest
{
    /// <summary>Empareja pregunta y respuesta. Lo pone el cliente.</summary>
    public int Id { get; set; }
}

public sealed record StatusRequest : ServiceRequest;

/// <summary>Los sensores que ve el servicio. Es el explorador de sensores de la GUI.</summary>
public sealed record SensorsRequest : ServiceRequest
{
    /// <summary>
    /// Cuántos identificadores se pueden pedir de una vez.
    /// </summary>
    /// <remarks>
    /// El tope existe por lo mismo que el de <see cref="JsonLineChannel.MaxLineChars"/>: la tubería
    /// está abierta a todos los usuarios locales y la atiende un proceso que en Windows corre como
    /// SYSTEM. Y aquí además la búsqueda se hace con el cerrojo del panel cogido, así que una lista de
    /// cien mil identificadores —que caben de sobra en el megabyte de una línea— dejaba al panel
    /// esperando a que terminara. La vista previa de un tema pide unos pocos.
    /// </remarks>
    public const int MaxIds = 512;

    /// <summary>
    /// Identificadores canónicos concretos. Solo se refresca el hardware del que cuelgan, que es lo
    /// que necesita una vista previa; refrescarlo todo incluye discos y red y se nota en el panel.
    /// Con esto puesto, <see cref="Filter"/> sobra.
    /// </summary>
    public IReadOnlyList<string>? Ids { get; set; }

    /// <summary>Subcadena del identificador canónico o del nombre nativo. Sin filtro, todos.</summary>
    public string? Filter { get; set; }
}

/// <summary>Cambia el layout y lo guarda en la configuración, para que sobreviva a un reinicio.</summary>
public sealed record SetLayoutRequest : ServiceRequest
{
    public required string Path { get; set; }

    /// <summary>
    /// Si esto es el usuario <b>eligiendo</b> layout, y no solo pintando uno.
    /// </summary>
    /// <remarks>
    /// Elegir en el desplegable del panel es decir «este», y apaga el sorteo de cada arranque.
    /// Aplicar desde el editor no: es enseñar en qué estás trabajando, así que el tema se queda
    /// puesto hasta el próximo arranque, hasta el siguiente sorteo a mano o hasta que apliques otro.
    /// Por omisión sí, que es lo que quiere decir quien manda la orden a pelo desde la CLI.
    /// </remarks>
    public bool Chosen { get; set; } = true;
}

/// <summary>
/// Pone o quita el modo de elegir layout al azar en cada arranque del servicio.
/// </summary>
/// <remarks>
/// Al ponerlo se elige uno ya mismo, que es la única forma de que el usuario vea que ha hecho algo:
/// lo que el modo cambia de verdad no se nota hasta el siguiente arranque del ordenador.
/// </remarks>
public sealed record SetRandomLayoutRequest : ServiceRequest
{
    public required bool On { get; set; }
}

/// <summary>Vuelve a leer del disco el layout vigente. Es lo que usa el editor para la vista previa.</summary>
public sealed record ReloadLayoutRequest : ServiceRequest;

public sealed record SetBrightnessRequest : ServiceRequest
{
    /// <summary>De 0 a 100. El servicio se encarga de que el panel lo reciba invertido.</summary>
    public required int Percent { get; set; }
}

public sealed record SetScreenPowerRequest : ServiceRequest
{
    public required bool On { get; set; }
}

/// <summary>
/// En qué idioma escribe el panel los nombres de los días y los meses de los relojes.
/// </summary>
/// <remarks>
/// Lo manda la ventana al cambiar de idioma, y al conectar si el del servicio no es el suyo: el panel
/// sigue al idioma de quien lo usa, y lo conserva al arrancar aunque no haya ventana abierta.
/// </remarks>
public sealed record SetLanguageRequest : ServiceRequest
{
    /// <summary>El código del idioma: <c>es</c> o <c>en</c>.</summary>
    public required string Language { get; set; }
}

/// <summary>
/// La respuesta a una orden. Lleva el resultado y, según la orden, el estado o los sensores; los
/// campos que no vengan a cuento se omiten del JSON.
/// </summary>
public sealed record ServiceResponse
{
    public int Id { get; set; }

    public bool Ok { get; set; }

    /// <summary>Por qué no se pudo.</summary>
    public Notice? Error { get; set; }

    /// <summary>Los problemas de un layout que no valida, uno por elemento.</summary>
    public IReadOnlyList<Notice>? Problems { get; set; }

    public ServiceStatus? Status { get; set; }

    public IReadOnlyList<SensorInfo>? Sensors { get; set; }

    public static ServiceResponse Success(int id) => new() { Id = id, Ok = true };

    public static ServiceResponse Failure(int id, Notice error, IReadOnlyList<Notice>? problems = null) =>
        new() { Id = id, Ok = false, Error = error, Problems = problems };
}

/// <summary>Lo que hace falta para pintar una bandeja o una ventana de estado sin preguntar más.</summary>
public sealed record ServiceStatus
{
    public required ServiceState State { get; set; }

    /// <summary>Detalle del estado: el último error de conexión, o el siguiente reintento.</summary>
    public Notice? Detail { get; set; }

    public string? PortName { get; set; }

    public string? Model { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public string? Orientation { get; set; }

    public string? LayoutPath { get; set; }

    public string? LayoutName { get; set; }

    /// <summary>Si el layout lo elige el azar en cada arranque. El que se pinta sigue siendo LayoutPath.</summary>
    public bool RandomLayout { get; set; }

    public int Brightness { get; set; }

    public bool ScreenOn { get; set; }

    public long Blits { get; set; }

    public long BytesSent { get; set; }

    /// <summary>Bytes por segundo de los últimos segundos, no la media desde que arrancó el layout.</summary>
    public double BytesPerSecond { get; set; }

    /// <summary>
    /// Fracción del presupuesto de envío que se está gastando. No es sobre el caudal del panel: el
    /// planificador solo reparte una parte, y es contra esa parte contra la que hay que mirar.
    /// </summary>
    public double LinkUtilization { get; set; }

    /// <summary>Caudal del panel en bytes por segundo. Es lo que el editor compara con lo que pide un tema.</summary>
    public int ThroughputBytesPerSecond { get; set; }

    public int SensorCount { get; set; }

    /// <summary>Lo que falta y se nota: sin ring0, sin PawnIO, sensores ausentes del layout.</summary>
    public IReadOnlyList<Notice> Warnings { get; set; } = [];

    /// <summary>
    /// En qué idioma escribe el panel lo que es texto de reloj: los nombres de los días y los meses.
    /// </summary>
    public string Language { get; set; } = Notices.Source;

    /// <summary>Veces que se ha perdido el panel y se ha vuelto a conectar.</summary>
    public int Reconnects { get; set; }

    public long UptimeSeconds { get; set; }
}

public sealed record SensorInfo
{
    /// <summary>Identificador canónico, como <c>cpu/0/temperature/package</c>.</summary>
    public required string Id { get; set; }

    /// <summary>Nombre que le da el backend, para que el usuario se reconozca.</summary>
    public required string Name { get; set; }

    /// <summary>Hardware del que cuelga: «AMD Ryzen 7 3700X», «Nuvoton NCT6687D».</summary>
    public required string Hardware { get; set; }

    /// <summary>Magnitud que mide, en minúsculas: <c>temperature</c>, <c>load</c>, <c>data</c>.</summary>
    public required string Kind { get; set; }

    /// <summary>Símbolo de la unidad nativa: <c>°C</c>, <c>%</c>, <c>GB</c>.</summary>
    public string? Unit { get; set; }

    /// <summary>Última lectura, o null si el sensor no da valor ahora mismo.</summary>
    public double? Value { get; set; }
}
