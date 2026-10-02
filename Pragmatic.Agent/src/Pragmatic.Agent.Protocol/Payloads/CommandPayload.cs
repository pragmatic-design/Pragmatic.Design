using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pragmatic.Agent.Protocol.Payloads;

/// <summary>Agent → App: execute a command (maintenance, drain, etc.).</summary>
public sealed class CommandPayload
{
    /// <summary>Command type name (e.g., "EnterMaintenanceCommand", "DrainCommand").</summary>
    [JsonPropertyName("commandType")]
    public required string CommandType { get; init; }

    /// <summary>
    ///     Stable identity of this command instance. The transport is at-least-once; the receiver
    ///     de-duplicates by this id so a re-delivered command executes exactly once.
    /// </summary>
    [JsonPropertyName("commandId")]
    public string? CommandId { get; init; }

    /// <summary>
    ///     The instance this command targets — its host id, as the roster lists it — and the
    ///     <c>commands/</c> key segment. The receiver writes <c>commands-ack/{targetHostId}/{commandId}</c> so
    ///     the daemon can clean up the delivered command key.
    /// </summary>
    /// <remarks>
    ///     An instance, not an app: two instances of one app are two targets, and a command for
    ///     the whole app is one command per instance.
    /// </remarks>
    [JsonPropertyName("targetHostId")]
    public string? TargetHostId { get; init; }

    /// <summary>
    ///     The full command payload as polymorphic JSON (carries the <c>$type</c> discriminator and all
    ///     command fields). Deserialized by the host command dispatcher into the concrete command.
    /// </summary>
    [JsonPropertyName("data")]
    public JsonElement? Data { get; init; }
}
