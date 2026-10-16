using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using VibeScreen.Core.Layout;
using PixelRect = VibeScreen.Protocol.Display.PixelRect;

namespace VibeScreen.App.Views;

/// <summary>Qué se está arrastrando: el widget entero o uno de sus lados.</summary>
public enum DragMode
{
    None,
    Move,
    ResizeLeft,
    ResizeRight,
    ResizeTop,
    ResizeBottom,
    ResizeTopLeft,
    ResizeTopRight,
    ResizeBottomLeft,
    ResizeBottomRight,
}

/// <summary>
/// El lienzo del editor: la imagen que va a ver el panel, ampliada, con una rejilla opcional encima y
/// el widget seleccionado con sus tiradores.
/// </summary>
/// <remarks>
/// La imagen sale del mismo <c>LayoutRenderer</c> que usa el servicio, píxel a píxel, así que la vista
/// previa no se parece al panel: es el panel. La rejilla y la selección se dibujan encima y nunca
/// entran en el mapa de bits, o acabarían en la pantalla.
/// </remarks>
public sealed class PanelCanvas : Control, IDisposable
{
    private const int HandleSize = 8;
    private const int MinimumSide = 4;

    /// <summary>
    /// Las plumas del lienzo, hechas una vez.
    /// </summary>
    /// <remarks>
    /// <see cref="Render"/> corre en cada fotograma y la rejilla dibuja decenas de líneas, así que una
    /// pluma por línea era basura inmediata a ritmo de pantalla. Guardan la referencia al pincel y no
    /// su color, así que cambiar el realce en Ajustes sigue repintándolas.
    /// </remarks>
    private static readonly IPen GridPen = new Pen(Palette.GridBrush, 1);
    private static readonly IPen EdgePen = new Pen(Palette.LineBrush, 1);
    private static readonly IPen SelectionPen = new Pen(Palette.AccentBrush, 1.5);
    private static readonly IPen HandlePen = new Pen(Palette.BackgroundBrush, 1);

    private WriteableBitmap? _front;
    private WriteableBitmap? _back;
    private PixelSize _frameSize;

    private DragMode _mode;
    private Point _grabbed;
    private PixelRect _original;

    public PanelCanvas()
    {
        Focusable = true;
        // Sin interpolación: aumentado, un píxel del panel tiene que verse como un cuadrado y no
        // como una mancha suavizada, que engaña sobre dónde acaba de verdad el widget.
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
    }

    /// <summary>El tema que se está editando. Solo se lee: el lienzo mueve widgets, no los crea.</summary>
    public LayoutDocument? Layout { get; set; }

    public LayoutWidget? Selected { get; set; }

    /// <summary>Los aumentos de la vista previa. No es entero porque uno de los tres lleva medio.</summary>
    /// <remarks>
    /// Nace en 1,5, que es con el que arranca el prototipo y el mayor de los tres con el que un panel
    /// apaisado de 480 cabe entero en la ventana de serie, con la columna de inspección a todo el
    /// alto. Al doble ya hay que desplazarlo para ver dónde se pone el widget.
    /// </remarks>
    public double Zoom { get; set; } = 1.5;

    public bool ShowGrid { get; set; } = true;

    public int GridStep { get; set; } = 8;

    public bool Snap { get; set; } = true;

    /// <summary>Alguien ha tocado un widget con el ratón o el teclado.</summary>
    public event Action<LayoutWidget?>? SelectionChanged;

    /// <summary>La caja del widget seleccionado ha cambiado.</summary>
    public event Action? Edited;

    /// <summary>Se ha pedido borrar el widget seleccionado.</summary>
    public event Action<LayoutWidget>? DeleteRequested;

    /// <summary>Se ha pedido copiar el widget seleccionado.</summary>
    public event Action<LayoutWidget>? CopyRequested;

    /// <summary>Se ha pedido pegar lo que haya copiado. Va sin widget: se pega sin nada seleccionado.</summary>
    public event Action? PasteRequested;

    /// <summary>Se ha pedido duplicar el widget seleccionado de una vez.</summary>
    public event Action<LayoutWidget>? DuplicateRequested;

