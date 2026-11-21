using Pragmatic.Persistence.Sql.Attributes;

namespace Pragmatic.Jobs.Engines.Sql;

/// <summary>
///     A job instance of the <c>[EnableJobPersistence]</c> boundary the Sql engine serves (<c>__Jobs</c>), read and written by
///     <see cref="SqlJobStore" />. The table is EF Core's (<c>JobEntityTypeConfiguration</c>), column for column, so a database holds one
///     <c>__Jobs</c> whichever engine writes it; its indexes are added by the generator, as EF Core's.
/// </summary>
[Entity]
[Table("__Jobs")]
public sealed class JobRow : IJobRecord
{
    public Guid Id { get; set; }

    [MaxLength(512)]
    public string JobType { get; set; } = "";

    /// <summary>64KB, the cap EF Core's configuration puts on the payload.</summary>
    [MaxLength(65535)]
    public string? ParametersJson { get; set; }

    [MaxLength(512)]
    public string? ParameterType { get; set; }

    /// <summary>The <see cref="JobStatus" />, by number.</summary>
    public int Status { get; set; }

    public DateTimeOffset ScheduledFor { get; set; }

    public int Priority { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    [MaxLength(4096)]
    public string? Error { get; set; }

    public int Attempt { get; set; }

    public int MaxAttempts { get; set; }

    public DateTimeOffset? LeaseExpiresAt { get; set; }

    [MaxLength(128)]
    public string? LeasedBy { get; set; }

    [MaxLength(512)]
    public string? ContinuationJobType { get; set; }

    public string? ContinuationParametersJson { get; set; }

    [MaxLength(64)]
    public string? CorrelationId { get; set; }

    [MaxLength(128)]
    public string? TenantId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
