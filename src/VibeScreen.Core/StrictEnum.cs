namespace VibeScreen.Core;

/// <summary>
/// Lectura estricta de enumerados: solo por nombre, sin distinguir mayúsculas. <c>Enum.TryParse</c>
/// acepta también números y listas separadas por comas, que pueden dar valores que no existen.
/// </summary>
public static class StrictEnum
{
    /// <summary>
    /// Lo que hay que saber de un enumerado, preguntado una sola vez.
    /// </summary>
    /// <remarks>
    /// <c>Enum.GetNames</c> reserva un <c>string[]</c> en cada llamada, y detrás venía un
    /// <c>Enum.Parse</c> que volvía a buscar el nombre que ya se había encontrado. Esto se llama por
    /// cada propiedad de enumerado de cada layout que se carga y por cada opción de la línea de
    /// comandos. Un genérico estático por tipo lo resuelve al cargarlo y ya está.
    /// </remarks>
    private static class Cache<TEnum> where TEnum : struct, Enum
    {
        /// <summary><c>GetNames</c> y <c>GetValues</c> van en el mismo orden, así que el índice vale para las dos.</summary>
        public static readonly string[] Names = Enum.GetNames<TEnum>();

        public static readonly TEnum[] Values = Enum.GetValues<TEnum>();

        public static readonly string Lowercase = string.Join(", ", Names.Select(n => n.ToLowerInvariant()));
    }

    public static bool TryParse<TEnum>(string? text, out TEnum value) where TEnum : struct, Enum
    {
        string[] names = Cache<TEnum>.Names;
        for (int i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], text, StringComparison.OrdinalIgnoreCase))
            {
                value = Cache<TEnum>.Values[i];
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>Los nombres válidos en minúsculas, para los mensajes de error.</summary>
    public static string Names<TEnum>() where TEnum : struct, Enum => Cache<TEnum>.Lowercase;
}
