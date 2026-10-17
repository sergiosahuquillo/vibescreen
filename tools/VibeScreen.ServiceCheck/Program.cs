using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VibeScreen.Contracts;
using VibeScreen.Core.Sensors;
using VibeScreen.Core.Storage;
using VibeScreen.Protocol.Display;
using VibeScreen.Service;

// Verificador del servicio. Comprueba sin hardware los mensajes del IPC, la trama de la tubería, la
// ida y vuelta por una tubería de verdad y, sobre todo, que el servicio aguanta que le quiten el panel.

int checks = 0, failures = 0;

void Check(bool condition, string what)
{
    checks++;
    if (condition) return;
    failures++;
    Console.WriteLine($"  FALLO  {what}");
}

bool Throws<T>(Action action) where T : Exception
{
    try
    {
        action();
        return false;
    }
    catch (T)
    {
        return true;
    }
}

async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        if (condition()) return true;
        await Task.Delay(10);
    }
    return condition();
}

string temp = Path.Combine(Path.GetTempPath(), $"vibescreen-check-{Environment.ProcessId}");
Directory.CreateDirectory(temp);

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
string layoutPath = Path.Combine(temp, "layout.json");

Header("Mensajes del IPC");
try
{

    string orden = JsonSerializer.Serialize<ServiceRequest>(
        new SetBrightnessRequest { Id = 7, Percent = 40 }, ServiceJsonContext.Default.ServiceRequest);
    Check(orden.Contains("""
        "command":"set-brightness"
        """, StringComparison.Ordinal), "el discriminador es el nombre de la orden");
    Check(orden.Contains("""
        "percent":40
        """, StringComparison.Ordinal), "las propiedades van en camelCase");

    var vuelta = JsonSerializer.Deserialize(orden, ServiceJsonContext.Default.ServiceRequest);
    Check(vuelta is SetBrightnessRequest { Id: 7, Percent: 40 }, "una orden va y vuelve igual");

    Check(Throws<JsonException>(() => JsonSerializer.Deserialize(
            """{"command":"formatea-el-disco"}""", ServiceJsonContext.Default.ServiceRequest)),
        "una orden desconocida se rechaza");
    Check(Throws<JsonException>(() => JsonSerializer.Deserialize(
            """{"command":"status","turbo":true}""", ServiceJsonContext.Default.ServiceRequest)),
        "un campo que no existe se rechaza en vez de ignorarse");
    Check(Throws<JsonException>(() => JsonSerializer.Deserialize(
            """{"command":"set-brightness"}""", ServiceJsonContext.Default.ServiceRequest)),
        "una orden a la que le faltan sus datos se rechaza");

    string respuesta = JsonSerializer.Serialize(
        new ServiceResponse { Id = 1, Ok = true, Status = new ServiceStatus { State = ServiceState.Painting } },
        ServiceJsonContext.Default.ServiceResponse);
    Check(respuesta.Contains("""
        "state":"Painting"
        """, StringComparison.Ordinal), "el estado viaja como texto, no como número");
    Check(!respuesta.Contains("error", StringComparison.Ordinal), "lo que no viene a cuento no se envía");
}
catch (Exception e)
{
    Broke("Mensajes del IPC", e);
}

Header("Trama por líneas");
try
{

    using (var buffer = new MemoryStream())
    {
        using (var writing = new JsonLineChannel(buffer))
        {
            await writing.WriteAsync<ServiceRequest>(new StatusRequest { Id = 1 }, ServiceJsonContext.Default.ServiceRequest);
            await writing.WriteAsync<ServiceRequest>(
                new SetLayoutRequest { Id = 2, Path = @"C:\temas\año nuevo.json" }, ServiceJsonContext.Default.ServiceRequest);
        }

        string texto = Encoding.UTF8.GetString(buffer.ToArray());
        Check(texto.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 2, "un mensaje por línea");
        Check(!texto.Contains('\r'), "el salto de línea no depende del sistema");

        buffer.Position = 0;
        using var reading = new JsonLineChannel(buffer);
        var uno = await reading.ReadAsync(ServiceJsonContext.Default.ServiceRequest);
        var dos = await reading.ReadAsync(ServiceJsonContext.Default.ServiceRequest);
        var tres = await reading.ReadAsync(ServiceJsonContext.Default.ServiceRequest);

        Check(uno is StatusRequest { Id: 1 }, "se lee el primer mensaje");
        Check(dos is SetLayoutRequest { Id: 2, Path: @"C:\temas\año nuevo.json" }, "el segundo llega con sus acentos intactos");
        Check(tres is null, "el fin de la tubería se distingue de un mensaje");
    }

    // El servicio se para mientras un cliente tiene la tubería abierta: cerrar el canal vacía lo que
    // quede pendiente y eso falla. Antes, esa excepción se llevaba por delante a la GUI.
    var tuberia = new BrokenStream();
    var rota = new JsonLineChannel(tuberia);
    tuberia.Broken = true;
    string? alCerrar = null;
    try { rota.Dispose(); }
    catch (Exception e) { alCerrar = e.Message; }
    Check(alCerrar is null, $"cerrar un canal sobre una tubería rota no revienta {alCerrar}");
}
catch (Exception e)
{
    Broke("Trama por líneas", e);
}

