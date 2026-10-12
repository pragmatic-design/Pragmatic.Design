using System.Text.Json.Serialization;
using Pragmatic.FeatureFlags;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Agent.Client;

/// <summary>
///     Source-generated JSON context for the concrete payloads the Agent client round-trips through
///     the KV store (tenants, feature flags, instance announcements, control-plane commands and events).
///     Default naming (no policy) preserves the exact wire shape the store already reads and writes —
///     AOT-safe without reflection.
/// </summary>
/// <remarks>
///     <c>HostCommand</c> and <c>ControlPlaneEvent</c> are polymorphic: their <c>[JsonDerivedType]</c>
///     declarations travel with them, so the generated metadata writes the same <c>$type</c> discriminator the
///     reflective serializer did (#142).
/// </remarks>
[JsonSerializable(typeof(TenantInfo))]
[JsonSerializable(typeof(FeatureFlagDefinition))]
[JsonSerializable(typeof(Pragmatic.Agent.Protocol.Payloads.InstanceAnnouncementPayload))]
[JsonSerializable(typeof(Pragmatic.ControlPlane.HostCommand))]
[JsonSerializable(typeof(Pragmatic.ControlPlane.ControlPlaneEvent))]
public partial class AgentClientJsonContext : JsonSerializerContext;
