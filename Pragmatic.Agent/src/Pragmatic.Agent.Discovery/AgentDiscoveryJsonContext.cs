using System.Text.Json.Serialization;
using Pragmatic.Discovery.Models;

namespace Pragmatic.Agent.Discovery;

/// <summary>
///     The host topology <see cref="AgentDiscoveryBackend" /> stores under <c>topology/{hostName}</c>, with the
///     default options it has always been written with. Generated rather than reflected (#142).
/// </summary>
[JsonSerializable(typeof(HostTopologyInfo))]
internal sealed partial class AgentDiscoveryJsonContext : JsonSerializerContext;
