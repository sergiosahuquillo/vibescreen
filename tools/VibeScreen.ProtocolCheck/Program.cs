using System.Text.Json;
using VibeScreen.Protocol.Display;
using VibeScreen.Protocol.Rendering;

// Verificador del protocolo. Contrasta el codificador de VibeScreen.Protocol contra vectores
// generados con la lógica exacta de turing-smart-screen-python, y comprueba el comportamiento
// del planificador. Sin dependencias externas para poder ejecutarse en cualquier sitio.

int checks = 0, failures = 0;

void Check(bool condition, string what)
{
    checks++;
    if (condition) return;
    failures++;
    Console.WriteLine($"  FALLO  {what}");
}

void CheckBytes(ReadOnlySpan<byte> actual, JsonElement expected, string what)
{
    checks++;
    int n = expected.GetArrayLength();
    if (actual.Length != n)
    {
        failures++;
        Console.WriteLine($"  FALLO  {what}: {actual.Length} bytes, se esperaban {n}");
        return;
    }
    for (int i = 0; i < n; i++)
    {
        byte want = expected[i].GetByte();
        if (actual[i] != want)
        {
            failures++;
            Console.WriteLine($"  FALLO  {what}: byte {i} = {actual[i]}, se esperaba {want}");
            return;
        }
    }
}

string vectorsPath = Path.Combine(AppContext.BaseDirectory, "vectors.json");
using var doc = JsonDocument.Parse(File.ReadAllText(vectorsPath));
var root = doc.RootElement;

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
const int Throughput = 261_600; // medido en la Fase 0

var caps = new DisplayCapabilities
{
    Model = "Turing 3.5\"", Revision = HardwareRevision.RevA,
    NativeWidth = 320, NativeHeight = 480, ThroughputBytesPerSecond = Throughput,
};

Header("Contraste con turing-smart-screen-python");
try
{

    foreach (var v in root.GetProperty("commands").EnumerateArray())
    {
        var cmd = Enum.Parse<RevACommand>(v.GetProperty("command").GetString()!);
        int x = v.GetProperty("x").GetInt32(), y = v.GetProperty("y").GetInt32();
        int ex = v.GetProperty("endX").GetInt32(), ey = v.GetProperty("endY").GetInt32();
        CheckBytes(RevAProtocol.Command(cmd, x, y, ex, ey), v.GetProperty("expected"),
                   $"{cmd}({x},{y},{ex},{ey})");
    }
    Console.WriteLine($"  empaquetado de comandos: {root.GetProperty("commands").GetArrayLength()} casos");

    foreach (var v in root.GetProperty("brightness").EnumerateArray())
    {
        int pct = v.GetProperty("percent").GetInt32();
        CheckBytes(RevAProtocol.SetBrightness(pct), v.GetProperty("expected"), $"Brillo({pct}%)");
    }
    Console.WriteLine($"  brillo: {root.GetProperty("brightness").GetArrayLength()} casos");

    foreach (var v in root.GetProperty("orientation").EnumerateArray())
    {
        var o = (Orientation)v.GetProperty("orientation").GetInt32();
        int w = v.GetProperty("nativeWidth").GetInt32(), h = v.GetProperty("nativeHeight").GetInt32();
        CheckBytes(RevAProtocol.SetOrientation(o, w, h), v.GetProperty("expected"), $"Orientación({o})");
    }
    Console.WriteLine($"  orientación: {root.GetProperty("orientation").GetArrayLength()} casos");

    CheckBytes(RevAProtocol.Hello(), root.GetProperty("hello").GetProperty("expected"), "Hello");

    int rgbCases = 0;
    foreach (var v in root.GetProperty("rgb565").EnumerateArray())
    {
        var pixels = v.GetProperty("pixels");
        int count = pixels.GetArrayLength();
        var bgra = new byte[count * 4];
        for (int i = 0; i < count; i++)
        {
            var px = pixels[i];
            bgra[i * 4 + 0] = px[2].GetByte(); // B
            bgra[i * 4 + 1] = px[1].GetByte(); // G
            bgra[i * 4 + 2] = px[0].GetByte(); // R
            bgra[i * 4 + 3] = 255;             // A
        }
        var outBuf = new byte[count * 2];
        Rgb565.FromBgra8888(bgra, count * 4, count, 1, outBuf);
        CheckBytes(outBuf, v.GetProperty("expected"), $"RGB565 x{count}");
        rgbCases++;
    }
    Console.WriteLine($"  RGB565: {rgbCases} casos");
}
catch (Exception e)
{
    Broke("Contraste con turing-smart-screen-python", e);
}

