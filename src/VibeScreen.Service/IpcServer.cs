using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VibeScreen.Contracts;
using VibeScreen.Core.Layout;
#if WINDOWS
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
#endif

namespace VibeScreen.Service;

/// <summary>
/// Atiende las órdenes que llegan por la tubería. Cada cliente va por su lado; el servicio es el
/// único que toca el panel, así que la exclusión la pone <see cref="PanelHost"/> y no esta clase.
/// </summary>
public sealed class IpcServer(PanelHost panel, ILogger<IpcServer> logger)
{
    /// <summary>La tubería donde se escucha. Las comprobaciones usan otro nombre para no chocar con el servicio de verdad.</summary>
    public string PipeName { get; set; } = IpcEndpoint.PipeName;

    /// <summary>
    /// Cuántos clientes se atienden a la vez.
    /// </summary>
    /// <remarks>
    /// De sobra para la GUI, la CLI y algún script, y muy por debajo de las 255 instancias que
    /// permite Windows. El tope importa porque la lista de control da escritura a todos los usuarios
    /// locales a propósito: sin él, cualquier proceso puede abrir conexiones y no cerrarlas hasta
    /// agotar el nombre, y a partir de ahí no se puede hablar con el servicio.
    /// </remarks>
    public int MaxClients { get; set; } = 32;

    /// <summary>Cuánto se espera si no se ha podido crear una instancia de la tubería.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Cuánto se insiste con la primera instancia antes de dar el nombre por ocupado.
    /// </summary>
    /// <remarks>
    /// La primera instancia reserva el nombre, y si ya lo tiene otro el servicio se niega a
    /// arrancar: es lo que impide que alguien se haga pasar por él. Pero hay un caso en el que el
    /// nombre está cogido y se va a soltar solo, y es justo el más común de todos: el servicio
    /// reiniciándose. El gestor lo levanta de nuevo enseguida —tras un fallo, o con un «stop» y un
    /// «start» seguidos— y la instancia anterior puede tardar un momento en soltar la suya; sin este
    /// margen, el servicio nuevo se encuentra su propia tubería a medio cerrar y no arranca.
    /// Rendirse sigue siendo lo que pasa al final, porque quien ocupa el nombre de verdad lo sigue
    /// ocupando pasados unos segundos.
    /// </remarks>
    public TimeSpan FirstPipeGrace { get; set; } = TimeSpan.FromSeconds(10);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var clients = new List<Task>();
        bool first = true;
        var giveUp = DateTimeOffset.UtcNow + FirstPipeGrace;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Un cliente que no se va no puede quedarse con el sitio de los demás: al llegar al
                // tope se espera a que alguno termine en vez de seguir aceptando.
                while (clients.Count >= MaxClients)
                {
                    await Task.WhenAny(clients).ConfigureAwait(false);
                    Prune(clients);
                }

                NamedPipeServerStream pipe;
                try
                {
                    // CA2000 no ve la cesión de propiedad: la tubería pasa a ServeAsync, que la suelta en
                    // su finally, y si esperar la conexión falla la suelta el catch de más abajo.
#pragma warning disable CA2000
                    pipe = CreatePipe(first);
#pragma warning restore CA2000
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    if (first && DateTimeOffset.UtcNow >= giveUp)
                        throw new IOException(
                            $"El nombre de tubería «{PipeName}» sigue ocupado después de {FirstPipeGrace.TotalSeconds:F0} s. " +
                            "Si no es una instancia anterior del servicio soltándolo, hay otro proceso ocupándolo: " +
                            "el servicio no arranca para que nadie pueda hacerse pasar por él.", e);

                    if (first)
                        logger.LogWarning(
                            "El nombre de la tubería está cogido, quizá por la instancia anterior. Se reintenta en {Segundos} s.",
                            RetryAfter.TotalSeconds);
                    else
                        // Quedarse sin instancias es pasajero: los clientes se van y el nombre se libera.
                        // Morirse aquí, en cambio, dejaría el servicio en pie y mudo para siempre.
                        logger.LogError(e, "No se ha podido crear la tubería. Se reintenta en {Segundos} s.",
                            RetryAfter.TotalSeconds);

                    await Task.Delay(RetryAfter, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                first = false;

                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                    throw;
                }

                clients.Add(ServeAsync(pipe, cancellationToken));
                Prune(clients);
            }
        }
        catch (OperationCanceledException)
        {
            // Parada normal.
        }

        try
        {
            await Task.WhenAll(clients).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // En la parada, lo que traiga un cliente ya no cambia nada; pero sin esto se propagaría
            // y el servicio terminaría por un cliente que se fue mal.
            logger.LogDebug("Al cerrar quedaban clientes con problemas: {Mensaje}", e.Message);
        }
    }

    /// <summary>
    /// Quita de la lista los clientes que ya terminaron, mirando cómo terminaron.
    /// </summary>
    /// <remarks>
    /// Descartarlos sin mirar deja una tarea fallida sin observar: no la ve nadie, no sale en el log
    /// y el fallo desaparece. <see cref="ServeAsync"/> ya lo atrapa todo, así que esto es el cinturón.
    /// </remarks>
    private void Prune(List<Task> clients) =>
        clients.RemoveAll(c =>
        {
            if (!c.IsCompleted) return false;
            if (c.Exception is { } failure)
                logger.LogError(failure, "La conexión de un cliente terminó con un fallo sin atender.");
            return true;
        });

    private NamedPipeServerStream CreatePipe(bool first)
    {
        var options = PipeOptions.Asynchronous;
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            // La primera instancia se reserva el nombre: si otro proceso ya lo tiene cogido, el
            // servicio falla al arrancar en vez de dejar que alguien se haga pasar por él.
            if (first) options |= PipeOptions.FirstPipeInstance;
            return CreateWindowsPipe(PipeName, options);
        }
