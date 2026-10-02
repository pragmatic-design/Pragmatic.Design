namespace Pragmatic.Jobs;

/// <summary>
///     Definition of a recurring job. Persisted in __RecurringJobs table.
///     The SG generates definitions from <c>[RecurringJob]</c> attributes at compile time.
/// </summary>
public sealed class RecurringJobDefinition
{
    /// <summary>Unique recurring job ID (e.g., "daily-report"). Derived from class name if not specified.</summary>
    public required string Id { get; set; }

    /// <summary>FQN of the job class.</summary>
    public required string JobType { get; set; }

    /// <summary>Cron expression (5 or 6 part).</summary>
    public required string CronExpression { get; set; }

    /// <summary>Serialized parameters (JSON). Null for parameterless jobs.</summary>
    public string? ParametersJson { get; set; }

    /// <summary>FQN of the parameter type.</summary>
    public string? ParameterType { get; set; }

    /// <summary>When the job last executed.</summary>
    public DateTimeOffset? LastExecutedAt { get; set; }

    /// <summary>When the next execution is due.</summary>
    public DateTimeOffset? NextExecutionAt { get; set; }

    /// <summary>Whether the recurring job is enabled.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Tenant ID for multi-tenancy.</summary>
    public string? TenantId { get; set; }

    /// <summary>IANA timezone for cron evaluation. Null = UTC.</summary>
    public string? TimeZoneId { get; set; }

    /// <summary>
    ///     What to do with an occurrence missed while the host was down or saturated. Defaults to
    ///     <see cref="Attributes.MisfirePolicy.RunOnce"/>.
    /// </summary>
    public Attributes.MisfirePolicy MisfirePolicy { get; set; } = Attributes.MisfirePolicy.RunOnce;
}