Header("Comportamiento propio");
try
{

    // --- brillo: ida y vuelta ---
    for (int pct = 0; pct <= 100; pct += 5)
    {
        byte raw = RevAProtocol.BrightnessRaw(pct);
        Check(Math.Abs(RevAProtocol.BrightnessPercentFromRaw(raw) - pct) <= 1, $"brillo ida y vuelta {pct}%");
    }
    // El valor crudo viaja en el campo X, que ocupa 10 bits a caballo entre los bytes 0 y 1.
    {
        byte[] frame = RevAProtocol.SetBrightness(20);
        int unpacked = (frame[0] << 2) | (frame[1] >> 6);
        Check(unpacked == RevAProtocol.BrightnessRaw(20), "brillo: el valor crudo se recupera del campo X");
        Check(RevAProtocol.BrightnessRaw(100) == 0 && RevAProtocol.BrightnessRaw(0) == 255,
              "brillo: la escala del panel va al revés que la interfaz");
    }

    // --- coordenadas fuera de rango ---
    Check(Throws(() => RevAProtocol.Command(RevACommand.Clear, 1024)), "coordenada 1024 rechazada");
    Check(Throws(() => RevAProtocol.Command(RevACommand.Clear, -1)), "coordenada negativa rechazada");
    Check(Throws(() => RevAProtocol.SetBrightness(101)), "brillo 101% rechazado");
    Check(Throws(() => RevAProtocol.SetOrientation((Orientation)4, 320, 480)), "orientación inexistente rechazada");

    // --- PixelRect ---
    var r = new PixelRect(10, 20, 100, 50);
    Check(r.Right == 109 && r.Bottom == 69, "PixelRect: extremos inclusivos");
    Check(r.Area == 5000, "PixelRect: área");
    Check(PixelRect.FromBounds(10, 20, 109, 69) == r, "PixelRect: FromBounds es la inversa");
    Check(r.Contains(new PixelRect(20, 30, 10, 10)), "PixelRect: contiene");
    Check(!r.Contains(new PixelRect(20, 30, 200, 10)), "PixelRect: no contiene lo que se sale");
    Check(r.IntersectsWith(new PixelRect(100, 60, 50, 50)), "PixelRect: solapa");
    Check(!r.IntersectsWith(new PixelRect(200, 200, 10, 10)), "PixelRect: no solapa");
    Check(new PixelRect(0, 0, 10, 10).Union(new PixelRect(90, 90, 10, 10)) == new PixelRect(0, 0, 100, 100),
          "PixelRect: unión");
    Check(new PixelRect(300, 470, 100, 100).ClampTo(320, 480) == new PixelRect(300, 470, 20, 10),
          "PixelRect: recorte al lienzo");
    Check(new PixelRect(400, 0, 10, 10).ClampTo(320, 480).IsEmpty, "PixelRect: fuera del lienzo queda vacío");

    // --- Rgb565 ---
    Check(Rgb565.Pack(255, 255, 255) == 0xFFFF, "RGB565: blanco");
    Check(Rgb565.Pack(0, 0, 0) == 0x0000, "RGB565: negro");
    Check(Rgb565.ByteCount(new PixelRect(0, 0, 320, 480)) == 307200, "RGB565: fotograma completo son 307.200 B");

    // --- DirtyRegionSet ---
    var kept = DirtyRegionSet.DropContained([
        new PixelRect(0, 0, 100, 100),
        new PixelRect(10, 10, 20, 20),      // contenido
        new PixelRect(200, 200, 50, 50),
    ]);
    Check(kept.Count == 2, "DirtyRegionSet: descarta el contenido");
    Check(kept.Any(k => k.Area == 10000) && kept.Any(k => k.Area == 2500), "DirtyRegionSet: conserva los otros dos");

    // --- ByteBudgetScheduler ---
    var sched = new ByteBudgetScheduler(Throughput, utilization: 0.25, maxBurstSeconds: 0.5);

    var textWidget = new PixelRect(0, 0, 100, 24); // 4.800 B
    sched.MarkDirty("cpu_temp", textWidget);
    sched.MarkDirty("gpu_temp", textWidget);
    var batch = sched.Drain(TimeSpan.FromSeconds(1)); // presupuesto: 65.400 B
    Check(batch.Count == 2, "planificador: los dos textos caben en un segundo");
    Check(batch.Sum(b => b.Bytes) == 9600, "planificador: cuenta los bytes");

    // La prioridad manda, y a igualdad manda quien lleva más esperando.
    sched.Clear();
    sched.MarkDirty("viejo", textWidget, priority: 0);
    sched.MarkDirty("urgente", textWidget, priority: 10);
    var ordered = sched.Drain(TimeSpan.FromSeconds(1));
    Check(ordered[0].WidgetId == "urgente", "planificador: la prioridad va primero");

    // Y manda también cuando el de más prioridad NO cabe todavía: el pequeño se espera en vez de
    // colarse. Saltar al siguiente dejaba que lo barato de prioridad 0 se comiera el presupuesto del
    // widget que el usuario marcó como importante.
    sched.Clear();
    var bigChart = new PixelRect(0, 0, 320, 100); // 64.000 B
    sched.MarkDirty("grafica", bigChart, priority: 10);
    sched.MarkDirty("texto", textWidget, priority: 0); // 4.800 B, cabría de sobra
    Check(sched.Drain(TimeSpan.FromSeconds(0.5)).Count == 0,
          "planificador: el pequeño no adelanta al grande de más prioridad");
    var afterWait = sched.Drain(TimeSpan.FromSeconds(0.5));
    Check(afterWait.Count == 1 && afterWait[0].WidgetId == "grafica",
          "planificador: en cuanto cabe, el de más prioridad sale primero");
    Check(sched.PendingCount == 1, "planificador: el pequeño sigue esperando su turno");

    // Marcar dos veces el mismo widget no lo envía dos veces: une las zonas.
    sched.Clear();
    sched.MarkDirty("w", new PixelRect(0, 0, 10, 10));
    sched.MarkDirty("w", new PixelRect(90, 90, 10, 10));
    Check(sched.PendingCount == 1, "planificador: un widget, una entrada");
    var merged = sched.Drain(TimeSpan.FromSeconds(1));
    Check(merged.Count == 1 && merged[0].Rect == new PixelRect(0, 0, 100, 100),
          "planificador: une las zonas del mismo widget");

    // Sin presupuesto no se envía nada.
    sched.Clear();
    sched.MarkDirty("w", textWidget);
    Check(sched.Drain(TimeSpan.Zero).Count == 0, "planificador: sin tiempo no hay presupuesto");
    Check(sched.PendingCount == 1, "planificador: lo no enviado sigue pendiente");

    // Un fotograma completo (307.200 B) supera con creces el tope de ráfaga (32.700 B).
    // Sin la excepción de inanición no se enviaría jamás.
    sched.Clear();
    var fullFrame = new PixelRect(0, 0, 320, 480);
    sched.MarkDirty("fondo", fullFrame);
    int ticks = 0;
    IReadOnlyList<BlitRequest> result;
    do { result = sched.Drain(TimeSpan.FromSeconds(1)); ticks++; }
    while (result.Count == 0 && ticks < 20);
    Check(result.Count == 1, $"planificador: el fotograma completo acaba saliendo (ciclos: {ticks})");
    Check(ticks is >= 4 and <= 6, $"planificador: tarda lo que dicta el presupuesto, no más (ciclos: {ticks})");

    // --- BandwidthBudget ---
    // El presupuesto, no el caudal del panel: el planificador solo reparte una fracción, y medir
    // contra el caudal íntegro daba un número muy optimista.
    var easy = BandwidthBudget.Analyze(
        Enumerable.Range(0, 5).Select(i => new WidgetBandwidth($"t{i}", textWidget, 1.0)), caps);
    Check(easy.BudgetBytesPerSecond == Throughput * ByteBudgetScheduler.DefaultUtilization,
          $"ancho de banda: el presupuesto es la parte que reparte el planificador ({easy.BudgetBytesPerSecond:N0} B/s)");
    Check(!easy.Oversubscribed && !easy.NearLimit,
          $"ancho de banda: 5 textos a 1 Hz van holgados ({easy.Utilization:P0})");

    // El tema que motivó subir la utilización: diez textos, ocho barras y dos rótulos, que es lo que
    // mueve sin despeinarse la implementación de referencia en Python. Son 76.550 B/s, el 29 % del
    // caudal del panel. Con el presupuesto en un cuarto se salía, y el editor pintaba en rojo un tema
    // que el hardware mueve de sobra.
    var reference = BandwidthBudget.Analyze(
        [
            .. Enumerable.Range(0, 10).Select(i => new WidgetBandwidth($"valor{i}", new PixelRect(0, 0, 102, 28), 1.0)),
            .. Enumerable.Range(0, 8).Select(i => new WidgetBandwidth($"barra{i}", new PixelRect(0, 0, 102, 10), 1.0)),
            .. Enumerable.Range(0, 2).Select(i => new WidgetBandwidth($"rotulo{i}", new PixelRect(0, 0, 216, 18), 0.2)),
        ], caps);
    Check(!reference.Oversubscribed && !reference.NearLimit,
          $"ancho de banda: un tema de 20 elementos como el de Python cabe sin saturar ({reference.Utilization:P0})");
    Check(reference.RequiredBytesPerSecond / reference.PanelBytesPerSecond < 0.35,
          "ancho de banda: y en el panel no llega ni a un tercio del caudal");

    // Una gráfica de 320x100 son 64.000 B. A 3 Hz satura; a 4 Hz ya no cabe.
    var tight = BandwidthBudget.Analyze(
        [new WidgetBandwidth("chart", new PixelRect(0, 0, 320, 100), 3.0)], caps);
    Check(!tight.Oversubscribed && tight.NearLimit,
          $"ancho de banda: gráfica a 3 Hz cabe pero satura ({tight.Utilization:P0})");
    var heavy = BandwidthBudget.Analyze(
        [new WidgetBandwidth("chart", new PixelRect(0, 0, 320, 100), 4.0)], caps);
    Check(heavy.Oversubscribed, $"ancho de banda: gráfica a 4 Hz no cabe ({heavy.Utilization:P0})");
    Check(heavy.Consumers[0].WidgetId == "chart", "ancho de banda: ordena por consumo");

    // Y lo que el informe dice, el planificador lo cumple. Se comprueba repartiendo de verdad y no
    // comparando números: es lo único que impide que los dos se vayan cada uno por su lado.
    var chartBox = new PixelRect(0, 0, 320, 100);
    var keepsUp = new ByteBudgetScheduler(Throughput);
    for (int third = 0; third < 30; third++) // 10 s a 3 Hz, lo que «cabe pero satura»
    {
        keepsUp.MarkDirty("chart", chartBox);
        keepsUp.Drain(TimeSpan.FromSeconds(1.0 / 3));
    }
    Check(keepsUp.PendingCount == 0, "planificador: lo que el informe da por cabido sale sin dejar cola");

    var swamped = new ByteBudgetScheduler(Throughput);
    for (int quarter = 0; quarter < 40; quarter++) // 10 s a 4 Hz, lo que «no cabe»
    {
        swamped.MarkDirty($"chart{quarter}", chartBox);
        swamped.Drain(TimeSpan.FromSeconds(0.25));
    }
    Check(swamped.PendingCount > 0, "planificador: y lo que da por no cabido va dejando cola");
}
catch (Exception e)
{
    Broke("Comportamiento propio", e);
}

