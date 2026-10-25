using System.Text.Json.Serialization;
using VibeScreen.Core.Sensors;
using VibeScreen.Protocol.Display;

namespace VibeScreen.Core.Layout;

/// <summary>Un tema: qué se pinta en el panel y de qué sensores sale. Es el contenido de <c>layout.json</c>.</summary>
/// <remarks>
/// Las propiedades del layout llevan <c>set</c> y no <c>init</c> a propósito: con el serializador generado
/// en compilación, una propiedad <c>init</c> que falta en el JSON recibe el valor por defecto de su tipo y
/// pisa el inicializador. Un texto sin <c>size</c> acabaría con tamaño 0.
/// </remarks>
public sealed record LayoutDocument
{
    /// <summary>Versión del formato. La actual es <see cref="LayoutLoader.CurrentSchema"/>.</summary>
    public required int Schema { get; set; }

    public LayoutMeta? Meta { get; set; }

    public required LayoutDisplay Display { get; set; }

    public LayoutBackground Background { get; set; } = new ColorBackground();

    public required IReadOnlyList<LayoutWidget> Widgets { get; set; }

    /// <summary>
    /// Carpeta del fichero del que salió el tema. No se serializa: es lo que hace que una ruta
    /// relativa signifique «al lado del tema» y no «al lado de donde se arrancó el proceso».
    /// </summary>
    [JsonIgnore]
    public string? Directory { get; set; }

    /// <summary>La ruta de un recurso del tema, resuelta contra su carpeta si viene relativa.</summary>
    public string Resolve(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return System.IO.Path.IsPathRooted(path) || Directory is null
            ? path
            : System.IO.Path.Combine(Directory, path);
    }
}

public sealed record LayoutMeta
{
    public string? Name { get; set; }

    public string? Author { get; set; }
}

/// <summary>Panel para el que está hecho el tema. Ancho y alto van ya en la orientación indicada.</summary>
public sealed record LayoutDisplay
{
    public required int Width { get; set; }

    public required int Height { get; set; }

    [JsonConverter(typeof(StrictEnumJsonConverter<Orientation>))]
    public Orientation Orientation { get; set; } = Orientation.Portrait;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ColorBackground), "color")]
[JsonDerivedType(typeof(ImageBackground), "image")]
public abstract record LayoutBackground;

public sealed record ColorBackground : LayoutBackground
{
    public string Color { get; set; } = "#000000";
}

/// <summary>Cómo se coloca una imagen que no mide lo mismo que el panel.</summary>
public enum ImageFit
{
    /// <summary>Llena el panel conservando la proporción. Lo que sobra se recorta.</summary>
    Cover,

    /// <summary>Entra entera conservando la proporción. Lo que falta queda del color de respaldo.</summary>
    Contain,

    /// <summary>Se estira hasta el panel, deformándola si hace falta.</summary>
    Stretch,

    /// <summary>Centrada a su tamaño, sin escalar.</summary>
    Center,
}

/// <summary>
/// Una imagen de fondo.
/// </summary>
/// <remarks>
/// La imagen se pinta una sola vez en la capa de fondo y de ahí sale el trozo que se restaura bajo
/// cada widget al repintarlo, así que no cuesta enlace: lo que se envía al panel sigue siendo la caja
/// del widget que cambia. La ruta puede ser relativa, y entonces se resuelve contra la carpeta del
/// tema: así el tema y su imagen se copian juntos a otra máquina.
/// </remarks>
public sealed record ImageBackground : LayoutBackground
{
    public required string Path { get; set; }

    [JsonConverter(typeof(StrictEnumJsonConverter<ImageFit>))]
    public ImageFit Fit { get; set; } = ImageFit.Cover;

    /// <summary>Lo que se ve donde no llega la imagen, y lo que queda si no se puede leer.</summary>
    public string Color { get; set; } = "#000000";
}

/// <summary>
/// Un elemento del tema. El discriminador es <c>type</c> y todos los tipos concretos se declaran aquí,
/// para que el JSON se lea de arriba abajo sin saltar entre ficheros.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextWidget), "text")]
[JsonDerivedType(typeof(ClockWidget), "clock")]
[JsonDerivedType(typeof(BarWidget), "bar")]
[JsonDerivedType(typeof(GaugeWidget), "gauge")]
[JsonDerivedType(typeof(ChartWidget), "chart")]
[JsonDerivedType(typeof(ImageWidget), "image")]
public abstract record LayoutWidget
{
    public required string Id { get; set; }

