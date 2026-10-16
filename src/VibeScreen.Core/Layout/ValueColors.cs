namespace VibeScreen.Core.Layout;

/// <summary>
/// En qué tramo cae una lectura y de qué color va.
/// </summary>
/// <remarks>
/// Vive aquí, y no en el renderizador, porque es una decisión y no un dibujo: así se comprueba con
/// unos números, sin lienzo, sin sensores y sin una ventana abierta. El editor usa las mismas
/// cuentas para enseñar los recorridos, de modo que lo que dice el formulario y lo que pinta el
/// panel no pueden separarse.
/// </remarks>
public static class ValueColors
{
    /// <summary>Transparencia del relleno de un histórico cuando lo tiñe la regla.</summary>
    /// <remarks>
    /// El relleno se dibuja debajo de la línea, y los dos del mismo color macizo convierten la
    /// gráfica en un bloque donde ya no se ve la línea. Con el color rebajado se lee la forma y
    /// sigue notándose el tramo, que es para lo que está.
    /// </remarks>
    public const byte FillAlpha = 0x33;

    /// <summary>Verde de <see cref="Traffic"/>.</summary>
    public const string Green = "#4CAF50";

    /// <summary>Ámbar de <see cref="Traffic"/>.</summary>
    public const string Amber = "#FFC107";

    /// <summary>Rojo de <see cref="Traffic"/>.</summary>
    public const string Red = "#F44336";

    /// <summary>En qué tramo cae un valor: 0 el bajo, 1 el medio y 2 el alto.</summary>
    /// <remarks>
    /// Los cortes cuentan por abajo, incluidos: con el corte en 40, un 40 clavado ya es del tramo
    /// medio. Y la cuenta es total a propósito: con los dos cortes cruzados —que el cargador
    /// rechaza, pero el editor rehace el dibujo con cada tecla y ahí todavía no ha validado nadie—
    /// devuelve igualmente un tramo que existe en vez de reventar.
    /// </remarks>
    public static int Band(ColorLevels levels, double value)
    {
        ArgumentNullException.ThrowIfNull(levels);

        if (value >= levels.HighFrom) return 2;
        return value >= levels.MediumFrom ? 1 : 0;
    }

    /// <summary>El color de un tramo.</summary>
    public static string Of(ColorLevels levels, int band)
    {
        ArgumentNullException.ThrowIfNull(levels);

        return band switch
        {
            <= 0 => levels.Low,
            1 => levels.Medium,
            _ => levels.High,
        };
    }

    /// <summary>
    /// Verde, ámbar y rojo repartidos por el recorrido, para no empezar con el formulario en blanco.
    /// </summary>
    /// <remarks>
    /// Es un punto de partida y no un criterio: los tres colores y los dos cortes quedan editables,
    /// porque lo que para una CPU es una temperatura normal para un disco no lo es. Los cortes van a
    /// dos quintos y a cuatro quintos del recorrido, que en el caso corriente de 0 a 100 deja el
    /// 40 y el 80 de toda la vida.
    /// </remarks>
    public static ColorLevels Traffic(float min, float max)
    {
        float span = max - min;
        return new ColorLevels
        {
            Low = Green,
            Medium = Amber,
            High = Red,
            MediumFrom = min + (span * 0.4f),
            HighFrom = min + (span * 0.8f),
        };
    }
}
