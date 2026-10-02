namespace Pragmatic.Messaging.Jobs;

/// <summary>
///     Parameters for <see cref="PublishMessageJob"/>: carries the serialized message
///     and its type name for AOT-safe deserialization at execution time. The optional
///     context fields preserve the original <see cref="MessageContext"/> across the
///     schedule boundary — redelivery keeps the original MessageId so idempotent
///     sibling handlers skip the redelivered message.
/// </summary>
/// <remarks>
///     Security: <see cref="SerializedMessage"/> is the FULL message body and lives at rest in the jobs
///     store for the entire schedule delay (no offload/redaction on this path — <c>[NotLogged]</c> covers
///     audit/logging, not job persistence). Do not schedule secrets in the message body; the jobs store
///     inherits the database's at-rest encryption, so rely on that (or reference the secret indirectly).
/// </remarks>
public sealed record PublishMessageParams(
    string MessageTypeName,
    string SerializedMessage,
    string? CorrelationId = null,
    string? TenantId = null,
    string? MessageId = null,
    string? UserId = null,
    string? SourceBoundary = null,
    int RetryCount = 0);
