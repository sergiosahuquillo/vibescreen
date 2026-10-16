using System.Text.Json.Serialization;

namespace VibeScreen.Contracts;

/// <summary>
/// Serializador del IPC, generado en compilación: sin reflexión, el recorte de Linux no se lo lleva por
/// delante y arranca más rápido. Rechaza los miembros que no conoce, así que una versión vieja del
/// servicio contesta con un error claro a una orden nueva en vez de ignorar campos en silencio.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ServiceRequest))]
[JsonSerializable(typeof(ServiceResponse))]
public sealed partial class ServiceJsonContext : JsonSerializerContext;
