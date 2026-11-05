namespace Pragmatic.Messaging.Engines;

/// <summary>
///     The fields an executed saga step is stored with on each engine: <c>__SagaSteps</c>' columns, as EF Core's <c>SagaEntityTypeConfiguration</c>
///     maps <c>SagaStep</c> (the status by number).
/// </summary>
public interface ISagaStepRecord
{
    Guid Id { get; set; }

    Guid SagaInstanceId { get; set; }

    string StepName { get; set; }

    string? Input { get; set; }

    string? Output { get; set; }

    int Status { get; set; }

    DateTimeOffset StartedAt { get; set; }

    DateTimeOffset? CompletedAt { get; set; }

    string? Error { get; set; }
}