Header("Driver rev. A con transporte grabador");
try
{

    // Trama de la referencia para un comando con su rectángulo exacto.
    JsonElement ReferenceCommand(string command, int x, int y, int endX, int endY) =>
        root.GetProperty("commands").EnumerateArray()
            .First(e => e.GetProperty("command").GetString() == command
                     && e.GetProperty("x").GetInt32() == x && e.GetProperty("y").GetInt32() == y
                     && e.GetProperty("endX").GetInt32() == endX && e.GetProperty("endY").GetInt32() == endY)
            .GetProperty("expected");

    // Inicializar fija la orientación y el encendido de forma explícita en lugar de heredarlos.
    {
        var tape = new RecordingTransport();
        var drv = new RevADisplayDriver(tape, caps);
        await drv.InitializeAsync(Orientation.Landscape);
        Check(tape.Frames.Count == 2 && tape.Frames[0].Length == RevAProtocol.OrientationFrameLength,
              $"driver: inicializar son 2 tramas, orientación y encendido ({tape.Frames.Count})");
        CheckBytes(tape.Frames[0], root.GetProperty("orientation")[2].GetProperty("expected"),
                   "driver: la trama de orientación coincide con la referencia");
        CheckBytes(tape.Frames[1], ReferenceCommand("ScreenOn", 0, 0, 0, 0),
                   "driver: el encendido coincide con la referencia");
        Check(drv.Width == 480 && drv.Height == 320, "driver: en apaisado se intercambian ancho y alto");
        Check(tape.Flushes == 2, "driver: vacía el búfer tras cada trama");
    }

    // Pedir la pantalla apagada no pasa por encenderla: apagar desde un proceso nuevo no da un destello.
    {
        var tape = new RecordingTransport();
        var drv = new RevADisplayDriver(tape, caps);
        await drv.InitializeAsync(Orientation.Portrait, screenOn: false);
        Check(tape.Frames.Count == 2 && tape.Frames[0][5] == (byte)RevACommand.SetOrientation,
              $"driver: inicializar apagada son 2 tramas, sin encender antes ({tape.Frames.Count})");
        CheckBytes(tape.Frames[1], ReferenceCommand("ScreenOff", 0, 0, 0, 0),
                   "driver: el apagado coincide con la referencia");
    }

    // El panel no conserva el brillo al encenderse: vuelve con el suyo. Encender tiene que repetirlo.
    {
        var tape = new RecordingTransport();
        var drv = new RevADisplayDriver(tape, caps);
        await drv.InitializeAsync(Orientation.Landscape);
        await drv.SetBrightnessAsync(30);
        tape.Reset();

        await drv.SetScreenPowerAsync(false);
        Check(tape.Frames.Count == 1 && tape.Frames[0][5] == (byte)RevACommand.ScreenOff,
              $"driver: apagar es una sola trama ({tape.Frames.Count})");

        await drv.SetBrightnessAsync(70);
        Check(tape.Frames.Count == 1, "driver: apagada, el brillo solo se apunta y no viaja");

        await drv.SetScreenPowerAsync(true);
        Check(tape.Frames.Count == 3, $"driver: encender son 2 tramas, encendido y brillo ({tape.Frames.Count - 1})");
        Check(tape.Frames[1][5] == (byte)RevACommand.ScreenOn, "driver: primero el encendido");
        Check(tape.Frames[2].SequenceEqual(RevAProtocol.SetBrightness(70)),
              "driver: y detrás el último brillo pedido, que es el que el usuario ve en la interfaz");
    }

    // Limpiar da un rodeo por vertical porque el firmware solo limpia bien ahí. Si ese rodeo falla a
    // medias, el driver no puede quedarse creyéndose vertical: a partir de ahí todo blit apaisado se
    // saldría de un panel que, para él, mide 320x480.
    {
        var tape = new RecordingTransport { FailOnCommand = RevACommand.Clear };
        var drv = new RevADisplayDriver(tape, caps);
        await drv.InitializeAsync(Orientation.Landscape);

        bool blew = false;
        try { await drv.ClearAsync(); }
        catch (IOException) { blew = true; }

        Check(blew, "driver: si el Clear falla, el fallo se nota");
        Check(drv.Orientation == Orientation.Landscape,
              $"driver: y la orientación vuelve a la que era ({drv.Orientation})");
        Check(drv.Width == 480 && drv.Height == 320, "driver: con el tamaño que le corresponde");
    }

    // Sin haber pedido brillo nunca no hay nada que devolverle al panel.
    {
        var tape = new RecordingTransport();
        var drv = new RevADisplayDriver(tape, caps);
        await drv.InitializeAsync(Orientation.Portrait);
        tape.Reset();
        await drv.SetScreenPowerAsync(true);
        Check(tape.Frames.Count == 1, $"driver: encender sin brillo puesto es una sola trama ({tape.Frames.Count})");
    }

    // Un brillo imposible se planta al pedirlo, esté la pantalla como esté: si se guardara sin mirar,
    // el error saldría luego, al encender, y en un sitio que no tiene nada que ver.
    {
        var tape = new RecordingTransport();
        var drv = new RevADisplayDriver(tape, caps);
        await drv.InitializeAsync(Orientation.Portrait, screenOn: false);
        Check(await ThrowsAsync(async () => await drv.SetBrightnessAsync(200)),
              "driver: apagada, un brillo imposible falla igual");
    }

    // Limpiar esquiva el defecto del firmware: vertical, limpiar, restaurar.
    {
        var tape = new RecordingTransport();
        var drv = new RevADisplayDriver(tape, caps);
        await drv.InitializeAsync(Orientation.Landscape);
        tape.Reset();
        await drv.ClearAsync();
        Check(tape.Frames.Count == 3, $"driver: limpiar en apaisado son 3 tramas ({tape.Frames.Count})");
        Check(tape.Frames[0][5] == (byte)RevACommand.SetOrientation && tape.Frames[0][6] == 100,
              "driver: primero pasa a vertical");
        Check(tape.Frames[1][5] == (byte)RevACommand.Clear, "driver: luego limpia");
        Check(tape.Frames[2][5] == (byte)RevACommand.SetOrientation && tape.Frames[2][6] == 102,
              "driver: y restaura el apaisado");
        Check(drv.Orientation == Orientation.Landscape, "driver: la orientación vuelve a su sitio");
    }

    // En vertical no hay que dar el rodeo.
    {
        var tape = new RecordingTransport();
        var drv = new RevADisplayDriver(tape, caps);
        await drv.InitializeAsync(Orientation.Portrait);
        tape.Reset();
        await drv.ClearAsync();
        Check(tape.Frames.Count == 1, "driver: limpiar en vertical es una sola trama");
    }

    // Un blit son cabecera más píxeles, y los bytes que salen son exactamente los que entran.
    {
        var tape = new RecordingTransport();
        var drv = new RevADisplayDriver(tape, caps, chunkSize: 1024);
        await drv.InitializeAsync(Orientation.Portrait);
        tape.Reset();

        var rect = new PixelRect(100, 24, 100, 24);       // 4.800 B
        var payload = new byte[Rgb565.ByteCount(rect)];
        Random.Shared.NextBytes(payload);
        await drv.BlitAsync(payload, rect);

        Check(tape.Frames[0].Length == RevAProtocol.CommandLength && tape.Frames[0][5] == (byte)RevACommand.DisplayBitmap,
              "driver: el blit empieza por la cabecera DisplayBitmap");
        CheckBytes(tape.Frames[0], root.GetProperty("commands")
                       .EnumerateArray()
                       .First(e => e.GetProperty("command").GetString() == "DisplayBitmap"
                                && e.GetProperty("x").GetInt32() == 100
                                && e.GetProperty("y").GetInt32() == 24)
                       .GetProperty("expected"),
                   "driver: la cabecera coincide con la referencia");
        Check(tape.PayloadBytes == payload.Length,
              $"driver: salen los 4.800 B de píxeles ({tape.PayloadBytes})");
        Check(tape.Frames.Count == 1 + 5, $"driver: 4.800 B en trozos de 1.024 son 5 escrituras ({tape.Frames.Count - 1})");
        Check(tape.Payload.SequenceEqual(payload), "driver: los píxeles llegan intactos y en orden");
        Check(tape.Flushes == 1, "driver: un solo vaciado por blit");
    }

    // Validaciones de contorno.
    {
        var tape = new RecordingTransport();
        var drv = new RevADisplayDriver(tape, caps);
        var rect = new PixelRect(0, 0, 10, 10);
        var payload = new byte[Rgb565.ByteCount(rect)];

        Check(await ThrowsAsync(async () => await drv.BlitAsync(payload, rect)),
              "driver: pintar sin inicializar falla");

        await drv.InitializeAsync(Orientation.Portrait);
        Check(await ThrowsAsync(async () => await drv.BlitAsync(new byte[10], rect)),
              "driver: un tamaño de píxeles que no cuadra falla");
        Check(await ThrowsAsync(async () => await drv.BlitAsync(
                  new byte[Rgb565.ByteCount(new PixelRect(300, 0, 100, 10))], new PixelRect(300, 0, 100, 10))),
              "driver: un rectángulo que se sale en vertical falla");

        // Ese mismo rectángulo sí cabe en apaisado, porque el panel mide 480 de ancho.
        await drv.SetOrientationAsync(Orientation.Landscape);
        Check(!await ThrowsAsync(async () => await drv.BlitAsync(
                  new byte[Rgb565.ByteCount(new PixelRect(300, 0, 100, 10))], new PixelRect(300, 0, 100, 10))),
              "driver: el mismo rectángulo cabe en apaisado");
    }

    // Una orientación que no existe no llega al panel ni cambia la vigente.
    {
        var tape = new RecordingTransport();
        var drv = new RevADisplayDriver(tape, caps);
        await drv.InitializeAsync(Orientation.Landscape);
        tape.Reset();
        Check(await ThrowsAsync(async () => await drv.SetOrientationAsync((Orientation)4)),
              "driver: una orientación inexistente falla");
        Check(tape.Frames.Count == 0 && drv.Orientation == Orientation.Landscape,
              "driver: y no escribe nada ni cambia la orientación vigente");
    }
}
catch (Exception e)
{
    Broke("Driver rev. A con transporte grabador", e);
}

