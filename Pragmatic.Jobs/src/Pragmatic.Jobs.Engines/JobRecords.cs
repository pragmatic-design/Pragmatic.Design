namespace Pragmatic.Jobs.Engines;

/// <summary>
///     A <see cref="JobInstance" /> and a <see cref="RecurringJobDefinition" /> in and out of the engines' stored forms, field for field, the
///     enums by number as EF Core's configuration stores them.
/// </summary>
public static class JobRecords
{
    /// <summary>The longest error the column takes; a longer one would fail the write that records the failure.</summary>
    public const int MaxErrorLength = 4096;

    /// <summary>Fills <paramref name="record" /> from <paramref name="job" />, and returns it.</summary>
    public static TRecord From<TRecord>(TRecord record, JobInstance job) where TRecord : IJobRecord
    {
        record.Id = job.Id;
        record.JobType = job.JobType;
        record.ParametersJson = job.ParametersJson;
        record.ParameterType = job.ParameterType;
        record.Status = (int)job.Status;
        record.ScheduledFor = job.ScheduledFor;
        record.Priority = job.Priority;
        record.StartedAt = job.StartedAt;
        record.CompletedAt = job.CompletedAt;
        record.Error = job.Error;
        record.Attempt = job.Attempt;
        record.MaxAttempts = job.MaxAttempts;
        record.LeaseExpiresAt = job.LeaseExpiresAt;
        record.LeasedBy = job.LeasedBy;
        record.ContinuationJobType = job.ContinuationJobType;
        record.ContinuationParametersJson = job.ContinuationParametersJson;
        record.CorrelationId = job.CorrelationId;
        record.TenantId = job.TenantId;
        record.CreatedAt = job.CreatedAt;
        return record;
    }

    /// <summary>The job <paramref name="record" /> holds.</summary>
    public static JobInstance ToInstance(IJobRecord record) => new()
    {
        Id = record.Id,
        JobType = record.JobType,
        ParametersJson = record.ParametersJson,
        ParameterType = record.ParameterType,
        Status = (JobStatus)record.Status,
        ScheduledFor = record.ScheduledFor,
        Priority = record.Priority,
        StartedAt = record.StartedAt,
        CompletedAt = record.CompletedAt,
        Error = record.Error,
        Attempt = record.Attempt,
        MaxAttempts = record.MaxAttempts,
        LeaseExpiresAt = record.LeaseExpiresAt,
        LeasedBy = record.LeasedBy,
        ContinuationJobType = record.ContinuationJobType,
        ContinuationParametersJson = record.ContinuationParametersJson,
        CorrelationId = record.CorrelationId,
        TenantId = record.TenantId,
        CreatedAt = record.CreatedAt,
    };

    /// <summary>Fills <paramref name="record" /> from <paramref name="definition" />, and returns it.</summary>
    public static TRecord From<TRecord>(TRecord record, RecurringJobDefinition definition) where TRecord : IRecurringJobRecord
    {
        record.Id = definition.Id;
        record.JobType = definition.JobType;
        record.CronExpression = definition.CronExpression;
        record.ParametersJson = definition.ParametersJson;
        record.ParameterType = definition.ParameterType;
        record.LastExecutedAt = definition.LastExecutedAt;
        record.NextExecutionAt = definition.NextExecutionAt;
        record.IsEnabled = definition.IsEnabled;
        record.TenantId = definition.TenantId;
        record.TimeZoneId = definition.TimeZoneId;
        record.MisfirePolicy = (int)definition.MisfirePolicy;
        return record;
    }

    /// <summary>The definition <paramref name="record" /> holds.</summary>
    public static RecurringJobDefinition ToDefinition(IRecurringJobRecord record) => new()
    {
        Id = record.Id,
        JobType = record.JobType,
        CronExpression = record.CronExpression,
        ParametersJson = record.ParametersJson,
        ParameterType = record.ParameterType,
        LastExecutedAt = record.LastExecutedAt,
        NextExecutionAt = record.NextExecutionAt,
        IsEnabled = record.IsEnabled,
        TenantId = record.TenantId,
        TimeZoneId = record.TimeZoneId,
        MisfirePolicy = (Attributes.MisfirePolicy)record.MisfirePolicy,
    };

    /// <summary><paramref name="error" />, cut to the column's width.</summary>
    public static string Truncate(string error) => error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];

    /// <summary>The instant of UTC <paramref name="ticks" />, as a document stores one; null for none.</summary>
    public static DateTimeOffset? Instant(long? ticks) => ticks is { } t ? new DateTimeOffset(t, TimeSpan.Zero) : null;
}