    /// <summary>
    /// Caja del widget, como <c>[x, y, ancho, alto]</c>. Es lo que se envía al panel cada vez que cambia,
    /// así que cuesta su área y no su contenido: conviene ajustarla a lo que muestra.
    /// </summary>
    [JsonConverter(typeof(PixelRectJsonConverter))]
    public required PixelRect Rect { get; set; }

    public int RefreshMs { get; set; } = 1000;

    /// <summary>Si el enlace no da para todo, sale antes lo de más prioridad.</summary>
    public int Priority { get; set; }

    /// <summary>Sensor del que depende, si depende de alguno. No se serializa: sale del tipo concreto.</summary>
    [JsonIgnore]
    public virtual string? SensorId => null;

    /// <summary>Regla de color por valor, si el tipo admite una. No se serializa: sale del tipo concreto.</summary>
    [JsonIgnore]
    public virtual ColorLevels? ValueLevels => null;
}

/// <summary>
/// De qué color va un widget según lo que marque su sensor: uno para los valores bajos, otro para
/// los medios y otro para los altos.
/// </summary>
/// <remarks>
/// <para>
/// No hay huecos por construcción y no por comprobación: no se declaran extremos, solo las dos
/// fronteras que separan los tres tramos, así que no queda ningún valor sin color ni hay solapes que
/// resolver. Por debajo de <see cref="MediumFrom"/> manda <see cref="Low"/>; desde ahí, incluido,
/// <see cref="Medium"/>; y desde <see cref="HighFrom"/>, incluido, <see cref="High"/>.
/// </para>
/// <para>
/// Los cortes van en la unidad del widget, igual que el mínimo y el máximo de una gráfica. Y los
/// tramos se llaman por su posición y no por lo que significan, porque hay sensores en los que lo
/// preocupante es lo bajo —memoria libre, batería, revoluciones de un ventilador—: así, invertir el
/// criterio es elegir los colores al revés y el fichero no dice nada que no sea verdad.
/// </para>
/// </remarks>
public sealed record ColorLevels
{
    /// <summary>Color por debajo de <see cref="MediumFrom"/>.</summary>
    public required string Low { get; set; }

    /// <summary>Color desde <see cref="MediumFrom"/>, incluido, hasta <see cref="HighFrom"/>.</summary>
    public required string Medium { get; set; }

    /// <summary>Color desde <see cref="HighFrom"/>, incluido.</summary>
    public required string High { get; set; }

    /// <summary>Valor desde el que empieza el tramo medio.</summary>
    public required float MediumFrom { get; set; }

    /// <summary>Valor desde el que empieza el tramo alto. Va por encima de <see cref="MediumFrom"/>.</summary>
    public required float HighFrom { get; set; }
}

/// <summary>Dónde va la unidad respecto al número.</summary>
public enum UnitPlacement
{
    /// <summary>Detrás: <c>65 °C</c>.</summary>
    Suffix,

    /// <summary>Delante, como los símbolos de moneda: <c>$ 65</c>.</summary>
    Prefix,
}

public enum TextAlignment
{
    Left,
    Center,
    Right,
}

/// <summary>
/// Lo común a los widgets que escriben algo: con qué letra, de qué color y hacia qué lado.
/// </summary>
public abstract record LabelWidget : LayoutWidget
{
    /// <summary>Familia tipográfica instalada en el sistema. Sin ella, la predeterminada.</summary>
    public string? Font { get; set; }

    public float Size { get; set; } = 16;

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public string Color { get; set; } = "#FFFFFF";

    [JsonConverter(typeof(StrictEnumJsonConverter<TextAlignment>))]
    public TextAlignment Align { get; set; } = TextAlignment.Left;
}

/// <summary>Un texto fijo, o el valor de un sensor con su unidad.</summary>
/// <remarks>
/// El valor se compone por partes en lugar de con una cadena de formato: la GUI necesita ofrecer la
/// unidad, sus decimales y de qué lado va, y eso no se puede editar con casillas si el usuario ha
/// escrito «{0:F1} °C» a mano.
/// </remarks>
public sealed record TextWidget : LabelWidget
{
    /// <summary>Identificador canónico del sensor, como <c>cpu/0/temperature/package</c>.</summary>
    public string? Sensor { get; set; }

    /// <summary>Texto fijo, para los widgets sin sensor.</summary>
    public string? Text { get; set; }

    /// <summary>Texto delante del valor, como una etiqueta: <c>«RAM »</c>.</summary>
    public string? Prefix { get; set; }

    /// <summary>Texto detrás del valor, después de la unidad.</summary>
    public string? Suffix { get; set; }

