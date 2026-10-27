using System.Diagnostics;
using SkiaSharp;
using VibeScreen.Contracts;
using VibeScreen.Core;
using VibeScreen.Core.Drivers;
using VibeScreen.Core.Layout;
using VibeScreen.Core.Rendering;
using VibeScreen.Core.Sensors;
using VibeScreen.Core.Transport;
using VibeScreen.Protocol.Display;
using VibeScreen.Protocol.Rendering;

// Herramienta de validación del driver contra el panel real.
// No es parte del producto: existe para comprobar que la implementación .NET se comporta
// igual que la de referencia antes de construir nada encima.

string command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
string? port = GetOption("--port");
if (!TryGetOrientation(out var orientation))
{
    Console.Error.WriteLine($"Orientación no válida: «{GetOption("--orientation")}». " +
                            "Usa portrait, landscape, reverseportrait o reverselandscape.");
    return 1;
}
if (!TryGetRevision(out var revision))
{
    Console.Error.WriteLine($"Revisión no válida: «{GetOption("--revision")}». " +
                            $"Las que hay: {string.Join(", ", DisplayDiscovery.Supported.Select(f => f.Revision))}.");
    return 1;
}

try
{
    return command switch
    {
        "ports" => ListPorts(),
        "detect" => Detect(),
        "info" => await InfoAsync(),
        "clear" => await ClearAsync(),
        "brightness" => await BrightnessAsync(),
        "on" => await ScreenPowerAsync(on: true),
        "off" => await ScreenPowerAsync(on: false),
        "pattern" => await PatternAsync(),
        "bench" => await BenchAsync(),
        "sensors" => ListSensors(),
        "run" => await RunLayoutAsync(),
        "preview" => Preview(),
        "service" => await ServiceCommandAsync(),
        _ => Help(),
    };
}
catch (LayoutException e)
{
    Console.Error.WriteLine($"Error: {e.Message}");
    foreach (var problem in e.Problems) Console.Error.WriteLine($"  - {problem}");
    return 1;
}
// TimeoutException es el caso corriente y faltaba: el panel desenchufado, o con el cable puesto y
// otro programa todavía con el puerto abierto. Sin él, lo que veía el usuario era una traza de pila.
catch (Exception e) when (e is IOException or InvalidOperationException or PlatformNotSupportedException
                              or TimeoutException or UnauthorizedAccessException or ArgumentException)
{
    Console.Error.WriteLine($"Error: {e.Message}");
    return 1;
}

