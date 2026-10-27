namespace VibeScreen.Protocol.Display;

/// <summary>Driver de la revisión A: Turing Smart Screen 3.5" y las UsbPCMonitor.</summary>
public sealed class RevADisplayDriver : IDisplayDriver
{
    /// <summary>
    /// Tamaño de trozo de escritura. La Fase 0 midió que da igual entre 512 B y 38 KB
    /// (menos de un 2% de diferencia), así que se elige por comodidad de búferes, no por
    /// rendimiento. Se mantiene configurable por si otro firmware sí es sensible.
    /// </summary>
    public const int DefaultChunkSize = 4096;

    private readonly IDisplayTransport _transport;
    private readonly int _chunkSize;
    private bool _initialized;
    private bool _screenOn = true;
    private int? _brightness;

    public RevADisplayDriver(IDisplayTransport transport, DisplayCapabilities capabilities, int chunkSize = DefaultChunkSize)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);

        // El panel pinta los bytes tal cual le llegan: con otro formato no fallaría, saldría con los
        // colores cambiados, y eso no lo delata nada.
        if (capabilities.PixelFormat != PixelFormat.Rgb565LittleEndian)
            throw new ArgumentException(
                $"La revisión A solo entiende {PixelFormat.Rgb565LittleEndian}, no {capabilities.PixelFormat}.",
                nameof(capabilities));

        _transport = transport;
        Capabilities = capabilities;
        _chunkSize = chunkSize;
    }

    public DisplayCapabilities Capabilities { get; }
    public Orientation Orientation { get; private set; } = Orientation.Portrait;

    public int Width => Orientation.IsRotated() ? Capabilities.NativeHeight : Capabilities.NativeWidth;
    public int Height => Orientation.IsRotated() ? Capabilities.NativeWidth : Capabilities.NativeHeight;

    /// <summary>
    /// Abre el canal y fija la orientación y el encendido de forma explícita.
    /// </summary>
    /// <remarks>
    /// Los dos son estado persistente del panel: sobreviven a que el proceso anterior termine.
    /// Si no se fija la orientación, los rectángulos se interpretan en la que dejara el último
    /// programa y el contenido sale rotado o recortado. Si no se fija el encendido, se pinta
    /// sobre una pantalla que otro programa dejó apagada y no se ve nada.
    /// </remarks>
    public async ValueTask InitializeAsync(Orientation orientation, bool screenOn = true, CancellationToken cancellationToken = default)
    {
        if (!_transport.IsOpen)
            await _transport.OpenAsync(cancellationToken).ConfigureAwait(false);

        await SetOrientationAsync(orientation, cancellationToken).ConfigureAwait(false);
        await SetScreenPowerAsync(screenOn, cancellationToken).ConfigureAwait(false);
        _initialized = true;
    }

    public async ValueTask SetOrientationAsync(Orientation orientation, CancellationToken cancellationToken = default)
    {
        var frame = RevAProtocol.SetOrientation(orientation, Capabilities.NativeWidth, Capabilities.NativeHeight);
        await _transport.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await _transport.FlushAsync(cancellationToken).ConfigureAwait(false);
        Orientation = orientation;
    }

    /// <summary>Fija el brillo, y lo recuerda para poder devolvérselo al panel cuando se encienda.</summary>
    /// <remarks>
    /// Con la pantalla apagada solo se apunta. Apagada no hay brillo que ver, y el comando se manda
    /// igualmente al encender, así que mandarlo ahora no aporta nada y sí puede despertar la
    /// retroiluminación de una pantalla que se pidió apagada.
    /// </remarks>
    public async ValueTask SetBrightnessAsync(int percent, CancellationToken cancellationToken = default)
    {
        _brightness = RevAProtocol.CheckedBrightness(percent);
        if (!_screenOn) return;

        await _transport.WriteAsync(RevAProtocol.SetBrightness(percent), cancellationToken).ConfigureAwait(false);
        await _transport.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Enciende o apaga la pantalla, dejándola siempre al brillo que se pidió por última vez.</summary>
    /// <remarks>
    /// El encendido no conserva el brillo: el panel vuelve con el suyo de fábrica y la pantalla
    /// aparecía a tope aunque la interfaz siguiera marcando otra cosa. Por eso el brillo se repite
    /// detrás del encendido, y no delante, que es donde se lo llevaría por delante.
    /// </remarks>
    public async ValueTask SetScreenPowerAsync(bool screenOn, CancellationToken cancellationToken = default)
    {
        var cmd = RevAProtocol.Command(screenOn ? RevACommand.ScreenOn : RevACommand.ScreenOff);
        await _transport.WriteAsync(cmd, cancellationToken).ConfigureAwait(false);
        await _transport.FlushAsync(cancellationToken).ConfigureAwait(false);
        _screenOn = screenOn;

        if (screenOn && _brightness is { } percent)
            await SetBrightnessAsync(percent, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Limpia la pantalla.
    /// </summary>
    /// <remarks>
    /// El firmware de la revisión A solo limpia bien en vertical, así que hay que pasar a
    /// vertical, limpiar y restaurar. Es un defecto conocido del hardware, no del driver.
    /// </remarks>
    public async ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        var previous = Orientation;
        if (previous != Orientation.Portrait)
            await SetOrientationAsync(Orientation.Portrait, cancellationToken).ConfigureAwait(false);

        try
        {
            await _transport.WriteAsync(RevAProtocol.Command(RevACommand.Clear), cancellationToken).ConfigureAwait(false);
            await _transport.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Aunque el Clear falle: si no, el driver se queda creyéndose vertical y a partir de ahí
            // todo blit apaisado se sale de un panel que, para él, mide 320x480.
            if (previous != Orientation.Portrait)
                await SetOrientationAsync(previous, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask BlitAsync(ReadOnlyMemory<byte> pixels, PixelRect rect, CancellationToken cancellationToken = default)
    {
        if (!_initialized)
            throw new InvalidOperationException($"Llama a {nameof(InitializeAsync)} antes de pintar.");
        if (rect.IsEmpty)
            throw new ArgumentException("El rectángulo está vacío.", nameof(rect));
        if (rect.X < 0 || rect.Y < 0 || rect.Right >= Width || rect.Bottom >= Height)
            throw new ArgumentOutOfRangeException(nameof(rect), rect,
                $"El rectángulo se sale del panel ({Width}x{Height} en {Orientation}).");

        int expected = Capabilities.ByteCount(rect);
        if (pixels.Length != expected)
            throw new ArgumentException(
                $"Se esperaban {expected} bytes para {rect} y llegaron {pixels.Length}.", nameof(pixels));

        await _transport.WriteAsync(RevAProtocol.DisplayBitmapHeader(rect), cancellationToken).ConfigureAwait(false);

        for (int offset = 0; offset < pixels.Length; offset += _chunkSize)
        {
            int length = Math.Min(_chunkSize, pixels.Length - offset);
            await _transport.WriteAsync(pixels.Slice(offset, length), cancellationToken).ConfigureAwait(false);
        }

        await _transport.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _transport.CloseAsync().ConfigureAwait(false);
        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}
