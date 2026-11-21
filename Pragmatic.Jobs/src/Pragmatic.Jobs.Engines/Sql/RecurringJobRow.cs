using Pragmatic.Persistence.Sql.Attributes;

namespace Pragmatic.Jobs.Engines.Sql;

/// <summary>
///     A recurring job definition of the <c>[EnableJobPersistence]</c> boundary the Sql engine serves (<c>__RecurringJobs</c>), read and
///     written by <see cref="SqlRecurringJobStore" />. The table is EF Core's (<c>RecurringJobEntityTypeConfiguration</c>), column for column.
/// </summary>
[Entity]
[Table("__RecurringJobs")]
public sealed class RecurringJobRow : IRecurringJobRecord
{
    /// <summary>The schedule's name, not a surrogate: what <c>[RecurringJob(Id = …)]</c> says.</summary>
    [Key]
    [MaxLength(256)]
    public string Id { get; set; } = "";

    [MaxLength(512)]
    public string JobType { get; set; } = "";

    [MaxLength(128)]
    public string CronExpression { get; set; } = "";

    [MaxLength(65535)]
    public string? ParametersJson { get; set; }

    [MaxLength(512)]
    public string? ParameterType { get; set; }

    public DateTimeOffset? LastExecutedAt { get; set; }

    public DateTimeOffset? NextExecutionAt { get; set; }

    public bool IsEnabled { get; set; }

    [MaxLength(128)]
    public string? TenantId { get; set; }

    [MaxLength(64)]
    public string? TimeZoneId { get; set; }

    /// <summary>The <see cref="Attributes.MisfirePolicy" />, by number.</summary>
    public int MisfirePolicy { get; set; }
}