string? GetOption(string name)
{
    int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

bool HasOption(string name) => Array.Exists(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

// Sin --orientation, vertical. Con ella, solo uno de los cuatro nombres, con la regla de StrictEnum.
bool TryGetOrientation(out Orientation value)
{
    value = Orientation.Portrait;
    return !HasOption("--orientation") || StrictEnum.TryParse(GetOption("--orientation"), out value);
}

// Sin --revision, la que se detecte. Con ella, solo cuenta esa familia, como con «revision» en la
// configuración del servicio.
bool TryGetRevision(out HardwareRevision? value)
{
    value = null;
    if (!HasOption("--revision")) return true;
    if (!StrictEnum.TryParse(GetOption("--revision"), out HardwareRevision parsed)) return false;
    value = parsed;
    return true;
}

int Help()
{
    Console.WriteLine("""
        vibescreen - validación del driver

          ports                      Lista los puertos serie con su VID/PID
          detect                     Busca paneles compatibles
          info                       Abre el panel y pregunta su submodelo
          clear                      Limpia la pantalla
          brightness <0-100>         Ajusta el brillo
          on | off                   Enciende o apaga la pantalla
          pattern                    Pinta una carta de ajuste con Skia
          bench                      Mide el caudal real del panel
          sensors [filtro]           Lista los sensores con su identificador canónico
          run <layout.json>          Pinta un layout con sensores reales
          preview <layout.json> [png] Pinta el layout en un PNG, sin panel

        A través del servicio, que es quien tiene el panel abierto:

          service status             Qué está haciendo el servicio
          service layout <nombre>    Cambia el layout y lo recuerda
          service reload             Relee del disco el layout vigente
          service random on | off    Elige un layout al azar en cada arranque
          service brightness <0-100> Ajusta el brillo
          service on | off           Enciende o apaga la pantalla
          service sensors [filtro]   Sensores que ve el servicio

        Opciones:
          --port COM3 | /dev/ttyACM0   Fuerza el puerto
          --revision reva              Fuerza la revisión del panel
          --orientation portrait|landscape|reverseportrait|reverselandscape
          --seconds N                  Con run, para al cabo de N segundos

        Cierra antes cualquier programa que tenga el panel abierto. La temperatura de la CPU
        necesita permisos de administrador y PawnIO instalado.
        """);
    return 0;
}

int ListPorts()
{
    var ports = SerialPortEnumerator.Create().List();
    if (ports.Count == 0)
    {
        Console.WriteLine("No hay puertos serie por USB.");
        return 0;
    }
    foreach (var p in ports) Console.WriteLine("  " + p);
    return 0;
}

int Detect()
{
    var found = new DisplayDiscovery().FindAll();
    if (found.Count == 0)
    {
        Console.WriteLine("Ningún panel compatible. Puertos vistos:");
        return ListPorts();
    }
    foreach (var d in found)
    {
        Console.WriteLine($"  {d.Capabilities.Model} en {d.Port.PortName}");
        Console.WriteLine($"     identificado por: {d.MatchedBy}");
        Console.WriteLine($"     revisión {d.Family.Revision}, se supone de {d.Capabilities.NativeWidth}x" +
                          $"{d.Capabilities.NativeHeight} hasta preguntarle el submodelo (info)");
    }
    return 0;
}

async Task<int> InfoAsync()
{
    // Con --port y sin panel reconocido en él, se pregunta igual con la familia que toque: es lo que
    // haría el servicio con ese mismo puerto forzado.
    var display = new DisplayDiscovery().Find(port, revision);
    if (display is null)
    {
        Console.Error.WriteLine("No se ha encontrado ningún panel. Usa --port.");
        return 1;
    }

    string portName = display.Port.PortName;
    await using var transport = new SerialDisplayTransport(portName);
    var caps = await display.Family.IdentifyAsync(display, transport);

    Console.WriteLine($"  Puerto:      {portName}");
    Console.WriteLine($"  Reconocido:  por {display.MatchedBy}");
    Console.WriteLine($"  Modelo:      {caps.Model}");
    Console.WriteLine($"  Revisión:    {caps.Revision}");
    Console.WriteLine($"  Resolución:  {caps.NativeWidth}x{caps.NativeHeight}");
    Console.WriteLine($"  Píxeles:     {caps.PixelFormat}");
    Console.WriteLine($"  Fotograma:   {caps.FullFrameBytes:N0} B");
    Console.WriteLine($"  Caudal:      {caps.ThroughputBytesPerSecond:N0} B/s (medido en Fase 0)");
    Console.WriteLine($"  Refresco total: {(double)caps.FullFrameBytes / caps.ThroughputBytesPerSecond:F2} s");
    return 0;
}

async Task<int> ClearAsync()
{
    await using var driver = await new DisplayDiscovery().OpenAsync(port, orientation, revision: revision);
    await driver.ClearAsync();
    Console.WriteLine("Pantalla limpia.");
    return 0;
}

async Task<int> BrightnessAsync()
{
    if (args.Length < 2 || !int.TryParse(args[1], out int percent) || percent is < 0 or > 100)
    {
        Console.Error.WriteLine("Uso: vibescreen brightness <0-100>");
        return 1;
    }
    await using var driver = await new DisplayDiscovery().OpenAsync(port, orientation, revision: revision);
    await driver.SetBrightnessAsync(percent);
    Console.WriteLine($"Brillo al {percent}%.");
    return 0;
}

async Task<int> ScreenPowerAsync(bool on)
{
    // Inicializar ya deja la pantalla en el estado pedido, así que apagar no pasa por encenderla.
    await using var driver = await new DisplayDiscovery().OpenAsync(port, orientation, screenOn: on, revision: revision);
    Console.WriteLine(on ? "Pantalla encendida." : "Pantalla apagada.");
    return 0;
}

async Task<int> PatternAsync()
{
    await using var driver = await new DisplayDiscovery().OpenAsync(port, orientation, revision: revision);
    using var surface = new PanelSurface(driver.Width, driver.Height);

    surface.Clear(new SKColor(12, 14, 20));
    using var paint = new SKPaint { IsAntialias = true };

    // Franjas de color: comprueban que el orden de canales y el empaquetado RGB565 son correctos.
    SKColor[] bars = [SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.Yellow,
                      SKColors.Magenta, SKColors.Cyan, SKColors.White];
    float barWidth = driver.Width / (float)bars.Length;
    for (int i = 0; i < bars.Length; i++)
    {
        paint.Color = bars[i];
        surface.Canvas.DrawRect(i * barWidth, 0, barWidth, driver.Height * 0.25f, paint);
    }

    // Marco de un píxel: comprueba que el rectángulo se mapea exactamente y no hay desfase.
    paint.Color = SKColors.White;
    paint.Style = SKPaintStyle.Stroke;
    paint.StrokeWidth = 1;
    surface.Canvas.DrawRect(0.5f, 0.5f, driver.Width - 1, driver.Height - 1, paint);

    paint.Style = SKPaintStyle.Fill;
    using var font = new SKFont(SKTypeface.Default, 18);
    surface.Canvas.DrawText("VibeScreen", 10, driver.Height * 0.45f, SKTextAlign.Left, font, paint);
    using var small = new SKFont(SKTypeface.Default, 12);
    surface.Canvas.DrawText($"{driver.Width}x{driver.Height}  {driver.Orientation}",
                            10, driver.Height * 0.45f + 20, SKTextAlign.Left, small, paint);

    var sw = Stopwatch.StartNew();
    var full = new PixelRect(0, 0, driver.Width, driver.Height);
    await driver.BlitAsync(surface.Extract(full, driver.Capabilities.PixelFormat), full);
    sw.Stop();

    int bytes = driver.Capabilities.ByteCount(full);
    Console.WriteLine($"Carta de ajuste enviada: {bytes:N0} B en {sw.Elapsed.TotalSeconds:F3} s " +
                      $"({bytes / sw.Elapsed.TotalSeconds / 1024:F1} KiB/s)");
    return 0;
}

async Task<int> BenchAsync()
{
    await using var driver = await new DisplayDiscovery().OpenAsync(port, orientation, revision: revision);
    Console.WriteLine($"Panel {driver.Width}x{driver.Height} en {driver.Orientation}");
    Console.WriteLine();

    (int W, int H)[] sizes = [(driver.Width, driver.Height), (driver.Width, driver.Height / 2),
                              (160, 120), (120, 40), (100, 24), (32, 16)];

    Console.WriteLine("  tamaño        bytes     mejor      caudal");
    var samples = new List<(int Bytes, double Seconds)>();

    foreach (var (w, h) in sizes)
    {
        var rect = new PixelRect(0, 0, w, h);
        var pixels = new byte[driver.Capabilities.ByteCount(rect)];
        Random.Shared.NextBytes(pixels);

        double best = double.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            var sw = Stopwatch.StartNew();
            await driver.BlitAsync(pixels, rect);
            sw.Stop();
            best = Math.Min(best, sw.Elapsed.TotalSeconds);
        }

        samples.Add((pixels.Length, best));
        Console.WriteLine($"  {w,4}x{h,-4}  {pixels.Length,8:N0}  {best,8:F4}s  {pixels.Length / best / 1024,8:F1} KiB/s");
    }

    // Misma regresión que el banco de pruebas en Python: separa coste fijo de coste por byte.
    int n = samples.Count;
    double sx = samples.Sum(s => (double)s.Bytes), sy = samples.Sum(s => s.Seconds);
    double sxx = samples.Sum(s => (double)s.Bytes * s.Bytes), sxy = samples.Sum(s => s.Bytes * s.Seconds);
    double slope = (n * sxy - sx * sy) / (n * sxx - sx * sx);
    double intercept = (sy - slope * sx) / n;

    Console.WriteLine();
    Console.WriteLine($"  Coste fijo por comando: {intercept * 1000:F2} ms");
    Console.WriteLine($"  Caudal asintótico:      {1 / slope:N0} B/s");
    Console.WriteLine($"  Referencia Fase 0:      {RevADisplayFamily.MeasuredThroughputBytesPerSecond:N0} B/s (Python)");

    await driver.ClearAsync();
    return 0;
}

int ListSensors()
{
    string? filter = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : null;

    using var sensors = SensorProviders.CreateDefault();
    // Las cargas se calculan entre dos muestras: con una sola lectura todavía no hay dato.
    Thread.Sleep(1000);
    sensors.UpdateAll();

    foreach (var warning in sensors.Warnings)
        Console.Error.WriteLine($"Aviso: {warning}");

    var readings = sensors.ReadAll()
        .Where(r => filter is null
                    || r.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || r.Native.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
        .ToList();

    foreach (var hardware in readings.GroupBy(r => r.Native.HardwareName))
    {
        Console.WriteLine();
        Console.WriteLine(hardware.Key);
        foreach (var r in hardware)
        {
            string value = r.Value is { } v ? $"{v:0.###} {r.Unit}" : TextComposer.MissingValue;
            Console.WriteLine($"  {r.Id,-44} {value,-14} {r.Native.Name}");
        }
    }

    Console.WriteLine();
    Console.WriteLine($"{readings.Count} sensores.");
    return 0;
}

int Preview()
{
    if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("Uso: vibescreen preview <layout.json> [salida.png]");
        return 1;
    }

    LayoutDocument layout;
    try
    {
        layout = LayoutLoader.Load(args[1]);
    }
    catch (LayoutException e)
    {
        Console.Error.WriteLine($"El layout {args[1]} no es válido:");
        foreach (var problem in e.Problems) Console.Error.WriteLine($"  - {problem}");
        return 1;
    }

    string output = args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal)
        ? args[2]
        : Path.ChangeExtension(args[1], ".png");

    using var sensors = SensorProviders.CreateDefault();
    foreach (var warning in LayoutInspector.Warnings(layout, sensors))
        Console.Error.WriteLine($"Aviso: {warning}");

    // El mismo renderizador que usa el servicio, así que lo que salga aquí es lo que vería el panel.
    using var renderer = new LayoutRenderer(layout);
    renderer.RenderAll(sensors, TimeSpan.Zero);

    using (var image = SKImage.FromBitmap(renderer.Surface.Bitmap))
    using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
    using (var file = File.Create(output))
    {
        data.SaveTo(file);
    }

    Console.WriteLine($"Escrito {output} ({layout.Display.Width}x{layout.Display.Height}).");
    return 0;
}

