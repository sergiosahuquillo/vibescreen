namespace VibeScreen.Core.Transport;

/// <summary>Un puerto serie con lo que hace falta para identificar qué hay enchufado.</summary>
/// <remarks>
/// <see cref="System.IO.Ports.SerialPort.GetPortNames"/> solo devuelve nombres, y eso no basta:
/// escribir a ciegas en un puerto cualquiera puede acabar hablándole al SAI o a un Arduino.
/// La identificación por VID/PID y número de serie es obligatoria antes de abrir nada.
/// </remarks>
public sealed record SerialPortDescriptor
{
    public required string PortName { get; init; }
    public int? VendorId { get; init; }
    public int? ProductId { get; init; }
    public string? SerialNumber { get; init; }
    public string? Description { get; init; }

    public bool Matches(int vendorId, int productId) => VendorId == vendorId && ProductId == productId;

    public override string ToString()
    {
        string ids = VendorId is null || ProductId is null ? "?" : $"{VendorId:x4}:{ProductId:x4}";
        return $"{PortName} [{ids}] {SerialNumber ?? "sin-serie"} {Description}".TrimEnd();
    }
}

public interface ISerialPortEnumerator
{
    IReadOnlyList<SerialPortDescriptor> List();
}
