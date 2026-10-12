using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pragmatic.Agent.Protocol.Serialization;

/// <summary>
///     The metadata of the socket frame, <see cref="AgentMessage" />, with the options the frame has always been
///     written with: camelCase, nulls omitted, names read without regard to case.
/// </summary>
/// <remarks>
///     Generated rather than reflected, so the Agent can be analysed for AOT without a warning on the one path
///     every message takes (#142). The payload a frame carries is a <see cref="JsonElement" /> written by
///     <see cref="AgentPayloadJsonContext" />, with its own options.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AgentMessage))]
internal sealed partial class AgentWireJsonContext : JsonSerializerContext;