    /// <summary>Decimales del valor, de 0 a 6.</summary>
    public int Decimals { get; set; }

    /// <summary>Unidad en la que se muestra. Sin ella, la nativa del sensor.</summary>
    [JsonConverter(typeof(StrictEnumJsonConverter<SensorUnit>))]
    public SensorUnit Unit { get; set; } = SensorUnit.Auto;

    /// <summary>Si se escribe la unidad o solo el número.</summary>
    public bool ShowUnit { get; set; } = true;

    [JsonConverter(typeof(StrictEnumJsonConverter<UnitPlacement>))]
    public UnitPlacement UnitPlacement { get; set; } = UnitPlacement.Suffix;

    /// <summary>Espacio entre el número y la unidad: <c>65 °C</c> frente a <c>65°C</c>.</summary>
    public bool UnitSpace { get; set; } = true;

    /// <summary>
    /// De qué color se escribe según lo que marque el sensor. Sin esto, del color de siempre.
    /// </summary>
    /// <remarks>
    /// Solo para los textos con sensor: un texto fijo no tiene valor que mirar, y lleva su color en
    /// <see cref="LabelWidget.Color"/>. Con la regla puesta, ese color pasa a ser el de «no hay
    /// dato»: es lo que se ve mientras el sensor no da lectura.
    /// </remarks>
    public ColorLevels? Levels { get; set; }

    [JsonIgnore]
    public override string? SensorId => Sensor;

    [JsonIgnore]
    public override ColorLevels? ValueLevels => Levels;
}

/// <summary>La hora, la fecha, o las dos.</summary>
/// <remarks>
/// No lleva sensor: el valor sale del reloj de la máquina. Como cualquier otro widget solo se envía
/// al panel cuando lo que muestra cambia, así que un «HH:mm» mirado una vez por segundo gasta enlace
/// una vez por minuto.
/// </remarks>
public sealed record ClockWidget : LabelWidget
{
    /// <summary>Patrón de <see cref="ClockFormat"/>, como <c>HH:mm</c> o <c>d 'de' MMMM</c>.</summary>
    public string Format { get; set; } = ClockFormat.Default;
}

/// <summary>Una imagen fija, o una animada como un GIF.</summary>
/// <remarks>
/// <para>
/// Lo que gasta enlace no es cuántos fotogramas tiene sino cuántos se envían por segundo, y cada uno
/// cuesta la caja entera. Una fija se envía una vez y ya está. Una animada va a la velocidad que
/// marca el propio fichero, y el refresco del widget dice cada cuánto se mira qué fotograma toca: con
/// uno más lento se saltan fotogramas, pero la animación no se ralentiza.
/// </para>
/// <para>
/// Como el fondo, la ruta puede ser relativa a la carpeta del tema, y al guardar la imagen se copia
/// dentro, en <see cref="LayoutLoader.ImagesFolder"/>.
/// </para>
/// </remarks>
public sealed record ImageWidget : LayoutWidget
{
    public required string Path { get; set; }

    [JsonConverter(typeof(StrictEnumJsonConverter<ImageFit>))]
    public ImageFit Fit { get; set; } = ImageFit.Contain;
}

/// <summary>
/// Lo común a los widgets que dibujan el valor en vez de escribirlo: de dónde sale, en qué unidad y
/// entre qué dos extremos.
/// </summary>
/// <remarks>
/// <see cref="Min"/> y <see cref="Max"/> van en la unidad del widget, no en la del sensor: si eliges
/// grados Fahrenheit, el recorrido se pone en Fahrenheit. Son del layout porque los máximos y mínimos
/// que da el backend son acumulados de la sesión y no sirven para escalar nada.
/// </remarks>
public abstract record GraphWidget : LayoutWidget
{
    /// <summary>Identificador canónico del sensor.</summary>
    public required string Sensor { get; set; }

    [JsonConverter(typeof(StrictEnumJsonConverter<SensorUnit>))]
    public SensorUnit Unit { get; set; } = SensorUnit.Auto;

    public float Min { get; set; }

    public float Max { get; set; } = 100;

    public string Color { get; set; } = "#8AB4F8";

    /// <summary>Color de la parte vacía. Sin él, el fondo del layout.</summary>
    public string? Background { get; set; }

    /// <summary>Color del contorno. Sin él, la gráfica va a hueso.</summary>
    public string? Outline { get; set; }

