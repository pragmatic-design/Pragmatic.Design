namespace Pragmatic.Messaging.Dashboard.Dtos;

/// <summary>Audit trail entry served by <c>GET {path}/audit</c>.</summary>
/// <remarks>
///     Reshaped when messaging moved onto the framework's shared audit trail. Two fields went away and
///     are not coming back here: the handler name, which the shared entry does not model, and the
///     duration, which is a performance measurement rather than a record of what happened — messaging
///     already emits it as a metric, and duplicating it into an append-only trail meant paying storage
///     and retention for telemetry.
/// </remarks>
public sealed record AuditItemDto(
    long Seq,
    string MessageType,
    string? MessageId,
    string Outcome,
    DateTimeOffset OccurredAt,
    string? Detail);
