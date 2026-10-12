using System.Text.Json.Serialization;

namespace Pragmatic.Agent.Gossip;

/// <summary>
///     The gossip datagram, with the options it has always been written with: camelCase, nulls omitted.
///     Generated rather than reflected (#142); a peer on another version reads the same bytes.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GossipMessage))]
internal sealed partial class GossipJsonContext : JsonSerializerContext;
