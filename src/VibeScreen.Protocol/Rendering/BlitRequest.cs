using VibeScreen.Protocol.Display;

namespace VibeScreen.Protocol.Rendering;

/// <summary>Un rectángulo listo para enviar al panel.</summary>
public readonly record struct BlitRequest(string WidgetId, PixelRect Rect, int Bytes, int Priority)
{
    public override string ToString() => $"{WidgetId} {Rect} {Bytes}B p{Priority}";
}