    /// <summary>
    /// Copia el lienzo recién pintado. Skia escribe directamente en el búfer de Avalonia.
    /// </summary>
    /// <remarks>
    /// Van dos mapas de bits y se turnan: se escribe siempre en el que no se está enseñando y después
    /// se cambia el turno. Con uno solo se estaría escribiendo encima del que el hilo de composición
    /// puede estar leyendo en ese mismo instante, y eso se ve como un parpadeo.
    /// </remarks>
    public void SetFrame(SKBitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var size = new PixelSize(source.Width, source.Height);
        if (_front is null || _back is null || _frameSize != size)
        {
            _front?.Dispose();
            _back?.Dispose();
            _front = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            _back = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            _frameSize = size;
            InvalidateMeasure();
        }

        using (var buffer = _back.Lock())
        using (var pixels = source.PeekPixels())
        {
            // Los dos son BGRA8888, así que Skia escribe en el búfer de Avalonia sin pasos intermedios.
            var info = new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            pixels.ReadPixels(info, buffer.Address, buffer.RowBytes);
        }

        (_front, _back) = (_back, _front);
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        Layout is { } layout
            ? new Size(layout.Display.Width * Zoom, layout.Display.Height * Zoom)
            : new Size(0, 0);

    public override void Render(DrawingContext context)
    {
        if (Layout is not { } layout) return;

        var area = new Rect(0, 0, layout.Display.Width * Zoom, layout.Display.Height * Zoom);
        context.FillRectangle(Palette.SurfaceBrush, area);

        if (_front is not null)
            context.DrawImage(_front, new Rect(0, 0, _frameSize.Width, _frameSize.Height), area);

        if (ShowGrid) DrawGrid(context, layout, area);

        // El borde del panel, para que se vea dónde se acaba la pantalla de verdad.
        context.DrawRectangle(EdgePen, area);

        if (Selected is { } selected) DrawSelection(context, selected);
    }

    private void DrawGrid(DrawingContext context, LayoutDocument layout, Rect area)
    {
        // Aquí había una retícula de un píxel que solo salía de cuatro aumentos en adelante. Con el
        // zoom limitado al doble no puede dibujarse nunca, y al doble serían líneas cada dos píxeles:
        // una trama gris, no una ayuda para colocar nada.
        if (GridStep <= 1) return;

        for (int x = GridStep; x < layout.Display.Width; x += GridStep)
            context.DrawLine(GridPen, new Point(x * Zoom, 0), new Point(x * Zoom, area.Height));
        for (int y = GridStep; y < layout.Display.Height; y += GridStep)
            context.DrawLine(GridPen, new Point(0, y * Zoom), new Point(area.Width, y * Zoom));
    }

    private void DrawSelection(DrawingContext context, LayoutWidget widget)
    {
        var box = Scale(widget.Rect);
        context.DrawRectangle(SelectionPen, box.Inflate(0.5));

        foreach (var handle in Handles(box))
            context.DrawRectangle(Palette.AccentBrush, HandlePen, handle);
    }

    private Rect Scale(in PixelRect rect) =>
        new(rect.X * Zoom, rect.Y * Zoom, rect.Width * Zoom, rect.Height * Zoom);

    /// <summary>Los ocho tiradores, en el orden de <see cref="DragMode"/>.</summary>
    private static IEnumerable<Rect> Handles(Rect box)
    {
        foreach (var (x, y) in Corners(box))
            yield return new Rect(x - (HandleSize / 2.0), y - (HandleSize / 2.0), HandleSize, HandleSize);
    }

    private static (double X, double Y)[] Corners(Rect b) =>
    [
        (b.Left, b.Top), (b.Center.X, b.Top), (b.Right, b.Top),
        (b.Left, b.Center.Y), (b.Right, b.Center.Y),
        (b.Left, b.Bottom), (b.Center.X, b.Bottom), (b.Right, b.Bottom),
    ];

    private static readonly DragMode[] HandleModes =
    [
        DragMode.ResizeTopLeft, DragMode.ResizeTop, DragMode.ResizeTopRight,
        DragMode.ResizeLeft, DragMode.ResizeRight,
        DragMode.ResizeBottomLeft, DragMode.ResizeBottom, DragMode.ResizeBottomRight,
    ];

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Layout is null) return;