#endif
        return new NamedPipeServerStream(PipeName, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, options);
    }

#if WINDOWS
    /// <summary>
    /// El servicio corre como SYSTEM y la GUI sin elevar, así que la tubería necesita una lista de
    /// control explícita: con la que Windows pone por defecto, el usuario no podría ni abrirla.
    /// Los usuarios pueden leer y escribir, pero no crear instancias del nombre.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static NamedPipeServerStream CreateWindowsPipe(string pipeName, PipeOptions options)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, options,
            inBufferSize: 0, outBufferSize: 0, security);
    }
#endif

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        try
        {
            using var channel = new JsonLineChannel(pipe);
            while (!cancellationToken.IsCancellationRequested)
            {
                ServiceRequest? request;
                try
                {
                    request = await channel.ReadAsync(ServiceJsonContext.Default.ServiceRequest, cancellationToken).ConfigureAwait(false);
                }
                catch (JsonException e)
                {
                    // Un mensaje ilegible no tira la conexión: se contesta con el error y se sigue
                    // escuchando, que es lo que espera un cliente que se ha quedado viejo.
                    await channel.WriteAsync(ServiceResponse.Failure(0, Notice.Of("ipc.bad-message", e.Message)),
                        ServiceJsonContext.Default.ServiceResponse, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (request is null) break;

                var response = await HandleAsync(request, cancellationToken).ConfigureAwait(false);
                try
                {
                    await channel.WriteAsync(response, ServiceJsonContext.Default.ServiceResponse, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException)
                {
                    // La respuesta no cabe en JSON. El mensaje se serializa entero antes de escribir
                    // nada, así que la tubería sigue limpia y se puede contestar el error: dejar al
                    // cliente esperando una respuesta que no va a llegar es lo único inaceptable.
                    logger.LogError("No se ha podido serializar la respuesta a {Orden}: {Mensaje}",
                        request.GetType().Name, e.Message);
                    await channel.WriteAsync(
                        ServiceResponse.Failure(request.Id, Notice.Of("ipc.cannot-serialize", e.Message)),
                        ServiceJsonContext.Default.ServiceResponse, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Parada normal.
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            logger.LogDebug("Cliente desconectado: {Mensaje}", e.Message);
        }
        catch (InvalidDataException e)
        {
            // Quien escribe así no está hablando el protocolo. Se le cierra y se deja constancia.
            logger.LogWarning("Se cierra una conexión que no respeta el protocolo: {Mensaje}", e.Message);
        }
        catch (Exception e)
        {
            // Lo que no se esperaba se queda aquí. Antes salía de la tarea del cliente, que se
            // descartaba sin observar: el cliente se quedaba sin respuesta y no había ni una línea
            // en el log que dijera por qué.
            logger.LogError(e, "La conexión de un cliente ha fallado de forma inesperada.");
        }
        finally
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Ejecuta una orden. Ningún fallo de una orden puede tirar el servicio.</summary>
    public async Task<ServiceResponse> HandleAsync(ServiceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            switch (request)
            {
                case StatusRequest:
                    return new ServiceResponse { Id = request.Id, Ok = true, Status = panel.GetStatus() };

                case SensorsRequest r:
                    return new ServiceResponse
                    {
                        Id = request.Id,
                        Ok = true,
                        Sensors = await panel.GetSensorsAsync(r.Ids, r.Filter, cancellationToken).ConfigureAwait(false),
                    };

                case SetLayoutRequest r:
                    await panel.SetLayoutAsync(r.Path, r.Chosen, cancellationToken).ConfigureAwait(false);
                    return ServiceResponse.Success(request.Id);

                case SetRandomLayoutRequest r:
                    await panel.SetRandomLayoutAsync(r.On, cancellationToken).ConfigureAwait(false);
                    return ServiceResponse.Success(request.Id);

                case ReloadLayoutRequest:
                    await panel.ReloadLayoutAsync(cancellationToken).ConfigureAwait(false);
                    return ServiceResponse.Success(request.Id);

                case SetBrightnessRequest r:
                    await panel.SetBrightnessAsync(r.Percent, cancellationToken).ConfigureAwait(false);
                    return ServiceResponse.Success(request.Id);

                case SetScreenPowerRequest r:
                    await panel.SetScreenPowerAsync(r.On, cancellationToken).ConfigureAwait(false);
                    return ServiceResponse.Success(request.Id);

                case SetLanguageRequest r:
                    await panel.SetLanguageAsync(r.Language, cancellationToken).ConfigureAwait(false);
                    return ServiceResponse.Success(request.Id);

                default:
                    return ServiceResponse.Failure(request.Id, Notice.Of("service.unsupported", request.GetType().Name));
            }
        }
        catch (LayoutException e)
        {
            return ServiceResponse.Failure(request.Id, e.Notice, e.Problems);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            logger.LogWarning("La orden {Orden} ha fallado: {Mensaje}", request.GetType().Name, e.Message);
            return ServiceResponse.Failure(request.Id, Notice.From(e));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // La lista de arriba enumera tipos, y enumerar tipos siempre se queda corto: el backend
            // de sensores baja a ring0 y puede tirar cualquier cosa. Sin esto, la excepción salía de
            // la orden, salía del cliente y se perdía: «ha cerrado la conexión sin contestar», sin
            // log. Contestar el fallo es lo único que permite enterarse.
            logger.LogError(e, "La orden {Orden} ha fallado de forma inesperada.", request.GetType().Name);
            return ServiceResponse.Failure(request.Id, Notice.Of("error.unexpected").With(Notice.From(e)));
        }
    }
}