    /// <summary>
    /// Grosor del contorno, en píxeles. Se dibuja hacia dentro de la caja del widget: un trazo
    /// centrado en el borde se perdería a la mitad al recortar, y la caja es lo que se envía al panel.
    /// </summary>
    public float OutlineWidth { get; set; } = 1;

    /// <summary>
    /// De qué color se dibuja según lo que marque el sensor. Sin esto, del color de siempre.
    /// </summary>
    /// <remarks>
    /// Tiñe lo que dibuja el valor —el relleno, la línea o el arco, su contorno y el relleno de un
    /// histórico—, pero nunca <see cref="Background"/>: eso es el hueco, y del mismo color que el
    /// relleno no se distinguiría dónde llega el valor. Con la regla puesta, los colores fijos pasan
    /// a ser los de «no hay dato».
    /// </remarks>
    public ColorLevels? Levels { get; set; }

    [JsonIgnore]
    public override string? SensorId => Sensor;

    [JsonIgnore]
    public override ColorLevels? ValueLevels => Levels;
}

/// <summary>Hacia dónde crece la barra.</summary>
public enum BarDirection
{
    Right,
    Left,
    Up,
    Down,
}

/// <summary>Una barra de nivel que se llena entre el mínimo y el máximo.</summary>
public sealed record BarWidget : GraphWidget
{
    [JsonConverter(typeof(StrictEnumJsonConverter<BarDirection>))]
    public BarDirection Direction { get; set; } = BarDirection.Right;

    /// <summary>Radio de las esquinas, en píxeles.</summary>
    public float Radius { get; set; }
}

/// <summary>El valor escrito en el hueco del arco.</summary>
/// <remarks>
/// No tiene sensor ni unidad propios: son los del arco, y por eso el número y la aguja no pueden
/// decir cosas distintas. Tampoco tiene caja ni alineación, porque va centrado en la del arco, que
/// es lo que se gana frente a poner un texto suelto encima y cuadrarlo a ojo.
/// </remarks>
public sealed record GaugeLabel
{
    /// <summary>Familia tipográfica instalada en el sistema. Sin ella, la predeterminada.</summary>
    public string? Font { get; set; }

    public float Size { get; set; } = 20;

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    /// <summary>Color del texto. Sin él, el del arco.</summary>
    public string? Color { get; set; }

    /// <summary>Decimales del valor, de 0 a 6.</summary>
    public int Decimals { get; set; }

    /// <summary>Si se escribe la unidad o solo el número.</summary>
    public bool ShowUnit { get; set; } = true;

    [JsonConverter(typeof(StrictEnumJsonConverter<UnitPlacement>))]
    public UnitPlacement UnitPlacement { get; set; } = UnitPlacement.Suffix;

    /// <summary>Espacio entre el número y la unidad: <c>65 °C</c> frente a <c>65°C</c>.</summary>
    public bool UnitSpace { get; set; } = true;

    /// <summary>Texto delante del valor.</summary>
    public string? Prefix { get; set; }

    /// <summary>Texto detrás del valor, después de la unidad.</summary>
    public string? Suffix { get; set; }
}

/// <summary>Un arco de aguja. Con 360 grados de recorrido es un anillo.</summary>
public sealed record GaugeWidget : GraphWidget
{
    /// <summary>Grosor del arco en píxeles.</summary>
    public float Thickness { get; set; } = 8;

    /// <summary>Grados desde las tres en punto, en el sentido de las agujas del reloj.</summary>
    public float StartAngle { get; set; } = 135;

    /// <summary>Grados que recorre el arco de extremo a extremo.</summary>
    public float SweepAngle { get; set; } = 270;

    /// <summary>Extremos redondeados.</summary>
    public bool Round { get; set; } = true;

    /// <summary>El valor escrito dentro del arco. Sin esto, el arco va solo.</summary>
    public GaugeLabel? Label { get; set; }
}

/// <summary>
/// Una gráfica de histórico que se desplaza hacia la izquierda, con una muestra por refresco.
/// </summary>
/// <remarks>
/// Es el widget más caro del catálogo: cambia en cada refresco, así que envía su caja entera cada vez.
/// Una tira de 320x100 a 1 Hz son 64.000 B/s, la cuarta parte del enlace del panel.
/// </remarks>
public sealed record ChartWidget : GraphWidget
{
    /// <summary>Muestras que caben en la gráfica. Con el refresco, marca cuánto histórico se ve.</summary>
    public int Samples { get; set; } = 60;

    public float Thickness { get; set; } = 2;

    /// <summary>Color del relleno bajo la línea. Sin él, solo la línea.</summary>
    public string? Fill { get; set; }
}
