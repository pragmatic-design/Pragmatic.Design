using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Agent.Protocol.Payloads;

namespace Pragmatic.Agent.Protocol.Serialization;

/// <summary>
///     The metadata of every payload the Agent and its clients exchange, inside a frame or as a KV value, with the
///     default options they have always been written with: each name is the one its <c>[JsonPropertyName]</c>
///     declares, and nulls are written.
/// </summary>
/// <remarks>
///     Generated rather than reflected (#142). <see cref="AgentPayloads" /> is the one way in for a call site
///     that is generic over the payload type.
/// </remarks>
[JsonSerializable(typeof(RegisterPayload))]
[JsonSerializable(typeof(HeartbeatPayload))]
[JsonSerializable(typeof(StateReportPayload))]
[JsonSerializable(typeof(StateChangePayload))]
[JsonSerializable(typeof(CommandPayload))]
[JsonSerializable(typeof(HostDescriptorPayload))]
[JsonSerializable(typeof(InstanceAnnouncementPayload))]
[JsonSerializable(typeof(KvGetPayload))]
[JsonSerializable(typeof(KvSetPayload))]
[JsonSerializable(typeof(KvDeletePayload))]
[JsonSerializable(typeof(KvPrefixPayload))]
[JsonSerializable(typeof(KvWatchPayload))]
[JsonSerializable(typeof(KvChangedPayload))]
[JsonSerializable(typeof(KvEntryPayload))]
[JsonSerializable(typeof(KvEntriesPayload))]
[JsonSerializable(typeof(KvSetResultPayload))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class AgentPayloadJsonContext : JsonSerializerContext;
