using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Pragmatic.Agent.Protocol.Serialization;

/// <summary>
///     Writes and reads a payload through <see cref="AgentPayloadJsonContext" />, for the call sites that are
///     generic over the payload type.
/// </summary>
/// <remarks>
///     A type the context does not declare is a programmer error: <see cref="JsonSerializerOptions.GetTypeInfo" />
///     throws <see cref="NotSupportedException" /> naming it, where the reflective serializer would have built
///     its metadata at run time.
/// </remarks>
public static class AgentPayloads
{
    /// <summary>The payload as the element a frame carries.</summary>
    public static JsonElement ToElement<T>(T payload) => JsonSerializer.SerializeToElement(payload, Info<T>());

    /// <summary>The payload a frame carries, or null when the element holds JSON null.</summary>
    public static T? From<T>(JsonElement element) => element.Deserialize(Info<T>());

    /// <summary>The payload as a KV value.</summary>
    public static string ToJson<T>(T payload) => JsonSerializer.Serialize(payload, Info<T>());

    /// <summary>The payload a KV value holds.</summary>
    public static T? FromJson<T>(string json) => JsonSerializer.Deserialize(json, Info<T>());

    private static JsonTypeInfo<T> Info<T>() => (JsonTypeInfo<T>)AgentPayloadJsonContext.Default.Options.GetTypeInfo(typeof(T));
}