async Task<int> RunLayoutAsync()
{
    if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("Uso: vibescreen run <layout.json> [--seconds N]");
        return 1;
    }
    if (HasOption("--orientation"))
    {
        Console.Error.WriteLine("Con run, la orientación la fija el layout: quita --orientation.");
        return 1;
    }

    int seconds = 0;
    if (HasOption("--seconds") && (!int.TryParse(GetOption("--seconds"), out seconds) || seconds <= 0))
    {
        Console.Error.WriteLine("--seconds lleva un número entero de segundos mayor que 0.");
        return 1;
    }

    LayoutDocument layout;
    try
    {
        layout = LayoutLoader.Load(args[1]);
    }
    catch (LayoutException e)
    {
        Console.Error.WriteLine($"El layout {args[1]} no es válido:");
        foreach (var problem in e.Problems) Console.Error.WriteLine($"  - {problem}");
        return 1;
    }

    using var sensors = SensorProviders.CreateDefault();
    foreach (var warning in sensors.Warnings)
        Console.Error.WriteLine($"Aviso: {warning}");
    foreach (var warning in LayoutInspector.Warnings(layout, sensors))
        Console.Error.WriteLine($"Aviso: {warning}");

    await using var driver = await new DisplayDiscovery().OpenAsync(port, layout.Display.Orientation, revision: revision);
    if (driver.Width != layout.Display.Width || driver.Height != layout.Display.Height)
    {
        Console.Error.WriteLine($"El layout es para {layout.Display.Width}x{layout.Display.Height} y el panel mide " +
                                $"{driver.Width}x{driver.Height} en {driver.Orientation}.");
        return 1;
    }

    // Lo que el tema le pide al panel en el peor caso, antes de empezar a pintarlo.
    var bandwidth = BandwidthBudget.Analyze(LayoutInspector.Consumers(layout), driver.Capabilities);
    Console.WriteLine($"El layout pide como mucho {bandwidth.RequiredBytesPerSecond:N0} B/s de los " +
                      $"{bandwidth.BudgetBytesPerSecond:N0} B/s del presupuesto ({bandwidth.Utilization:P0}), " +
                      $"de {bandwidth.PanelBytesPerSecond:N0} B/s que traga el panel.");
    if (bandwidth.Oversubscribed)
        Console.Error.WriteLine($"Aviso: no cabe. El que más gasta es «{bandwidth.Consumers[0].WidgetId}» " +
                                $"con {bandwidth.Consumers[0].BytesPerSecond:N0} B/s.");

    using var runner = new LayoutRunner(driver, layout, sensors);
    using var stop = new CancellationTokenSource();
    if (seconds > 0) stop.CancelAfter(TimeSpan.FromSeconds(seconds));

    ConsoleCancelEventHandler onCancel = (_, e) =>
    {
        e.Cancel = true;
        stop.Cancel();
    };
    Console.CancelKeyPress += onCancel;

    string name = layout.Meta?.Name ?? Path.GetFileName(args[1]);
    Console.WriteLine(seconds > 0 ? $"Pintando «{name}» durante {seconds} s." : $"Pintando «{name}». Ctrl+C para parar.");

    var clock = Stopwatch.StartNew();
    try
    {
        await runner.RunAsync(stop.Token);
    }
    catch (OperationCanceledException) when (stop.IsCancellationRequested)
    {
    }
    finally
    {
        Console.CancelKeyPress -= onCancel;
    }

    Console.WriteLine($"Enviados {runner.BlitCount} rectángulos y {runner.BytesSent:N0} B en {clock.Elapsed.TotalSeconds:F1} s.");
    return 0;
}

