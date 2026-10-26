using SkiaSharp;
using VibeScreen.Core.Layout;
using VibeScreen.Core.Sensors;
using VibeScreen.Protocol.Display;

namespace VibeScreen.Core.Rendering;

/// <summary>Un widget cuyo aspecto ha cambiado y hay que enviar al panel.</summary>
public readonly record struct DirtyWidget(string WidgetId, PixelRect Rect, int Priority);

/// <summary>
/// La última imagen de fondo que se decodificó, para no volver a hacerlo en cada reconstrucción.
/// </summary>
/// <remarks>
/// El editor rehace el renderizador con cada propiedad que toca el usuario —incluso con cada tecla
/// en una casilla de texto— y decodificar una foto de varios megapíxeles cada vez lo dejaría a
/// tirones. Se guarda solo la última, que es la del tema que se está editando, y se descarta si el
/// fichero cambia de fecha: así, quien retoca la imagen en otro programa la ve actualizada.
/// </remarks>
internal static class BackgroundImages
{
    private static readonly Lock Gate = new();
    private static string? _path;
    private static DateTime _stamp;
    private static SKBitmap? _bitmap;

    /// <summary>
    /// Usa la imagen de ese fichero, si está, sin soltarla fuera del candado.
    /// </summary>
    /// <remarks>
    /// Devolver la referencia y dejar que quien la recibe la use después sería una trampa: otro hilo
    /// que pidiera una imagen distinta dispondría ese mismo mapa de bits mientras el primero dibuja
    /// con él, y eso no es una excepción que se pueda atrapar, es una violación de acceso. Hoy no
    /// hay dos hilos que puedan hacerlo, pero la clase está escrita como caché con candado y tiene
    /// que serlo de verdad, no solo al mutar.
    /// </remarks>
    public static void Use(string path, Action<SKBitmap> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);

        DateTime stamp;
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return;
            stamp = file.LastWriteTimeUtc;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return;
        }

        lock (Gate)
        {
            if (_bitmap is null || _path != path || _stamp != stamp)
            {
                SKBitmap? decoded;
                try
                {
                    decoded = SKBitmap.Decode(path);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    // Ancho a propósito. Antes solo se atrapaban las dos de disco, y una imagen corrupta
                    // o de un formato que Skia no entiende tira otra cosa desde aquí, que es el
                    // constructor del renderizador: un tema traído de otra máquina con un fondo roto
                    // tiene que poder abrirse para arreglarlo, y de avisar se encarga LayoutInspector.
                    decoded = null;
                }

                _bitmap?.Dispose();
                _bitmap = decoded;
                _path = path;
                _stamp = stamp;
            }

            if (_bitmap is not null) draw(_bitmap);
        }
    }
}

/// <summary>
/// Las tipografías que se le han pedido a Skia, guardadas.
/// </summary>
/// <remarks>
/// <para>
/// El editor rehace el renderizador con cada propiedad que toca el usuario —con cada tecla de una
/// casilla y con cada movimiento del ratón sobre el selector de color—, y cada widget que escribe
/// pedía la suya otra vez. Resolver una familia es una consulta al gestor de fuentes del sistema, así
/// que un tema de diez textos hacía diez consultas por movimiento del ratón.
/// </para>
/// <para>
/// Un <see cref="SKTypeface"/> es inmutable y se puede compartir, así que se pide una vez por
/// combinación de familia, peso e inclinación y no se suelta: las claves posibles son las fuentes
/// instaladas por cuatro, y quien las usa es el proceso entero mientras dure.
/// </para>
/// </remarks>
internal static class Typefaces
{
    private static readonly Lock Gate = new();

    private static readonly Dictionary<(string Family, SKFontStyleWeight Weight, SKFontStyleSlant Slant), SKTypeface> Cache = [];

    /// <summary>La tipografía de esa familia y estilo, o la predeterminada si la familia no está.</summary>
    public static SKTypeface Of(string? family, bool bold, bool italic)
    {
        var weight = bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant = italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;

        // Para Skia, null y la cadena vacía son lo mismo: la familia predeterminada.
        var key = (family ?? "", weight, slant);

        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var known)) return known;

            var typeface = SKTypeface.FromFamilyName(family, weight, SKFontStyleWidth.Normal, slant) ?? SKTypeface.Default;
            Cache[key] = typeface;
            return typeface;
        }
    }
}

/// <summary>
/// Pinta un layout sobre <see cref="PanelSurface"/> y dice qué widgets han cambiado.
/// </summary>
/// <remarks>
/// <para>
/// Un widget solo se marca sucio si lo que muestra ha cambiado de verdad: una temperatura que sigue en
/// 39.0 °C no gasta enlace aunque se haya vuelto a leer, y una barra cuyo relleno no llega a moverse un
/// píxel tampoco.
/// </para>
/// <para>
/// El fondo se pinta una vez en una capa aparte. Al repintar un widget se restaura su caja desde esa
/// capa antes de dibujar, así que no quedan restos del valor anterior; el mismo mecanismo servirá para
/// fondos con imagen.
/// </para>
/// </remarks>
public sealed class LayoutRenderer : IDisposable
{
    private readonly SKBitmap _background;
    private readonly List<WidgetState> _widgets;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, float?> _lastRead = new(StringComparer.Ordinal);

