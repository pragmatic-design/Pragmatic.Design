using Pragmatic.Persistence.Documents.Attributes;
using Pragmatic.Persistence.Documents.Schema;

namespace Pragmatic.Messaging.Engines.Documents;

/// <summary>
///     A saga instance of an <c>[EnableSagaPersistence]</c> boundary the documents engine serves (collection <c>__SagaInstances</c>), read and
///     written by <see cref="DocSagaRepository{TSaga, TState}" />. The fields are <see cref="Sql.SagaInstanceRow" />'s columns, its instants UTC
///     ticks (a BSON date keeps milliseconds only); <see cref="ISagaInstanceRecord" /> sees them as instants, through its explicit members.
/// </summary>
[Document("__SagaInstances")]
public sealed partial class SagaInstanceDocument : ISagaInstanceRecord
{
    /// <summary>
    ///     The collection in a boundary's <c>{Boundary}DocSchema</c>: EF Core's three indexes, by name. The unique one holds the active sagas only
    ///     (a completed saga keeps its correlation in the history without blocking a new one): on MongoDB, the documents whose <see cref="Ended" />
    ///     is false; Cosmos DB has no partial unique key and reports it.
    /// </summary>
    public static readonly DocCollectionSchema Schema = new("__SagaInstances", "/id",
    [
        new DocIndex("IX___SagaInstances_SagaType_CorrelationId_TenantId", ["SagaType", "CorrelationId", "TenantId"], Unique: true, LiveField: nameof(Ended)),
        new DocIndex("IX___SagaInstances_Status_SagaType", ["Status", "SagaType"]),
        new DocIndex("IX___SagaInstances_Status_TimeoutAt", ["Status", "TimeoutAt"]),
    ], []);

    [Key]
    public Guid Id { get; set; }

    public string SagaType { get; set; } = "";

    public string CorrelationId { get; set; } = "";

    public int State { get; set; }

    public string? StateData { get; set; }

    /// <summary>The <c>SagaStatus</c>, by number; <see cref="Ended" /> follows it.</summary>
    public int Status
    {
        get;
        set
        {
            field = value;
            Ended = value != 0;
        }
    }

    /// <summary>
    ///     True once the saga is no longer active (its status not <c>Active</c>): the field the unique index's partial filter reads, which compares
    ///     a boolean. An update that sets the status on the server sets it too.
    /// </summary>
    public bool Ended { get; set; }

    public long StartedAt { get; set; }

    public long? CompletedAt { get; set; }

    public long? LastStepAt { get; set; }

    /// <summary>The deadline of the current step, in UTC ticks: what the timeout scan compares.</summary>
    public long? TimeoutAt { get; set; }

    public string? LastError { get; set; }

    public string TenantId { get; set; } = "";

    public int Version { get; set; }

    DateTimeOffset ISagaInstanceRecord.StartedAt
    {
        get => new(StartedAt, TimeSpan.Zero);
        set => StartedAt = value.UtcTicks;
    }

    DateTimeOffset? ISagaInstanceRecord.CompletedAt
    {
        get => Instant(CompletedAt);
        set => CompletedAt = value?.UtcTicks;
    }

    DateTimeOffset? ISagaInstanceRecord.LastStepAt
    {
        get => Instant(LastStepAt);
        set => LastStepAt = value?.UtcTicks;
    }

    DateTimeOffset? ISagaInstanceRecord.TimeoutAt
    {
        get => Instant(TimeoutAt);
        set => TimeoutAt = value?.UtcTicks;
    }

    private static DateTimeOffset? Instant(long? ticks) => ticks is { } t ? new DateTimeOffset(t, TimeSpan.Zero) : null;
}
