namespace VibeScreen.Protocol.Display;

/// <summary>Orientación lógica del panel.</summary>
/// <remarks>
/// La orientación es estado persistente del dispositivo: sobrevive a que el proceso
/// que la fijó termine. El driver debe establecerla explícitamente al inicializar en
/// lugar de heredar la que dejara el programa anterior, porque los rectángulos de
/// <c>DisplayBitmap</c> se interpretan en la orientación vigente.
/// </remarks>
public enum Orientation
{
    Portrait = 0,
    ReversePortrait = 1,
    Landscape = 2,
    ReverseLandscape = 3,
}

public static class OrientationExtensions
{
    /// <summary>True cuando la orientación intercambia ancho y alto respecto al panel nativo.</summary>
    public static bool IsRotated(this Orientation o) =>
        o is Orientation.Landscape or Orientation.ReverseLandscape;
}
