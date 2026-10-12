using System.Text.Json.Serialization;

namespace Pragmatic.Agent.Client;

/// <summary>
///     The leadership lease <see cref="AgentClusterLeadership" /> writes to the KV store, with the default options
///     it has always been written with. Generated rather than reflected (#142); internal, as the lease is.
/// </summary>
[JsonSerializable(typeof(AgentClusterLeadership.LeaseRecord))]
internal sealed partial class AgentLeaseJsonContext : JsonSerializerContext;
