namespace VibeScreen.Core.Transport;

/// <summary>
/// Enumera puertos leyendo sysfs. Sin privilegios y sin dependencias: los atributos USB están
/// en el dispositivo padre del interfaz, así que se sube por el árbol hasta encontrarlos.
/// </summary>
public sealed class LinuxSerialPortEnumerator : ISerialPortEnumerator
{
    private const string TtyClassRoot = "/sys/class/tty";

    public IReadOnlyList<SerialPortDescriptor> List()
    {
        var results = new List<SerialPortDescriptor>();
        if (!Directory.Exists(TtyClassRoot)) return results;

        foreach (string ttyPath in Directory.EnumerateDirectories(TtyClassRoot))
        {
            string name = Path.GetFileName(ttyPath);
            string deviceLink = Path.Combine(ttyPath, "device");
            if (!Directory.Exists(deviceLink) && !File.Exists(deviceLink)) continue;

            string? usbDevice = FindUsbDeviceDirectory(deviceLink);
            if (usbDevice is null) continue; // No es un serie por USB: lo ignoramos.

            results.Add(new SerialPortDescriptor
            {
                PortName = "/dev/" + name,
                VendorId = ReadHex(usbDevice, "idVendor"),
                ProductId = ReadHex(usbDevice, "idProduct"),
                SerialNumber = ReadText(usbDevice, "serial"),
                Description = ReadText(usbDevice, "product"),
            });
        }

        return results.OrderBy(r => r.PortName, StringComparer.Ordinal).ToList();
    }

    /// <summary>Sube por el árbol de sysfs hasta el directorio que tiene los atributos del dispositivo USB.</summary>
    private static string? FindUsbDeviceDirectory(string startPath)
    {
        string? current;
        try
        {
            current = Path.GetFullPath(new DirectoryInfo(startPath).LinkTarget ?? startPath,
                                       Path.GetDirectoryName(startPath) ?? "/");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        // Ocho niveles cubren de sobra interfaz -> dispositivo -> concentrador.
        for (int depth = 0; depth < 8 && !string.IsNullOrEmpty(current) && current != "/"; depth++)
        {
            if (File.Exists(Path.Combine(current, "idVendor")) && File.Exists(Path.Combine(current, "idProduct")))
                return current;
            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    private static int? ReadHex(string directory, string attribute)
    {
        string? raw = ReadText(directory, attribute);
        return raw is not null && int.TryParse(raw, System.Globalization.NumberStyles.HexNumber,
                                               System.Globalization.CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
    }

    private static string? ReadText(string directory, string attribute)
    {
        string path = Path.Combine(directory, attribute);
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
