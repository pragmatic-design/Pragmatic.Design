using Pragmatic.Persistence.Documents.Attributes;
using Pragmatic.Persistence.Documents.Schema;

namespace Pragmatic.Jobs.Engines.Documents;

/// <summary>
///     A recurring job definition of the <c>[EnableJobPersistence]</c> boundary the documents engine serves (collection <c>__RecurringJobs</c>),
///     read and written by <see cref="DocRecurringJobStore" />. The fields are <see cref="Sql.RecurringJobRow" />'s columns, the instants UTC
///     ticks: the tick is claimed by comparing the next occurrence with the one the host read, which a millisecond date would not give back.
/// </summary>
[Document("__RecurringJobs")]
public sealed partial class RecurringJobDocument : IRecurringJobRecord
{
    /// <summary>The collection in a boundary's <c>{Boundary}DocSchema</c>: the index EF Core's configuration declares, with its name.</summary>
    public static readonly DocCollectionSchema Schema = new("__RecurringJobs", "/id",
        [new DocIndex("IX___RecurringJobs_IsEnabled_NextExecutionAt", ["IsEnabled", "NextExecutionAt"])], []);

    /// <summary>The schedule's name, not a surrogate: what <c>[RecurringJob(Id = …)]</c> says.</summary>
    [Key]
    public string Id { get; set; } = "";

    public string JobType { get; set; } = "";

    public string CronExpression { get; set; } = "";

    public string? ParametersJson { get; set; }

    public string? ParameterType { get; set; }

    public long? LastExecutedAt { get; set; }

    /// <summary>When the next occurrence is due, in UTC ticks.</summary>
    public long? NextExecutionAt { get; set; }

    public bool IsEnabled { get; set; }

    public string? TenantId { get; set; }

    public string? TimeZoneId { get; set; }

    /// <summary>The <see cref="Attributes.MisfirePolicy" />, by number.</summary>
    public int MisfirePolicy { get; set; }

    DateTimeOffset? IRecurringJobRecord.LastExecutedAt
    {
        get => JobRecords.Instant(LastExecutedAt);
        set => LastExecutedAt = value?.UtcTicks;
    }

    DateTimeOffset? IRecurringJobRecord.NextExecutionAt
    {
        get => JobRecords.Instant(NextExecutionAt);
        set => NextExecutionAt = value?.UtcTicks;
    }
}
