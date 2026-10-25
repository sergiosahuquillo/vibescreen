using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Avalonia.Threading;
using VibeScreen.Core.Updates;

namespace VibeScreen.App.Updates;

/// <summary>En qué anda el actualizador.</summary>
public enum UpdatePhase
{
    /// <summary>No se ha buscado, o está apagado.</summary>
    Idle,

    Checking,

    /// <summary>La que hay es la última.</summary>
    UpToDate,

    /// <summary>Hay una nueva y no se ha bajado.</summary>
    Available,

    Downloading,

    /// <summary>El instalador está bajado y comprobado, esperando a que se lance.</summary>
    Ready,

    /// <summary>El instalador está en marcha. Si va bien, cierra esta ventana y abre la nueva.</summary>
    Installing,
}

/// <summary>Por qué falló lo último que se intentó.</summary>
public enum UpdateFailure
{
    /// <summary>No se llegó a GitHub, o contestó con un error.</summary>
    Network,

    /// <summary>GitHub no contestó a tiempo.</summary>
    Timeout,

    /// <summary>La release no se puede ofrecer: ver <see cref="ReleaseProblem"/>.</summary>
    Release,

    /// <summary>Lo bajado no es lo publicado. Se ha borrado.</summary>
    Hash,

    /// <summary>No se pudo guardar el instalador.</summary>
    Disk,

    /// <summary>Alguien dijo que no al permiso de administrador.</summary>
    Cancelled,

    /// <summary>El instalador terminó con error. El detalle es su código, y el registro dice el resto.</summary>
    Installer,
}

/// <summary>Una foto del actualizador. La interfaz pinta esto y nada más.</summary>
/// <param name="Release">La versión nueva, desde que se sabe que la hay.</param>
/// <param name="Percent">Lo bajado, mientras se baja.</param>
/// <param name="Failure">Lo último que falló, si lo último falló.</param>
/// <param name="Detail">De qué va el fallo, en crudo: el mensaje de la excepción, el código HTTP…</param>
/// <param name="CheckedAt">La última vez que GitHub contestó.</param>
public sealed record UpdateState(
    UpdatePhase Phase,
    ReleaseInfo? Release = null,
    int Percent = 0,
    UpdateFailure? Failure = null,
    string? Detail = null,
    DateTimeOffset? CheckedAt = null);

/// <summary>
/// Busca versiones nuevas en las releases de GitHub, baja el instalador, lo comprueba y lo lanza.
/// </summary>
/// <remarks>
/// <para>
/// Lo hace la ventana, no el servicio. El servicio corre como SYSTEM y su tubería la puede escribir
/// cualquier usuario del equipo: si instalara él, cualquiera podría hacer que se ejecutara como
/// SYSTEM lo que hubiera publicado, sin que nadie lo aprobase. Así, cada actualización pasa por el
/// permiso de administrador de Windows, igual que instalar a mano.
/// </para>
/// <para>
/// Un instalador solo se lanza si su SHA-256 es el que GitHub da para ese fichero. Se baja a un
/// <c>.part</c> y solo pasa a llamarse como el instalador cuando el hash cuadra.
/// </para>
/// <para>
/// Todo se llama desde el hilo de la interfaz, y <see cref="Changed"/> se avisa en él.
/// </para>
/// </remarks>
public sealed class Updater : IDisposable
{
    private static readonly HttpClient Http = CreateClient();

    // Lo que tarda en buscar después de abrirse: el arranque ya tiene bastante con el servicio, los
    // sensores y el primer pintado, y saber si hay versión nueva no corre ninguna prisa.
    private static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan Every = TimeSpan.FromHours(24);
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(15);

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _daily = new() { Interval = Every };
    private IDisposable? _first;
    private CancellationTokenSource? _work;

    public Updater(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _daily.Tick += (_, _) => _ = CheckAsync(quiet: true);
    }

    /// <summary>Cambia cada vez que cambia <see cref="State"/>.</summary>
    public event Action? Changed;

