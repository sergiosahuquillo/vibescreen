namespace VibeScreen.Core.Storage;

/// <summary>
/// Escribir un fichero entero, o no escribirlo.
/// </summary>
/// <remarks>
/// <para>
/// <c>File.WriteAllText</c> trunca primero y escribe después, así que un corte en medio deja el
/// fichero a cero. Cuando lo que hay dentro es la configuración del usuario —la ruta de su tema, su
/// color, con qué se pinta la ventana— el resultado es perderla sin que nadie diga nada: quien la
/// lee encuentra un JSON roto, se queda con los valores de fábrica y machaca el fichero en el
/// siguiente guardado, de modo que ni siquiera queda evidencia de lo que había.
/// </para>
/// <para>
/// Se escribe al lado y se mueve encima. Dentro del mismo volumen, mover es atómico: o está el
/// contenido nuevo o sigue el viejo, nunca la mitad de ninguno.
/// </para>
/// </remarks>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        string full = Path.GetFullPath(path);
        string temporary = full + ".tmp";

        // Una ruta raíz no tiene carpeta, y aquí acababa en una excepción de argumento nulo desde
        // dentro de CreateDirectory, que no dice gran cosa de lo que ha pasado.
        if (Path.GetDirectoryName(full) is not { Length: > 0 } folder)
            throw new ArgumentException($"«{path}» no es la ruta de un fichero.", nameof(path));

        Directory.CreateDirectory(folder);

        try
        {
            Write(temporary, contents);
            File.Move(temporary, full, overwrite: true);
        }
        catch
        {
            Forget(temporary);
            throw;
        }
    }

    /// <summary>
    /// Escribe el temporal y se asegura de que está en el disco, no en una caché.
    /// </summary>
    /// <remarks>
    /// Sin el volcado, mover encima es atómico respecto a otros procesos pero no respecto a un corte
    /// de corriente: el sistema puede haber apuntado el cambio de nombre y no el contenido, y entonces
    /// lo que queda es un fichero vacío donde estaba el bueno. Que es exactamente lo que esta clase
    /// existe para evitar.
    /// </remarks>
    private static void Write(string temporary, string contents)
    {
        using var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None);
        using (var writer = new StreamWriter(file, leaveOpen: true))
        {
            writer.Write(contents);
        }

        file.Flush(flushToDisk: true);
    }

    /// <summary>Borra un temporal que ya no sirve. Que no se pueda es justo lo de menos ahora mismo.</summary>
    public static void Forget(string temporary)
    {
        try
        {
            File.Delete(temporary);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Queda un .tmp huérfano al lado. Comparado con perder el fichero de verdad, da igual.
        }
    }
}