    /// <summary>Los widgets a los que les toca en este ciclo, y de qué sensores dependen.</summary>
    /// <remarks>
    /// Se reutilizan las dos: <see cref="Update"/> corre veinte veces por segundo, y antes reservaba
    /// una lista y una cadena de LINQ en cada pasada aunque no venciera ninguno.
    /// </remarks>
    private readonly List<WidgetState> _due = [];
    private readonly HashSet<string> _dueSensors = new(StringComparer.Ordinal);

    /// <param name="clock">
    /// De dónde sale la hora de los relojes del tema. Por omisión, la de la máquina; las pruebas
    /// pasan la suya para poder plantarse en un instante concreto.
    /// </param>
    /// <param name="language">
    /// En qué idioma escriben los relojes los nombres de los días y los meses. Por omisión, español,
    /// que es como han salido siempre.
    /// </param>
    public LayoutRenderer(LayoutDocument layout, TimeProvider? clock = null, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(layout);

        _clock = clock ?? TimeProvider.System;
        string names = language ?? ClockFormat.DefaultLanguage;

        // Se monta en variables y se asigna al final: si algo de esto lanza —un color a medio
        // teclear basta— los campos nunca llegan a existir y nadie podría llamar a Dispose, así que
        // se quedarían dos mapas de bits del tamaño del panel y los pinceles ya creados en manos del
        // finalizador. Y el editor rehace esto con cada tecla, o sea que pasa a menudo.
        var surface = new PanelSurface(layout.Display.Width, layout.Display.Height);
        SKBitmap? background = null;
        List<WidgetState> widgets = [];

        try
        {
            background = new SKBitmap(new SKImageInfo(surface.Width, surface.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(background))
            {
                switch (layout.Background)
                {
                    case ColorBackground color:
                        canvas.Clear(SKColor.Parse(color.Color));
                        break;
                    case ImageBackground image:
                        canvas.Clear(SKColor.Parse(image.Color));
                        DrawBackground(canvas, image, layout);
                        break;
                    default:
                        throw new NotSupportedException($"Fondo sin renderizador: {layout.Background.GetType().Name}.");
                }
            }

            foreach (var widget in layout.Widgets)
                widgets.Add(widget switch
                {
                    TextWidget text => new TextState(text),
                    ClockWidget clock2 => new ClockState(clock2, names),
                    BarWidget bar => new BarState(bar),
                    GaugeWidget gauge => new GaugeState(gauge),
                    ChartWidget chart => new ChartState(chart),
                    ImageWidget image => new ImageState(image, layout),
                    _ => throw new NotSupportedException($"Widget «{widget.Id}»: tipo sin renderizador."),
                });
        }
        catch
        {
            foreach (var state in widgets) state.Dispose();
            background?.Dispose();
            surface.Dispose();
            throw;
        }

        Surface = surface;
        _background = background;
        _widgets = widgets;

        SensorIds = _widgets.Select(w => w.Widget.SensorId).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
    }

    public PanelSurface Surface { get; }

    /// <summary>Sensores de los que depende el layout.</summary>
    public IReadOnlyList<string> SensorIds { get; }

    /// <summary>
    /// Lo que dio cada sensor la última vez que este layout lo leyó para pintar. Un sensor que aún
    /// no se ha leído no está; uno que se leyó y no dio lectura está, con null.
    /// </summary>
    /// <remarks>
    /// Existe para que nadie más tenga que muestrear el hardware por su cuenta. Los sensores que se
    /// calculan por diferencia entre dos lecturas —las velocidades de red y de disco, los relojes
    /// efectivos— miden el hueco entre una actualización y la siguiente, así que un segundo
    /// observador no solo ve otro instante: le parte la ventana al primero y los dos acaban
    /// promediando trozos distintos del mismo segundo.
    ///
    /// Si dos widgets comparten sensor con refrescos distintos, queda el del que refrescó más tarde,
    /// que es el que está en la pantalla.
    /// </remarks>
    public IReadOnlyDictionary<string, float?> LastRead => _lastRead;

    /// <summary>Texto que muestra ahora un widget, o null si no escribe nada. Para diagnóstico y pruebas.</summary>
    public string? DisplayedText(string widgetId) =>
        _widgets.FirstOrDefault(w => w.Widget.Id == widgetId) switch
        {
            LabelState label => label.Displayed,
            GaugeState gauge => gauge.Displayed,
            _ => null,
        };

    /// <summary>Pinta el fondo y todos los widgets sin mirar intervalos, para enviar el lienzo entero.</summary>
    public void RenderAll(ISensorProvider sensors, TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(sensors);

        var time = _clock.GetLocalNow();
        Surface.Canvas.DrawBitmap(_background, 0, 0);
        sensors.Update(SensorIds);
        foreach (var state in _widgets)
        {
            state.Update(sensors, time);
            Repaint(state);
            state.NextRefresh = now + TimeSpan.FromMilliseconds(state.Widget.RefreshMs);
        }

        Record(_widgets);
    }

    /// <summary>Refresca los widgets a los que les toca y devuelve los que han cambiado.</summary>
    public IReadOnlyList<DirtyWidget> Update(ISensorProvider sensors, TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(sensors);

        _due.Clear();
        foreach (var state in _widgets)
            if (now >= state.NextRefresh) _due.Add(state);

        if (_due.Count == 0) return [];

        _dueSensors.Clear();
        foreach (var state in _due)
            if (state.Widget.SensorId is { } id) _dueSensors.Add(id);

        sensors.Update(_dueSensors);

        var time = _clock.GetLocalNow();
        List<DirtyWidget>? dirty = null;
        foreach (var state in _due)
        {
            state.NextRefresh = now + TimeSpan.FromMilliseconds(state.Widget.RefreshMs);
            if (!state.Update(sensors, time)) continue;

            Repaint(state);

            // Sin lista mientras no haya nada sucio, que es lo normal en un ciclo.
            (dirty ??= []).Add(new DirtyWidget(state.Widget.Id, state.Widget.Rect, state.Widget.Priority));
        }

        // Fuera del bucle a propósito: ahí arriba se sale con «continue» en cuanto un widget no
        // cambia de aspecto, y la lectura hay que apuntarla igual. Que el número no se mueva no
        // quiere decir que no se haya leído.
        Record(_due);
        return dirty ?? (IReadOnlyList<DirtyWidget>)[];
    }

    /// <summary>Apunta lo que acaban de leer estos widgets, para quien no quiera muestrear otra vez.</summary>
    private void Record(IEnumerable<WidgetState> states)
    {
        foreach (var state in states)
            if (state.Widget.SensorId is { } id) _lastRead[id] = state.LastValue;
    }

    /// <summary>
    /// Pinta la imagen de fondo sobre el color de respaldo. Si no se puede leer, se queda el color:
    /// un tema traído de otra máquina tiene que poder abrirse para arreglarlo, y de avisar se encarga
    /// <see cref="LayoutInspector"/>.
    /// </summary>
    private static void DrawBackground(SKCanvas canvas, ImageBackground image, LayoutDocument layout) =>
        BackgroundImages.Use(layout.Resolve(image.Path), bitmap =>
            canvas.DrawBitmap(bitmap,
                Placement(image.Fit, bitmap.Width, bitmap.Height, layout.Display.Width, layout.Display.Height)));

    /// <summary>Dónde cae la imagen en el panel según el ajuste elegido. Lo que se salga, el lienzo lo recorta.</summary>
    public static SKRect Placement(ImageFit fit, float imageWidth, float imageHeight, float panelWidth, float panelHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0) return SKRect.Create(0, 0, panelWidth, panelHeight);

        float scale = fit switch
        {
            ImageFit.Cover => Math.Max(panelWidth / imageWidth, panelHeight / imageHeight),
            ImageFit.Contain => Math.Min(panelWidth / imageWidth, panelHeight / imageHeight),
            ImageFit.Center => 1,
            _ => 0, // Estirar no conserva la proporción, así que no tiene una escala única.
        };

        if (scale == 0) return SKRect.Create(0, 0, panelWidth, panelHeight);

        float width = imageWidth * scale;
        float height = imageHeight * scale;
        return SKRect.Create((panelWidth - width) / 2, (panelHeight - height) / 2, width, height);
    }

