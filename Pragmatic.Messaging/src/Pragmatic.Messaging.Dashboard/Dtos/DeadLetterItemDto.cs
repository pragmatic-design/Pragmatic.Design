namespace Pragmatic.Messaging.Dashboard.Dtos;

/// <summary>Dead-lettered message served by <c>GET {path}/dead-letters</c>.</summary>
public sealed record DeadLetterItemDto(
    Guid Id,
    string MessageType,
    string Error,
    int RetryCount,
    DateTimeOffset FailedAt,
    string? CorrelationId);
