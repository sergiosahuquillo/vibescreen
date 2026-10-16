namespace VibeScreen.Core.Sensors;

/// <summary>Elige el backend de sensores que corresponde al sistema operativo.</summary>
public static class SensorProviders
{
    public static ISensorProvider CreateDefault()
    {
#if WINDOWS
        if (OperatingSystem.IsWindows()) return new LibreHardwareMonitorSensorProvider();
#endif
        throw new PlatformNotSupportedException(
            "De momento solo hay sensores en Windows, con el objetivo net10.0-windows. El backend de Linux está pendiente.");
    }
}
