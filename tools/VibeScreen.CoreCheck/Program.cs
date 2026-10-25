using SkiaSharp;
using VibeScreen.Contracts;
using VibeScreen.Core.Layout;
using VibeScreen.Core.Rendering;
using VibeScreen.Core.Drivers;
using VibeScreen.Core.Sensors;
using VibeScreen.Core.Transport;
using VibeScreen.Core.Updates;
using VibeScreen.Protocol.Display;
using VibeScreen.Protocol.Rendering;

// Verificador de VibeScreen.Core. Comprueba sin hardware el nombrado canónico de sensores, la lectura
// de layouts y el render hasta el driver, con sensores y panel de mentira.

int checks = 0, failures = 0;

void Check(bool condition, string what)
{
    checks++;
    if (condition) return;
    failures++;
    Console.WriteLine($"  FALLO  {what}");
}

/// <summary>
/// Una sección, con su cabecera y su red debajo.
/// </summary>
/// <remarks>
/// Lo que se escape de una sección cuenta como un fallo y la tanda sigue. Antes no: una excepción a
/// mitad de fichero se llevaba por delante todas las comprobaciones que venían detrás, y el caso no
/// es teórico —aquí hay ayudantes que hacen ElementAt sobre una lista que puede quedarse corta—.
/// Justo cuando algo se rompe es cuando más falta hace ver el resto.
/// </remarks>
void Header(string title)
{
    Console.WriteLine();
    Console.WriteLine(title);
    Console.WriteLine(new string('-', title.Length));
}

/// <summary>Una sección que se ha roto por lo alto. Cuenta como un fallo, con el tipo y el mensaje.</summary>
void Broke(string title, Exception e)
{
    checks++;
    failures++;
    Console.WriteLine($"  FALLO  la sección «{title}» se ha roto: {e.GetType().Name}: {e.Message}");
}

// Lo que comparten varias secciones vive aquí, porque lo que se declara dentro de un try no sale
// del bloque: así cada sección puede tener su red sin dejar a la siguiente sin lo que necesita.
LayoutDocument? parsed = null;
LayoutDocument graphs = null!;

bool Near(double a, double b) => Math.Abs(a - b) < 1e-6;

string Minimal(string widgets, string display = """{ "width": 480, "height": 320, "orientation": "landscape" }""") =>
    $$"""
    { "schema": 2, "display": {{display}}, "background": { "type": "color", "color": "#0C0E14" }, "widgets": [ {{widgets}} ] }
    """;

LayoutDocument? TryParse(string json, out LayoutException? error)
{
    try
    {
        error = null;
        return LayoutLoader.Parse(json, "prueba");
    }
    catch (LayoutException e)
    {
        error = e;
        return null;
    }
}

bool Rejects(string json, string mustMention)
{
    TryParse(json, out var error);
    return error is not null && error.Problems.Any(p => p.ToString().Contains(mustMention, StringComparison.OrdinalIgnoreCase));
}

const string CpuTemp = """{ "type": "text", "id": "cpu", "rect": [10, 10, 150, 48], "sensor": "cpu/0/temperature/package", "decimals": 1 }""";

var background = new SKColor(0x0C, 0x0E, 0x14);

var caps = new DisplayCapabilities
{
    Model = "Turing 3.5\"", Revision = HardwareRevision.Simulated,
    NativeWidth = 320, NativeHeight = 480, ThroughputBytesPerSecond = 261_600,
};

var loads = new FakeSensors();

IReadOnlyList<string> Problems(string widget)
{
    try
    {
        LayoutLoader.Parse(Minimal(widget), "prueba");
        return [];
    }
    catch (LayoutException e)
    {
        // Redactados en español, que es como los lee la CLI: así estas comprobaciones miran también
        // que el catálogo dice lo que tiene que decir.
        return [.. e.Problems.Select(p => p.ToString())];
    }
}

var red = new SKColor(0xFF, 0x00, 0x00);
var greenLine = new SKColor(0x00, 0xFF, 0x00);

Header("Identificadores canónicos");
try
{

    NativeSensor Sensor(HardwareClass hardware, string hardwareId, SensorKind kind, string name, int index,
                        string? nativeId = null, int rank = 0) => new()
    {
        HardwareClass = hardware,
        HardwareId = hardwareId,
        HardwareRank = rank,
        HardwareName = hardwareId,
        Kind = kind,
        Name = name,
        NativeIndex = index,
        NativeId = nativeId ?? $"{hardwareId}/{kind.Segment()}/{index}",
    };

    // Nombres reales del equipo de referencia: Ryzen 7 3700X, RTX 3060 Ti y placa MSI con Nuvoton NCT6687D.
    NativeSensor[] reference =
    [
        Sensor(HardwareClass.Cpu, "/amdcpu/0", SensorKind.Temperature, "Core (Tctl/Tdie)", 2),
        Sensor(HardwareClass.Cpu, "/amdcpu/0", SensorKind.Load, "CPU Total", 0),
        Sensor(HardwareClass.Cpu, "/amdcpu/0", SensorKind.Load, "CPU Core #1", 1),
        Sensor(HardwareClass.Cpu, "/amdcpu/0", SensorKind.Load, "CPU Core #1 Thread #2", 2),
        Sensor(HardwareClass.Cpu, "/amdcpu/0", SensorKind.Clock, "Core #4", 4),
        Sensor(HardwareClass.Cpu, "/amdcpu/0", SensorKind.Voltage, "Core (SVI2 TFN)", 0),
        Sensor(HardwareClass.Gpu, "/gpu-nvidia/0", SensorKind.Temperature, "GPU Core", 0),
        Sensor(HardwareClass.Gpu, "/gpu-nvidia/0", SensorKind.Temperature, "GPU Hot Spot", 2),
        // LibreHardwareMonitor repite este identificador con dos nombres.
        Sensor(HardwareClass.Gpu, "/gpu-nvidia/0", SensorKind.Load, "GPU Bus", 3, "/gpu-nvidia/0/load/3"),
        Sensor(HardwareClass.Gpu, "/gpu-nvidia/0", SensorKind.Load, "GPU Memory", 3, "/gpu-nvidia/0/load/3"),
        // Y este nombre con dos identificadores.
        Sensor(HardwareClass.Gpu, "/gpu-nvidia/0", SensorKind.Load, "D3D Copy", 10),
        Sensor(HardwareClass.Gpu, "/gpu-nvidia/0", SensorKind.Load, "D3D Copy", 11),
        Sensor(HardwareClass.Gpu, "/gpu-nvidia/0", SensorKind.Fan, "GPU Fan 1", 1),
        // Los sensores del Super I/O cuelgan de la placa como subhardware.
        Sensor(HardwareClass.Motherboard, "/motherboard", SensorKind.Voltage, "Vcore", 2, "/lpc/nct6687d/0/voltage/2"),
    ];

    var assigned = CanonicalSensorIds.Assign(reference);
    string IdOf(string name, SensorKind kind, int nth = 0) =>
        assigned.Where(a => a.Sensor.Name == name && a.Sensor.Kind == kind).Select(a => a.Id).ElementAt(nth);

    Check(IdOf("Core (Tctl/Tdie)", SensorKind.Temperature) == "cpu/0/temperature/package", "Ryzen: Tctl/Tdie es la temperatura del paquete");
    Check(IdOf("CPU Total", SensorKind.Load) == "cpu/0/load/total", "carga total de la CPU");
    Check(IdOf("CPU Core #1", SensorKind.Load) == "cpu/0/load/core/0", "los núcleos cuentan desde 0");
    Check(IdOf("CPU Core #1 Thread #2", SensorKind.Load) == "cpu/0/load/core/0/thread/1", "cada hilo dentro de su núcleo");
    Check(IdOf("Core #4", SensorKind.Clock) == "cpu/0/clock/core/3", "frecuencia por núcleo");
    Check(IdOf("Core (SVI2 TFN)", SensorKind.Voltage) == "cpu/0/voltage/vcore", "Ryzen: la tensión del SMU es la vcore");
    Check(IdOf("GPU Core", SensorKind.Temperature) == "gpu/0/temperature/core", "temperatura del núcleo de la GPU");
    Check(IdOf("GPU Hot Spot", SensorKind.Temperature) == "gpu/0/temperature/hotspot", "punto caliente de la GPU");
    Check(IdOf("GPU Bus", SensorKind.Load) == "gpu/0/load/gpu-bus" && IdOf("GPU Memory", SensorKind.Load) == "gpu/0/load/gpu-memory",
          "un identificador repetido de LHM da dos identificadores canónicos");
    Check(IdOf("D3D Copy", SensorKind.Load, 0) == "gpu/0/load/d3d-copy" && IdOf("D3D Copy", SensorKind.Load, 1) == "gpu/0/load/d3d-copy#2",
          "un nombre repetido se desempata con #2");
    Check(IdOf("GPU Fan 1", SensorKind.Fan) == "gpu/0/fan/0", "los ventiladores de la GPU cuentan desde 0");
    Check(IdOf("Vcore", SensorKind.Voltage) == "motherboard/0/voltage/vcore", "el Super I/O cuenta como la placa");
    Check(assigned.Select(a => a.Id).Distinct().Count() == reference.Length, "ningún identificador se repite");

    var intel = CanonicalSensorIds.Assign([Sensor(HardwareClass.Cpu, "/intelcpu/0", SensorKind.Temperature, "CPU Package", 0)]);
    Check(intel[0].Id == "cpu/0/temperature/package", "Intel: un layout hecho en AMD encuentra igual la temperatura");

    var shuffled = CanonicalSensorIds.Assign(Enumerable.Reverse(reference));
    Check(shuffled.Select(a => (a.Sensor.NativeId, a.Sensor.Name, a.Id)).OrderBy(x => x.Id)
              .SequenceEqual(assigned.Select(a => (a.Sensor.NativeId, a.Sensor.Name, a.Id)).OrderBy(x => x.Id)),
          "el orden de llegada no cambia los identificadores");

    var gpus = CanonicalSensorIds.Assign([
        Sensor(HardwareClass.Gpu, "/gpu-intel/0", SensorKind.Temperature, "GPU Core", 0, rank: 2),
        Sensor(HardwareClass.Gpu, "/gpu-nvidia/0", SensorKind.Temperature, "GPU Core", 0, rank: 0),
    ]);
    Check(gpus.Single(a => a.Sensor.HardwareId == "/gpu-nvidia/0").Id == "gpu/0/temperature/core" &&
          gpus.Single(a => a.Sensor.HardwareId == "/gpu-intel/0").Id == "gpu/1/temperature/core",
          "la GPU de menor rango es la 0 aunque llegue después");

    Check(CanonicalSensorIds.Slug("Core (Tctl/Tdie)") == "core-tctl-tdie", "slug: minúsculas y guiones");
    Check(CanonicalSensorIds.Slug(" (?) ") == "sensor", "slug: un nombre sin letras no queda vacío");
}
catch (Exception e)
{
    Broke("Identificadores canónicos", e);
}

Header("Unidades");
try
{


    Check(SensorKind.Temperature.NativeUnit() == SensorUnit.Celsius, "la temperatura llega en grados centígrados");
    Check(SensorKind.Data.NativeUnit() == SensorUnit.Gigabytes && SensorKind.SmallData.NativeUnit() == SensorUnit.Megabytes,
          "los datos llegan en GB y los pequeños en MB");
    Check(SensorUnit.Celsius.Symbol() == "°C" && SensorUnit.Percent.Symbol() == "%" && SensorUnit.Rpm.Symbol() == "RPM", "símbolos");
    Check(Near(SensorUnits.Convert(39, SensorUnit.Celsius, SensorUnit.Fahrenheit), 102.2), "39 °C son 102,2 °F");
    Check(Near(SensorUnits.Convert(102.2, SensorUnit.Fahrenheit, SensorUnit.Celsius), 39), "y al revés");
    Check(Near(SensorUnits.Convert(1, SensorUnit.Gigabytes, SensorUnit.Megabytes), 1024), "1 GB son 1024 MB");
    Check(Near(SensorUnits.Convert(4250, SensorUnit.Megahertz, SensorUnit.Gigahertz), 4.25), "4250 MHz son 4,25 GHz");
    Check(Near(SensorUnits.Convert(0.5, SensorUnit.MegabytesPerSecond, SensorUnit.MegabitsPerSecond), 4), "medio MB/s son 4 Mb/s");
    Check(!SensorUnits.CanConvert(SensorUnit.Celsius, SensorUnit.Percent), "no se convierten grados en porcentaje");
    Check(SensorUnits.Convert(39, SensorUnit.Celsius, SensorUnit.Percent) == 39, "y el valor se queda tal cual en vez de inventarse");
    Check(SensorUnits.Resolve(SensorUnit.Auto, SensorKind.Clock) == SensorUnit.Megahertz, "auto es la unidad nativa del sensor");
    Check(SensorUnits.Resolve(SensorUnit.Gigahertz, SensorKind.Clock) == SensorUnit.Gigahertz, "y una unidad elegida se respeta");
    Check(SensorUnits.For(SensorKind.Temperature) is [SensorUnit.Celsius, SensorUnit.Fahrenheit],
          "las unidades que puede elegir el usuario salen con la nativa primero");
    Check(SensorUnits.For(SensorKind.Fan) is [SensorUnit.Rpm], "un ventilador solo se mide en RPM");
}
catch (Exception e)
{
    Broke("Unidades", e);
}