    /// <summary>Restaura el fondo bajo el widget y lo vuelve a dibujar.</summary>
    private void Repaint(WidgetState state)
    {
        var rect = state.Widget.Rect;
        var box = SKRect.Create(rect.X, rect.Y, rect.Width, rect.Height);
        var canvas = Surface.Canvas;

        canvas.Save();
        canvas.ClipRect(box);
        canvas.DrawBitmap(_background, box, box);
        state.Draw(canvas, box);
        canvas.Restore();
    }

    public void Dispose()
    {
        foreach (var state in _widgets) state.Dispose();
        _background.Dispose();
        Surface.Dispose();
    }

    /// <summary>
    /// Lo que el renderizador guarda de un widget entre fotogramas: cuándo le toca refrescarse y lo
    /// último que se dibujó, para saber si hace falta volver a enviarlo.
    /// </summary>
    private abstract class WidgetState(LayoutWidget widget) : IDisposable
    {
        public LayoutWidget Widget { get; } = widget;

        public TimeSpan NextRefresh { get; set; }

        /// <summary>Relee de dónde sea que salga el valor. Devuelve true si lo que se ve ha cambiado.</summary>
        /// <remarks>
        /// El sensor se lee aquí, una sola vez, y se reparte. El arco ya lo necesitaba dos veces —para
        /// la aguja y para el número que lleva dentro— y con el color por valor serían tres consultas
        /// para un mismo fotograma, cada una con su lectura recién reservada.
        /// </remarks>
        public bool Update(ISensorProvider sensors, DateTimeOffset time)
        {
            var reading = Widget.SensorId is { } id ? sensors.Read(id) : null;
            LastValue = reading?.Value;
            return Update(reading, time);
        }

        /// <summary>Lo que dio el sensor en el último refresco de este widget. Sin sensor, siempre null.</summary>
        public float? LastValue { get; private set; }

        /// <param name="reading">La lectura del sensor del widget, o null si no tiene o no la hay.</param>
        protected abstract bool Update(SensorReading? reading, DateTimeOffset time);

        public abstract void Draw(SKCanvas canvas, SKRect box);

        public virtual void Dispose() { }
    }

    /// <summary>Con qué se pinta el valor: el color, y si lo manda la regla o es el de siempre.</summary>
    private readonly record struct Ink(SKColor Color, bool Ruled);

