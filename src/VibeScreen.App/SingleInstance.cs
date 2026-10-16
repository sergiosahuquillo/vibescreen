using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace VibeScreen.App;

/// <summary>
/// Una sola ventana por usuario. La segunda vez que se abre la aplicación, avisa a la que ya está para
/// que se ponga delante y termina.
/// </summary>
/// <remarks>
/// <para>
/// Sin esto, pinchar el acceso directo con la aplicación escondida en la bandeja arrancaba un segundo
/// proceso: otro icono en la bandeja, otra conexión con el servicio y dos ventanas guardando los mismos
/// ajustes, la última pisando a la primera.
/// </para>
/// <para>
/// Quién es la primera lo decide un mutex con nombre, y el aviso viaja por una tubería con nombre y no
/// por un evento: los eventos con nombre no existen en Linux, y la tubería es el mismo código en los dos
/// sistemas, igual que la del servicio. Los dos llevan el usuario y la sesión en el nombre, porque las
/// tuberías de Windows son de toda la máquina y dos usuarios con la sesión abierta tienen derecho a su
/// ventana cada uno.
/// </para>
/// </remarks>
internal sealed class SingleInstance : IDisposable
{
    /// <summary>
    /// Lo pasa <c>Restart</c> al proceso que lo sustituye. Ese proceso arranca cuando el viejo aún no
    /// ha terminado, así que con él espera a quedarse solo en vez de despertar al que se está yendo.
    /// </summary>
    public const string RestartArgument = "--restart";

    // Lo que tarda de sobra el proceso viejo en cerrar la ventana, guardar los ajustes y salir.
    private static readonly TimeSpan RestartWait = TimeSpan.FromSeconds(15);

    private static readonly string Name = $"VibeScreen.App.{Environment.UserName}.{SessionId()}";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
        _ = ListenAsync(_stop.Token);
    }

    /// <summary>
    /// Alguien ha abierto la aplicación otra vez. Llega desde un hilo cualquiera.
    /// </summary>
    public event Action? Woken;

    /// <summary>
    /// Se queda con el sitio si está libre. Null si ya hay otra ventana: entonces toca
    /// <see cref="WakeExisting"/> y salir.
    /// </summary>
    /// <param name="restarting">Viene de un <see cref="RestartArgument"/>: esperar a que el otro se vaya.</param>
    public static SingleInstance? Acquire(bool restarting)
    {
        var mutex = new Mutex(initiallyOwned: false, Name);
        bool owned;
        try
        {
            owned = mutex.WaitOne(restarting ? RestartWait : TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // La anterior se cayó sin soltarlo. El mutex ya es nuestro, que es justo lo que hace falta.
            owned = true;
        }

        if (owned) return new SingleInstance(mutex);

        mutex.Dispose();
        return null;
    }

    /// <summary>Avisa a la ventana que ya está para que se ponga delante.</summary>
    public static void WakeExisting()
    {
        // A este proceso lo ha lanzado quien tiene el primer plano —el Explorador, al pinchar el acceso
        // directo— y por eso puede ponerse delante. El otro no: Windows no deja que un proceso en
        // segundo plano le robe el foco al usuario, y sin esto la ventana solo parpadearía en la barra
        // de tareas. Así que se le cede el permiso antes de despertarlo.
        if (OperatingSystem.IsWindows()) _ = AllowSetForegroundWindow(AnyProcess);

        try
        {
            using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(2000);
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            // La otra todavía está arrancando, o se ha colgado. Abrir una segunda no arregla ninguna de
            // las dos cosas, así que no hay más que hacer.
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Basta con que alguien se conecte: no hay nada más que decir.
                await using var pipe = new NamedPipeServerStream(Name, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                Woken?.Invoke();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // Una conexión que se corta a medias no es motivo para dejar de escuchar las siguientes.
            }
        }
    }

    /// <summary>
    /// Suelta el sitio. Desde el hilo que lo cogió: un mutex es de quien lo coge, y soltarlo desde otro
    /// tira una excepción.
    /// </summary>
    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    private static int SessionId()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return process.SessionId;
    }

    private const int AnyProcess = -1;

    // DllImport y no LibraryImport: este exige compilar toda la ventana con código no seguro, y una
    // firma de dos enteros no necesita que nadie le genere el cálculo de los parámetros.
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int AllowSetForegroundWindow(int processId);
}
