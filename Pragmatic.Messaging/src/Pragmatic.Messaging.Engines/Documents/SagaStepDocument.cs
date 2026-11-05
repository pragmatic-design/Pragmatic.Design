using Pragmatic.Persistence.Documents.Attributes;
using Pragmatic.Persistence.Documents.Schema;

namespace Pragmatic.Messaging.Engines.Documents;

/// <summary>
///     An executed step of a saga instance on the documents engine (collection <c>__SagaSteps</c>), written in the save that recorded it. The
///     fields are <see cref="Sql.SagaStepRow" />'s columns, its instants UTC ticks.
/// </summary>
[Document("__SagaSteps")]
public sealed partial class SagaStepDocument : ISagaStepRecord
{
    /// <summary>The collection in a boundary's <c>{Boundary}DocSchema</c>: the index the executed steps of an instance are read by, as EF Core's.</summary>
    public static readonly DocCollectionSchema Schema = new("__SagaSteps", "/id",
    [
        new DocIndex("IX___SagaSteps_SagaInstanceId", ["SagaInstanceId"]),
    ], []);

    [Key]
    public Guid Id { get; set; }

    public Guid SagaInstanceId { get; set; }

    public string StepName { get; set; } = "";

    public string? Input { get; set; }

    public string? Output { get; set; }

    /// <summary>The <c>SagaStepStatus</c>, by number.</summary>
    public int Status { get; set; }

    public long StartedAt { get; set; }

    public long? CompletedAt { get; set; }

    public string? Error { get; set; }

    DateTimeOffset ISagaStepRecord.StartedAt
    {
        get => new(StartedAt, TimeSpan.Zero);
        set => StartedAt = value.UtcTicks;
    }

    DateTimeOffset? ISagaStepRecord.CompletedAt
    {
        get => CompletedAt is { } t ? new DateTimeOffset(t, TimeSpan.Zero) : null;
        set => CompletedAt = value?.UtcTicks;
    }
}