    /// <summary>
    /// Los colores de la regla por valor, ya convertidos, y cuál toca para una lectura.
    /// </summary>
    /// <remarks>
    /// Se convierten una vez, como todo lo demás aquí: lo que cambia por fotograma es cuál se usa, no
    /// lo que cuesta averiguarlo. Sin regla se queda vacía y devuelve siempre el color fijo del
    /// widget, así que el reloj y los temas de toda la vida pasan por este mismo camino sin un solo
    /// «if» de tipo. Y sin lectura también: el color fijo es lo que se ve cuando no hay dato.
    /// </remarks>
    private sealed class Tints
    {
        private readonly ColorLevels? _levels;
        private readonly SKColor[] _colors = [];

        public Tints(ColorLevels? levels)
        {
            if (levels is null) return;

            _levels = levels;
            _colors = [SKColor.Parse(levels.Low), SKColor.Parse(levels.Medium), SKColor.Parse(levels.High)];
        }

        public Ink Of(double? value, SKColor plain) =>
            _levels is { } levels && value is { } shown
                ? new Ink(_colors[ValueColors.Band(levels, shown)], Ruled: true)
                : new Ink(plain, Ruled: false);
    }

    /// <summary>
    /// Con qué se escribe y dónde cae el renglón. Está aparte del widget porque escriben dos cosas
    /// distintas: los widgets de texto y el valor que va dentro de un arco.
    /// </summary>
    private sealed class TextPen : IDisposable
    {
        private readonly SKFont _font;
        private readonly SKPaint _paint;

        public TextPen(string? family, float size, bool bold, bool italic, string color)
        {
            _font = new SKFont(Typefaces.Of(family, bold, italic), size) { Edging = SKFontEdging.Antialias, Subpixel = true };
            _paint = new SKPaint { Color = SKColor.Parse(color), IsAntialias = true };
        }

        /// <summary>La tinta. Se cambia sin rehacer la letra: del tramo solo depende el color.</summary>
        public SKColor Ink
        {
            get => _paint.Color;
            set => _paint.Color = value;
        }

        /// <summary>Escribe dentro de la caja, alineado a lo ancho y siempre centrado a lo alto.</summary>
        public void Draw(SKCanvas canvas, SKRect box, string text, TextAlignment align)
        {
            float width = _font.MeasureText(text, _paint);
            float x = align switch
            {
                TextAlignment.Center => box.Left + (box.Width - width) / 2,
                TextAlignment.Right => box.Right - width,
                _ => box.Left,
            };

            // Centrado vertical con las métricas de la fuente y no con las del texto concreto: si no, el
            // número subiría y bajaría según lleve o no caracteres con rabo.
            var metrics = _font.Metrics;
            float baseline = box.Top + (box.Height - (metrics.Descent - metrics.Ascent)) / 2 - metrics.Ascent;

            canvas.DrawText(text, x, baseline, SKTextAlign.Left, _font, _paint);
        }

        public void Dispose()
        {
            _paint.Dispose();
            _font.Dispose();

            // La tipografía no: es compartida y la guarda Typefaces.
        }
    }

    /// <summary>
    /// Lo común a los widgets que escriben: la tipografía, el color y dónde cae el renglón. Lo único
    /// que cambia de uno a otro es de dónde sale el texto.
    /// </summary>
    private abstract class LabelState : WidgetState
    {
        private readonly LabelWidget _widget;
        private readonly Tints _tints;
        private readonly TextPen _pen;
        private readonly SKColor _plain;
        private SKColor _ink;

        protected LabelState(LabelWidget widget) : base(widget)
        {
            // Lo primero de todo: un color de la regla a medio teclear lanza aquí, y así no deja atrás
            // ni una letra ni un pincel que nadie va a poder soltar.
            _tints = new Tints(widget.ValueLevels);
            _widget = widget;
            _pen = new TextPen(widget.Font, widget.Size, widget.Bold, widget.Italic, widget.Color);
            _plain = _ink = _pen.Ink;
        }

        public string? Displayed { get; private set; }

        /// <summary>Lo que toca escribir ahora mismo.</summary>
        protected abstract string Compose(SensorReading? reading, DateTimeOffset time);

        /// <summary>El valor con el que se elige el color. El reloj no tiene ninguno.</summary>
        protected virtual double? Level(SensorReading? reading) => null;

        protected override bool Update(SensorReading? reading, DateTimeOffset time)
        {
            string text = Compose(reading, time);
            var ink = _tints.Of(Level(reading), _plain);

            // El color entra en la comparación porque no siempre viaja con el texto: con cero
            // decimales y un corte en 40, un 39,6 y un 40,4 escriben los dos «40 °C» y lo único que
            // cambia es el color. Mirando solo el texto, el panel se quedaría verde a 40,4.
            if (text == Displayed && ink.Color == _ink) return false;

            Displayed = text;
            _ink = ink.Color;
            _pen.Ink = _ink;
            return true;
        }

        public override void Draw(SKCanvas canvas, SKRect box) =>
            _pen.Draw(canvas, box, Displayed ?? "", _widget.Align);

        public override void Dispose()
        {
            _pen.Dispose();
            base.Dispose();
        }
    }

