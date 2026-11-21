using Pragmatic.Persistence.Documents.Attributes;
using Pragmatic.Persistence.Documents.Schema;

namespace Pragmatic.Jobs.Engines.Documents;

/// <summary>
///     A job instance of the <c>[EnableJobPersistence]</c> boundary the documents engine serves (collection <c>__Jobs</c>), read and written by
///     <see cref="DocJobStore" />. The fields are <see cref="Sql.JobRow" />'s columns, except the instants: UTC ticks, as
///     <c>NotificationDocument</c>'s, because a BSON datetime keeps milliseconds only and a lease would not read back the instant it was written
///     with. <see cref="IJobRecord" /> sees them as instants, through its explicit members.
/// </summary>
[Document("__Jobs")]
public sealed partial class JobDocument : IJobRecord
{
    /// <summary>
    ///     The collection in a boundary's <c>{Boundary}DocSchema</c>: the indexes EF Core's configuration declares, with its names, and on Cosmos
    ///     DB the composite index the poll's ordering needs (priority first, then schedule).
    /// </summary>
    public static readonly DocCollectionSchema Schema = new("__Jobs", "/id",
    [
        new DocIndex("IX___Jobs_Status_Priority_ScheduledFor", ["Status", "Priority", "ScheduledFor"]),
        new DocIndex("IX___Jobs_Status_LeaseExpiresAt", ["Status", "LeaseExpiresAt"]),
        new DocIndex("IX___Jobs_CorrelationId", ["CorrelationId"]),
        new DocIndex("IX___Jobs_CompletedAt", ["CompletedAt"]),
    ], [[("/Priority", true), ("/ScheduledFor", false)]]);

    [Key]
    public Guid Id { get; set; }

    public string JobType { get; set; } = "";

    public string? ParametersJson { get; set; }

    public string? ParameterType { get; set; }

    /// <summary>The <see cref="JobStatus" />, by number.</summary>
    public int Status { get; set; }

    /// <summary>When the job is due, in UTC ticks.</summary>
    public long ScheduledFor { get; set; }

    public int Priority { get; set; }

    public long? StartedAt { get; set; }

    public long? CompletedAt { get; set; }

    public string? Error { get; set; }

    public int Attempt { get; set; }

    public int MaxAttempts { get; set; }

    /// <summary>Until when, in UTC ticks, the lease holds.</summary>
    public long? LeaseExpiresAt { get; set; }

    public string? LeasedBy { get; set; }

    public string? ContinuationJobType { get; set; }

    public string? ContinuationParametersJson { get; set; }

    public string? CorrelationId { get; set; }

    public string? TenantId { get; set; }

    public long CreatedAt { get; set; }

    DateTimeOffset IJobRecord.ScheduledFor
    {
        get => new(ScheduledFor, TimeSpan.Zero);
        set => ScheduledFor = value.UtcTicks;
    }

    DateTimeOffset? IJobRecord.StartedAt
    {
        get => JobRecords.Instant(StartedAt);
        set => StartedAt = value?.UtcTicks;
    }

    DateTimeOffset? IJobRecord.CompletedAt
    {
        get => JobRecords.Instant(CompletedAt);
        set => CompletedAt = value?.UtcTicks;
    }

    DateTimeOffset? IJobRecord.LeaseExpiresAt
    {
        get => JobRecords.Instant(LeaseExpiresAt);
        set => LeaseExpiresAt = value?.UtcTicks;
    }

    DateTimeOffset IJobRecord.CreatedAt
    {
        get => new(CreatedAt, TimeSpan.Zero);
        set => CreatedAt = value.UtcTicks;
    }
}
