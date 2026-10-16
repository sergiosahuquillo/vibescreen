namespace VibeScreen.Protocol.Display;

/// <summary>Rectángulo en píxeles con origen arriba-izquierda. <see cref="Right"/> y <see cref="Bottom"/> son inclusivos.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width - 1;
    public int Bottom => Y + Height - 1;
    /// <summary>Píxeles que cubre. En <c>long</c> porque es de donde salen las cuentas de bytes.</summary>
    public long Area => (long)Width * Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PixelRect FromBounds(int left, int top, int right, int bottom) =>
        new(left, top, right - left + 1, bottom - top + 1);

    public bool Contains(in PixelRect other) =>
        other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;

    public bool IntersectsWith(in PixelRect other) =>
        X <= other.Right && other.X <= Right && Y <= other.Bottom && other.Y <= Bottom;

    public PixelRect Union(in PixelRect other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        int l = Math.Min(X, other.X), t = Math.Min(Y, other.Y);
        int r = Math.Max(Right, other.Right), b = Math.Max(Bottom, other.Bottom);
        return FromBounds(l, t, r, b);
    }

    /// <summary>Recorta el rectángulo al lienzo dado. Devuelve un rectángulo vacío si queda fuera por completo.</summary>
    public PixelRect ClampTo(int canvasWidth, int canvasHeight)
    {
        int l = Math.Max(0, X), t = Math.Max(0, Y);
        int r = Math.Min(canvasWidth - 1, Right), b = Math.Min(canvasHeight - 1, Bottom);
        return r < l || b < t ? default : FromBounds(l, t, r, b);
    }

    public override string ToString() => $"({X},{Y}) {Width}x{Height}";
}
