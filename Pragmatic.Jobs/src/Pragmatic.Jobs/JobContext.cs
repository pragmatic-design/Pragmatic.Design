namespace Pragmatic.Jobs;

/// <summary>
///     Immutable execution context passed to every job invocation.
/// </summary>
/// <param name="JobId">Unique job instance identifier.</param>
/// <param name="JobType">FQN of the job class.</param>
/// <param name="ScheduledAt">When the job was scheduled to run.</param>
/// <param name="Attempt">Current attempt number (0 = first attempt).</param>
/// <param name="MaxAttempts">Maximum retry attempts configured.</param>
/// <param name="CorrelationId">Optional correlation for tracing.</param>
/// <param name="TenantId">Optional tenant for multi-tenancy.</param>
public sealed record JobContext(
    Guid JobId,
    string JobType,
    DateTimeOffset ScheduledAt,
    int Attempt,
    int MaxAttempts,
    string? CorrelationId = null,
    string? TenantId = null);