    public UpdateState State { get; private set; } = new(UpdatePhase.Idle);

    /// <summary>
    /// Si en esta compilación se puede actualizar. Solo en Windows, que es lo único que tiene
    /// instalador, y solo en una release: una compilación de desarrollo no tiene con qué compararse.
    /// </summary>
    public static bool Supported => OperatingSystem.IsWindows() && Current is not null;

    /// <summary>La versión que está corriendo, si es una publicada.</summary>
    public static AppVersion? Current { get; } = ReadCurrent();

    /// <summary>Dónde se guarda el instalador mientras tanto, y el registro de la instalación.</summary>
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VibeScreen", "updates");

    public static string InstallLog => Path.Combine(Folder, "install.log");

    /// <summary>Se pone en marcha según el modo elegido. Se llama una vez, con la ventana ya abierta.</summary>
    public void Start()
    {
        ForgetInstalled();
        ModeChanged();
    }

    /// <summary>
    /// Borra los instaladores de versiones que ya están puestas.
    /// </summary>
    /// <remarks>
    /// El instalador cierra esta ventana, así que nadie puede limpiar después de él: lo hace la
    /// ventana nueva al abrirse. Son cuarenta megas por actualización que, si no, se quedarían en
    /// el perfil del usuario para siempre. El registro de la instalación sí se queda, que es lo
    /// que hay que pedir cuando alguien cuenta que algo no le ha ido.
    /// </remarks>
    private static void ForgetInstalled()
    {
        if (!Supported || !Directory.Exists(Folder)) return;

        try
        {
            foreach (string file in Directory.EnumerateFiles(Folder, "VibeScreen-*-setup.exe*"))
            {
                // VibeScreen-1.0.2-setup.exe, o su .part si se quedó a medias.
                string[] parts = Path.GetFileName(file).Split('-');
                if (parts.Length >= 3 && AppVersion.TryParse(parts[1], out var version) && version <= Current!.Value)
                    File.Delete(file);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Si no se deja borrar ahora, se intentará en el siguiente arranque.
        }
    }

    /// <summary>
    /// El modo ha cambiado en Ajustes.
    /// </summary>
    /// <remarks>
    /// Pasar a avisar busca en ese momento, que es lo que espera quien acaba de encenderlo. Apagarlo
    /// para lo que esté buscando o bajando y olvida lo que se supiera: con «no actualizar» no tiene
    /// que quedar ningún aviso de una versión nueva por ningún lado.
    /// </remarks>
    public void ModeChanged()
    {
        _first?.Dispose();
        _first = null;
        _daily.Stop();

        if (!Supported) return;

        switch (_settings.Updates)
        {
            case UpdateMode.Notify:
                _first = DispatcherTimer.RunOnce(() => _ = CheckAsync(quiet: true), FirstCheck);
                _daily.Start();
                break;

            case UpdateMode.Off:
                if (State.Phase != UpdatePhase.Installing)
                {
                    _work?.Cancel();
                    Set(new UpdateState(UpdatePhase.Idle));
                }
                break;
        }
    }

    /// <summary>Pregunta a GitHub cuál es la última. No lanza: lo que falle queda en el estado.</summary>
    /// <param name="quiet">
    /// La comprobación sola, la de al abrir y la diaria. No pisa una descarga en curso ni un
    /// instalador ya bajado, y no hace nada si entretanto se ha cambiado el modo.
    /// </param>
    public async Task CheckAsync(bool quiet = false)
    {
        if (!Supported || _settings.Updates == UpdateMode.Off) return;
        if (State.Phase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Installing) return;
        if (quiet && (_settings.Updates != UpdateMode.Notify || State.Phase == UpdatePhase.Ready)) return;

        var previous = State;
        Set(previous with { Phase = UpdatePhase.Checking, Failure = null, Detail = null });

        using var work = Begin();
        try
        {
            work.CancelAfter(AskTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, Feed);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Http.SendAsync(request, work.Token);

            if (!response.IsSuccessStatusCode)
            {
                Fail(previous, UpdateFailure.Network, $"HTTP {(int)response.StatusCode}");
                return;
            }

            string json = await response.Content.ReadAsStringAsync(work.Token);
            if (!ReleaseFeed.TryParse(json, out var release, out var problem))
            {
                Fail(previous, UpdateFailure.Release, problem.ToString());
                return;
            }

            if (!Trusted(release.Download))
            {
                Fail(previous, UpdateFailure.Release, release.Download.Scheme);
                return;
            }

            var now = DateTimeOffset.Now;
            Set(release.Version > Current!.Value
                ? new UpdateState(UpdatePhase.Available, release, CheckedAt: now)
                : new UpdateState(UpdatePhase.UpToDate, CheckedAt: now));
        }
        catch (OperationCanceledException) when (_settings.Updates == UpdateMode.Off)
        {
            // Lo ha parado el propio cambio de modo, que ya ha dejado el estado como tiene que estar.
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
        {
            if (e is OperationCanceledException) Fail(previous, UpdateFailure.Timeout, "");
            else Fail(previous, UpdateFailure.Network, e.Message);
        }
        finally
        {
            End(work);
        }
    }

    /// <summary>
    /// Baja la versión nueva, la comprueba y lanza su instalador. No lanza: lo que falle queda en el
    /// estado.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El instalador se lanza tal cual, sin pedir que se eleve: lo hace él. Así sabe quién era el
    /// usuario de antes de elevarse, y es con ese con el que vuelve a abrir la ventana al acabar
    /// (<c>runasoriginaluser</c>); elevada, Windows le rompería el arrastrar y soltar.
    /// </para>
    /// <para>
    /// Esta ventana no se cierra sola. Si el instalador sigue adelante, para el servicio y la cierra
    /// él, y la abre otra vez al acabar. Si no —el permiso denegado, un error antes de empezar—, la
    /// ventana sigue aquí para contarlo, que si se hubiera ido no habría nadie.
    /// </para>
    /// </remarks>
    public async Task InstallAsync()
    {
        if (!Supported || State.Release is not { } release) return;
        if (State.Phase is not (UpdatePhase.Available or UpdatePhase.Ready)) return;

        using var work = Begin();
        string installer = Path.Combine(Folder, release.FileName);
        try
        {
            var locked = await OpenVerifiedAsync(installer, release.Sha256, work.Token);
            if (locked is null)
            {
                Set(new UpdateState(UpdatePhase.Downloading, release, CheckedAt: State.CheckedAt));
                if (!await DownloadAsync(release, installer, work.Token)) return;

                // Entre dejarlo con su nombre y abrirlo, alguien ha podido cambiarlo.
                locked = await OpenVerifiedAsync(installer, release.Sha256, work.Token);
                if (locked is null)
                {
                    File.Delete(installer);
                    Set(new UpdateState(UpdatePhase.Available, release, Failure: UpdateFailure.Hash,
                                        CheckedAt: State.CheckedAt));
                    return;
                }
            }

            await using (locked)
            {
                Set(new UpdateState(UpdatePhase.Installing, release, CheckedAt: State.CheckedAt));
                await RunInstallerAsync(release, installer);
            }
        }
        catch (OperationCanceledException)
        {
            // Lo ha parado el cambio de modo a «no actualizar», que ya ha dejado el estado limpio.
        }
        catch (HttpRequestException e)
        {
            Set(new UpdateState(UpdatePhase.Available, release, Failure: UpdateFailure.Network, Detail: e.Message,
                                CheckedAt: State.CheckedAt));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Set(new UpdateState(UpdatePhase.Available, release, Failure: UpdateFailure.Disk, Detail: e.Message,
                                CheckedAt: State.CheckedAt));
        }
        finally
        {
            End(work);
        }
    }

    /// <summary>
    /// Baja el instalador a un <c>.part</c> calculando el hash por el camino, y solo si cuadra lo
    /// deja con su nombre. Antes borra lo que hubiera de otras versiones.
    /// </summary>
    private async Task<bool> DownloadAsync(ReleaseInfo release, string installer, CancellationToken cancel)
    {
        Directory.CreateDirectory(Folder);
        foreach (string old in Directory.EnumerateFiles(Folder, "VibeScreen-*"))
            File.Delete(old);

        string part = installer + ".part";
        using var response = await Http.GetAsync(release.Download, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (!response.IsSuccessStatusCode)
        {
            Set(new UpdateState(UpdatePhase.Available, release, Failure: UpdateFailure.Network,
                                Detail: $"HTTP {(int)response.StatusCode}", CheckedAt: State.CheckedAt));
            return false;
        }

        long total = response.Content.Headers.ContentLength ?? release.Size;

        // El bucle fuera del hilo de la interfaz: son unos cuantos cientos de vueltas, y la ventana
        // tiene que poder seguir repintando el progreso entre ellas.
        string hash = await Task.Run(async () =>
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var input = await response.Content.ReadAsStreamAsync(cancel);
            await using var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None,
                                                    bufferSize: 1 << 16, useAsync: true);

            var buffer = new byte[1 << 16];
            long done = 0;
            int shown = -1;
            int read;
            while ((read = await input.ReadAsync(buffer, cancel)) > 0)
            {
                sha.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancel);
                done += read;

                int percent = total > 0 ? (int)Math.Min(100, done * 100 / total) : 0;
                if (percent == shown) continue;
                shown = percent;
                Dispatcher.UIThread.Post(() =>
                {
                    if (State.Phase == UpdatePhase.Downloading) Set(State with { Percent = percent });
                });
            }

            return Convert.ToHexStringLower(sha.GetHashAndReset());
        }, cancel);

        if (!string.Equals(hash, release.Sha256, StringComparison.Ordinal))
        {
            File.Delete(part);
            Set(new UpdateState(UpdatePhase.Available, release, Failure: UpdateFailure.Hash, Detail: hash,
                                CheckedAt: State.CheckedAt));
            return false;
        }

        File.Move(part, installer, overwrite: true);
        return true;
    }