        Focus();
        var point = e.GetPosition(this);

        // El botón derecho selecciona lo que haya debajo y deja que salga el menú, pero no arrastra:
        // abrir el menú y mover el widget a la vez es la forma segura de descolocarlo sin querer.
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            var under = WidgetAt(point);
            if (under != Selected)
            {
                Selected = under;
                SelectionChanged?.Invoke(under);
                InvalidateVisual();
            }
            return;
        }

        // Primero los tiradores del widget que ya está seleccionado: caen encima de su borde y de
        // cualquier otro widget que haya debajo.
        if (Selected is { } selected && HandleAt(selected, point) is { } mode)
        {
            Start(mode, point, selected);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        var hit = WidgetAt(point);
        if (hit != Selected)
        {
            Selected = hit;
            SelectionChanged?.Invoke(hit);
            InvalidateVisual();
        }

        if (hit is not null)
        {
            Start(DragMode.Move, point, hit);
            e.Pointer.Capture(this);
        }

        e.Handled = true;
    }

    private void Start(DragMode mode, Point point, LayoutWidget widget)
    {
        _mode = mode;
        _grabbed = point;
        _original = widget.Rect;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Layout is null) return;

        var point = e.GetPosition(this);
        if (_mode == DragMode.None)
        {
            Cursor = CursorCache(CursorFor(point));
            return;
        }

        if (Selected is not { } widget) return;

        int dx = (int)Math.Round((point.X - _grabbed.X) / Zoom);
        int dy = (int)Math.Round((point.Y - _grabbed.Y) / Zoom);
        var moved = Apply(_original, dx, dy);
        if (moved == widget.Rect) return;

        widget.Rect = moved;
        Edited?.Invoke();
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _mode = DragMode.None;
        e.Pointer.Capture(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Layout is null) return;

        // Pegar es lo único que se hace sin nada seleccionado, así que va antes del guardián.
        // Los atajos viven aquí y no en la ventana entera a propósito: el lienzo solo tiene el foco
        // cuando se ha pinchado en él, así que Ctrl+C dentro de una casilla de las propiedades sigue
        // copiando texto. Es la misma razón por la que Supr está aquí y no borra widgets mientras se
        // escribe.
        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (control && e.Key == Key.V)
        {
            PasteRequested?.Invoke();
            e.Handled = true;
            return;
        }

        if (Selected is not { } widget) return;

        if (control && e.Key is Key.C or Key.D)
        {
            if (e.Key == Key.C) CopyRequested?.Invoke(widget);
            else DuplicateRequested?.Invoke(widget);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            DeleteRequested?.Invoke(widget);
            e.Handled = true;
            return;
        }

        // Las flechas mueven de píxel en píxel, que es para lo que existe un editor de paneles
        // pequeños; con mayúsculas, de rejilla en rejilla.
        int step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? Math.Max(1, GridStep) : 1;
        var (dx, dy) = e.Key switch
        {
            Key.Left => (-step, 0),
            Key.Right => (step, 0),
            Key.Up => (0, -step),
            Key.Down => (0, step),
            _ => (0, 0),
        };
        if (dx == 0 && dy == 0) return;

        var rect = widget.Rect;
        widget.Rect = Clamp(new PixelRect(rect.X + dx, rect.Y + dy, rect.Width, rect.Height));
        Edited?.Invoke();
        InvalidateVisual();
        e.Handled = true;
    }

    private PixelRect Apply(PixelRect original, int dx, int dy)
    {
        int left = original.X;
        int top = original.Y;
        int right = original.X + original.Width;
        int bottom = original.Y + original.Height;

        switch (_mode)
        {
            case DragMode.Move:
                left += dx; top += dy; right += dx; bottom += dy;
                if (Snap)
                {
                    int width = right - left, height = bottom - top;
                    left = Round(left); top = Round(top);
                    right = left + width; bottom = top + height;
                }
                break;

            default:
                if (_mode is DragMode.ResizeLeft or DragMode.ResizeTopLeft or DragMode.ResizeBottomLeft) left = Edge(left + dx);
                if (_mode is DragMode.ResizeRight or DragMode.ResizeTopRight or DragMode.ResizeBottomRight) right = Edge(right + dx);
                if (_mode is DragMode.ResizeTop or DragMode.ResizeTopLeft or DragMode.ResizeTopRight) top = Edge(top + dy);
                if (_mode is DragMode.ResizeBottom or DragMode.ResizeBottomLeft or DragMode.ResizeBottomRight) bottom = Edge(bottom + dy);
                break;
        }

        if (right - left < MinimumSide) right = left + MinimumSide;
        if (bottom - top < MinimumSide) bottom = top + MinimumSide;
        return Clamp(new PixelRect(left, top, right - left, bottom - top));
    }

    private int Edge(int value) => Snap ? Round(value) : value;

    private int Round(int value) => GridStep <= 1 ? value : (int)Math.Round(value / (double)GridStep) * GridStep;

    /// <summary>Un widget no puede salirse del panel: el cargador lo rechazaría al guardar.</summary>
    private PixelRect Clamp(PixelRect rect)
    {
        if (Layout is not { } layout) return rect;

        int width = Math.Min(rect.Width, layout.Display.Width);
        int height = Math.Min(rect.Height, layout.Display.Height);
        int x = Math.Clamp(rect.X, 0, layout.Display.Width - width);
        int y = Math.Clamp(rect.Y, 0, layout.Display.Height - height);
        return new PixelRect(x, y, width, height);
    }

    private LayoutWidget? WidgetAt(Point point)
    {
        if (Layout is not { } layout) return null;

        int x = (int)(point.X / Zoom);
        int y = (int)(point.Y / Zoom);

        // De arriba abajo: el último de la lista es el que se pinta encima.
        for (int i = layout.Widgets.Count - 1; i >= 0; i--)
        {
            var rect = layout.Widgets[i].Rect;
            if (x >= rect.X && x <= rect.Right && y >= rect.Y && y <= rect.Bottom) return layout.Widgets[i];
        }
        return null;
    }

    private DragMode? HandleAt(LayoutWidget widget, Point point)
    {
        var box = Scale(widget.Rect);
        int index = 0;
        foreach (var handle in Handles(box))
        {
            if (handle.Inflate(2).Contains(point)) return HandleModes[index];
            index++;
        }
        return null;
    }

    private static readonly Dictionary<StandardCursorType, Cursor> Cursors = [];

    /// <summary>Los cursores se guardan: cada uno reserva un recurso del sistema, y aquí se piden en cada movimiento del ratón.</summary>
    private static Cursor CursorCache(StandardCursorType type)
    {
        if (!Cursors.TryGetValue(type, out var cursor)) Cursors[type] = cursor = new Cursor(type);
        return cursor;
    }

    /// <summary>
    /// Suelta los dos mapas de bits del fotograma.
    /// </summary>
    /// <remarks>
    /// <see cref="SetFrame"/> solo suelta el par anterior cuando cambia el <b>tamaño</b> del fotograma,
    /// así que al morir el lienzo quedan dos del tamaño del panel sin soltar. Lo llama
    /// <c>EditorView</c>, que es quien lo construye.
    /// </remarks>
    public void Dispose()
    {
        _front?.Dispose();
        _back?.Dispose();
        _front = null;
        _back = null;
    }

    private StandardCursorType CursorFor(Point point)
    {
        if (Selected is { } selected && HandleAt(selected, point) is { } mode)
            return mode switch
            {
                DragMode.ResizeLeft or DragMode.ResizeRight => StandardCursorType.SizeWestEast,
                DragMode.ResizeTop or DragMode.ResizeBottom => StandardCursorType.SizeNorthSouth,
                DragMode.ResizeTopLeft or DragMode.ResizeBottomRight => StandardCursorType.TopLeftCorner,
                _ => StandardCursorType.TopRightCorner,
            };

        return WidgetAt(point) is not null ? StandardCursorType.SizeAll : StandardCursorType.Arrow;
    }
}