    private sealed class TextState(TextWidget widget) : LabelState(widget)
    {
        protected override string Compose(SensorReading? reading, DateTimeOffset time) =>
            TextComposer.Compose(widget, reading);

        protected override double? Level(SensorReading? reading) => SensorUnits.Shown(reading, widget.Unit);
    }

    private sealed class ClockState(ClockWidget widget, string language) : LabelState(widget)
    {
        protected override string Compose(SensorReading? reading, DateTimeOffset time) =>
            ClockFormat.Compose(widget.Format, time, language);
    }

    /// <summary>Base de los widgets que dibujan el valor: lectura, conversión de unidad y recorrido.</summary>
    private abstract class GraphState : WidgetState
    {
        private readonly GraphWidget _widget;
        private readonly Tints _tints;
        private readonly SKColor _plainForeground;
        private readonly SKColor _plainOutline;

        protected GraphState(GraphWidget widget) : base(widget)
        {
            // Lo primero de todo: un color de la regla a medio teclear lanza aquí, y así no deja
            // atrás ni un pincel que nadie va a poder soltar.
            _tints = new Tints(widget.Levels);
            _widget = widget;
            Foreground = new SKPaint { Color = SKColor.Parse(widget.Color), IsAntialias = true };
            Track = widget.Background is { } background
                ? new SKPaint { Color = SKColor.Parse(background), IsAntialias = true }
                : null;
            Outline = widget.Outline is { } border && widget.OutlineWidth > 0
                ? new SKPaint
                {
                    Color = SKColor.Parse(border),
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = widget.OutlineWidth,
                }
                : null;

            _plainForeground = Foreground.Color;
            _plainOutline = Outline?.Color ?? default;
        }

        protected SKPaint Foreground { get; }

        /// <summary>Pincel de la parte vacía, o null para dejar ver el fondo del layout.</summary>
        protected SKPaint? Track { get; }

        /// <summary>Pincel del contorno, o null si la gráfica no lleva.</summary>
        protected SKPaint? Outline { get; }

        /// <summary>
        /// La caja metida hacia dentro medio trazo. Un trazo va centrado en la línea que se le pide,
        /// así que dibujarlo sobre el borde dejaría fuera la mitad y el recorte de la caja se la comería.
        /// </summary>
        protected static SKRect Inside(SKRect box, float width)
        {
            var inner = box;
            inner.Inflate(-width / 2, -width / 2);
            return inner;
        }

        /// <summary>La lectura ya escalada y con qué pintarla.</summary>
        protected readonly record struct Level(float? Fraction, Ink Ink);

        /// <summary>
        /// Dónde cae la lectura entre el mínimo y el máximo, de 0 a 1, y de qué color va.
        /// </summary>
        /// <remarks>
        /// El color sale del valor convertido y <b>no</b> de la fracción, que va recortada: con el
        /// máximo en 80 y un corte en 100, el dibujo ya está al tope pero el color todavía tiene que
        /// cambiar. Por lo mismo sigue saliendo del valor aunque el recorrido sea de longitud cero.
        /// </remarks>
        protected Level Read(SensorReading? reading)
        {
            double? shown = SensorUnits.Shown(reading, _widget.Unit);
            var ink = _tints.Of(shown, _plainForeground);
            if (shown is not { } value) return new Level(null, ink);

            float span = _widget.Max - _widget.Min;
            if (span == 0) return new Level(0, ink);
            return new Level(Math.Clamp((float)((value - _widget.Min) / span), 0, 1), ink);
        }

        /// <summary>
        /// Tiñe lo que dibuja el valor. El fondo nunca: es el hueco, y del mismo color que el relleno
        /// no se vería hasta dónde llega el valor.
        /// </summary>
        protected virtual void Apply(Ink ink)
        {
            Foreground.Color = ink.Color;

            // Sin regla que aplicar, el contorno recupera el suyo y no hereda el del relleno.
            if (Outline is { } outline) outline.Color = ink.Ruled ? ink.Color : _plainOutline;
        }

        public override void Dispose()
        {
            Foreground.Dispose();
            Track?.Dispose();
            Outline?.Dispose();
            base.Dispose();
        }
    }

    private sealed class BarState(BarWidget widget) : GraphState(widget)
    {
        private int _pixels = -1;
        private Ink _ink;

        protected override bool Update(SensorReading? reading, DateTimeOffset time)
        {
            // El widget solo está sucio si el relleno se mueve al menos un píxel —por debajo de eso el
            // panel recibiría un rectángulo idéntico al que ya tiene— o si cambia de color, que puede
            // pasar sin que el relleno se mueva: por encima del máximo la barra está llena igual.
            float length = widget.Direction is BarDirection.Left or BarDirection.Right ? widget.Rect.Width : widget.Rect.Height;
            var level = Read(reading);
            int pixels = (int)MathF.Round((level.Fraction ?? 0) * length);
            if (pixels == _pixels && level.Ink == _ink) return false;

            _pixels = pixels;
            _ink = level.Ink;
            Apply(_ink);
            return true;
        }