    private async Task RunInstallerAsync(ReleaseInfo release, string installer)
    {
        var ready = new UpdateState(UpdatePhase.Ready, release, CheckedAt: State.CheckedAt);

        Process? setup;
        try
        {
            // /SILENT y no /VERYSILENT: con la ventana cerrada, la barra de progreso del instalador es
            // lo único que dice que algo está pasando. /RELAUNCH=1 es nuestro: le pide que abra la
            // ventana al acabar, que en silencio no lo haría.
            setup = Process.Start(new ProcessStartInfo(installer)
            {
                UseShellExecute = true,
                Arguments = $"/SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH=1 /LOG=\"{InstallLog}\"",
            });
        }
        catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
        {
            Set(ready with { Failure = UpdateFailure.Cancelled });
            return;
        }
        catch (Win32Exception e)
        {
            Set(ready with { Failure = UpdateFailure.Installer, Detail = e.Message });
            return;
        }

        if (setup is null)
        {
            Set(ready);
            return;
        }

        using (setup)
        {
            await setup.WaitForExitAsync(CancellationToken.None);

            // Si ha ido bien no se llega aquí: el instalador ya ha cerrado esta ventana. Volver quiere
            // decir que no ha llegado a instalar, y el porqué está en su código de salida.
            int code = setup.ExitCode;
            Set(code switch
            {
                0 => new UpdateState(UpdatePhase.Idle),
                SetupCancelled => ready with { Failure = UpdateFailure.Cancelled },
                _ => ready with { Failure = UpdateFailure.Installer, Detail = code.ToString(CultureInfo.InvariantCulture) },
            });
        }
    }