Console.WriteLine(new string('=', 48));
Console.WriteLine(failures == 0
    ? $"CORRECTO: {checks} comprobaciones"
    : $"FALLOS: {failures} de {checks} comprobaciones");
Console.WriteLine(new string('=', 48));
return failures == 0 ? 0 : 1;

static bool Throws(Action a)
{
    try { a(); return false; }
    catch (ArgumentException) { return true; }
}

static async Task<bool> ThrowsAsync(Func<Task> a)
{
    try { await a(); return false; }
    catch (ArgumentException) { return true; }
    catch (InvalidOperationException) { return true; }
}

/// <summary>Transporte que no habla con nada: apunta cada escritura para poder afirmar sobre ella.</summary>
sealed class RecordingTransport : IDisplayTransport
{
    private readonly List<byte> _payload = [];

    public List<byte[]> Frames { get; } = [];
    public int Flushes { get; private set; }
    public bool IsOpen { get; private set; }
    public string Name => "grabador";

    /// <summary>Todo lo escrito salvo la primera trama de cada secuencia, que es la cabecera.</summary>
    public IReadOnlyList<byte> Payload => _payload;
    public int PayloadBytes => _payload.Count;

    public ValueTask OpenAsync(CancellationToken cancellationToken = default)
    {
        IsOpen = true;
        return ValueTask.CompletedTask;
    }

    /// <summary>Un comando que se atraganta, para simular un tirón del cable en un punto concreto.</summary>
    public RevACommand? FailOnCommand { get; set; }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (FailOnCommand is { } bad &&
            data.Length == RevAProtocol.CommandLength &&
            data.Span[5] == (byte)bad)
        {
            throw new IOException("El panel ya no está.");
        }

        var copy = data.ToArray();
        Frames.Add(copy);
        // La cabecera de un blit son 6 bytes con el comando DisplayBitmap; el resto son píxeles.
        bool isHeader = copy.Length == RevAProtocol.CommandLength || copy.Length == RevAProtocol.OrientationFrameLength;
        if (!isHeader) _payload.AddRange(copy);
        return ValueTask.CompletedTask;
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        Flushes++;
        return ValueTask.CompletedTask;
    }

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(0);

    public ValueTask CloseAsync()
    {
        IsOpen = false;
        return ValueTask.CompletedTask;
    }

    public void Reset()
    {
        Frames.Clear();
        _payload.Clear();
        Flushes = 0;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
