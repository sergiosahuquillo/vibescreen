#if WINDOWS
using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace VibeScreen.Core.Transport;

/// <summary>
/// Enumera puertos recorriendo <c>HKLM\SYSTEM\CurrentControlSet\Enum\USB</c>.
/// </summary>
/// <remarks>
/// Se usa el registro en lugar de WMI porque no añade dependencias, no arranca un servicio y
/// responde en microsegundos en vez de en cientos de milisegundos. Solo hace lecturas.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsSerialPortEnumerator : ISerialPortEnumerator
{
    private const string UsbEnumKey = @"SYSTEM\CurrentControlSet\Enum\USB";

    public IReadOnlyList<SerialPortDescriptor> List()
    {
        var results = new List<SerialPortDescriptor>();

        using RegistryKey? usbRoot = Registry.LocalMachine.OpenSubKey(UsbEnumKey);
        if (usbRoot is null) return results;

        foreach (string deviceKeyName in usbRoot.GetSubKeyNames())
        {
            // La clave es del estilo "VID_1A86&PID_5722".
            var (vendorId, productId) = ParseVidPid(deviceKeyName);
            if (vendorId is null || productId is null) continue;

            using RegistryKey? deviceKey = usbRoot.OpenSubKey(deviceKeyName);
            if (deviceKey is null) continue;

            foreach (string instanceName in deviceKey.GetSubKeyNames())
            {
                using RegistryKey? instance = deviceKey.OpenSubKey(instanceName);
                if (instance is null) continue;

                using RegistryKey? parameters = instance.OpenSubKey("Device Parameters");
                if (parameters?.GetValue("PortName") is not string portName || portName.Length == 0) continue;

                results.Add(new SerialPortDescriptor
                {
                    PortName = portName,
                    VendorId = vendorId,
                    ProductId = productId,
                    // Cuando el dispositivo no declara número de serie, Windows genera un id que
                    // empieza por '&' a partir del concentrador y el puerto: eso no es un serie.
                    SerialNumber = instanceName.StartsWith('&') ? null : instanceName,
                    Description = instance.GetValue("FriendlyName") as string
                                  ?? instance.GetValue("DeviceDesc") as string,
                });
            }
        }

        return results.OrderBy(r => r.PortName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static (int? VendorId, int? ProductId) ParseVidPid(string key)
    {
        const string VidMarker = "VID_";
        const string PidMarker = "PID_";

        int vidIndex = key.IndexOf(VidMarker, StringComparison.OrdinalIgnoreCase);
        int pidIndex = key.IndexOf(PidMarker, StringComparison.OrdinalIgnoreCase);
        if (vidIndex < 0 || pidIndex < 0) return (null, null);

        return (ParseHexAt(key, vidIndex + VidMarker.Length), ParseHexAt(key, pidIndex + PidMarker.Length));
    }

    private static int? ParseHexAt(string source, int offset)
    {
        if (offset + 4 > source.Length) return null;
        return int.TryParse(source.AsSpan(offset, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
    }
}
#endif