Header("Configuración");
try
{

    string configPath = Path.Combine(temp, "config.json");
    var store = new FileConfigStore(configPath, NullLogger.Instance);
    store.Save(new ServiceConfig { Brightness = 33, LayoutPath = @"C:\temas\uno.json", ScreenOn = false });
    var recuperada = store.Load();
    Check(recuperada.Brightness == 33 && recuperada.LayoutPath == @"C:\temas\uno.json" && !recuperada.ScreenOn,
        "la configuración sobrevive a un reinicio");

    File.WriteAllText(configPath, "{ esto no es json");
    Check(store.Load().Brightness == 50, "una configuración rota no impide arrancar");
    File.Delete(configPath);

    // El primer arranque después de instalar: sin fichero, el panel pinta el tema de serie en vez de
    // quedarse en negro. Pero solo entonces.
    const string deSerie = @"C:\ProgramData\VibeScreen\layouts\VibeScreen\VibeScreen.json";
    var primera = new FileConfigStore(configPath, NullLogger.Instance, deSerie);
    Check(primera.Load().LayoutPath == deSerie, "sin configuración, arranca con el tema de serie");
    Check(store.Load().LayoutPath is null, "sin configuración ni tema de serie, arranca sin tema");

    primera.Save(new ServiceConfig { LayoutPath = null });
    Check(primera.Load().LayoutPath is null, "con la configuración escrita, «ningún tema» se respeta");

    File.WriteAllText(configPath, "{ esto no es json");
    Check(primera.Load().LayoutPath is null, "una configuración rota no se toma por un primer arranque");
    File.Delete(configPath);
}
catch (Exception e)
{
    Broke("Configuración", e);
}

Header("Medidor de caudal");
try
{

    var rate = new RateWindow(TimeSpan.FromSeconds(5));
    Check(rate.PerSecond == 0, "sin muestras no hay caudal");

    rate.Add(TimeSpan.Zero, 0);
    Check(rate.PerSecond == 0, "con una sola muestra, tampoco");

    // El fotograma completo del arranque: 307.200 B en 1,2 s son los 256 KB/s del panel, no un infinito.
    rate.Add(TimeSpan.FromSeconds(1.2), 307_200);
    Check(Math.Abs(rate.PerSecond - (307_200 / 1.2)) < 1, $"el fotograma completo se reparte entre lo que tardó ({rate.PerSecond:N0} B/s)");

    // Y a los diez segundos ya no cuenta: solo pesa lo enviado dentro de la ventana.
    for (int second = 2; second <= 12; second++) rate.Add(TimeSpan.FromSeconds(second), 307_200 + ((second - 1) * 5_000));
    Check(Math.Abs(rate.PerSecond - 5_000) < 1, $"la ventana olvida el arranque y mide el régimen de ahora ({rate.PerSecond:N0} B/s)");

    rate.Reset();
    Check(rate.PerSecond == 0, "al cambiar de layout, el contador anterior ya no vale");
}
catch (Exception e)
{
    Broke("Medidor de caudal", e);
}

