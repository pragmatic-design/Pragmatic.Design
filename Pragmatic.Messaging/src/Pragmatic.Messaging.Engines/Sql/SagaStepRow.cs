using Pragmatic.Persistence.Sql.Attributes;

namespace Pragmatic.Messaging.Engines.Sql;

/// <summary>
///     An executed step of a saga instance on the Sql engine (<c>__SagaSteps</c>), written in the save that recorded it. The table is EF Core's,
///     column for column; the foreign key to <c>__SagaInstances</c> (cascading) and its index are added by the generator, as EF Core's.
/// </summary>
[Entity]
[Table("__SagaSteps")]
public sealed class SagaStepRow : ISagaStepRecord
{
    public Guid Id { get; set; }

    public Guid SagaInstanceId { get; set; }

    [MaxLength(256)]
    public string StepName { get; set; } = "";

    public string? Input { get; set; }

    public string? Output { get; set; }

    /// <summary>The <c>SagaStepStatus</c>, by number.</summary>
    public int Status { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    [MaxLength(2048)]
    public string? Error { get; set; }
}