Header("Layouts");
try
{





    parsed = TryParse(Minimal(CpuTemp), out var parseError);
    Check(parsed is not null, $"un layout mínimo es válido {parseError?.Message}");
    var cpuWidget = parsed?.Widgets[0] as TextWidget;
    Check(cpuWidget is { Sensor: "cpu/0/temperature/package", Rect: { X: 10, Y: 10, Width: 150, Height: 48 } },
          "texto con sensor y caja [x, y, ancho, alto]");
    Check(parsed?.Display.Orientation == Orientation.Landscape, "la orientación se lee por nombre");
    Check(cpuWidget is { RefreshMs: 1000, Align: TextAlignment.Left, Color: "#FFFFFF" }, "valores por defecto");
    Check(cpuWidget is { Unit: SensorUnit.Auto, ShowUnit: true, UnitPlacement: UnitPlacement.Suffix, UnitSpace: true },
          "y la unidad, la del sensor, detrás y separada");

    Check(Rejects(Minimal(CpuTemp.Replace("\"text\"", "\"sparkline\"")), "sparkline"), "un tipo de widget desconocido se rechaza");
    Check(Rejects(Minimal(CpuTemp.Replace("\"sensor\"", "\"sensr\"")), "sensr"), "una propiedad mal escrita se rechaza en vez de ignorarse");
    Check(Rejects(Minimal(CpuTemp, """{ "width": 480, "height": 320, "orientation": 2 }"""), "portrait"), "la orientación no admite números");
    Check(Rejects(Minimal(CpuTemp, """{ "width": 480, "height": 320, "orientation": "portrait,landscape" }"""), "portrait"),
          "ni listas separadas por comas");
    Check(Rejects(Minimal(CpuTemp.Replace("[10, 10, 150, 48]", "[400, 10, 150, 48]")), "se sale"), "una caja fuera del panel se rechaza");
    Check(Rejects(Minimal(CpuTemp.Replace("[10, 10, 150, 48]", "[10, 10, 150]")), "cuatro enteros"), "la caja lleva cuatro enteros");
    Check(Rejects(Minimal(CpuTemp + ", " + CpuTemp), "repetido"), "un id repetido se rechaza");
    Check(Rejects(Minimal(CpuTemp.Replace("\"decimals\": 1", "\"decimals\": 9")), "decimals"), "más decimales de los que caben se rechaza");
    Check(Rejects(Minimal(CpuTemp.Replace("\"decimals\"", "\"text\": \"hola\", \"decimals\"")), "uno de los dos"),
          "sensor y texto a la vez se rechaza");
    Check(Rejects(Minimal("""{ "type": "text", "id": "t", "rect": [10, 10, 80, 20], "text": "CPU", "prefix": "Uso " }"""), "prefix"),
          "una etiqueta con texto fijo se rechaza: el texto ya va entero en «text»");
    Check(Rejects(Minimal(CpuTemp).Replace("\"schema\": 2", "\"schema\": 1"), "versión anterior"),
          "un layout de la versión anterior del formato se rechaza diciéndolo");
    Check(Rejects(Minimal(CpuTemp.Replace("\"decimals\": 1", "\"unit\": \"celcius\"")), "celsius"),
          "una unidad mal escrita se rechaza con la lista de las buenas");

    const string Bar = """{ "type": "bar", "id": "barra", "rect": [10, 100, 200, 20], "sensor": "cpu/0/load/total" }""";
    Check(TryParse(Minimal(Bar), out _) is not null, "una barra con lo mínimo es válida");
    Check(Rejects(Minimal(Bar.Replace("}", """, "min": 100, "max": 0 }""")), "max"), "una barra al revés se rechaza");
    Check(Rejects(Minimal(Bar.Replace("}", """, "color": "azul" }""")), "azul"), "un color que no es un color se rechaza");
    Check(Rejects(Minimal("""{ "type": "gauge", "id": "g", "rect": [10, 10, 40, 40], "sensor": "cpu/0/load/total", "thickness": 30 }"""), "cabe"),
          "un arco más grueso que su caja se rechaza");
    Check(Rejects(Minimal("""{ "type": "chart", "id": "c", "rect": [10, 10, 100, 40], "sensor": "cpu/0/load/total", "samples": 1 }"""), "samples"),
          "una gráfica de una sola muestra se rechaza");

    // El histórico reserva un float por muestra y el panel, dos mapas de bits y el búfer de envío: los
    // tres del tamaño que diga el fichero. Y el fichero puede llegar por la tubería, que está abierta a
    // todos los usuarios locales y la atiende un proceso que en Windows corre como SYSTEM.
    Check(Rejects(Minimal("""{ "type": "chart", "id": "c", "rect": [10, 10, 100, 40], "sensor": "cpu/0/load/total", "samples": 200000000 }"""), "samples"),
          "doscientos millones de muestras se rechazan: son 800 MB de float[] pedidos con un JSON de dos líneas");
    Check(Rejects(Minimal(CpuTemp, """{ "width": 100000, "height": 100000, "orientation": "landscape" }"""), "pasa de"),
          "un panel de cien mil píxeles de lado se rechaza");
    Check(TryParse(Minimal(CpuTemp, $$"""{ "width": {{LayoutLoader.MaxDisplaySide}}, "height": {{LayoutLoader.MaxDisplaySide}}, "orientation": "landscape" }"""), out _) is not null,
          "y el panel más grande que se admite sigue siendo válido");

    // El área por dos desbordaba el int y devolvía un tamaño negativo, que aguas abajo es un new byte[]
    // imposible. Con el tope de arriba ya no se llega, pero el que no se llegue no puede depender de que
    // alguien se acuerde del tope.
    Check(Rgb565.ByteCount(new PixelRect(0, 0, LayoutLoader.MaxDisplaySide, LayoutLoader.MaxDisplaySide)) > 0,
          "el panel más grande admitido cabe en un int de bytes");
    Check(Throws<OverflowException>(() => Rgb565.ByteCount(new PixelRect(0, 0, 46000, 46000))),
          "y un rectángulo que no cabe se planta en vez de devolver un tamaño negativo");
    Check(Rejects(Minimal("""{ "type": "bar", "id": "b", "rect": [10, 10, 100, 40] }"""), "sensor"),
          "una barra sin sensor se rechaza");

    TryParse(Minimal(CpuTemp.Replace("[10, 10, 150, 48]", "[400, 10, 150, 48]").Replace("\"decimals\": 1", "\"decimals\": 9")), out var several);
    Check(several?.Problems.Count == 2, $"se informa de todos los problemas a la vez ({several?.Problems.Count})");

    // Ida y vuelta: lo que escribe el editor tiene que volver a leerse igual.
    var everything = LayoutLoader.Parse(Minimal($"{CpuTemp}, {Bar}, " + """
        { "type": "gauge", "id": "aguja", "rect": [250, 100, 80, 80], "sensor": "gpu/0/load/core", "unit": "percent",
          "thickness": 6, "outline": "#FFFFFF", "outlineWidth": 2,
          "label": { "size": 18, "bold": true, "decimals": 0, "color": "#FFFFFF", "suffix": " GPU" } },
        { "type": "chart", "id": "historico", "rect": [10, 200, 120, 60], "sensor": "cpu/0/load/total", "fill": "#16223A", "samples": 10 },
        { "type": "clock", "id": "reloj", "rect": [10, 280, 140, 30], "format": "dddd d 'de' MMMM", "bold": true }
        """), "prueba");
    string written = LayoutLoader.Serialize(everything);
    Check(!written.Contains("sensorId", StringComparison.Ordinal), "lo que se guarda no lleva campos calculados");
    Check(!written.Contains("null", StringComparison.Ordinal), "ni propiedades vacías");
    var reparsed = TryParse(written, out var roundTripError);
    Check(reparsed is not null, $"lo serializado se vuelve a leer {roundTripError?.Message}");
    Check(reparsed is not null && reparsed.Widgets.Count == everything.Widgets.Count
          && reparsed.Widgets.Zip(everything.Widgets).All(p => p.First == p.Second),
          "y sale exactamente el mismo tema");
}
catch (Exception e)
{
    Broke("Layouts", e);
}

Header("Fondos");
try
{

    string themeFolder = Path.Combine(Path.GetTempPath(), $"vibescreen-check-{Environment.ProcessId}");
    Directory.CreateDirectory(themeFolder);

    // Una imagen de dos píxeles, rojo y azul, para reconocer a simple vista dónde acaba cada mitad.
    string imagePath = Path.Combine(themeFolder, "fondo.png");
    using (var source = new SKBitmap(new SKImageInfo(2, 1, SKColorType.Bgra8888, SKAlphaType.Premul)))
    {
        source.SetPixel(0, 0, SKColors.Red);
        source.SetPixel(1, 0, SKColors.Blue);
        using var encoded = SKImage.FromBitmap(source).Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(imagePath);
        encoded.SaveTo(file);
    }

    string ThemeWith(string background, string widgets = """{ "type": "text", "id": "t", "rect": [0, 0, 40, 20], "text": "x" }""")
    {
        string themePath = Path.Combine(themeFolder, "tema.json");
        File.WriteAllText(themePath, $$"""
            { "schema": 2, "display": { "width": 480, "height": 320, "orientation": "landscape" },
              "background": {{background}}, "widgets": [ {{widgets}} ] }
            """);
        return themePath;
    }

    var withImage = LayoutLoader.Load(ThemeWith("""{ "type": "image", "path": "fondo.png", "fit": "stretch" }"""));
    Check(withImage.Background is ImageBackground { Path: "fondo.png", Fit: ImageFit.Stretch }, "un fondo de imagen se lee con su ajuste");
    Check(withImage.Directory == themeFolder, "y el tema recuerda su carpeta");
    Check(withImage.Resolve("fondo.png") == imagePath, "para resolver las rutas relativas contra ella");

    Check(Rejects(Minimal(CpuTemp).Replace("""{ "type": "color", "color": "#0C0E14" }""", """{ "type": "image", "path": "" }"""), "ruta"),
          "una imagen sin ruta se rechaza");
    Check(Rejects(Minimal(CpuTemp).Replace("""{ "type": "color", "color": "#0C0E14" }""", """{ "type": "image", "path": "x.png", "fit": "encajar" }"""), "cover"),
          "un ajuste que no existe se rechaza con la lista de los buenos");

    // 2x1 sobre 480x320: llenar escala por el lado que más pide y se sale por arriba y por abajo.
    Check(LayoutRenderer.Placement(ImageFit.Cover, 2, 1, 480, 320) == SKRect.Create(-80, 0, 640, 320), "llenar recorta lo que sobra");
    Check(LayoutRenderer.Placement(ImageFit.Contain, 2, 1, 480, 320) == SKRect.Create(0, 40, 480, 240), "encajar deja franjas");
    Check(LayoutRenderer.Placement(ImageFit.Stretch, 2, 1, 480, 320) == SKRect.Create(0, 0, 480, 320), "estirar ocupa el panel entero");
    Check(LayoutRenderer.Placement(ImageFit.Center, 2, 1, 480, 320) == SKRect.Create(239, 159.5f, 2, 1), "centrada se queda a su tamaño");

    using (var renderer = new LayoutRenderer(withImage))
    {
        renderer.RenderAll(new FakeSensors(), TimeSpan.Zero);
        Check(renderer.Surface.Bitmap.GetPixel(100, 300) == SKColors.Red, "estirada, la mitad izquierda es la mitad izquierda de la imagen");
        Check(renderer.Surface.Bitmap.GetPixel(400, 300) == SKColors.Blue, "y la derecha, la derecha");
    }

    var contained = LayoutLoader.Load(ThemeWith("""{ "type": "image", "path": "fondo.png", "fit": "contain", "color": "#00FF00" }"""));
    using (var renderer = new LayoutRenderer(contained))
    {
        renderer.RenderAll(new FakeSensors(), TimeSpan.Zero);
        Check(renderer.Surface.Bitmap.GetPixel(100, 160) == SKColors.Red, "encajada, la imagen está en el centro");
        Check(renderer.Surface.Bitmap.GetPixel(100, 10) == SKColors.Lime, "y donde no llega se ve el color de respaldo");
    }

    var missing = LayoutLoader.Load(ThemeWith("""{ "type": "image", "path": "no-esta.png", "color": "#123456" }"""));
    var missingWarnings = LayoutInspector.Warnings(missing, new FakeSensors());
    Check(missingWarnings.Count == 1 && missingWarnings[0].Code == "layout.warn.background-missing"
          && missingWarnings[0].Args.Contains("no-esta.png"),
          "una imagen que falta no impide abrir el tema, avisa");
    using (var renderer = new LayoutRenderer(missing))
    {
        renderer.RenderAll(new FakeSensors(), TimeSpan.Zero);
        Check(renderer.Surface.Bitmap.GetPixel(100, 160) == new SKColor(0x12, 0x34, 0x56), "y el panel se queda del color de respaldo");
    }

    // Guardar junto al tema deja la ruta relativa; fuera, la entera.
    var toSave = LayoutLoader.Load(ThemeWith("""{ "type": "image", "path": "fondo.png" }"""));
    ((ImageBackground)toSave.Background).Path = imagePath;
    string savedPath = Path.Combine(themeFolder, "guardado.json");
    LayoutLoader.Save(toSave, savedPath);
    Check(((ImageBackground)toSave.Background).Path == "fondo.png", "al guardar, una imagen que está junto al tema pasa a ruta relativa");
    Check(File.ReadAllText(savedPath).Contains("\"path\": \"fondo.png\"", StringComparison.Ordinal), "y así queda en el fichero");

    Directory.Delete(themeFolder, recursive: true);
}
catch (Exception e)
{
    Broke("Fondos", e);
}