Header("Órdenes por la tubería");
try
{

    File.WriteAllText(layoutPath, """
        {
          "schema": 2,
          "meta": { "name": "Prueba" },
          "display": { "width": 480, "height": 320, "orientation": "landscape" },
          "widgets": [
            { "type": "text", "id": "temp", "rect": [0, 0, 200, 40], "sensor": "cpu/0/temperature/package",
              "decimals": 1, "showUnit": false, "size": 24, "refreshMs": 100 }
          ]
        }
        """);

    var connector = new FakeConnector();
    var sensors = new CountingSensors("cpu/0/temperature/package");
    await using (var host = new PanelHost(new MemoryConfigStore(), sensors, connector, NullLogger<PanelHost>.Instance) { LayoutRoot = temp })
    {
        var server = new IpcServer(host, NullLogger<IpcServer>.Instance)
        {
            PipeName = $"vibescreen-check-{Environment.ProcessId}",
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var serving = server.RunAsync(cts.Token);

        await using (var client = await ServiceClient.ConnectAsync(server.PipeName, cts.Token))
        {
            var estado = await client.SendAsync(new StatusRequest(), cts.Token);
            Check(estado.Ok && estado.Status is not null, "el estado va y vuelve por una tubería de verdad");
            Check(estado.Id == 1, "la respuesta lleva el identificador de la pregunta");

            var fuera = await client.SendAsync(new SetBrightnessRequest { Percent = 200 }, cts.Token);
            Check(!fuera.Ok && fuera.Error is not null, "un brillo imposible se contesta con un error, no con una excepción");

            var sinLayout = await client.SendAsync(new ReloadLayoutRequest(), cts.Token);
            Check(!sinLayout.Ok, "recargar sin layout configurado es un error");

            var noExiste = await client.SendAsync(new SetLayoutRequest { Path = Path.Combine(temp, "no-existe.json") }, cts.Token);
            Check(!noExiste.Ok, "un layout que no está se contesta con un error");

            var puesto = await client.SendAsync(new SetLayoutRequest { Path = layoutPath }, cts.Token);
            Check(puesto.Ok, "se puede poner un layout antes de que haya panel");

            var conLayout = await client.SendAsync(new StatusRequest(), cts.Token);
            Check(conLayout.Status?.LayoutName == "Prueba", "el estado dice qué layout está puesto");
            Check(conLayout.Status?.Language == "es", "de fábrica, el panel escribe los relojes en español");

            var idioma = await client.SendAsync(new SetLanguageRequest { Language = "en" }, cts.Token);
            var enIngles = await client.SendAsync(new StatusRequest(), cts.Token);
            Check(idioma.Ok && enIngles.Status?.Language == "en", "set-language cambia el idioma de los relojes del panel");
        }

        // Una segunda conexión, para comprobar que el servidor sigue escuchando después de la primera.
        await using (var client = await ServiceClient.ConnectAsync(server.PipeName, cts.Token))
        {
            var lista = await client.SendAsync(new SensorsRequest { Filter = "temperature" }, cts.Token);
            Check(lista.Ok && lista.Sensors is { Count: 1 }, "atiende a otro cliente y le da los sensores filtrados");
            Check(lista.Sensors?[0].Kind == "temperature" && lista.Sensors[0].Hardware.Length > 0,
                  "y cada sensor dice de qué magnitud es y de qué hardware cuelga");

            var uno = await client.SendAsync(new SensorsRequest { Ids = ["cpu/0/temperature/package"] }, cts.Token);
            Check(uno.Ok && uno.Sensors is { Count: 1 }, "se pueden pedir sensores sueltos por identificador");

            var ninguno = await client.SendAsync(new SensorsRequest { Ids = ["no/0/existe/nada"] }, cts.Token);
            Check(ninguno.Ok && ninguno.Sensors is { Count: 0 }, "y pedir uno que no existe no es un error, es una lista vacía");
        }

        string malPath = Path.Combine(temp, "mal.json");
        File.WriteAllText(malPath, """
            { "schema": 2, "display": { "width": 480, "height": 320 },
              "widgets": [ { "type": "text", "id": "x", "rect": [0, 0, 9000, 10] } ] }
            """);
        var invalido = await server.HandleAsync(new SetLayoutRequest { Id = 9, Path = malPath }, cts.Token);
        Check(!invalido.Ok && invalido.Problems is { Count: > 0 }, "un layout no válido devuelve todos sus problemas de una vez");
        Check(invalido.Id == 9, "la respuesta de error también lleva el identificador");

        await cts.CancelAsync();
        await serving;
    }
}
catch (Exception e)
{
    Broke("Órdenes por la tubería", e);
}

Header("Lecturas imposibles");
try
{

    await using (var host = new PanelHost(new MemoryConfigStore(), new BrokenSensors(), new FakeConnector(), NullLogger<PanelHost>.Instance) { LayoutRoot = temp })
    {
        var server = new IpcServer(host, NullLogger<IpcServer>.Instance);
        var response = await server.HandleAsync(new SensorsRequest { Id = 1 });

        Check(response.Ok && response.Sensors is { Count: 2 }, "un sensor que devuelve NaN no impide listar los demás");
        Check(response.Sensors?.FirstOrDefault(s => s.Id == "cpu/0/fan/roto")?.Value is null,
              "un NaN llega como «sin lectura» y no como número");

        // Antes de esto, un solo NaN entre 328 sensores hacía que el servicio cerrara la conexión sin
        // contestar: el serializador se planta y la excepción se llevaba por delante al cliente.
        string? problem = null;
        try { _ = JsonSerializer.Serialize(response, ServiceJsonContext.Default.ServiceResponse); }
        catch (Exception e) { problem = e.Message; }
        Check(problem is null, $"y la respuesta entera se puede serializar {problem}");
    }
}
catch (Exception e)
{
    Broke("Lecturas imposibles", e);
}

Header("El panel manda sobre sus sensores");
try
{

    // La ventana sondea una vez por segundo para su vista previa. Si eso muestreara el hardware sería un
    // segundo observador del mismo sitio: a los sensores que se calculan por diferencia entre dos
    // lecturas —velocidades de red y de disco, relojes efectivos— le partiría en dos la ventana de medida
    // al panel y cada uno acabaría promediando un trozo distinto del mismo segundo. O sea que tener el
    // editor abierto empeoraba lo que mide la pantalla. Así que lo que el panel pinta lo muestrea el
    // panel, y aquí se sirve lo que leyó.
    string quietPath = Path.Combine(temp, "quieto.json");
    File.WriteAllText(quietPath, """
        {
          "schema": 2,
          "meta": { "name": "Quieto" },
          "display": { "width": 480, "height": 320, "orientation": "landscape" },
          "widgets": [
            { "type": "text", "id": "temp", "rect": [0, 0, 200, 40], "sensor": "cpu/0/temperature/package",
              "decimals": 1, "showUnit": false, "size": 24, "refreshMs": 60000 }
          ]
        }
        """);

    var tracked = new TrackingSensors("cpu/0/temperature/package", "gpu/0/temperature/core") { Value = 40f };
    await using (var host = new PanelHost(new MemoryConfigStore(), tracked, new FakeConnector(), NullLogger<PanelHost>.Instance) { LayoutRoot = temp })
    {
        var server = new IpcServer(host, NullLogger<IpcServer>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        tracked.Updated.Clear();
        var parado = await server.HandleAsync(new SensorsRequest { Ids = ["cpu/0/temperature/package"] }, cts.Token);
        Check(tracked.Updated.Contains("cpu/0/temperature/package"), "sin panel no pinta nadie, así que se muestrea el hardware");
        Check(parado.Sensors?[0].Value == 40, "y se sirve esa lectura recién tomada");

        await host.SetLayoutAsync(quietPath);
        var running = host.RunAsync(cts.Token);
        Check(await WaitUntil(() => host.GetStatus().State == ServiceState.Painting, TimeSpan.FromSeconds(10)),
              "el panel arranca pintando");

        // El widget refresca cada minuto: el panel leyó 40 al arrancar y no va a volver a leer en toda la
        // prueba, así que si alguien contesta 99 es porque ha muestreado por su cuenta.
        tracked.Value = 99f;
        tracked.Updated.Clear();
        var pintado = await server.HandleAsync(new SensorsRequest { Ids = ["cpu/0/temperature/package"] }, cts.Token);
        Check(tracked.Updated.Count == 0, "un sensor que el panel está pintando no se vuelve a muestrear");
        Check(pintado.Sensors?[0].Value == 40,
              $"y se sirve lo que pintó el panel, no una muestra nueva ({pintado.Sensors?[0].Value})");

        // Un sensor que el tema no usa no lo lee nadie: ese sí hay que ir a buscarlo. Es el caso de montar
        // un widget nuevo en el editor, o de tener abierto un tema distinto del que hay en la pantalla.
        tracked.Updated.Clear();
        var ajeno = await server.HandleAsync(new SensorsRequest { Ids = ["gpu/0/temperature/core"] }, cts.Token);
        Check(tracked.Updated.Contains("gpu/0/temperature/core"), "un sensor que el tema no usa sí se muestrea");
        Check(ajeno.Sensors?[0].Value == 99, "y llega fresco");

        // La rama del filtro es la del explorador y la de «vibescreen service sensors», y su trabajo es el
        // contrario: decir qué marca el sensor ahora.
        var todos = await server.HandleAsync(new SensorsRequest { Filter = "temperature" }, cts.Token);
        Check(tracked.UpdatedAll, "el listado por filtro sigue refrescándolo todo");
        Check(todos.Sensors?.FirstOrDefault(s => s.Id == "cpu/0/temperature/package")?.Value == 99,
              "y dice lo que marca el sensor, no lo que hay en la pantalla");

        // Con la pantalla apagada el bucle no pinta, así que vuelve a no haber quien refresque.
        await host.SetScreenPowerAsync(false);
        tracked.Updated.Clear();
        var apagada = await server.HandleAsync(new SensorsRequest { Ids = ["cpu/0/temperature/package"] }, cts.Token);
        Check(tracked.Updated.Contains("cpu/0/temperature/package"), "con la pantalla apagada se vuelve a muestrear");
        Check(apagada.Sensors?[0].Value == 99, "y se sirve lo que marca ahora");

        await cts.CancelAsync();
        await running;
    }
}
catch (Exception e)
{
    Broke("El panel manda sobre sus sensores", e);
}

Header("Supervivencia a desconexiones");
try
{

    var flaky = new FakeConnector { FailFirst = 2 };
    var esperas = new List<TimeSpan>();
    var config = new MemoryConfigStore();
    await using (var host = new PanelHost(config, new CountingSensors("cpu/0/temperature/package"), flaky, NullLogger<PanelHost>.Instance) { LayoutRoot = temp })
    {
        host.Delay = (wait, _) =>
        {
            esperas.Add(wait);
            return Task.CompletedTask;
        };

        await host.SetLayoutAsync(layoutPath);
        Check(config.Saved?.LayoutPath == Path.GetFullPath(layoutPath), "el layout queda guardado para el siguiente arranque");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var running = host.RunAsync(cts.Token);

        Check(await WaitUntil(() => host.GetStatus().State == ServiceState.Painting, TimeSpan.FromSeconds(10)),
            "acaba pintando aunque el panel no estuviera al arrancar");
        Check(flaky.Opens == 3, "reintenta hasta que el panel aparece");
        Check(esperas.SequenceEqual([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]),
            "la espera entre intentos va subiendo");

        var perdido = flaky.Last!;
        Check(await WaitUntil(() => perdido.Blits > 1, TimeSpan.FromSeconds(5)), "pinta el fotograma completo y luego los cambios");
        Check(perdido.Brightness is [50], "al conectar le dice al panel el brillo configurado");

        await host.SetBrightnessAsync(20);
        Check(config.Saved?.Brightness == 20, "el brillo queda guardado para el siguiente arranque");

        // Le quitamos el panel de debajo en mitad de un blit.
        perdido.FailFromBlit = perdido.Blits + 1;

        Check(await WaitUntil(() => host.GetStatus().Reconnects == 1 && host.GetStatus().State == ServiceState.Painting,
                TimeSpan.FromSeconds(10)),
            "se recupera de que le desenchufen el panel");
        Check(perdido.Disposed, "el puerto del panel perdido se cierra");
        Check(flaky.Last != perdido && flaky.Last!.Blits > 0, "el panel nuevo recibe su fotograma completo");
        Check(flaky.Last!.Brightness is [20], "y vuelve al brillo de la configuración, no al de fábrica");

        // Vuelve de suspensión, o de un «Apagar» con el inicio rápido de Windows: el puerto no falla,
        // pero el panel se ha quedado sin corriente y está en vertical y en blanco.
        var dormido = flaky.Last!;
        int abiertos = flaky.Opens;
        host.PanelMayHaveReset();

        // Se espera al panel nuevo y no a la cuenta de reconexiones: lo que se comprueba es que se ha
        // reabierto, y la cuenta sube antes de que el panel viejo se haya cerrado.
        Check(await WaitUntil(() => flaky.Last != dormido && host.GetStatus().State == ServiceState.Painting,
                TimeSpan.FromSeconds(10)),
            "al volver de suspensión reabre el panel aunque el puerto no haya fallado");
        Check(host.GetStatus().Reconnects == 2, "y lo cuenta como una reconexión");
        Check(dormido.Disposed, "cierra el que tenía antes de reabrirlo");
        Check(flaky.Opens == abiertos + 1, "y lo abre una sola vez");
        Check(esperas[^1] == PanelHost.ResumeSettle, "después de dejarle arrancar");
        Check(flaky.Last!.Blits > 0, "el panel despertado recibe su fotograma completo");
        Check(flaky.Last!.Brightness is [20], "y el brillo de la configuración");

        // Una cancelación que no es del servicio. El transporte ya traduce la que da el puerto al
        // suspender, pero si se cuela otra, el bucle no puede tomarla por apagar el servicio: salía,
        // el proceso seguía en pie sin hacer nada y la pantalla se quedaba congelada.
        var cortado = flaky.Last!;
        cortado.FailAsCancelled = true;
        cortado.FailFromBlit = cortado.Blits + 1;

        Check(await WaitUntil(() => flaky.Last != cortado && host.GetStatus().State == ServiceState.Painting,
                TimeSpan.FromSeconds(10)),
            "una cancelación que no es del servicio no saca al bucle: reconecta");
        Check(!running.IsCompleted, "y el bucle sigue en marcha");

        await cts.CancelAsync();
        await running;
        Check(host.GetStatus().State == ServiceState.Stopping, "al parar deja de pintar");
    }
}
catch (Exception e)
{
    Broke("Supervivencia a desconexiones", e);
}

Header("Rutas que el servicio acepta");
try
{

    // La tubería está abierta a todos los usuarios locales y en Windows la atiende SYSTEM: pedirle que
    // abra una ruta de red es la primitiva clásica de escalada por relay de NTLM.
    Check(LayoutPathRule.Problem(Path.Combine(temp, "tema.json"), out _, temp) is null,
          "ruta: un .json de la carpeta de temas vale");

    // El tema tiene que estar en la carpeta de temas. La tubería está abierta a todos los usuarios
    // locales, así que sin esto cualquiera podía pedirle a SYSTEM que abriera y analizara un .json
    // al que él no llega, y el error de vuelta decía si existe y si es JSON.
    Check(LayoutPathRule.Problem(Path.Combine(temp, "tema.json"), out _, Path.Combine(temp, "otra")) is not null,
          "ruta: un .json local de fuera de la carpeta de temas ya no vale");
    Check(LayoutPathRule.Problem(Path.Combine(temp + "-vecina", "tema.json"), out _, temp) is not null,
          "ruta: y una carpeta que solo empieza igual tampoco cuela");
    Check(LayoutPathRule.Problem(@"\\servidor\temas\t.json", out _) is not null, "ruta: una de red no");
    Check(LayoutPathRule.Problem(@"\\.\pipe\vibescreen", out _) is not null, "ruta: un dispositivo tampoco");
    Check(LayoutPathRule.Problem("http://ejemplo.invalid/t.json", out _) is not null, "ruta: una URL tampoco");
    Check(LayoutPathRule.Problem(Path.Combine(temp, "tema.txt"), out _) is not null, "ruta: un tema es un .json");
    Check(LayoutPathRule.Problem("   ", out _) is not null, "ruta: vacía no");
    Check(LayoutPathRule.ResourceProblem(@"\\servidor\fotos\fondo.png") is not null,
          "ruta: la imagen de fondo sigue la misma regla");
    Check(LayoutPathRule.ResourceProblem("fondo.png") is null, "ruta: una imagen junto al tema vale");

    string gordo = Path.Combine(temp, "gordo.json");
    File.WriteAllBytes(gordo, new byte[LayoutPathRule.MaxBytes + 1]);
    Check(LayoutPathRule.SizeProblem(gordo) is not null, "ruta: un fichero enorme se rechaza antes de leerlo");
    Check(LayoutPathRule.SizeProblem(layoutPath) is null, "ruta: un tema de verdad cabe de sobra");

    await using (var host = new PanelHost(new MemoryConfigStore(), new CountingSensors("x"), new FakeConnector(), NullLogger<PanelHost>.Instance) { LayoutRoot = temp })
    {
        var server = new IpcServer(host, NullLogger<IpcServer>.Instance);
        var remota = await server.HandleAsync(new SetLayoutRequest { Id = 7, Path = @"\\servidor\temas\t.json" });
        Check(!remota.Ok && remota.Error?.Code == "layout.path.remote",
              "la orden set-layout rechaza una ruta de red y lo explica");

        // La búsqueda por identificadores se hace con el cerrojo del panel cogido, así que cuántos se
        // piden no lo puede decidir quien manda la orden: en el megabyte que admite una línea caben cien
        // mil, y el panel se quedaba esperando a que terminaran de buscarse.
        var muchos = await server.HandleAsync(new SensorsRequest
        {
            Id = 8,
            Ids = [.. Enumerable.Range(0, SensorsRequest.MaxIds + 1).Select(n => $"cpu/{n}/load/total")],
        });
        Check(!muchos.Ok && muchos.Error?.Code == "sensors.too-many" && muchos.Error.Args[0] == "512",
              $"la orden sensors rechaza pedir más de {SensorsRequest.MaxIds} identificadores de una vez");

        var pocos = await server.HandleAsync(new SensorsRequest { Id = 9, Ids = ["x"] });
        Check(pocos.Ok && pocos.Sensors?.Count == 1, "y los que pide una vista previa pasan de largo");

        // Un null dentro de la lista es posible: el serializador del IPC no comprueba las anotaciones de
        // nulabilidad de los elementos de una colección. Antes acababa en «fallo inesperado».
        var conNulo = await server.HandleAsync(new SensorsRequest { Id = 10, Ids = [null!, "x"] });
        Check(conNulo.Ok && conNulo.Sensors?.Count == 1, "y un null en la lista no es un fallo inesperado");
    }
}
catch (Exception e)
{
    Broke("Rutas que el servicio acepta", e);
}

Header("El layout del arranque se puede echar a suertes");
try
{

    {
        var azar = new MemoryConfigStore(new ServiceConfig { LayoutPath = layoutPath, RandomLayout = true });
        await using var host = new PanelHost(azar, new CountingSensors("x"), new FakeConnector(), NullLogger<PanelHost>.Instance) { LayoutRoot = temp };

        Check(host.GetStatus().RandomLayout, "el estado cuenta que el layout lo elige el azar");

        await host.ReloadLayoutAsync();
        Check(azar.Saved?.RandomLayout == true, "recargar el layout no apaga el modo: recargar no es elegir");

        await host.SetLayoutAsync(layoutPath, chosen: false);
        Check(azar.Saved?.RandomLayout == true,
              "aplicar desde el editor tampoco: es enseñar en qué trabajas, no elegir el de cada día");
        Check(azar.Saved?.LayoutPath == layoutPath, "aunque el tema aplicado sí se queda puesto");

        await host.SetLayoutAsync(layoutPath);
        Check(azar.Saved?.RandomLayout == false,
              "pero elegir uno en el desplegable sí lo apaga: es decir «este», no «otro mañana»");
        Check(!host.GetStatus().RandomLayout, "y el estado lo cuenta también");

        await host.SetRandomLayoutAsync(false);
        Check(azar.Saved?.LayoutPath == layoutPath, "quitar el modo no cambia lo que se está pintando");
    }
}
catch (Exception e)
{
    Broke("El layout del arranque se puede echar a suertes", e);
}

Header("El nombre de la tubería, cuando lo tiene el de antes");
try
{

    if (OperatingSystem.IsWindows())
    {
        // Reservar el nombre es cosa de Windows, así que el margen solo se puede comprobar aquí. El caso
        // que reproduce es el del servicio reiniciándose encima de su propia instancia anterior.
        await using var host = new PanelHost(new MemoryConfigStore(), new CountingSensors("x"), new FakeConnector(), NullLogger<PanelHost>.Instance) { LayoutRoot = temp };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        string nombre = $"vibescreen-check-reinicio-{Environment.ProcessId}";
        var anterior = new NamedPipeServerStream(nombre, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.FirstPipeInstance);

        var server = new IpcServer(host, NullLogger<IpcServer>.Instance)
        {
            PipeName = nombre,
            FirstPipeGrace = TimeSpan.FromSeconds(10),
        };
        var sirviendo = server.RunAsync(cts.Token);

        await Task.Delay(1200, cts.Token);
        Check(!sirviendo.IsCompleted, "con el nombre cogido, el servidor insiste en vez de rendirse");

        // El de antes suelta, que es lo que pasa cuando termina de cerrarse.
        await anterior.DisposeAsync();
        await Task.Delay(IpcServer.RetryAfter + TimeSpan.FromMilliseconds(600), cts.Token);

        await using (var cliente = await ServiceClient.ConnectAsync(nombre, cts.Token))
        {
            var contestado = await cliente.SendAsync(new StatusRequest(), cts.Token);
            Check(contestado.Ok, "y en cuanto se suelta, el servicio atiende sin haber tenido que reiniciarse");
        }

        await cts.CancelAsync();
        try
        {
            await sirviendo;
        }
        catch (OperationCanceledException)
        {
            // Parada normal.
        }

        // Y si no lo suelta nadie, se rinde: el nombre es suyo o no es.
        string terco = $"vibescreen-check-ocupado-{Environment.ProcessId}";
        using var squatter = new NamedPipeServerStream(terco, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.FirstPipeInstance);

        var rendido = new IpcServer(host, NullLogger<IpcServer>.Instance)
        {
            PipeName = terco,
            FirstPipeGrace = TimeSpan.FromMilliseconds(200),
        };

        string? dicho = null;
        try
        {
            using var corto = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await rendido.RunAsync(corto.Token);
        }
        catch (IOException e)
        {
            dicho = e.Message;
        }

        Check(dicho?.Contains("ocupado", StringComparison.Ordinal) == true,
              "pero si nadie lo suelta, el servicio no arranca y explica por qué");
    }
    else
    {
        Check(true, "reservar el nombre de la tubería es cosa de Windows: aquí no hay nada que comprobar");
    }
}
catch (Exception e)
{
    Broke("El nombre de la tubería, cuando lo tiene el de antes", e);
}

Header("Escritura entera o nada");
try
{

    string atomico = Path.Combine(temp, "atomico.json");
    AtomicFile.WriteAllText(atomico, "uno");
    AtomicFile.WriteAllText(atomico, "dos");
    Check(File.ReadAllText(atomico) == "dos", "escritura atómica: sobrescribe con lo nuevo");
    Check(!File.Exists(atomico + ".tmp"), "escritura atómica: no deja el temporal por ahí");
    Check(File.Exists(Path.Combine(temp, "atomico.json")), "escritura atómica: el fichero acaba donde se pidió");
}
catch (Exception e)
{
    Broke("Escritura entera o nada", e);
}

Header("Un cliente no puede tumbar al servicio");
try
{

    // Escribir sin mandar nunca un salto de línea hacía crecer al servicio hasta quedarse sin memoria.
    using (var flood = new MemoryStream())
    {
        var pen = new StreamWriter(flood, new UTF8Encoding(false), leaveOpen: true);
        pen.Write(new string('a', JsonLineChannel.MaxLineChars + 16));
        pen.Flush();
        flood.Position = 0;

        using var channel = new JsonLineChannel(flood);
        string? cut = null;
        try
        {
            _ = await channel.ReadAsync(ServiceJsonContext.Default.ServiceRequest);
        }
        catch (InvalidDataException e)
        {
            cut = e.Message;
        }

        Check(cut is not null, "tubería: una línea sin fin se corta en el tope en vez de crecer sin freno");
    }
}
catch (Exception e)
{
    Broke("Un cliente no puede tumbar al servicio", e);
}

try
{
    Directory.Delete(temp, recursive: true);
}
catch (IOException)
{
    // Un temporal que no se deja borrar no invalida nada.
}

Console.WriteLine();
Console.WriteLine("================================================");
Console.WriteLine(failures == 0 ? $"CORRECTO: {checks} comprobaciones" : $"FALLOS: {failures} de {checks} comprobaciones");
Console.WriteLine("================================================");
return failures == 0 ? 0 : 1;

/// <summary>Configuración en memoria: las comprobaciones no tienen por qué tocar el disco del usuario.</summary>
sealed class MemoryConfigStore(ServiceConfig? initial = null) : IConfigStore
{
    public ServiceConfig? Saved { get; private set; } = initial;

    public ServiceConfig Load() => Saved ?? new ServiceConfig();

    public void Save(ServiceConfig config) => Saved = config;
}

/// <summary>Un panel que se puede quitar y poner.</summary>
sealed class FakeConnector : IPanelConnector
{
    public static readonly DisplayCapabilities Capabilities = new()
    {
        Model = "Panel de mentira",
        Revision = HardwareRevision.Simulated,
        NativeWidth = 320,
        NativeHeight = 480,
        ThroughputBytesPerSecond = 261_600,
    };

    /// <summary>Cuántos intentos de apertura fallan antes de que el panel aparezca.</summary>
    public int FailFirst { get; init; }

    public int Opens { get; private set; }

    public FakeDriver? Last { get; private set; }

    public ValueTask<PanelConnection> OpenAsync(
        string? portName, Orientation orientation, bool screenOn, CancellationToken cancellationToken = default)
    {
        Opens++;
        if (Opens <= FailFirst)
            throw new InvalidOperationException("No se ha encontrado ningún panel compatible.");

        Last = new FakeDriver(orientation);
        return ValueTask.FromResult(new PanelConnection(Last, "COM-prueba", Capabilities.Model));
    }
}

sealed class FakeDriver(Orientation orientation) : IDisplayDriver
{
    public int Blits { get; private set; }

    /// <summary>A partir de este blit, el panel se comporta como si lo hubieran desenchufado.</summary>
    public int? FailFromBlit { get; set; }

    /// <summary>Que el fallo llegue como una cancelación que no es de nadie, y no como un panel perdido.</summary>
    public bool FailAsCancelled { get; set; }

    public bool Disposed { get; private set; }

    public DisplayCapabilities Capabilities => FakeConnector.Capabilities;

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
        Blits++;
        if (FailFromBlit is { } limit && Blits >= limit)
            throw FailAsCancelled ? new OperationCanceledException() : new IOException("El panel se ha desenchufado.");

        return ValueTask.CompletedTask;
    }

    /// <summary>Los brillos que le han pedido, en orden. El panel de verdad no se puede preguntar.</summary>
    public List<int> Brightness { get; } = [];

    public ValueTask SetBrightnessAsync(int percent, CancellationToken cancellationToken = default)
    {
        Brightness.Add(percent);
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask SetScreenPowerAsync(bool screenOn, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Un sensor que cambia en cada lectura. Hace falta para que el layout ensucie rectángulos: si el
/// valor no cambia, no se envía nada y no habría forma de notar que el panel ya no está.
/// </summary>
sealed class CountingSensors(string id) : ISensorProvider
{
    private float _value;

    public IReadOnlyList<Notice> Warnings => [];

    public IReadOnlyList<SensorReading> ReadAll() => [Reading()];

    public SensorReading? Read(string wanted) => string.Equals(wanted, id, StringComparison.Ordinal) ? Reading() : null;

    public void Update(IEnumerable<string> ids)
    {
    }

    public void UpdateAll()
    {
    }

    public void Dispose()
    {
    }

    private SensorReading Reading() => new()
    {
        Id = id,
        Value = _value++,
        Native = new NativeSensor
        {
            HardwareClass = HardwareClass.Cpu,
            HardwareId = "/prueba",
            HardwareRank = 0,
            HardwareName = "Prueba",
            Kind = SensorKind.Temperature,
            Name = "Core (Tctl/Tdie)",
            NativeId = "/prueba/temperature/0",
            NativeIndex = 0,
        },
    };
}

/// <summary>
/// Un backend cuyo valor pone la prueba y que apunta a quién le piden refrescar, para distinguir a
/// quien muestrea el hardware de quien se limita a leer lo que ya hay.
/// </summary>
sealed class TrackingSensors(params string[] ids) : ISensorProvider
{
    public List<string> Updated { get; } = [];

    public bool UpdatedAll { get; private set; }

    public float Value { get; set; }

    public IReadOnlyList<Notice> Warnings => [];

    public IReadOnlyList<SensorReading> ReadAll() => [.. ids.Select(Reading)];

    public SensorReading? Read(string id) => ids.Contains(id, StringComparer.Ordinal) ? Reading(id) : null;

    public void Update(IEnumerable<string> wanted) => Updated.AddRange(wanted);

    public void UpdateAll() => UpdatedAll = true;

    public void Dispose()
    {
    }

    private SensorReading Reading(string id) => new()
    {
        Id = id,
        Value = Value,
        Native = new NativeSensor
        {
            HardwareClass = HardwareClass.Cpu,
            HardwareId = "/prueba",
            HardwareRank = 0,
            HardwareName = "Prueba",
            Kind = SensorKind.Temperature,
            Name = id,
            NativeId = id,
            NativeIndex = 0,
        },
    };
}

/// <summary>Un backend con un sensor que no da lectura y la devuelve como NaN, que es lo que hace el de verdad.</summary>
sealed class BrokenSensors : ISensorProvider
{
    public IReadOnlyList<Notice> Warnings => [];

    public IReadOnlyList<SensorReading> ReadAll() =>
        [Reading("cpu/0/temperature/package", 42, SensorKind.Temperature), Reading("cpu/0/fan/roto", float.NaN, SensorKind.Fan)];

    public SensorReading? Read(string id) => ReadAll().FirstOrDefault(r => r.Id == id);

    public void Update(IEnumerable<string> ids)
    {
    }

    public void UpdateAll()
    {
    }

    public void Dispose()
    {
    }

    private static SensorReading Reading(string id, float value, SensorKind kind) => new()
    {
        Id = id,
        Value = value,
        Native = new NativeSensor
        {
            HardwareClass = HardwareClass.Cpu,
            HardwareId = "/prueba",
            HardwareName = "Prueba",
            Kind = kind,
            Name = id,
            NativeId = id,
        },
    };
}

/// <summary>Una tubería que ya no está: escribir o vaciar da error, como cuando el otro extremo se va.</summary>
sealed class BrokenStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => 0; set => throw new NotSupportedException(); }

    /// <summary>Se rompe cuando se le dice, no antes: una tubería recién abierta acepta escrituras.</summary>
    public bool Broken { get; set; }

    public override void Flush()
    {
        if (Broken) throw new IOException("Pipe is broken.");
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Pipe is broken.");

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (Broken) throw new IOException("Pipe is broken.");
    }
}
