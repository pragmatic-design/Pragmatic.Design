using System.Text.Json.Serialization;
using Pragmatic.Agent.Protocol.Payloads;

namespace Pragmatic.Agent.Platform;

/// <summary>
///     The descriptor <see cref="PlatformMaintenanceEnforcer" /> reads from the roster, with the options it always
///     read it with: names matched without regard to case. Generated rather than reflected (#142).
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(HostDescriptorPayload))]
internal sealed partial class PlatformJsonContext : JsonSerializerContext;