    /// <summary>
    /// Abre el instalador para que nadie pueda tocarlo, y lo devuelve abierto si es el bueno; si no
    /// está o no lo es, null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Abierto para leer y dejando leer a los demás, pero no escribir, ni renombrarlo, ni borrarlo.
    /// Quien lo llama lo tiene así hasta que el instalador termina: Windows lo ejecuta leyéndolo de
    /// la carpeta, que es del usuario, y lo que se ejecuta tiene que ser lo que se ha comprobado y no
    /// lo que haya ahí en ese momento. Sin esto, algo que corriera con este usuario podría cambiarlo
    /// entre comprobarlo y lanzarlo, y lo suyo saldría con permisos de administrador.
    /// </para>
    /// <para>
    /// El hash se calcula por este mismo descriptor, ya con el fichero cerrado a cambios. Es también
    /// lo que deja reintentar sin bajarlo otra vez.
    /// </para>
    /// </remarks>
    private static async Task<FileStream?> OpenVerifiedAsync(string installer, string sha256, CancellationToken cancel)
    {
        if (!File.Exists(installer)) return null;

        var file = new FileStream(installer, FileMode.Open, FileAccess.Read, FileShare.Read,
                                  bufferSize: 1 << 16, useAsync: true);
        try
        {
            byte[] hash = await SHA256.HashDataAsync(file, cancel);
            if (string.Equals(Convert.ToHexStringLower(hash), sha256, StringComparison.Ordinal))
                return file;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }

        await file.DisposeAsync();
        return null;
    }