        public override void Draw(SKCanvas canvas, SKRect box)
        {
            // Con «using»: un SKRoundRect es un objeto nativo, esto se repinta mientras dure el proceso
            // y sin soltarlo la cuenta la pagaba el finalizador.
            using var round = new SKRoundRect(box, widget.Radius, widget.Radius);
            if (Track is { } track) canvas.DrawRoundRect(round, track);

            if (_pixels > 0)
            {
                var filled = widget.Direction switch
                {
                    BarDirection.Right => SKRect.Create(box.Left, box.Top, _pixels, box.Height),
                    BarDirection.Left => SKRect.Create(box.Right - _pixels, box.Top, _pixels, box.Height),
                    BarDirection.Up => SKRect.Create(box.Left, box.Bottom - _pixels, box.Width, _pixels),
                    _ => SKRect.Create(box.Left, box.Top, box.Width, _pixels),
                };

                // El relleno se recorta contra la caja redondeada para que herede sus esquinas.
                canvas.Save();
                canvas.ClipRoundRect(round, SKClipOperation.Intersect, antialias: true);
                canvas.DrawRect(filled, Foreground);
                canvas.Restore();
            }

            // El contorno va encima de todo: es el borde de la barra entera, esté llena o vacía.
            if (Outline is not { } outline) return;

            var edge = Inside(box, outline.StrokeWidth);
            if (edge.Width <= 0 || edge.Height <= 0) return;

            // Las esquinas se encogen con la caja, para que el borde quede paralelo al de la barra.
            float radius = Math.Max(0, widget.Radius - (outline.StrokeWidth / 2));
            using var border = new SKRoundRect(edge, radius, radius);
            canvas.DrawRoundRect(border, outline);
        }
    }

    private sealed class GaugeState : GraphState
    {
        private readonly GaugeWidget _widget;
        private readonly TextPen? _pen;

        /// <summary>Si el número de dentro va del color del arco, y por tanto también de su tramo.</summary>
        private readonly bool _inherits;
        private int _sweep = int.MinValue;
        private string _value = "";
        private Ink _ink;

        public GaugeState(GaugeWidget widget) : base(widget)
        {
            _widget = widget;
            Stroke(Foreground);
            if (Track is { } track) Stroke(track);

            // Sin color propio, el del arco: es lo que se quiere casi siempre y ahorra elegirlo dos veces.
            if (widget.Label is { } label)
                _pen = new TextPen(label.Font, label.Size, label.Bold, label.Italic, label.Color ?? widget.Color);

            _inherits = widget.Label is { Color: null };

            void Stroke(SKPaint paint)
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = widget.Thickness;
                paint.StrokeCap = widget.Round ? SKStrokeCap.Round : SKStrokeCap.Butt;
            }
        }

        /// <summary>Lo que hay escrito dentro del arco, o null si este no lleva valor.</summary>
        public string? Displayed => _pen is null ? null : _value;

        protected override bool Update(SensorReading? reading, DateTimeOffset time)
        {
            var level = Read(reading);

            // En décimas de grado: es lo que distingue dos dibujos en un arco del tamaño de un panel.
            int sweep = (int)MathF.Round((level.Fraction ?? 0) * _widget.SweepAngle * 10);
            string value = _pen is null ? "" : TextComposer.Compose(_widget, reading);

            // Se miran los tres y se guardan los tres. Cada uno cambia a su ritmo —el número mucho más
            // despacio que la aguja, y el color más despacio todavía—, así que salir en cuanto uno
            // coincide dejaría sin actualizar a los otros y el arco pintaría algo viejo.
            bool changed = sweep != _sweep || value != _value || level.Ink != _ink;
            _sweep = sweep;
            _value = value;
            _ink = level.Ink;
            if (changed) Apply(_ink);
            return changed;
        }

        /// <summary>El número de dentro sigue al arco, tramo incluido, salvo que tenga color propio.</summary>
        protected override void Apply(Ink ink)
        {
            base.Apply(ink);
            if (_inherits && _pen is not null) _pen.Ink = ink.Color;
        }

        public override void Draw(SKCanvas canvas, SKRect box)
        {
            DrawArc(canvas, box);

            // El valor, lo último: va en el hueco del arco, pero con una banda gruesa y una letra
            // grande pueden llegar a tocarse, y entonces lo que hay que poder leer es el número.
            // Centrado en la caja, que es también el centro de la circunferencia del arco.
            _pen?.Draw(canvas, box, _value, TextAlignment.Center);
        }

        private void DrawArc(SKCanvas canvas, SKRect box)
        {
            // El arco se dibuja sobre su línea central, así que hay que meterse medio grosor —y medio
            // contorno, si lo lleva— para que la banda entera quepa dentro de la caja.
            float margin = (_widget.Thickness + (Outline?.StrokeWidth ?? 0)) / 2;
            var oval = box;
            oval.Inflate(-margin, -margin);
            if (oval.Width <= 0 || oval.Height <= 0) return;

            if (Track is { } track) canvas.DrawArc(oval, _widget.StartAngle, _widget.SweepAngle, useCenter: false, track);
            if (_sweep != 0) canvas.DrawArc(oval, _widget.StartAngle, _sweep / 10f, useCenter: false, Foreground);

            if (Outline is not { } outline) return;

            // El contorno de un arco no es un rectángulo: se le pide a Skia la silueta del trazo del
            // recorrido entero y se dibuja su borde, así rodea la banda en vez de rellenarla.
            using var arc = new SKPath();
            arc.AddArc(oval, _widget.StartAngle, _widget.SweepAngle);

            using var band = new SKPath();
            if (Foreground.GetFillPath(arc, band)) canvas.DrawPath(band, outline);
        }

