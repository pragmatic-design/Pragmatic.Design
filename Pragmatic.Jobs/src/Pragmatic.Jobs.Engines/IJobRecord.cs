namespace Pragmatic.Jobs.Engines;

/// <summary>
///     The fields of a stored job, as the Sql row and the document share them: <see cref="JobRecords" /> moves a <see cref="JobInstance" /> in
///     and out of either through these, so the two stores convert it the same way.
/// </summary>
public interface IJobRecord
{
    Guid Id { get; set; }

    string JobType { get; set; }

    string? ParametersJson { get; set; }

    string? ParameterType { get; set; }

    /// <summary>The <see cref="JobStatus" />, by number.</summary>
    int Status { get; set; }

    DateTimeOffset ScheduledFor { get; set; }

    int Priority { get; set; }

    DateTimeOffset? StartedAt { get; set; }

    DateTimeOffset? CompletedAt { get; set; }

    string? Error { get; set; }

    int Attempt { get; set; }

    int MaxAttempts { get; set; }

    DateTimeOffset? LeaseExpiresAt { get; set; }

    string? LeasedBy { get; set; }

    string? ContinuationJobType { get; set; }

    string? ContinuationParametersJson { get; set; }

    string? CorrelationId { get; set; }

    string? TenantId { get; set; }

    DateTimeOffset CreatedAt { get; set; }
}