Header("Textos");
try
{

    SensorReading Sample(float? value, SensorKind kind) => new()
    {
        Id = "prueba",
        Value = value,
        Native = new NativeSensor
        {
            HardwareClass = HardwareClass.Cpu, HardwareId = "/prueba", HardwareName = "Prueba",
            Kind = kind, Name = "Prueba", NativeId = "/prueba/0",
        },
    };

    TextWidget Widget(string properties) => (TextWidget)LayoutLoader.Parse(
        Minimal($$"""{ "type": "text", "id": "t", "rect": [0, 0, 200, 30], "sensor": "cpu/0/temperature/package"{{properties}} }"""), "prueba").Widgets[0];

    string Shown(string properties, float? value = 39.04f, SensorKind kind = SensorKind.Temperature) =>
        TextComposer.Compose(Widget(properties), value is null ? null : Sample(value, kind));

    Check(Shown(""", "decimals": 1""") == "39.0 °C", $"valor, unidad nativa y un espacio ({Shown(""", "decimals": 1""")})");
    Check(Shown("") == "39 °C", "sin decimales, el número redondo");
    Check(Shown(""", "showUnit": false""") == "39", "se puede quitar la unidad");
    Check(Shown(""", "unitSpace": false""") == "39°C", "y el espacio");
    Check(Shown(""", "unitPlacement": "prefix" """) == "°C 39", "la unidad puede ir delante");
    Check(Shown(""", "unit": "fahrenheit", "decimals": 1""") == "102.3 °F", "se convierte a la unidad que pida el layout");
    Check(Shown(""", "prefix": "Uso ", "suffix": " !" """, 50f, SensorKind.Load) == "Uso 50 % !", "con etiqueta delante y detrás");
    Check(Shown(""", "decimals": 2""", 9.0543f, SensorKind.Data) == "9.05 GB", "la memoria en gigas");
    Check(Shown(""", "unit": "megabytes" """, 1f, SensorKind.Data) == "1024 MB", "o en megas, convertida");
    Check(Shown("", null) == "--", "sin sensor en esta máquina, solo el marcador");
    Check(Shown(""", "unit": "celsius" """, null) == "-- °C", "y con la unidad puesta a mano, el marcador con su unidad");
    Check(TextComposer.Compose(
              (TextWidget)LayoutLoader.Parse(Minimal("""{ "type": "text", "id": "t", "rect": [0, 0, 80, 20], "text": "VibeScreen" }"""), "prueba").Widgets[0],
              null) == "VibeScreen",
          "un texto fijo sale tal cual");
}
catch (Exception e)
{
    Broke("Textos", e);
}

Header("Render");
try
{


    var layout = LayoutLoader.Parse(Minimal(
        """
        { "type": "text", "id": "cpu", "rect": [10, 10, 150, 48], "sensor": "cpu/0/temperature/package", "decimals": 1, "size": 32 },
        { "type": "text", "id": "gpu", "rect": [200, 10, 150, 48], "sensor": "gpu/0/temperature/core", "unit": "celsius", "decimals": 1, "size": 32, "refreshMs": 2000 },
        { "type": "text", "id": "titulo", "rect": [10, 200, 200, 30], "text": "VibeScreen" }
        """), "prueba");

    var sensors = new FakeSensors();
    sensors.Set("cpu/0/temperature/package", 39.04f);

    var driver = new RecordingDriver(caps, Orientation.Landscape);
    using (var runner = new LayoutRunner(driver, layout, sensors))
    {
        await runner.StartAsync(TimeSpan.Zero);
        Check(driver.Blits.Count == 1 && driver.Blits[0].Rect == new PixelRect(0, 0, 480, 320), "arranca con un fotograma completo");
        Check(runner.Renderer.DisplayedText("cpu") == "39.0 °C", $"formatea el valor ({runner.Renderer.DisplayedText("cpu")})");
        Check(runner.Renderer.DisplayedText("gpu") == "-- °C",
              $"un sensor que no existe muestra el marcador y conserva la unidad ({runner.Renderer.DisplayedText("gpu")})");
        Check(runner.Renderer.DisplayedText("titulo") == "VibeScreen", "texto fijo");
        Check(runner.Renderer.Surface.Bitmap.GetPixel(470, 310) == background, "fuera de los widgets queda el fondo");

        // Se relee, pero con un decimal sigue siendo 39.0: no gasta enlace.
        driver.Blits.Clear();
        sensors.Set("cpu/0/temperature/package", 39.01f);
        await runner.TickAsync(TimeSpan.FromSeconds(1));
        Check(driver.Blits.Count == 0, "si el texto no cambia, no se envía nada");

        sensors.Set("cpu/0/temperature/package", 41.5f);
        await runner.TickAsync(TimeSpan.FromSeconds(2));
        Check(driver.Blits.Count == 1 && driver.Blits[0].Rect == new PixelRect(10, 10, 150, 48),
              $"solo se envía la caja del widget que cambia ({driver.Blits.Count})");
        Check(driver.Blits.Count == 1 && driver.Blits[0].Pixels.Length == 150 * 48 * 2, "con los bytes de su caja");
        Check(runner.Renderer.DisplayedText("cpu") == "41.5 °C", "y con el valor nuevo");

        // La GPU refresca cada 2 s: a los 3 s solo toca la CPU.
        sensors.Updated.Clear();
        await runner.TickAsync(TimeSpan.FromSeconds(3));
        Check(sensors.Updated.Contains("cpu/0/temperature/package") && !sensors.Updated.Contains("gpu/0/temperature/core"),
              "cada widget se relee a su ritmo");
    }

    Check(ThrowsArgument(() => new LayoutRunner(new RecordingDriver(caps, Orientation.Portrait), layout, sensors)),
          "un layout apaisado no arranca con el panel en vertical");

    var warnings = LayoutInspector.Warnings(layout, sensors);
    Check(warnings.Count == 1 && warnings[0].Code == "layout.warn.missing-sensor" && warnings[0].Args[0] == "gpu/0/temperature/core",
          $"se avisa del sensor que falta ({warnings.Count})");
}
catch (Exception e)
{
    Broke("Render", e);
}

Header("Lo que leyó el panel");
try
{

    // El renderizador apunta lo que le dio cada sensor, para que nadie más tenga que muestrear el
    // hardware por su cuenta. Un segundo observador no solo ve otro instante: a los sensores que se
    // calculan por diferencia entre dos lecturas les parte la ventana al primero.
    var mirrored = LayoutLoader.Parse(Minimal(
        """
        { "type": "text", "id": "cpu", "rect": [10, 10, 150, 48], "sensor": "cpu/0/temperature/package", "decimals": 1, "size": 32 },
        { "type": "text", "id": "gpu", "rect": [200, 10, 150, 48], "sensor": "gpu/0/temperature/core", "decimals": 1, "size": 32, "refreshMs": 4000 },
        { "type": "text", "id": "titulo", "rect": [10, 200, 200, 30], "text": "VibeScreen" }
        """), "espejo");

    var mirror = new FakeSensors();
    mirror.Set("cpu/0/temperature/package", 39.04f);
    mirror.Set("gpu/0/temperature/core", 50f);
    mirror.Set("gpu/0/fan/0", 900f, SensorKind.Fan);

    var mirrorDriver = new RecordingDriver(caps, Orientation.Landscape);
    using (var runner = new LayoutRunner(mirrorDriver, mirrored, mirror))
    {
        await runner.StartAsync(TimeSpan.Zero);
        var read = runner.Renderer.LastRead;

        Check(read.TryGetValue("cpu/0/temperature/package", out float? started) && started == 39.04f,
              "al arrancar se apunta lo que dio cada sensor");
        Check(read.ContainsKey("gpu/0/temperature/core"), "los dos sensores del tema, no solo el que se lee bien");
        Check(!read.ContainsKey("gpu/0/fan/0"), "un sensor que el tema no usa no se apunta");
        Check(read.Count == 2, $"y nada más: un texto fijo no tiene sensor que apuntar ({read.Count})");

        // Con un decimal, 39.04 y 39.01 escriben los dos «39.0»: el widget no cambia de aspecto y no se
        // envía nada, pero leerse se ha leído.
        mirrorDriver.Blits.Clear();
        mirror.Set("cpu/0/temperature/package", 39.01f);
        await runner.TickAsync(TimeSpan.FromSeconds(1));
        Check(mirrorDriver.Blits.Count == 0, "un valor que no mueve el texto no se envía");
        Check(runner.Renderer.LastRead["cpu/0/temperature/package"] == 39.01f,
              "y la lectura se apunta igual: que el número no se mueva no quiere decir que no se haya leído");

        // La GPU refresca cada 4 s: a los 2 s su entrada sigue siendo la del arranque.
        mirror.Set("gpu/0/temperature/core", 70f);
        await runner.TickAsync(TimeSpan.FromSeconds(2));
        Check(runner.Renderer.LastRead["gpu/0/temperature/core"] == 50f,
              "un widget al que no le toca refrescar conserva la lectura que pintó");

        await runner.TickAsync(TimeSpan.FromSeconds(4));
        Check(runner.Renderer.LastRead["gpu/0/temperature/core"] == 70f, "y la renueva cuando le toca");

        mirror.Set("cpu/0/temperature/package", null);
        await runner.TickAsync(TimeSpan.FromSeconds(5));
        Check(runner.Renderer.LastRead.TryGetValue("cpu/0/temperature/package", out float? gone) && gone is null,
              "un sensor que deja de dar lectura se apunta con null, que no es lo mismo que no estar");
    }
}
catch (Exception e)
{
    Broke("Lo que leyó el panel", e);
}

Header("Gráficas");
try
{

    graphs = LayoutLoader.Parse(Minimal("""
        { "type": "bar", "id": "barra", "rect": [10, 100, 200, 20], "sensor": "cpu/0/load/total", "color": "#8AB4F8" },
        { "type": "gauge", "id": "aguja", "rect": [250, 100, 80, 80], "sensor": "cpu/0/load/total", "color": "#81C995", "thickness": 8 },
        { "type": "chart", "id": "historico", "rect": [10, 200, 120, 60], "sensor": "cpu/0/load/total", "color": "#8AB4F8", "samples": 10 }
        """), "prueba");

    loads.Set("cpu/0/load/total", 50f, SensorKind.Load);
    using (var renderer = new LayoutRenderer(graphs))
    {
        SKColor Pixel(int x, int y) => renderer.Surface.Bitmap.GetPixel(x, y);

        renderer.RenderAll(loads, TimeSpan.Zero);
        Check(renderer.SensorIds is ["cpu/0/load/total"], "las gráficas también declaran de qué sensor tiran");
        Check(Pixel(60, 110) != background, "la barra se pinta hasta la mitad");
        Check(Pixel(180, 110) == background, "y no más allá");
        Check(Pixel(290, 104) != background, "el arco se pinta por arriba");
        Check(Pixel(290, 140) == background, "y por el centro no, que es un arco y no un círculo");

        loads.Set("cpu/0/load/total", 50.2f, SensorKind.Load);
        var moved = renderer.Update(loads, TimeSpan.FromSeconds(1));
        Check(!moved.Any(d => d.WidgetId == "barra"), "una barra que no se mueve ni un píxel no gasta enlace");
        Check(moved.Any(d => d.WidgetId == "historico"), "la gráfica sí, que le ha entrado una muestra");

        loads.Set("cpu/0/load/total", 60f, SensorKind.Load);
        moved = renderer.Update(loads, TimeSpan.FromSeconds(2));
        Check(moved.Any(d => d.WidgetId == "barra" && d.Rect == new PixelRect(10, 100, 200, 20)), "con diez puntos más, sí");
        Check(Pixel(125, 110) != background, "y la barra llega más lejos");

        // Con el valor quieto, el histórico acaba lleno de muestras iguales y deja de cambiar.
        for (int second = 3; second <= 14; second++) moved = renderer.Update(loads, TimeSpan.FromSeconds(second));
        Check(moved.Count == 0, $"un histórico plano deja de gastar enlace ({moved.Count})");
        Check(Pixel(70, 224) != background, "y aun así se sigue viendo la línea");
    }

    /// <summary>Los problemas que da un widget suelto, para no repetir el try/catch en cada comprobación.</summary>

    // El contorno es el borde de la gráfica entera, esté llena o vacía, y va por dentro de la caja: lo
    // que se envía al panel es la caja, así que un trazo a caballo del borde se perdería a la mitad.
    var outlined = LayoutLoader.Parse(Minimal("""
        { "type": "bar", "id": "barra", "rect": [10, 10, 100, 20], "sensor": "cpu/0/load/total",
          "color": "#8AB4F8", "outline": "#FF0000", "outlineWidth": 2 },
        { "type": "chart", "id": "historico", "rect": [10, 50, 100, 40], "sensor": "cpu/0/load/total",
          "color": "#8AB4F8", "outline": "#00FF00", "outlineWidth": 2, "samples": 10 },
        { "type": "gauge", "id": "aguja", "rect": [150, 10, 80, 80], "sensor": "cpu/0/load/total",
          "color": "#81C995", "thickness": 10, "startAngle": 0, "sweepAngle": 360,
          "outline": "#FF00FF", "outlineWidth": 2 }
        """), "prueba");

    var magenta = new SKColor(0xFF, 0x00, 0xFF);

    using (var renderer = new LayoutRenderer(outlined))
    {
        renderer.RenderAll(loads, TimeSpan.Zero);
        SKColor Pixel(int x, int y) => renderer.Surface.Bitmap.GetPixel(x, y);

        bool Somewhere(PixelRect box, SKColor color)
        {
            for (int y = box.Y; y <= box.Bottom; y++)
            {
                for (int x = box.X; x <= box.Right; x++)
                {
                    if (Pixel(x, y) == color) return true;
                }
            }
            return false;
        }

        Check(Pixel(10, 20) == red, $"la barra lleva contorno, y cabe entero en su caja ({Pixel(10, 20)})");
        Check(Pixel(60, 20) != red, "por el medio no, que es un contorno y no un relleno");
        Check(Pixel(10, 70) == greenLine, $"el histórico lleva marco ({Pixel(10, 70)})");
        Check(Somewhere(new PixelRect(150, 10, 80, 80), magenta), "y el arco también");
        Check(Pixel(224, 50) != magenta, "que rodea la banda del arco en vez de rellenarla");
        Check(Pixel(190, 50) == background, "y deja el agujero del anillo como estaba");
    }

    // Sin contorno, el borde deja ver el fondo: es el color lo que lo enciende, no el grosor.
    var bare = LayoutLoader.Parse(Minimal(
        """{ "type": "bar", "id": "barra", "rect": [10, 10, 100, 20], "sensor": "cpu/0/load/total", "color": "#8AB4F8" }"""), "prueba");
    using (var renderer = new LayoutRenderer(bare))
    {
        renderer.RenderAll(loads, TimeSpan.Zero);
        Check(renderer.Surface.Bitmap.GetPixel(10, 20) == new SKColor(0x8A, 0xB4, 0xF8),
              "una barra sin contorno llega con su color hasta el borde de la caja");
    }

    Check(Problems("""{ "type": "bar", "id": "b", "rect": [0, 0, 100, 20], "sensor": "s", "outline": "rojo" }""")
              .Any(p => p.Contains("contorno", StringComparison.Ordinal)),
          "un color de contorno que no lo es se rechaza al cargar");
    Check(Problems("""{ "type": "bar", "id": "b", "rect": [0, 0, 100, 20], "sensor": "s", "outline": "#FF0000", "outlineWidth": 0 }""")
              .Any(p => p.Contains("outlineWidth", StringComparison.Ordinal)),
          "y un contorno de grosor cero también");
    Check(Problems("""
        { "type": "gauge", "id": "g", "rect": [0, 0, 80, 80], "sensor": "s", "thickness": 40, "outline": "#FF0000", "outlineWidth": 2 }
        """).Any(p => p.Contains("no cabe", StringComparison.Ordinal)),
          "el contorno del arco ensancha el anillo, y el anillo tiene que caber");
    Check(Problems("""
        { "type": "gauge", "id": "g", "rect": [0, 0, 80, 80], "sensor": "s", "thickness": 40 }
        """).Count == 0, "sin contorno, ese mismo arco cabe justo");
}
catch (Exception e)
{
    Broke("Gráficas", e);
}

Header("El valor dentro del arco");
try
{

    // El número comparte sensor y unidad con el arco: un arco en Fahrenheit tiene que escribir
    // Fahrenheit, o el dibujo y el número dirían cosas distintas del mismo sensor.
    var withValue = LayoutLoader.Parse(Minimal("""
        { "type": "gauge", "id": "con", "rect": [10, 10, 100, 100], "sensor": "cpu/0/temperature/package",
          "unit": "fahrenheit", "min": 32, "max": 212, "thickness": 8, "startAngle": 135, "sweepAngle": 270,
          "label": { "size": 20, "decimals": 0 } },
        { "type": "gauge", "id": "sin", "rect": [150, 10, 100, 100], "sensor": "cpu/0/temperature/package",
          "thickness": 8 }
        """), "prueba");

    var degrees = new FakeSensors();
    degrees.Set("cpu/0/temperature/package", 50f);
    using (var renderer = new LayoutRenderer(withValue))
    {
        SKColor Pixel(int x, int y) => renderer.Surface.Bitmap.GetPixel(x, y);

        renderer.RenderAll(degrees, TimeSpan.Zero);
        Check(renderer.DisplayedText("con") == "122 °F", $"el número sale en la unidad del arco ({renderer.DisplayedText("con")})");
        Check(renderer.DisplayedText("sin") is null, "un arco sin valor no escribe nada");
        Check(Pixel(60, 60) != background, "el número se pinta en el centro del arco");
        Check(Pixel(200, 60) == background, "y el arco sin valor deja su agujero como estaba");

        // Con cero decimales la aguja se mueve mucho antes que el número, que es el caso normal.
        degrees.Set("cpu/0/temperature/package", 50.2f);
        var moved = renderer.Update(degrees, TimeSpan.FromSeconds(1));
        Check(moved.Any(d => d.WidgetId == "con"), "la aguja se mueve y el arco se envía");
        Check(renderer.DisplayedText("con") == "122 °F", $"con el número quieto ({renderer.DisplayedText("con")})");
    }

    // Y al revés: con bastantes decimales y poco recorrido, el número se mueve antes que la aguja. Si
    // cada uno pudiera cortar la comprobación del otro, el arco acabaría enseñando un número viejo.
    var fine = LayoutLoader.Parse(Minimal("""
        { "type": "gauge", "id": "fino", "rect": [0, 0, 100, 100], "sensor": "cpu/0/load/total",
          "thickness": 6, "sweepAngle": 30, "label": { "size": 16, "decimals": 2 } }
        """), "prueba");

    var fraction = new FakeSensors();
    fraction.Set("cpu/0/load/total", 50f, SensorKind.Load);
    using (var renderer = new LayoutRenderer(fine))
    {
        renderer.RenderAll(fraction, TimeSpan.Zero);
        Check(renderer.DisplayedText("fino") == "50.00 %", $"el valor sale con la unidad del sensor ({renderer.DisplayedText("fino")})");

        fraction.Set("cpu/0/load/total", 50.01f, SensorKind.Load);
        var moved = renderer.Update(fraction, TimeSpan.FromSeconds(1));
        Check(moved.Any(d => d.WidgetId == "fino"), "un cambio que solo mueve el número también se envía");
        Check(renderer.DisplayedText("fino") == "50.01 %", $"y el número que se pinta es el nuevo ({renderer.DisplayedText("fino")})");
    }

    // Sin color propio, el del arco: es lo que se quiere casi siempre y ahorra elegirlo dos veces.
    var inherited = LayoutLoader.Parse(Minimal("""
        { "type": "gauge", "id": "g", "rect": [0, 0, 120, 120], "sensor": "cpu/0/load/total", "color": "#FF0000",
          "thickness": 6, "label": { "size": 40, "decimals": 0, "showUnit": false } }
        """), "prueba");
    using (var renderer = new LayoutRenderer(inherited))
    {
        fraction.Set("cpu/0/load/total", 50f, SensorKind.Load);
        renderer.RenderAll(fraction, TimeSpan.Zero);
        Check(renderer.DisplayedText("g") == "50", $"se puede escribir el número a secas ({renderer.DisplayedText("g")})");

        // El agujero del anillo, sin tocar la banda, que también es roja.
        bool painted = false;
        for (int y = 40; y <= 80 && !painted; y++)
            for (int x = 30; x <= 90 && !painted; x++)
                painted = renderer.Surface.Bitmap.GetPixel(x, y) == red;
        Check(painted, "y sin color propio hereda el del arco");
    }

    Check(Problems("""
        { "type": "gauge", "id": "g", "rect": [0, 0, 80, 80], "sensor": "s", "label": { "color": "rojo" } }
        """).Any(p => p.Contains("color del valor", StringComparison.Ordinal)),
          "un color del valor que no lo es se rechaza al cargar");
    Check(Problems("""
        { "type": "gauge", "id": "g", "rect": [0, 0, 80, 80], "sensor": "s", "label": { "size": 0 } }
        """).Any(p => p.Contains("label.size", StringComparison.Ordinal)),
          "y un tamaño de letra de cero también");
    Check(Problems("""
        { "type": "gauge", "id": "g", "rect": [0, 0, 80, 80], "sensor": "s", "label": { "decimals": 9 } }
        """).Any(p => p.Contains("label.decimals", StringComparison.Ordinal)),
          "y más decimales de los que caben en un panel");
    Check(Problems("""
        { "type": "gauge", "id": "g", "rect": [0, 0, 80, 80], "sensor": "s", "label": { "sensor": "otro" } }
        """).Count > 0, "el valor no lleva sensor propio: el del arco es el que hay");

    var mismatch = LayoutLoader.Parse(
        Minimal("""{ "type": "text", "id": "t", "rect": [0, 0, 100, 20], "sensor": "cpu/0/load/total", "unit": "celsius" }"""), "prueba");
    var wrongUnit = LayoutInspector.Warnings(mismatch, loads);
    Check(wrongUnit.Count == 1 && wrongUnit[0].Code == "layout.warn.unit-mismatch",
          $"se avisa de una unidad que no pega con su sensor ({wrongUnit.Count})");

    var report = BandwidthBudget.Analyze(LayoutInspector.Consumers(graphs), caps);
    Check(Near(report.RequiredBytesPerSecond, ((200 * 20) + (80 * 80) + (120 * 60)) * 2),
          $"el coste de un tema es la caja entera de cada widget en cada refresco ({report.RequiredBytesPerSecond:N0} B/s)");
    Check(report.Consumers[0].WidgetId == "historico", "y el que más gasta es el histórico");

    var clipped = LayoutLoader.Parse(Minimal(
        """{ "type": "text", "id": "largo", "rect": [100, 100, 40, 30], "text": "Un texto mucho más largo que su caja", "size": 24 }"""), "prueba");
    using (var renderer = new LayoutRenderer(clipped))
    {
        renderer.RenderAll(new FakeSensors(), TimeSpan.Zero);
        bool inside = false, outside = false;
        for (int y = 100; y < 130; y++)
        {
            for (int x = 100; x < 140; x++) inside |= renderer.Surface.Bitmap.GetPixel(x, y) != background;
            for (int x = 140; x < 480; x++) outside |= renderer.Surface.Bitmap.GetPixel(x, y) != background;
        }
        Check(inside, "el texto se pinta dentro de su caja");
        Check(!outside, "y no se sale de ella aunque no quepa");
    }
}
catch (Exception e)
{
    Broke("El valor dentro del arco", e);
}

Header("Color por valor");
try
{

    // Tres tramos, dos cortes y tres colores, todos del tema: aquí no hay ningún umbral del programa.
    const string Ruled = """
        { "type": "bar", "id": "barra", "rect": [10, 10, 200, 20], "sensor": "cpu/0/temperature/package",
          "min": 0, "max": 100, "color": "#FFFFFF", "background": "#404040", "outline": "#0000FF", "outlineWidth": 2,
          "levels": { "low": "#00FF00", "medium": "#FFFF00", "high": "#FF0000", "mediumFrom": 40, "highFrom": 80 } }
        """;

    const string Rule = """
        "levels": { "low": "#00FF00", "medium": "#FFFF00", "high": "#FF0000", "mediumFrom": 40, "highFrom": 80 }
        """;

    var ruled = TryParse(Minimal(Ruled), out var ruledError);
    Check((ruled?.Widgets[0] as BarWidget)?.Levels is
          { Low: "#00FF00", Medium: "#FFFF00", High: "#FF0000", MediumFrom: 40, HighFrom: 80 },
          $"una gráfica lee sus tres colores y sus dos cortes {ruledError?.Message}");

    Check(Rejects(Minimal(Ruled.Replace("\"low\": \"#00FF00\", ", "", StringComparison.Ordinal)), "low"),
          "sin el color de un tramo se rechaza, en vez de dejarlo en nada");
    Check(Rejects(Minimal(Ruled.Replace("\"mediumFrom\": 40, ", "", StringComparison.Ordinal)), "mediumFrom"),
          "y sin un corte también, en vez de que valga 0 sin que nadie lo haya dicho");
    Check(Rejects(Minimal(Ruled.Replace("\"mediumFrom\": 40", "\"mediumFrom\": 90", StringComparison.Ordinal)), "90"),
          "los cortes cruzados se rechazan, diciendo los dos números");
    Check(Rejects(Minimal(Ruled.Replace("\"mediumFrom\": 40", "\"mediumFrom\": 80", StringComparison.Ordinal)), "80"),
          "y dos cortes en el mismo sitio también: no dejarían tramo medio");
    Check(Rejects(Minimal(Ruled.Replace("\"#FFFF00\"", "\"ámbar\"", StringComparison.Ordinal)), "#RRGGBB"),
          "un color de tramo que no es un color se rechaza como cualquier otro");
    Check(Rejects(Minimal($$"""{ "type": "text", "id": "t", "rect": [10, 10, 80, 20], "text": "CPU", {{Rule}} }"""), "texto fijo"),
          "un texto fijo con regla de color se rechaza: no tiene valor que mirar");
    Check(Rejects(Minimal($$"""{ "type": "clock", "id": "r", "rect": [10, 10, 80, 20], {{Rule}} }"""), "levels"),
          "y un reloj igual, que su valor no sale de ningún sensor");

    string ruledJson = LayoutLoader.Serialize(ruled!);
    Check(TryParse(ruledJson, out _)?.Widgets[0] is BarWidget again && again.Levels == (ruled!.Widgets[0] as BarWidget)?.Levels,
          "la regla se guarda y se vuelve a leer igual");
    Check(!LayoutLoader.Serialize(parsed!).Contains("levels", StringComparison.Ordinal),
          "y un tema sin regla se guarda sin la propiedad, como hasta ahora");

    var traffic = ValueColors.Traffic(0, 100);
    Check(traffic is { MediumFrom: 40, HighFrom: 80 }, "el preset corta a dos quintos y a cuatro quintos del recorrido");
    Check(ValueColors.Traffic(32, 212) is { MediumFrom: 104, HighFrom: 176 },
          "que en un recorrido en Fahrenheit son los mismos 40 y 80 grados");
    Check(ValueColors.Band(traffic, 39.999) == 0 && ValueColors.Band(traffic, 40) == 1, "el corte cuenta por abajo, incluido");
    Check(ValueColors.Band(traffic, 79.999) == 1 && ValueColors.Band(traffic, 80) == 2, "y el de arriba igual");
    Check(ValueColors.Band(traffic, 1000) == 2 && ValueColors.Band(traffic, -40) == 0, "fuera de los cortes mandan los extremos");

    // El editor rehace el dibujo con cada tecla, sobre un tema que todavía no ha validado nadie.
    var crossed = new ColorLevels { Low = "#000000", Medium = "#111111", High = "#222222", MediumFrom = 90, HighFrom = 10 };
    Check(ValueColors.Of(crossed, ValueColors.Band(crossed, 50)) == "#222222",
          "con los cortes cruzados sigue saliendo un tramo que existe, en vez de reventar");

    // Cómo se lee un tramo («menos de 40 °C») es texto de la ventana y va con su idioma: lo
    // comprueba la LocalizationCheck, en los dos.

    var amber = new SKColor(0xFF, 0xFF, 0x00);
    var blue = new SKColor(0x00, 0x00, 0xFF);
    var cyan = new SKColor(0x00, 0xFF, 0xFF);
    var track = new SKColor(0x40, 0x40, 0x40);
    var plainFill = new SKColor(0x16, 0x22, 0x3A);

    /// <summary>Si en esa caja hay algún píxel exactamente de ese color.</summary>
    bool Shows(LayoutRenderer renderer, PixelRect box, SKColor color)
    {
        for (int y = box.Y; y <= box.Bottom; y++)
            for (int x = box.X; x <= box.Right; x++)
                if (renderer.Surface.Bitmap.GetPixel(x, y) == color) return true;
        return false;
    }

    var ruledPair = LayoutLoader.Parse(Minimal($$"""
        {{Ruled}},
        { "type": "text", "id": "texto", "rect": [10, 60, 200, 48], "sensor": "cpu/0/temperature/package",
          "size": 40, "decimals": 0, "showUnit": false, "color": "#FFFFFF", {{Rule}} }
        """), "prueba");

    var barBox = new PixelRect(10, 10, 200, 20);
    var textBox = new PixelRect(10, 60, 200, 48);
    var ruler = new FakeSensors();

    using (var renderer = new LayoutRenderer(ruledPair))
    {
        // Sin lectura no hay tramo que elegir, así que mandan los colores fijos del widget. Es lo que
        // hace que sigan significando algo: son los de «no hay dato».
        renderer.RenderAll(ruler, TimeSpan.Zero);
        Check(Shows(renderer, barBox, blue), "sin lectura, el contorno se pinta de su propio color");
        Check(!Shows(renderer, barBox, greenLine), "y ningún tramo se aplica todavía");

        ruler.Set("cpu/0/temperature/package", 20f);
        renderer.Update(ruler, TimeSpan.FromSeconds(1));
        Check(Shows(renderer, barBox, greenLine) && !Shows(renderer, barBox, amber), "con 20 grados la barra va del tramo bajo");
        Check(Shows(renderer, barBox, track), "el fondo se queda como está: es el hueco, no el valor");
        Check(!Shows(renderer, barBox, blue), "y el contorno sí se tiñe, que es parte de lo que dibuja el valor");
        Check(Shows(renderer, textBox, greenLine), "el texto del mismo sensor va del mismo tramo");

        ruler.Set("cpu/0/temperature/package", 50f);
        var moved = renderer.Update(ruler, TimeSpan.FromSeconds(2));
        Check(moved.Count == 2, $"a 50 grados cambian los dos widgets ({moved.Count})");
        Check(Shows(renderer, barBox, amber) && !Shows(renderer, barBox, greenLine), "y la barra pasa al tramo medio");

        // Con cero decimales, 39,6 y 40,4 escriben los dos «40» y solo cambian de color.
        ruler.Set("cpu/0/temperature/package", 39.6f);
        renderer.Update(ruler, TimeSpan.FromSeconds(3));
        string before = renderer.DisplayedText("texto") ?? "";
        ruler.Set("cpu/0/temperature/package", 40.4f);
        moved = renderer.Update(ruler, TimeSpan.FromSeconds(4));
        Check(renderer.DisplayedText("texto") == before, $"un texto puede cambiar de tramo sin cambiar de texto ({before})");
        Check(moved.Any(d => d.WidgetId == "texto"), "y aun así se envía, o se quedaría del color de antes para siempre");
        Check(Shows(renderer, textBox, amber), "porque lo que ha cambiado es el color");
    }

    // Un corte por encima del máximo: el dibujo ya está al tope, pero el color todavía cambia.
    var beyond = LayoutLoader.Parse(Minimal("""
        { "type": "bar", "id": "barra", "rect": [10, 10, 100, 20], "sensor": "cpu/0/load/total",
          "min": 0, "max": 100, "color": "#FFFFFF",
          "levels": { "low": "#00FF00", "medium": "#FFFF00", "high": "#FF0000", "mediumFrom": 40, "highFrom": 130 } }
        """), "prueba");

    var full = new PixelRect(10, 10, 100, 20);
    var saturated = new FakeSensors();
    saturated.Set("cpu/0/load/total", 120f, SensorKind.Load);
    using (var renderer = new LayoutRenderer(beyond))
    {
        renderer.RenderAll(saturated, TimeSpan.Zero);
        Check(Shows(renderer, full, amber), "por encima del máximo la barra está llena, y del tramo que le toque");

        saturated.Set("cpu/0/load/total", 140f, SensorKind.Load);
        var moved = renderer.Update(saturated, TimeSpan.FromSeconds(1));
        Check(moved.Count == 1, "cruzar un corte sin mover el relleno ni un píxel también ensucia el widget");
        Check(Shows(renderer, full, red) && !Shows(renderer, full, amber), "y se ve, que para eso está");

        saturated.Set("cpu/0/load/total", 150f, SensorKind.Load);
        Check(renderer.Update(saturated, TimeSpan.FromSeconds(2)).Count == 0, "con el relleno y el color quietos no se envía nada");
    }

    Check(LayoutInspector.Warnings(beyond, saturated).Any(w => w.Code == "layout.warn.levels-outside"),
          "el inspector avisa del corte que cae fuera del recorrido");
    Check(!LayoutInspector.Warnings(ruledPair, ruler).Any(w => w.Code == "layout.warn.levels-outside"),
          "y no avisa de los que caen dentro");

    // Con el recorrido de longitud cero la barra no se mueve, pero el color sale igual del valor.
    var flat = LayoutLoader.Parse(Minimal($$"""
        { "type": "bar", "id": "barra", "rect": [10, 10, 100, 20], "sensor": "cpu/0/load/total",
          "min": 50, "max": 50.001, "color": "#FFFFFF", "outline": "#0000FF", "outlineWidth": 2, {{Rule}} }
        """), "prueba");

    using (var renderer = new LayoutRenderer(flat))
    {
        renderer.RenderAll(saturated, TimeSpan.Zero);
        Check(Shows(renderer, full, red) && !Shows(renderer, full, blue),
              "el color sale del valor y no del dibujo: con un recorrido sin longitud sigue saliendo");
    }

    var arcs = LayoutLoader.Parse(Minimal($$"""
        { "type": "gauge", "id": "hereda", "rect": [10, 10, 100, 100], "sensor": "cpu/0/load/total",
          "thickness": 8, "color": "#FFFFFF", "label": { "size": 24, "decimals": 0, "showUnit": false }, {{Rule}} },
        { "type": "gauge", "id": "propio", "rect": [150, 10, 100, 100], "sensor": "cpu/0/load/total",
          "thickness": 8, "color": "#FFFFFF", "label": { "size": 24, "decimals": 0, "showUnit": false, "color": "#00FFFF" }, {{Rule}} }
        """), "prueba");

    // El hueco de dentro del arco, donde solo puede haber número: la banda va por el borde de la caja.
    var heirBox = new PixelRect(35, 45, 50, 30);
    var own = new PixelRect(175, 45, 50, 30);
    using (var renderer = new LayoutRenderer(arcs))
    {
        renderer.RenderAll(saturated, TimeSpan.Zero);
        Check(Shows(renderer, heirBox, red), "el número de dentro del arco hereda el tramo, como hereda el color");
        Check(Shows(renderer, own, cyan) && !Shows(renderer, own, red), "salvo que tenga color propio, que entonces manda el suyo");
    }

    var charts = LayoutLoader.Parse(Minimal($$"""
        { "type": "chart", "id": "con", "rect": [10, 10, 120, 60], "sensor": "cpu/0/load/total",
          "color": "#FFFFFF", "fill": "#16223A", "samples": 10, {{Rule}} },
        { "type": "chart", "id": "sin", "rect": [10, 100, 120, 60], "sensor": "cpu/0/load/total",
          "color": "#FFFFFF", "fill": "#16223A", "samples": 10 }
        """), "prueba");

    var chartBox = new PixelRect(10, 10, 120, 60);
    var plainChart = new PixelRect(10, 100, 120, 60);
    using (var renderer = new LayoutRenderer(charts))
    {
        // El histórico entero lleno, que hasta que no lo está no hay área que rellenar a la izquierda.
        for (int second = 0; second <= 12; second++) renderer.Update(saturated, TimeSpan.FromSeconds(second));

        var under = renderer.Surface.Bitmap.GetPixel(70, 65);
        Check(Shows(renderer, chartBox, red), "la línea del histórico va del tramo");
        Check(!Shows(renderer, chartBox, plainFill), "y el relleno deja de ser el suyo fijo");
        Check(under != background && under != red, "pero rebajado, o la línea desaparecería dentro de él");
        Check(Shows(renderer, plainChart, plainFill), "un histórico sin regla se queda exactamente como estaba");
    }

    // Un color a medio teclear lanza al construir, igual que cualquier otro del tema: el editor rehace
    // el renderizador con cada tecla y ya sabe recoger esto.
    var typing = new LayoutDocument
    {
        Schema = LayoutLoader.CurrentSchema,
        Display = new LayoutDisplay { Width = 100, Height = 40 },
        Widgets =
        [
            new BarWidget
            {
                Id = "barra",
                Rect = new PixelRect(0, 0, 50, 20),
                Sensor = "cpu/0/load/total",
                Levels = new ColorLevels { Low = "#00FF0", Medium = "#FFFF00", High = "#FF0000", MediumFrom = 40, HighFrom = 80 },
            },
        ],
    };

    bool complained = false;
    try
    {
        using var renderer = new LayoutRenderer(typing);
    }
    catch (Exception e) when (e is ArgumentException or FormatException)
    {
        complained = true;
    }
    Check(complained, "un color de la regla a medio teclear lanza al construir el renderizador");
}
catch (Exception e)
{
    Broke("Color por valor", e);
}

Header("Elección de tipografía");
try
{

    // La lista de instaladas se pasa a mano: así la comprobación dice lo mismo en la máquina de quien
    // tenga Cascadia Mono y en la de quien no, que es justo el caso que hay que cubrir.
    string[] windowsFonts = ["Segoe UI", "Consolas", "Courier New", "Arial"];
    string[] linuxFonts = ["DejaVu Sans", "DejaVu Sans Mono", "Liberation Mono"];
    string[] oddFonts = ["Comic Sans MS"];

    Check(FontChoice.Resolve(null, windowsFonts) == "Consolas",
          $"sin elegir nada, la mejor monoespaciada que haya ({FontChoice.Resolve(null, windowsFonts)})");
    Check(FontChoice.Resolve(null, ["Cascadia Mono", ..windowsFonts]) == "Cascadia Mono",
          "y Cascadia Mono le gana a Consolas cuando está");
    Check(FontChoice.Resolve(null, linuxFonts) == "DejaVu Sans Mono", "en Linux sale la suya");
    Check(FontChoice.Resolve(null, oddFonts) is null, "sin ninguna conocida se deja decidir al sistema");

    Check(FontChoice.Resolve("Courier New", windowsFonts) == "Courier New", "la elegida manda si está instalada");
    Check(FontChoice.Resolve("JetBrains Mono", windowsFonts) == "Consolas",
          $"y si no está se cae a la lista en vez de dejarlo en blanco ({FontChoice.Resolve("JetBrains Mono", windowsFonts)})");
    Check(FontChoice.Resolve("consolas", windowsFonts) == "consolas",
          "el nombre no distingue mayúsculas: Windows y fontconfig no las escriben igual");
    Check(FontChoice.Resolve("JetBrains Mono", oddFonts) is null, "una elegida que no está y ninguna conocida: el sistema");

    Check(FontChoice.FirstInstalled([null, "", "  ", "Arial"], windowsFonts) == "Arial",
          "los huecos de la lista se saltan en vez de pedir una fuente sin nombre");
    Check(FontChoice.Monospace.Distinct(StringComparer.OrdinalIgnoreCase).Count() == FontChoice.Monospace.Count,
          "la lista de preferencia no repite ninguna");
}
catch (Exception e)
{
    Broke("Elección de tipografía", e);
}

Header("Render de la ventana");
try
{

    // El modo se fija al construir la aplicación, así que la vuelta atrás tiene que sobrevivir a un
    // reinicio: el intento se apunta y se borra antes de usarlo, y confirmarlo es cosa de la ventana.
    var quiet = GuiRenderChoice.Start(RenderMode.Software, null);
    Check(quiet.Mode == RenderMode.Software && !quiet.OnTrial, "sin nada que probar se arranca con el que se sabe bueno");

    var trial = GuiRenderChoice.Start(RenderMode.Software, RenderMode.Direct3D);
    Check(trial.Mode == RenderMode.Direct3D && trial.Trying == RenderMode.Direct3D,
          "lo que se pide probar se usa, y queda a prueba");

    var same = GuiRenderChoice.Start(RenderMode.OpenGl, RenderMode.OpenGl);
    Check(same.Mode == RenderMode.OpenGl && !same.OnTrial,
          "probar el que ya se usa no es probar nada, y no deja una confirmación pendiente");

    // Esto es la vuelta atrás entera: el arranque que probó ya borró el intento del disco, así que el
    // siguiente llega aquí sin él y usa el bueno. Una ventana que no se ve no se prueba dos veces.
    var after = GuiRenderChoice.Start(RenderMode.Software, null);
    Check(after.Mode == RenderMode.Software && !after.OnTrial,
          "y el arranque siguiente, ya sin el intento, vuelve solo al anterior");

    var forced = GuiRenderChoice.Start(RenderMode.Direct3D, RenderMode.OpenGl, RenderMode.Software);
    Check(forced.Mode == RenderMode.Software && !forced.OnTrial,
          "la variable de entorno manda sobre los dos, y lo suyo no se pone a prueba");

    Check(GuiRenderChoice.Parse("software") == RenderMode.Software, "«software» sigue valiendo lo que valía");
    Check(GuiRenderChoice.Parse("  SOFTWARE  ") == RenderMode.Software, "sin distinguir mayúsculas ni espacios");
    Check(GuiRenderChoice.Parse("gpu") == RenderMode.Direct3D && GuiRenderChoice.Parse("angle") == RenderMode.Direct3D,
          "«gpu» y «angle» son Direct3D");
    Check(GuiRenderChoice.Parse("opengl") == RenderMode.OpenGl && GuiRenderChoice.Parse("wgl") == RenderMode.OpenGl
          && GuiRenderChoice.Parse("glx") == RenderMode.OpenGl,
          "«opengl», «wgl» y «glx» son OpenGL nativo, que nadie escribe «OpenGl» a mano");
    Check(GuiRenderChoice.Parse("egl") == RenderMode.Egl,
          "«egl» es lo suyo y no OpenGL nativo: en Linux son dos caminos distintos");
    Check(GuiRenderChoice.Parse("vulkan") == RenderMode.Vulkan && GuiRenderChoice.Parse("vk") == RenderMode.Vulkan,
          "y Vulkan se puede pedir, que el automático no lo prueba");
    Check(GuiRenderChoice.Parse(null) is null && GuiRenderChoice.Parse("") is null && GuiRenderChoice.Parse("chorrada") is null,
          "lo que no nombra ningún modo no fuerza nada");
}
catch (Exception e)
{
    Broke("Render de la ventana", e);
}

Header("Fecha y hora");
try
{

    // Un miércoles, para que el nombre del día no salga bien por casualidad.
    var instant = new DateTimeOffset(2026, 9, 16, 14, 5, 9, TimeSpan.Zero);
    string Clock(string format) => ClockFormat.Compose(format, instant);

    Check(Clock("HH:mm") == "14:05", $"la hora, con el cero delante ({Clock("HH:mm")})");
    Check(Clock("HH:mm:ss") == "14:05:09", "con segundos");
    Check(Clock("H:m:s") == "14:5:9", "o sin rellenar");
    Check(Clock("h:mm tt") == "2:05 PM", $"el reloj de doce horas ({Clock("h:mm tt")})");
    Check(ClockFormat.Compose("h:mm tt", new DateTimeOffset(2026, 9, 16, 0, 30, 0, TimeSpan.Zero)) == "12:30 AM",
          "y las doce de la noche son las 12, no las 0");
    Check(Clock("dd/MM/yyyy") == "16/09/2026", $"la fecha ({Clock("dd/MM/yyyy")})");
    Check(Clock("d/M/yy") == "16/9/26", "en corto");
    Check(Clock("dddd") == "miércoles", $"el día de la semana, en castellano ({Clock("dddd")})");
    Check(Clock("MMMM") == "septiembre", $"y el mes ({Clock("MMMM")})");
    Check(Clock("ddd d MMM") == "mié 16 sep", $"abreviados ({Clock("ddd d MMM")})");
    Check(Clock("dddd d 'de' MMMM") == "miércoles 16 de septiembre",
          $"entre comillas va texto tal cual, que si no la «d» sería el día ({Clock("dddd d 'de' MMMM")})");
    Check(Clock("[HH]") == "[14]", "lo que no es una marca pasa de largo");

    // Los nombres van con el idioma que se pide, y lo que escribe el tema entre comillas, no.
    string English(string format) => ClockFormat.Compose(format, instant, "en");
    Check(English("dddd") == "Wednesday" && English("MMMM") == "September", $"en inglés, sus nombres ({English("dddd MMMM")})");
    Check(English("ddd d MMM") == "Wed 16 Sep", $"y abreviados ({English("ddd d MMM")})");
    Check(English("dddd d 'de' MMMM") == "Wednesday 16 de September", "lo que va entre comillas es del tema, no del idioma");
    Check(ClockFormat.Compose("dddd", instant, "xx") == "miércoles", "un idioma que no se conoce sale en español");
    Check(English("HH:mm") == "14:05", "las cifras no cambian con el idioma");
    Check(Clock("") == "", "un patrón vacío no da texto");

    Check(ClockFormat.Period("HH:mm:ss") == TimeSpan.FromSeconds(1), "un reloj con segundos puede cambiar cada segundo");
    Check(ClockFormat.Period("HH:mm") == TimeSpan.FromMinutes(1), "sin ellos, una vez por minuto");
    Check(ClockFormat.Period("h 'en punto'") == TimeSpan.FromHours(1), "con la hora sola, una vez por hora");
    Check(ClockFormat.Period("dddd d 'de' MMMM") == TimeSpan.FromDays(1), "y una fecha, una vez al día");

    var clockLayout = LayoutLoader.Parse(Minimal("""
        { "type": "clock", "id": "hora", "rect": [10, 10, 120, 34], "format": "HH:mm", "size": 26 },
        { "type": "clock", "id": "fecha", "rect": [10, 60, 200, 30], "format": "dddd d 'de' MMMM" }
        """), "prueba");

    var fake = new FakeClock(instant);
    using (var renderer = new LayoutRenderer(clockLayout, fake))
    {
        renderer.RenderAll(new FakeSensors(), TimeSpan.Zero);
        Check(renderer.SensorIds.Count == 0, "un reloj no le pide sensores a nadie");
        Check(renderer.DisplayedText("hora") == "14:05", $"y muestra la hora de la máquina ({renderer.DisplayedText("hora")})");
        Check(renderer.DisplayedText("fecha") == "miércoles 16 de septiembre", "o la fecha");

        // Medio minuto después el reloj sigue diciendo lo mismo, así que no hay nada que enviar.
        fake.Now = instant.AddSeconds(30);
        Check(renderer.Update(new FakeSensors(), TimeSpan.FromSeconds(1)).Count == 0,
              "un «HH:mm» que no ha cambiado de minuto no gasta enlace");

        fake.Now = instant.AddMinutes(1);
        var ticked = renderer.Update(new FakeSensors(), TimeSpan.FromSeconds(2));
        Check(ticked.Any(d => d.WidgetId == "hora"), "y cuando cambia, sí");
        Check(!ticked.Any(d => d.WidgetId == "fecha"), "pero la fecha sigue siendo la misma");
        Check(renderer.DisplayedText("hora") == "14:06", "con el minuto nuevo");
    }

    // Por eso el medidor tampoco puede cobrarle un refresco por segundo: cabe de sobra y saldría en rojo.
    var clockCost = BandwidthBudget.Analyze(LayoutInspector.Consumers(clockLayout), caps);
    double hourly = clockCost.Consumers.First(c => c.WidgetId == "hora").BytesPerSecond;
    Check(Near(hourly, 120 * 34 * 2 / 60.0), $"un reloj cuesta lo que puede cambiar, no lo que se le mira ({hourly:N0} B/s)");

    Check(Problems("""{ "type": "clock", "id": "c", "rect": [0, 0, 100, 20], "format": "" }""")
              .Any(p => p.Contains("formato", StringComparison.Ordinal)),
          "un reloj sin formato no se carga");
    Check(Problems("""{ "type": "clock", "id": "c", "rect": [0, 0, 100, 20], "sensor": "cpu/0/load/total" }""").Count > 0,
          "y un reloj no lleva sensor");
}
catch (Exception e)
{
    Broke("Fecha y hora", e);
}

Header("Un layout es una carpeta que se sostiene sola");
try
{

    {
        // Del nombre sale la carpeta y el fichero: el usuario no elige ruta ninguna. El nombre lleva el
        // identificador del proceso porque esto mira la carpeta de layouts de esta máquina, y uno fijo
        // se rompía el día que alguien guardara un layout llamado igual. Pasó con «naruto».
        string inventado = $"no-guardado-{Environment.ProcessId}";
        string sitio = LayoutPaths.For(inventado);
        Check(Path.GetFileName(sitio) == inventado + ".json" && Path.GetFileName(Path.GetDirectoryName(sitio)!) == inventado,
              $"un layout va en una carpeta con su nombre ({sitio})");
        Check(Path.GetFullPath(sitio).StartsWith(Path.GetFullPath(LayoutPaths.Root), StringComparison.OrdinalIgnoreCase),
              "y siempre dentro de la carpeta de layouts");
        Check(LayoutPaths.NameOf(sitio) is null, "que no exista todavía no se cuenta como layout");

        // Los nombres se comprueban al escribirlos, no al guardar.
        Check(LayoutPaths.NameProblem("Mi layout") is null, "un nombre corriente vale");
        Check(LayoutPaths.NameProblem("") is not null, "uno vacío no");
        Check(LayoutPaths.NameProblem("  con espacios  ") is not null, "ni con espacios a los lados");
        Check(LayoutPaths.NameProblem("CPU: carga") is not null, "ni con caracteres que no valen en una carpeta");
        Check(LayoutPaths.NameProblem("acaba en punto.") is not null, "ni acabando en punto");
        Check(LayoutPaths.NameProblem("CON") is not null, "ni con un nombre reservado del sistema");
        Check(LayoutPaths.NameProblem("com3") is not null, "aunque venga en minúsculas");

        // Borrar un layout es borrar su carpeta entera y recursivamente, así que lo único que hay entre
        // un nombre escrito en la ventana y un Directory.Delete es NameProblem. Que lo mire antes de
        // tocar el disco es la mitad del asunto.
        Check(ThrowsArgument(() => LayoutPaths.Delete("")), "borrar: un nombre vacío no llega al disco");
        Check(ThrowsArgument(() => LayoutPaths.Delete("..")), "borrar: «..» tampoco, que acaba en punto");
        Check(ThrowsArgument(() => LayoutPaths.Delete(Path.Combine("..", "otro"))), "borrar: ni una ruta con separadores");
        Check(ThrowsArgument(() => LayoutPaths.Delete("CON")), "borrar: ni un nombre reservado del sistema");
        Check(!LayoutPaths.Delete(inventado), "borrar uno que no está no es un error, es un false");

        // Y la vuelta entera, en la carpeta de layouts de verdad, porque Delete sale de ahí y no de una
        // ruta que se le pase. El nombre lleva el identificador del proceso por lo mismo que el de arriba.
        string aBorrar = $"borrar-{Environment.ProcessId}";
        string carpeta = Path.Combine(LayoutPaths.Root, aBorrar);
        try
        {
            Directory.CreateDirectory(carpeta);
            File.WriteAllText(Path.Combine(carpeta, aBorrar + ".json"), Minimal(""));
            File.WriteAllBytes(Path.Combine(carpeta, "fondo.png"), [1, 2, 3, 4]);

            Check(LayoutPaths.All().Contains(aBorrar, StringComparer.OrdinalIgnoreCase), "una carpeta con su json dentro se lista");
            Check(LayoutPaths.Delete(aBorrar), "borrarla dice que sí");
            Check(!Directory.Exists(carpeta), "y se lleva la carpeta entera, con la imagen dentro");
            Check(!LayoutPaths.All().Contains(aBorrar, StringComparer.OrdinalIgnoreCase), "y deja de listarse");
        }
        finally
        {
            // Si algo se ha torcido por el camino, que no quede criando polvo en la máquina de nadie.
            if (Directory.Exists(carpeta)) Directory.Delete(carpeta, recursive: true);
        }

        string banco = Path.Combine(Path.GetTempPath(), "vibescreen-carpeta-" + Environment.ProcessId);
        string fuera = Path.Combine(banco, "descargas");
        Directory.CreateDirectory(fuera);

        // Una imagen que vive fuera del layout, como la que cualquiera se acaba de descargar.
        string origen = Path.Combine(fuera, "itachi.png");
        File.WriteAllBytes(origen, [1, 2, 3, 4]);

        var doc = new LayoutDocument
        {
            Schema = LayoutLoader.CurrentSchema,
            Display = new LayoutDisplay { Width = 480, Height = 320, Orientation = Orientation.Landscape },
            Background = new ImageBackground { Path = origen, Color = "#000000" },
            Widgets = [],
        };

        string destino = Path.Combine(banco, "mitema", "mitema.json");
        LayoutLoader.Save(doc, destino);

        string dentro = Path.Combine(Path.GetDirectoryName(destino)!, "fondo.png");
        Check(File.Exists(dentro), "la imagen de fondo se copia dentro de la carpeta del layout");
        Check(((ImageBackground)doc.Background).Path == "fondo.png", "y el layout la nombra relativa, no por dónde estaba");

        // Lo que justifica copiarla: el usuario vacía la carpeta de descargas.
        File.Delete(origen);
        var vuelto = LayoutLoader.Load(destino);
        string resuelto = vuelto.Resolve(((ImageBackground)vuelto.Background).Path);
        Check(File.Exists(resuelto), "y borrar la original no deja al layout sin fondo");
        Check(LayoutInspector.Warnings(vuelto, new FakeSensors()).Count == 0, "ni saca ningún aviso");

        // Guardar otra vez sin tocar nada no se lía consigo mismo.
        LayoutLoader.Save(vuelto, destino);
        Check(File.Exists(dentro) && ((ImageBackground)vuelto.Background).Path == "fondo.png",
              "guardar de nuevo sin cambiar la imagen la deja como está");

        // Y cambiarla por otra de distinto formato no deja la anterior criando polvo.
        string otra = Path.Combine(fuera, "sharingan.jpg");
        File.WriteAllBytes(otra, [5, 6, 7, 8]);
        ((ImageBackground)vuelto.Background).Path = otra;
        LayoutLoader.Save(vuelto, destino);
        Check(File.Exists(Path.Combine(Path.GetDirectoryName(destino)!, "fondo.jpg")), "cambiar de imagen la trae igual");
        Check(!File.Exists(dentro), "y se lleva por delante la del formato anterior");

        // El nombre con el que se guarda es el que se ve en la pestaña Panel y en «service status», y
        // sale del fichero: por eso tiene que entrar en el documento antes de escribirlo.
        var bautizado = new LayoutDocument
        {
            Schema = LayoutLoader.CurrentSchema,
            Display = new LayoutDisplay { Width = 480, Height = 320, Orientation = Orientation.Landscape },
            Meta = new LayoutMeta { Author = "Itachi" },
            Widgets = [],
        };

        string uno = Path.Combine(banco, "uno", "uno.json");
        LayoutLoader.Save(bautizado, uno, "Uno");
        Check(LayoutLoader.Load(uno).Meta?.Name == "Uno", "el nombre con el que se guarda queda dentro del fichero");
        Check(LayoutLoader.Load(uno).Meta?.Author == "Itachi", "sin llevarse por delante lo demás del meta");

        // Y el mismo documento guardado otra vez con otro nombre: en el fichero no vale el de la vez anterior.
        string dos = Path.Combine(banco, "dos", "dos.json");
        LayoutLoader.Save(bautizado, dos, "Dos");
        Check(LayoutLoader.Load(dos).Meta?.Name == "Dos", "y guardar con otro nombre no deja el del guardado anterior");

        try { Directory.Delete(banco, recursive: true); }
        catch (IOException) { }
    }
}
catch (Exception e)
{
    Broke("Un layout es una carpeta que se sostiene sola", e);
}

Header("Copiar un widget");
try
{

    // Duplicar un widget para cambiarle solo el sensor. Va por una ida y vuelta del serializador porque
    // «with» copiaría por encima y la copia compartiría la regla de color y la etiqueta del arco con el
    // original, que el editor cambia en sitio.
    var aCopiar = LayoutLoader.Parse(Minimal(
        """
        { "type": "text", "id": "t", "rect": [0, 0, 100, 30], "sensor": "cpu/0/temperature/package", "decimals": 1,
          "font": "Consolas", "color": "#FFEF08", "bold": true, "align": "right", "prefix": "CPU ", "showUnit": false,
          "levels": { "low": "#4CAF50", "medium": "#FFC107", "high": "#F44336", "mediumFrom": 40, "highFrom": 80 } },
        { "type": "gauge", "id": "g", "rect": [120, 0, 80, 80], "sensor": "gpu/0/temperature/core", "max": 90,
          "outline": "#222222", "round": false, "label": { "size": 18, "decimals": 0, "prefix": "GPU " },
          "levels": { "low": "#4CAF50", "medium": "#FFC107", "high": "#F44336", "mediumFrom": 50, "highFrom": 85 } },
        { "type": "chart", "id": "h", "rect": [0, 100, 200, 60], "sensor": "cpu/0/load/total", "fill": "#16223A", "samples": 40 },
        { "type": "clock", "id": "r", "rect": [0, 200, 100, 20], "format": "HH:mm" }
        """), "copiar");

    // La igualdad por valor del record lo prueba de una vez: si se hubiera perdido una sola propiedad
    // por el camino, la copia ya no sería igual.
    foreach (var original in aCopiar.Widgets)
    {
        var copia = LayoutLoader.Copy(original);
        Check(copia == original, $"copiar «{original.Id}» no pierde ni una propiedad");
        Check(!ReferenceEquals(copia, original), $"y sale otro objeto, no el mismo ({original.Id})");
    }

    // La copia sale exacta porque ninguna propiedad anulable del modelo lleva inicializador: al escribir
    // se omiten los nulos, y una anulable con inicializador volvería con él puesto. Un widget con todo lo
    // opcional sin poner es el que lo destaparía.
    var pelado = LayoutLoader.Parse(Minimal(
        """{ "type": "text", "id": "p", "rect": [0, 0, 60, 20], "text": "hola" }"""), "pelado").Widgets[0];
    Check(LayoutLoader.Copy(pelado) == pelado, "un widget con todo lo opcional sin poner también sale igual");

    var texto = (TextWidget)aCopiar.Widgets[0];
    var textoCopia = (TextWidget)LayoutLoader.Copy(texto);
    Check(!ReferenceEquals(textoCopia.Levels, texto.Levels), "la regla de color de la copia es otro objeto");
    texto.Levels!.Low = "#000000";
    Check(textoCopia.Levels!.Low == "#4CAF50", "así que retocar el original después no le cambia el color a la copia");

    var arco = (GaugeWidget)aCopiar.Widgets[1];
    var arcoCopia = (GaugeWidget)LayoutLoader.Copy(arco);
    Check(!ReferenceEquals(arcoCopia.Label, arco.Label), "y el número de dentro del arco, también");
    arco.Label!.Prefix = "otro";
    Check(arcoCopia.Label!.Prefix == "GPU ", "y retocarlo en el original tampoco toca la copia");
}
catch (Exception e)
{
    Broke("Copiar un widget", e);
}

Header("Guardar no toca el tema si no llega a escribir");
try
{

    {
        string folder = Path.Combine(Path.GetTempPath(), "vibescreen-check-" + Environment.ProcessId);
        string near = Path.Combine(folder, "cerca");
        Directory.CreateDirectory(near);

        string image = Path.Combine(near, "fondo.png");
        File.WriteAllBytes(image, [0]);

        var theme = LayoutLoader.Parse($$"""
            {
              "schema": 2,
              "display": { "width": 480, "height": 320, "orientation": "landscape" },
              "background": { "type": "image", "path": {{JsonEscape(image)}}, "color": "#000000" },
              "widgets": []
            }
            """);
        theme.Directory = folder;

        // Un widget que no cabe: guardar tiene que fallar al validar, antes de escribir nada.
        theme.Widgets = [new TextWidget { Id = "t", Rect = new PixelRect(0, 0, 9999, 20), Text = "x" }];

        string target = Path.Combine(near, "tema.json");
        string before = ((ImageBackground)theme.Background).Path;
        bool failed = false;
        try { LayoutLoader.Save(theme, target); }
        catch (LayoutException) { failed = true; }

        Check(failed, "guardar un tema inválido falla");
        Check(((ImageBackground)theme.Background).Path == before,
              "y la ruta de la imagen queda como estaba, no a medio convertir en relativa");
        Check(!File.Exists(target + ".tmp"), "sin dejar ningún temporal al lado");

        // El nombre va por el mismo camino que la imagen: guardar con uno que no llega al disco no deja
        // el tema llamándose así, ni le inventa un meta para apuntarlo.
        bool refused = false;
        try { LayoutLoader.Save(theme, target, "Tema que no cabe"); }
        catch (LayoutException) { refused = true; }

        Check(refused, "guardar con nombre un tema inválido falla igual");
        Check(theme.Meta is null, "y el tema no se queda con el nombre que no llegó a escribirse");

        // Y cuando sí se guarda, la imagen que vive al lado pasa a relativa.
        theme.Widgets = [];
        LayoutLoader.Save(theme, target);
        Check(((ImageBackground)theme.Background).Path == "fondo.png", "al guardar de verdad, la imagen queda relativa");
        Check(theme.Resolve(((ImageBackground)theme.Background).Path) == image, "y se sigue encontrando donde está");

        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { }
    }
}
catch (Exception e)
{
    Broke("Guardar no toca el tema si no llega a escribir", e);
}

Header("Un tema que ya no cabe en su panel");
try
{

    {
        // Cambiar el tamaño del panel en el editor puede dejar widgets fuera. El cargador los rechaza al
        // guardar, así que hay que decirlo mientras se edita y no media hora después.
        var shrunk = LayoutLoader.Parse("""
            {
              "schema": 2,
              "display": { "width": 480, "height": 320, "orientation": "landscape" },
              "background": { "type": "color", "color": "#000000" },
              "widgets": [
                { "type": "text", "id": "dentro", "rect": [0, 0, 100, 24], "text": "x" }
              ]
            }
            """);

        Check(LayoutInspector.Warnings(shrunk, new FakeSensors(), _ => true).Count == 0,
              "un tema que cabe no tiene nada que decir");

        shrunk.Display.Width = 64;
        var avisos = LayoutInspector.Warnings(shrunk, new FakeSensors(), _ => true);
        Check(avisos.Any(w => w.Code.StartsWith("layout.warn.outside.", StringComparison.Ordinal)),
              "al encoger el panel, avisa de que un widget se sale");
        Check(avisos.Any(w => w.ToString().Contains("guardar", StringComparison.Ordinal)),
              "y de que por eso no se va a poder guardar");
        Check(LayoutLoader.Validate(shrunk).Count > 0, "que es justo lo que el cargador rechaza");
    }
}
catch (Exception e)
{
    Broke("Un tema que ya no cabe en su panel", e);
}

Header("Medir un tema a medio escribir");
try
{

    {
        // El medidor del editor se recalcula mientras se teclea el número de refresco: un 0 a medio
        // escribir daba infinito, y un negativo restaba consumo del total.
        var half = LayoutLoader.Parse("""
            {
              "schema": 2,
              "display": { "width": 480, "height": 320, "orientation": "landscape" },
              "background": { "type": "color", "color": "#000000" },
              "widgets": []
            }
            """);
        half.Widgets =
        [
            new TextWidget { Id = "cero", Rect = new PixelRect(0, 0, 100, 24), Text = "x", RefreshMs = 0 },
            new TextWidget { Id = "menos", Rect = new PixelRect(0, 30, 100, 24), Text = "x", RefreshMs = -500 },
        ];

        var cost = BandwidthBudget.Analyze(LayoutInspector.Consumers(half), caps);
        Check(double.IsFinite(cost.RequiredBytesPerSecond),
              $"un refreshMs a medio teclear no da un consumo infinito ({cost.RequiredBytesPerSecond:N0} B/s)");
        Check(cost.RequiredBytesPerSecond > 0, "ni negativo");
        Check(cost.Consumers.All(c => c.RefreshHz <= 1000.0 / LayoutLoader.MinRefreshMs),
              "se le supone el refresco más rápido que el formato admite, que es la cota honrada");
    }
}
catch (Exception e)
{
    Broke("Medir un tema a medio escribir", e);
}

Header("Elegir un layout al azar");
try
{

    {
        Check(LayoutChoice.AtRandom([]) is null, "sin layouts guardados no hay nada que elegir");
        Check(LayoutChoice.AtRandom(["uno"]) == "uno", "con uno solo, ese");
        Check(LayoutChoice.AtRandom(["uno"], "uno") == "uno",
              "y ese aunque venga de repetir: con uno no hay nada que evitar");
        Check(LayoutChoice.AtRandom(["uno", "dos"], "UNO") == "dos",
              "el de la última vez se reconoce sin mirar mayúsculas, que es como se comparan las carpetas");

        // Doscientas tiradas con los dados fijados: ni una repetición, y los tres salen.
        string[] tres = ["uno", "dos", "tres"];
        var dados = new Random(7);
        var salidos = new HashSet<string>(StringComparer.Ordinal);
        string? anterior = "dos";
        bool repitio = false;

        for (int i = 0; i < 200; i++)
        {
            string elegido = LayoutChoice.AtRandom(tres, anterior, dados)!;
            if (string.Equals(elegido, anterior, StringComparison.Ordinal)) repitio = true;
            salidos.Add(elegido);
            anterior = elegido;
        }

        Check(!repitio, "habiendo más de uno, nunca sale el de la última vez");
        Check(salidos.Count == 3, "y en doscientos arranques salen todos");
    }
}
catch (Exception e)
{
    Broke("Elegir un layout al azar", e);
}

Header("El tema de serie");
try
{

    // El que deja el instalador y con el que arranca el panel la primera vez. Si no carga, la primera
    // impresión de quien acaba de instalar es un panel en negro con un aviso.
    string bundled = Path.Combine(AppContext.BaseDirectory, "themes", LayoutPaths.Bundled, LayoutPaths.Bundled + ".json");
    Check(File.Exists(bundled),
          $"está en packaging/themes/{LayoutPaths.Bundled}/{LayoutPaths.Bundled}.json, que es donde el servicio lo va a buscar");

    if (File.Exists(bundled))
    {
        LayoutDocument? theme = null;
        string? problem = null;
        try { theme = LayoutLoader.Load(bundled); }
        catch (LayoutException e) { problem = e.Message; }
        Check(theme is not null, $"carga sin errores {problem}");

        if (theme is not null)
        {
            Check(theme.Background is not ImageBackground image || File.Exists(theme.Resolve(image.Path)),
                  "si lleva imagen de fondo, va dentro de su carpeta");

            // Los sensores no se pueden comprobar aquí —dependen de la máquina—, pero lo demás sí.
            var warnings = LayoutInspector.Warnings(theme, new FakeSensors())
                .Where(w => w.Code != "layout.warn.missing-sensor")
                .Select(w => w.Code)
                .ToList();
            Check(warnings.Count == 0, $"sin avisos en el editor ({string.Join(", ", warnings)})");

            var cost = BandwidthBudget.Analyze(LayoutInspector.Consumers(theme), caps);
            Check(!cost.NearLimit, $"cabe en el panel con holgura ({cost.Utilization:P0} del presupuesto)");
        }
    }
}
catch (Exception e)
{
    Broke("El tema de serie", e);
}

Header("Identificar el panel con la respuesta partida");
try
{

    {
        // Sobre CDC-ACM los seis bytes de la respuesta pueden llegar en varias transferencias.
        var partida = new SplitTransport([[0x02], [0x02, 0x02], [0x02, 0x02, 0x02]]);
        var caps480 = await RevADisplayProbe.IdentifySubModelAsync(partida);
        Check(caps480.NativeWidth == 480 && caps480.NativeHeight == 800,
              $"una respuesta en tres trozos identifica el panel igual ({caps480.NativeWidth}x{caps480.NativeHeight})");

        var callada = new SplitTransport([]);
        var caps320 = await RevADisplayProbe.IdentifySubModelAsync(callada);
        Check(caps320.NativeWidth == 320 && caps320.NativeHeight == 480,
              "y el silencio sigue queriendo decir la 3.5\" de 320x480");
    }
}
catch (Exception e)
{
    Broke("Identificar el panel con la respuesta partida", e);
}

Header("Un panel que deja de contestar no cuelga el bucle");
try
{

    {
        // Lo que pasó de verdad: el panel dejó de aceptar bytes, la escritura solapada se quedó
        // pendiente para siempre y el servicio se quedó esperándola. No falló —que se recupera solo—,
        // es que no volvió, y un await que no vuelve no lo recoge ningún catch.
        var plazo = TimeSpan.FromMilliseconds(200);
        var colgado = new StuckTransport();
        var conPlazo = new TimeoutDisplayTransport(colgado, plazo);

        var reloj = System.Diagnostics.Stopwatch.StartNew();
        Exception? salio = null;
        try { await conPlazo.WriteAsync(new byte[] { 1, 2, 3 }); }
        catch (Exception e) { salio = e; }
        reloj.Stop();

        Check(salio is TimeoutException, $"una escritura que no vuelve acaba en TimeoutException ({salio?.GetType().Name ?? "nada"})");
        Check(reloj.Elapsed < plazo * 10, $"y en cuanto vence el plazo, no cuando le parezca al panel ({reloj.ElapsedMilliseconds} ms)");

        // TimeoutException es justo lo que PanelHost.IsPanelFailure da por panel caído, así que el
        // bucle de RunAsync cierra el puerto y reconecta. Ahí está todo el arreglo.
        Check(salio!.Message.Contains("no ha respondido", StringComparison.Ordinal), "y dice que el panel no responde");

        // El plazo no puede cobrarse a quien sí contesta.
        var normal = new TimeoutDisplayTransport(new SplitTransport([[0x02]]), plazo);
        await normal.WriteAsync(new byte[] { 1 });
        var leido = new byte[1];
        Check(await normal.ReadAsync(leido) == 1, "un panel que contesta pasa de largo sin enterarse del plazo");

        // Apagar el servicio se cancela, y eso no es un panel que no responde: son dos cosas distintas
        // y el estado que sale en pantalla depende de saberlas separar.
        using var apagando = new CancellationTokenSource();
        await apagando.CancelAsync();
        Exception? alApagar = null;
        try { await conPlazo.WriteAsync(new byte[] { 1 }, apagando.Token); }
        catch (Exception e) { alApagar = e; }

        Check(alApagar is OperationCanceledException,
              $"y cancelar es cancelar, no un plazo vencido ({alApagar?.GetType().Name ?? "nada"})");

        // Lo contrario también pasó de verdad: al suspender el equipo con una escritura a medias, el
        // driver serie la aborta y SerialStream lo cuenta como cancelación sin que nadie haya cancelado.
        // El bucle del panel lo tomaba por apagar el servicio, se salía y dejaba la pantalla congelada.
        var abortado = new TimeoutDisplayTransport(new AbortedTransport(), plazo);
        Exception? alSuspender = null;
        try { await abortado.WriteAsync(new byte[] { 1 }); }
        catch (Exception e) { alSuspender = e; }

        Check(alSuspender is IOException,
              $"una escritura que aborta el sistema es un panel perdido, no apagar el servicio ({alSuspender?.GetType().Name ?? "nada"})");
        Check(alSuspender?.InnerException is OperationCanceledException, "y lleva dentro la cancelación que la causó");
    }
}
catch (Exception e)
{
    Broke("Un panel que deja de contestar no cuelga el bucle", e);
}

Header("Actualizaciones");
try
{

    {
        Check(AppVersion.TryParse("1.0.2", out var plana) && plana == new AppVersion(1, 0, 2), "lee 1.0.2");
        Check(AppVersion.TryParse("v1.0.10", out var tag) && tag == new AppVersion(1, 0, 10), "y el tag v1.0.10");
        Check(AppVersion.TryParse("1.0.2+63077ec", out var hash) && hash == new AppVersion(1, 0, 2),
              "y le quita el hash de git que pone el SDK");

        // Lo que se compila fuera de main no se compara con nada: no busca actualizaciones.
        foreach (string dev in new[] { "1.0.0-dev", "1.0.3-dev.42+abc", "1.0", "1.0.2.4", "1.-0.2", " ", "" })
            Check(!AppVersion.TryParse(dev, out _), $"«{dev}» no es una versión publicada");

        Check(new AppVersion(1, 0, 10) > new AppVersion(1, 0, 9), "1.0.10 va después de 1.0.9, no antes como texto");
        Check(new AppVersion(1, 1, 0) > new AppVersion(1, 0, 99), "y 1.1.0 después de 1.0.99");
        Check(new AppVersion(2, 0, 0).ToString() == "2.0.0", "y se escribe sin la v");

        const string Hash = "8f434346648f6b96df89dda901c5176b10a6d83961dd3c1ac88b59b2dc327aa4";

        string Release(string tag, string asset, string? digest) => $$"""
            {
              "url": "https://api.github.com/repos/sergiosahuquillo/vibescreen/releases/1",
              "html_url": "https://github.com/sergiosahuquillo/vibescreen/releases/tag/{{tag}}",
              "tag_name": "{{tag}}", "draft": false, "prerelease": false,
              "author": { "login": "sergiosahuquillo" },
              "assets": [
                { "name": "otra-cosa.zip", "size": 1, "browser_download_url": "https://example.com/otra-cosa.zip",
                  "digest": "sha256:{{Hash}}" },
                { "name": "{{asset}}", "size": 41234567, "content_type": "application/x-msdownload",
                  "browser_download_url": "https://github.com/sergiosahuquillo/vibescreen/releases/download/{{tag}}/{{asset}}",
                  "digest": {{(digest is null ? "null" : $"\"{digest}\"")}} }
              ]
            }
            """;

        Check(ReleaseFeed.TryParse(Release("v1.0.3", "VibeScreen-1.0.3-setup.exe", "sha256:" + Hash.ToUpperInvariant()),
                                   out var buena, out var sinProblema) && sinProblema == ReleaseProblem.None,
              $"lee una release de verdad, campos que no usa incluidos ({sinProblema})");
        Check(buena?.Version == new AppVersion(1, 0, 3), "con su versión");
        Check(buena?.FileName == "VibeScreen-1.0.3-setup.exe" && buena.Size == 41234567, "su instalador y lo que mide");
        Check(buena?.Download.AbsoluteUri.EndsWith("/v1.0.3/VibeScreen-1.0.3-setup.exe", StringComparison.Ordinal) == true,
              "y de dónde se baja ese, no el otro fichero");
        Check(buena?.Sha256 == Hash, "el hash, en minúsculas");
        Check(buena?.Page?.AbsoluteUri.EndsWith("/tag/v1.0.3", StringComparison.Ordinal) == true, "y la página de la release");

        Check(!ReleaseFeed.TryParse(Release("v1.0.3", "VibeScreen-1.0.3-setup.exe", null), out _, out var sinHash) &&
              sinHash == ReleaseProblem.NoDigest, "sin hash no se ofrece");
        Check(!ReleaseFeed.TryParse(Release("v1.0.3", "VibeScreen-1.0.3-setup.exe", "sha1:abc"), out _, out var otroHash) &&
              otroHash == ReleaseProblem.NoDigest, "ni con un hash que no es SHA-256");
        Check(!ReleaseFeed.TryParse(Release("v1.0.3", "VibeScreen-1.0.2-setup.exe", "sha256:" + Hash), out _, out var otroNumero) &&
              otroNumero == ReleaseProblem.NoInstaller, "ni con el instalador de otra versión");
        Check(!ReleaseFeed.TryParse(Release("nightly", "VibeScreen-1.0.3-setup.exe", "sha256:" + Hash), out _, out var sinTag) &&
              sinTag == ReleaseProblem.Malformed, "ni con un tag que no es una versión");
        Check(!ReleaseFeed.TryParse("""{ "message": "Not Found" }""", out _, out var noEs) && noEs == ReleaseProblem.Malformed,
              "y lo que no es una release, tampoco");
        Check(!ReleaseFeed.TryParse("<html>", out _, out var roto) && roto == ReleaseProblem.Malformed,
              "ni lo que ni siquiera es JSON");
    }
}
catch (Exception e)
{
    Broke("Actualizaciones", e);
}

Console.WriteLine(new string('=', 48));
Console.WriteLine(failures == 0
    ? $"CORRECTO: {checks} comprobaciones"
    : $"FALLOS: {failures} de {checks} comprobaciones");
Console.WriteLine(new string('=', 48));
return failures == 0 ? 0 : 1;

static bool ThrowsArgument(Action action)
{
    try { action(); return false; }
    catch (ArgumentException) { return true; }
}

static bool Throws<T>(Action action) where T : Exception
{
    try { action(); return false; }
    catch (T) { return true; }
}

static string JsonEscape(string value) => System.Text.Json.JsonSerializer.Serialize(value);


/// <summary>Un panel que acepta el puerto y luego no contesta nunca. Ni falla: se queda callado.</summary>
/// <remarks>
/// Es el panel del atasco de verdad, no uno desenchufado: desenchufado lanza y el bucle se recupera
/// solo. Este devuelve una espera que no termina, que es lo que no se recuperaba.
/// </remarks>
sealed class StuckTransport : IDisplayTransport
{
    public bool IsOpen => true;
    public string Name => "atascado";

    public ValueTask OpenAsync(CancellationToken cancellationToken = default) => Never();

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) => Never();

    public ValueTask FlushAsync(CancellationToken cancellationToken = default) => Never();

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        new(new TaskCompletionSource<int>().Task);

    public ValueTask CloseAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Sin el token a propósito: una escritura solapada ya en vuelo tampoco se entera de que la
    // cancelan, y el plazo tiene que funcionar igual.
    private static ValueTask Never() => new(new TaskCompletionSource().Task);
}

/// <summary>Un puerto al que el sistema le aborta la E/S, como al suspender el equipo con una escritura a medias.</summary>
sealed class AbortedTransport : IDisplayTransport
{
    public bool IsOpen => true;
    public string Name => "abortado";

    public ValueTask OpenAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) => Aborted();

    public ValueTask FlushAsync(CancellationToken cancellationToken = default) => Aborted();

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Aborted().ConfigureAwait(false);
        return 0;
    }

    public ValueTask CloseAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Así llega de SerialStream: termina más tarde, y cancelada con un token que no es de nadie.
    private static async ValueTask Aborted()
    {
        await Task.Yield();
        throw new OperationCanceledException();
    }
}

/// <summary>Un transporte que contesta por trozos, como hace de verdad un puerto serie.</summary>
sealed class SplitTransport(byte[][] answers) : IDisplayTransport
{
    private int _next;

    public bool IsOpen { get; private set; } = true;
    public string Name => "partido";

    public ValueTask OpenAsync(CancellationToken cancellationToken = default)
    {
        IsOpen = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask FlushAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_next >= answers.Length) return ValueTask.FromResult(0);

        var piece = answers[_next++];
        int copied = Math.Min(piece.Length, buffer.Length);
        piece.AsSpan(0, copied).CopyTo(buffer.Span);
        return ValueTask.FromResult(copied);
    }

    public ValueTask CloseAsync()
    {
        IsOpen = false;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => CloseAsync();
}

/// <summary>Sensores de mentira: devuelven lo que se les pone y apuntan qué se les pide refrescar.</summary>
/// <summary>Un reloj que se queda donde se le ponga, para comprobar la fecha y la hora sin esperar.</summary>
sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

sealed class FakeSensors : ISensorProvider
{
    private readonly Dictionary<string, (float? Value, SensorKind Kind)> _values = new(StringComparer.Ordinal);

    public List<string> Updated { get; } = [];

    public IReadOnlyList<Notice> Warnings => [];

    public void Set(string id, float? value, SensorKind kind = SensorKind.Temperature) => _values[id] = (value, kind);

    public IReadOnlyList<SensorReading> ReadAll() => _values.Select(kv => Reading(kv.Key, kv.Value)).ToList();

    public SensorReading? Read(string id) => _values.TryGetValue(id, out var value) ? Reading(id, value) : null;

    public void Update(IEnumerable<string> ids) => Updated.AddRange(ids);

    public void UpdateAll()
    {
    }

    public void Dispose()
    {
    }

    private static SensorReading Reading(string id, (float? Value, SensorKind Kind) sensor) => new()
    {
        Id = id,
        Value = sensor.Value,
        Native = new NativeSensor
        {
            HardwareClass = HardwareClass.Other, HardwareId = "/prueba", HardwareName = "Prueba",
            Kind = sensor.Kind, Name = id, NativeId = id,
        },
    };
}

/// <summary>Panel de mentira que apunta cada rectángulo que recibe.</summary>
sealed class RecordingDriver(DisplayCapabilities capabilities, Orientation orientation) : IDisplayDriver
{
    public List<(PixelRect Rect, byte[] Pixels)> Blits { get; } = [];

    public DisplayCapabilities Capabilities { get; } = capabilities;

    public Orientation Orientation { get; private set; } = orientation;

    public int Width => Orientation.IsRotated() ? Capabilities.NativeHeight : Capabilities.NativeWidth;

    public int Height => Orientation.IsRotated() ? Capabilities.NativeWidth : Capabilities.NativeHeight;

    public ValueTask InitializeAsync(Orientation value, bool screenOn = true, CancellationToken cancellationToken = default)
    {
        Orientation = value;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetOrientationAsync(Orientation value, CancellationToken cancellationToken = default)
    {
        Orientation = value;
        return ValueTask.CompletedTask;
    }

    public ValueTask BlitAsync(ReadOnlyMemory<byte> pixels, PixelRect rect, CancellationToken cancellationToken = default)
    {
        Blits.Add((rect, pixels.ToArray()));
        return ValueTask.CompletedTask;
    }

    public ValueTask SetBrightnessAsync(int percent, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask ClearAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask SetScreenPowerAsync(bool screenOn, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
