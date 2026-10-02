namespace Pragmatic.Messaging.Dashboard.Dtos;

/// <summary>Pending outbox message served by <c>GET {path}/outbox</c>.</summary>
public sealed record OutboxItemDto(
    Guid Id,
    string Boundary,
    string MessageType,
    DateTimeOffset CreatedAt,
    DateTimeOffset? NextAttemptAt,
    int RetryCount,
    string? Error);