async Task<int> ServiceCommandAsync()
{
    string sub = args.Length > 1 ? args[1].ToLowerInvariant() : "status";
    ServiceRequest request;

    switch (sub)
    {
        case "status":
            request = new StatusRequest();
            break;
        case "sensors":
            request = new SensorsRequest
            {
                Filter = args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal) ? args[2] : null,
            };
            break;
        case "layout":
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Falta el nombre del layout.");
                return 1;
            }

            string? chosen = LayoutTarget(args[2]);
            if (chosen is null) return 1;

            request = new SetLayoutRequest { Path = chosen };
            break;
        case "reload":
            request = new ReloadLayoutRequest();
            break;
        case "random":
            if (args.Length < 3 || args[2].ToLowerInvariant() is not ("on" or "off"))
            {
                Console.Error.WriteLine("Hace falta decir «on» u «off».");
                return 1;
            }
            request = new SetRandomLayoutRequest { On = args[2].Equals("on", StringComparison.OrdinalIgnoreCase) };
            break;
        case "brightness":
            if (args.Length < 3 || !int.TryParse(args[2], out int percent) || percent is < 0 or > 100)
            {
                Console.Error.WriteLine("El brillo va de 0 a 100.");
                return 1;
            }
            request = new SetBrightnessRequest { Percent = percent };
            break;
        case "on":
            request = new SetScreenPowerRequest { On = true };
            break;
        case "off":
            request = new SetScreenPowerRequest { On = false };
            break;
        default:
            Console.Error.WriteLine($"Orden de servicio desconocida: {sub}.");
            return 1;
    }

    ServiceResponse response;
    try
    {
        await using var client = await ServiceClient.ConnectAsync();
        response = await client.SendAsync(request);
    }
    catch (ServiceUnavailableException e)
    {
        Console.Error.WriteLine($"Error: {e.Message}");
        return 1;
    }

    if (!response.Ok)
    {
        Console.Error.WriteLine($"Error: {response.Error}");
        foreach (var problem in response.Problems ?? [])
            Console.Error.WriteLine("  - " + problem);
        return 1;
    }

    if (response.Status is { } status) PrintServiceStatus(status);
    else if (response.Sensors is { } sensors) PrintServiceSensors(sensors);
    else Console.WriteLine("Hecho.");
    return 0;
}

