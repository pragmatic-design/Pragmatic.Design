namespace Pragmatic.Jobs;

/// <summary>
///     Represents a single job execution instance.
///     Persisted in __Jobs table when using EF Core store.
/// </summary>
public sealed class JobInstance
{
    /// <summary>Unique job instance ID.</summary>
    public Guid Id { get; set; }

    /// <summary>FQN of the job class.</summary>
    public required string JobType { get; set; }

    /// <summary>Serialized parameters (JSON). Null for parameterless jobs.</summary>
    public string? ParametersJson { get; set; }

    /// <summary>FQN of the parameter type (for AOT deserialization). Null for parameterless.</summary>
    public string? ParameterType { get; set; }

    /// <summary>Current job status.</summary>
    public JobStatus Status { get; set; }

    /// <summary>When the job should be executed.</summary>
    public DateTimeOffset ScheduledFor { get; set; }

    /// <summary>
    ///     Scheduling priority — higher runs first among jobs that are due, ties broken by
    ///     <see cref="ScheduledFor"/>. Default: 0.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>When execution started.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When execution completed (success or final failure).</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Last error message.</summary>
    public string? Error { get; set; }

    /// <summary>Current attempt number.</summary>
    public int Attempt { get; set; }

    /// <summary>
    ///     Maximum retry attempts. Default: 1 (no retry).
    ///     <see cref="Services.JobScheduler"/> overrides this from <see cref="Configuration.JobsOptions.DefaultMaxRetries"/>;
    ///     callers constructing <see cref="JobInstance"/> directly must set this explicitly.
    /// </summary>
    public int MaxAttempts { get; set; } = 1;

    /// <summary>Lease expiration for distributed locking.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; set; }

    /// <summary>Worker ID that holds the lease.</summary>
    public string? LeasedBy { get; set; }

    /// <summary>FQN of continuation job to enqueue on success.</summary>
    public string? ContinuationJobType { get; set; }

    /// <summary>Serialized parameters for the continuation job.</summary>
    public string? ContinuationParametersJson { get; set; }

    /// <summary>Correlation ID for distributed tracing.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Tenant ID for multi-tenancy.</summary>
    public string? TenantId { get; set; }

    /// <summary>When the job was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