    // Lo que devuelve ShellExecute cuando se rechaza el permiso de administrador.
    private const int ErrorCancelled = 1223;

    // Lo que devuelve Inno Setup cuando no llega a empezar porque alguien ha dicho que no: el
    // permiso de administrador incluido.
    private const int SetupCancelled = 2;

    // Lo que se supiera antes de buscar sigue valiendo: si ya se sabía de una versión nueva, no buscar
    // bien esta vez no la hace desaparecer.
    private void Fail(UpdateState previous, UpdateFailure failure, string detail) =>
        Set(previous with { Failure = failure, Detail = detail });

    private CancellationTokenSource Begin()
    {
        var work = new CancellationTokenSource();
        _work = work;
        return work;
    }

    private void End(CancellationTokenSource work)
    {
        if (ReferenceEquals(_work, work)) _work = null;
    }

    private void Set(UpdateState state)
    {
        State = state;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _first?.Dispose();
        _daily.Stop();
        _work?.Cancel();
    }

    private static HttpClient CreateClient()
    {
        // Sin plazo general: la descarga es de decenas de megas y cada petición lleva el suyo, o
        // ninguno si es la descarga, que se para cancelándola.
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        // GitHub rechaza las llamadas al API que no dicen quién las hace.
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VibeScreen", BuildInfo.Version));
        return http;
    }

    /// <summary>
    /// De dónde se lee la última release. En Debug se puede apuntar a otro sitio para probar la
    /// actualización entera sin publicar nada: un servidor local con un JSON con la misma forma.
    /// </summary>
    private static Uri Feed
    {
        get
        {
#if DEBUG
            if (Environment.GetEnvironmentVariable("VIBESCREEN_UPDATE_FEED") is { Length: > 0 } feed)
                return new Uri(feed);
#endif
            return ReleaseFeed.Latest;
        }
    }

    /// <summary>
    /// Solo por HTTPS. El hash protege el fichero, pero viene en la misma respuesta que el enlace, y
    /// las dos cosas tienen que llegar por un canal que nadie pueda tocar por el camino.
    /// </summary>
    private static bool Trusted(Uri download)
    {
#if DEBUG
        if (download.IsLoopback) return true;
#endif
        return download.Scheme == Uri.UriSchemeHttps;
    }

    private static AppVersion? ReadCurrent()
    {
#if DEBUG
        // Para probar sin tener que instalar una versión vieja: hacerse pasar por ella.
        if (AppVersion.TryParse(Environment.GetEnvironmentVariable("VIBESCREEN_FAKE_VERSION"), out var fake))
            return fake;
#endif
        return AppVersion.TryParse(BuildInfo.Version, out var version) ? version : null;
    }
}