        public override void Dispose()
        {
            _pen?.Dispose();
            base.Dispose();
        }
    }

    /// <summary>
    /// Una imagen, fija o animada. Cada fotograma se escala a la caja la primera vez que se pinta, y a
    /// partir de ahí cada repintado es copiarlo.
    /// </summary>
    /// <remarks>
    /// Se escala al pintar y no al montar el renderizador porque la caja puede cambiar sin que se
    /// rehaga: el editor, al estirarla con el ratón, solo repinta, que rehacerlo en cada movimiento se
    /// llevaría el histórico de las gráficas. Los demás widgets dibujan con la caja que reciben y no se
    /// enteran; la imagen tiene que darse cuenta de que ya no mide lo mismo. Y fotograma a fotograma, no
    /// todos de golpe: durante un arrastre, cada movimiento escala solo el que se ve.
    /// </remarks>
    private sealed class ImageState : WidgetState
    {
        private readonly ImageWidget _widget;
        private readonly string _path;
        private readonly SKImage?[] _frames;

        /// <summary>Cuándo empieza cada fotograma dentro de una vuelta de la animación, en milisegundos.</summary>
        private readonly long[] _starts;

        private readonly long _loop;
        private int _shown = -1;

        /// <summary>El tamaño de caja para el que están escalados los fotogramas de <see cref="_frames"/>.</summary>
        private (int Width, int Height) _scaledFor;

        public ImageState(ImageWidget widget, LayoutDocument layout) : base(widget)
        {
            _widget = widget;
            _path = layout.Resolve(widget.Path);

            var starts = new List<long>();
            long loop = 0;
            WidgetImages.Use(_path, source =>
            {
                foreach (int duration in source.Durations)
                {
                    starts.Add(loop);
                    loop += duration;
                }
            });

            _frames = new SKImage?[starts.Count];
            _starts = [.. starts];
            _loop = loop;
        }

        protected override bool Update(SensorReading? reading, DateTimeOffset time)
        {
            int frame = _frames.Length > 1 && _loop > 0 ? FrameAt(time) : 0;
            if (frame == _shown) return false;

            _shown = frame;
            return true;
        }

        /// <summary>
        /// El fotograma que toca a esa hora.
        /// </summary>
        /// <remarks>
        /// Sale del reloj y no de contar refrescos, y por eso la animación va a su velocidad aunque el
        /// widget se mire más despacio: se salta fotogramas, no se ralentiza. Y contado desde una fecha
        /// fija y no desde que se montó el renderizador, para que no vuelva a empezar cada vez que el
        /// editor lo rehace —con cada tecla— y para que la vista previa vaya a la par que el panel.
        /// </remarks>
        private int FrameAt(DateTimeOffset time)
        {
            long at = ((time.ToUnixTimeMilliseconds() % _loop) + _loop) % _loop;
            int frame = 0;
            while (frame + 1 < _starts.Length && _starts[frame + 1] <= at) frame++;
            return frame;
        }

        public override void Draw(SKCanvas canvas, SKRect box)
        {
            if (_shown < 0 || _shown >= _frames.Length) return;

            var size = ((int)box.Width, (int)box.Height);
            if (size != _scaledFor)
            {
                Forget();
                _scaledFor = size;
            }

            if ((_frames[_shown] ??= Scale(_shown, size.Item1, size.Item2)) is { } frame)
                canvas.DrawImage(frame, box.Left, box.Top);
        }

        /// <summary>Ese fotograma a la medida de la caja, o null si no se puede o no se debe pintar.</summary>
        private SKImage? Scale(int index, int width, int height)
        {
            // Los fotogramas escalados son de este widget y no de la caché, así que también cuentan:
            // una imagen pequeña estirada a todo el panel ocupa lo que el panel.
            if (width <= 0 || height <= 0 || (long)width * height * 4 * _frames.Length > WidgetImages.MaxBytes) return null;

            SKImage? scaled = null;
            WidgetImages.Use(_path, source =>
            {
                // El fichero ha cambiado desde que se montó el renderizador y ya no tiene este fotograma.
                if (index >= source.Bitmaps.Count) return;

                var bitmap = source.Bitmaps[index];
                var target = Placement(_widget.Fit, bitmap.Width, bitmap.Height, width, height);

                // Lo que se amplía al doble o más casi siempre es un icono o pixel art, y suavizado se
                // vuelve borroso. Lo que se reduce, en cambio, necesita el filtro para no dentar.
                var sampling = target.Width >= bitmap.Width * 2 && target.Height >= bitmap.Height * 2
                    ? new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)
                    : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);

                using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
                surface.Canvas.Clear(SKColors.Transparent);

                // Sin copiar los píxeles: la imagen vive lo que dura este dibujo, dentro del candado.
                using (var pixels = bitmap.PeekPixels())
                using (var image = SKImage.FromPixels(pixels))
                    surface.Canvas.DrawImage(image, target, sampling);

                scaled = surface.Snapshot();
            });
            return scaled;
        }

        /// <summary>Suelta los fotogramas escalados, que ya no sirven para la caja de ahora.</summary>
        private void Forget()
        {
            for (int i = 0; i < _frames.Length; i++)
            {
                _frames[i]?.Dispose();
                _frames[i] = null;
            }
        }

        public override void Dispose()
        {
            Forget();
            base.Dispose();
        }
    }

    private sealed class ChartState : GraphState
    {
        private readonly ChartWidget _widget;
        private readonly float[] _samples;
        private readonly SKPaint? _fill;
        private readonly SKColor _plainFill;

        /// <summary>
        /// Lo último que se dibujó, para saber si la gráfica ha cambiado de verdad.
        /// </summary>
        /// <remarks>
        /// Una copia de las muestras y no su hash. Un hash es una prueba de igualdad con colisiones, y
        /// una colisión aquí es un refresco que no se envía y una gráfica que se queda atrás. Y el
        /// centinela tampoco puede ser un valor del propio dominio: esto era un <c>int</c> que empezaba
        /// en 0, que es un hash perfectamente posible, así que la primera vez podía no marcarse sucia.
        /// Comparar sesenta flotantes cuesta lo mismo que hashearlos.
        /// </remarks>
        private readonly float[] _drawn;
        private bool _ever;
        private Ink _ink;

        public ChartState(ChartWidget widget) : base(widget)
        {
            _widget = widget;
            _samples = new float[widget.Samples];
            _drawn = new float[widget.Samples];
            Array.Fill(_samples, float.NaN);
            Foreground.Style = SKPaintStyle.Stroke;
            Foreground.StrokeWidth = widget.Thickness;
            Foreground.StrokeCap = SKStrokeCap.Round;
            Foreground.StrokeJoin = SKStrokeJoin.Round;
            _fill = widget.Fill is { } color ? new SKPaint { Color = SKColor.Parse(color), IsAntialias = true } : null;
            _plainFill = _fill?.Color ?? default;
        }

        protected override bool Update(SensorReading? reading, DateTimeOffset time)
        {
            var level = Read(reading);

            // La gráfica se desplaza una muestra por refresco. Mover 60 flotantes cuesta menos que
            // llevar la cuenta de un índice circular en todos los sitios que recorren el histórico.
            Array.Copy(_samples, 1, _samples, 0, _samples.Length - 1);
            _samples[^1] = level.Fraction ?? float.NaN;

            // El color entra en la comparación: dos valores que se recortan al mismo sitio dibujan la
            // misma línea, y aun así pueden ser de tramos distintos.
            //
            // SequenceEqual compara con IEquatable, donde NaN es igual a NaN: un histórico todavía
            // vacío sigue contando como «no ha cambiado», que es lo que hace falta.
            if (_ever && level.Ink == _ink && _samples.AsSpan().SequenceEqual(_drawn)) return false;

            _samples.AsSpan().CopyTo(_drawn);
            _ink = level.Ink;
            _ever = true;
            Apply(level.Ink);
            return true;
        }

        /// <summary>
        /// El relleno sigue a la línea, pero rebajado: los dos del mismo color macizo convertirían la
        /// gráfica en un bloque donde ya no se ve la línea que va encima.
        /// </summary>
        protected override void Apply(Ink ink)
        {
            base.Apply(ink);
            if (_fill is { } fill)
                fill.Color = ink.Ruled ? ink.Color.WithAlpha(ValueColors.FillAlpha) : _plainFill;
        }

        public override void Draw(SKCanvas canvas, SKRect box)
        {
            if (Track is { } track) canvas.DrawRect(box, track);

            // Media línea de margen arriba y abajo, o los extremos saldrían cortados por la caja.
            float margin = _widget.Thickness / 2;
            float top = box.Top + margin;
            float height = box.Height - _widget.Thickness;
            if (height <= 0) return;

            float step = box.Width / (_samples.Length - 1f);
            using var line = new SKPath();
            using var area = new SKPath();
            bool open = false;

            for (int i = 0; i < _samples.Length; i++)
            {
                float sample = _samples[i];
                if (float.IsNaN(sample))
                {
                    // Un hueco en el histórico corta la línea en vez de inventar una recta entre los
                    // dos valores que lo rodean.
                    if (open) area.LineTo(box.Left + (i - 1) * step, box.Bottom);
                    open = false;
                    continue;
                }

                float x = box.Left + i * step;
                float y = top + (1 - sample) * height;
                if (open)
                {
                    line.LineTo(x, y);
                    area.LineTo(x, y);
                }
                else
                {
                    // El segmento de longitud cero no se ve cuando la línea sigue, y pinta un punto
                    // cuando la muestra está sola: al estrenar un tema, el histórico aún está vacío.
                    line.MoveTo(x, y);
                    line.LineTo(x, y);
                    area.MoveTo(x, box.Bottom);
                    area.LineTo(x, y);
                    open = true;
                }
            }

            if (open) area.LineTo(box.Right, box.Bottom);

            if (_fill is { } fill) canvas.DrawPath(area, fill);
            canvas.DrawPath(line, Foreground);

            // El marco, lo último: la línea llega hasta el borde de la caja y, dibujado antes, se lo
            // comería justo donde el valor está alto.
            if (Outline is not { } outline) return;

            var edge = Inside(box, outline.StrokeWidth);
            if (edge.Width > 0 && edge.Height > 0) canvas.DrawRect(edge, outline);
        }

        public override void Dispose()
        {
            _fill?.Dispose();
            base.Dispose();
        }
    }
}
