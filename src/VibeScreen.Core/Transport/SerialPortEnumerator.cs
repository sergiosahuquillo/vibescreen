namespace VibeScreen.Core.Transport;

/// <summary>Elige el enumerador que corresponde al sistema operativo.</summary>
public static class SerialPortEnumerator
{
    public static ISerialPortEnumerator Create()
    {
#if WINDOWS
        if (OperatingSystem.IsWindows()) return new WindowsSerialPortEnumerator();
#endif
        if (OperatingSystem.IsLinux()) return new LinuxSerialPortEnumerator();

        throw new PlatformNotSupportedException(
            "Solo hay enumeración de puertos para Windows y Linux. " +
            "En Windows, compila con el objetivo net10.0-windows.");
    }
}
