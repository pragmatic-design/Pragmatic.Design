using System.Text.Json.Serialization;
using Pragmatic.FeatureFlags;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Agent.Client;

/// <summary>
///     Source-generated JSON context for the concrete payloads the Agent client round-trips through
///     the KV store (tenants, feature flags, instance announcements). Default naming (no policy) preserves
///     the exact wire shape the store already reads and writes — AOT-safe without reflection.
/// </summary>
[JsonSerializable(typeof(TenantInfo))]
[JsonSerializable(typeof(FeatureFlagDefinition))]
[JsonSerializable(typeof(Pragmatic.Agent.Protocol.Payloads.InstanceAnnouncementPayload))]
public partial class AgentClientJsonContext : JsonSerializerContext;