/// <summary>
/// La ruta del layout que nombra el argumento, o null si no hay tal layout —y entonces ya se ha
/// dicho por qué—.
/// </summary>
/// <remarks>
/// Se admite el nombre a secas, que es lo que el usuario conoce: los layouts viven en una carpeta
/// que decide el programa y nadie se sabe la ruta de memoria. Una ruta entera sigue valiendo, para
/// los guiones de siempre, pero el servicio solo acepta las de esa carpeta.
/// </remarks>
string? LayoutTarget(string argument)
{
    if (argument.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return argument;

    if (LayoutPaths.NameProblem(argument) is { } bad)
    {
        Console.Error.WriteLine($"Error: {bad}");
        return null;
    }

    if (LayoutPaths.Exists(argument)) return LayoutPaths.For(argument);

    Console.Error.WriteLine($"Error: no hay ningún layout que se llame «{argument}».");
    var saved = LayoutPaths.All();
    if (saved.Count == 0)
    {
        Console.Error.WriteLine("No hay ninguno guardado. Haz uno en el editor.");
        return null;
    }

    Console.Error.WriteLine("Los que hay:");
    foreach (string name in saved) Console.Error.WriteLine("  " + name);
    return null;
}

void PrintServiceStatus(ServiceStatus s)
{
    Console.WriteLine($"Estado     {s.State}{(s.Detail is null ? "" : "  " + s.Detail)}");
    if (s.PortName is not null)
        Console.WriteLine($"Panel      {s.Model} en {s.PortName}, {s.Width}x{s.Height} {s.Orientation}");
    Console.WriteLine($"Layout     {s.LayoutName ?? "(sin nombre)"}  {s.LayoutPath ?? "(ninguno)"}" +
                      (s.RandomLayout ? "  [al azar en cada arranque]" : ""));
    Console.WriteLine($"Pantalla   {(s.ScreenOn ? "encendida" : "apagada")}, brillo {s.Brightness} %");
    Console.WriteLine($"Enlace     {s.Blits} rectángulos, {s.BytesSent:N0} B, {s.BytesPerSecond:N0} B/s ({s.LinkUtilization:P1} del presupuesto)");
    Console.WriteLine($"Sensores   {s.SensorCount}");
    Console.WriteLine($"En marcha  {s.UptimeSeconds} s, {s.Reconnects} reconexiones");
    foreach (var warning in s.Warnings) Console.WriteLine($"Aviso      {warning}");
}

void PrintServiceSensors(IReadOnlyList<SensorInfo> sensors)
{
    foreach (var s in sensors)
    {
        string value = s.Value is { } v ? $"{v:0.###} {s.Unit}" : "--";
        Console.WriteLine($"  {s.Id,-44} {value,-14} {s.Name}");
    }
    Console.WriteLine($"{sensors.Count} sensores.");
}
