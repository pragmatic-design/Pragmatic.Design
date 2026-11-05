namespace Pragmatic.Messaging.Engines;

/// <summary>
///     The fields a saga instance is stored with on each engine: <c>__SagaInstances</c>' columns, as EF Core's <c>SagaEntityTypeConfiguration</c>
///     maps <c>SagaInstance</c> (the state and the status by number, the saga's data as JSON), so the Sql row and the document convert through one
///     mapping (<see cref="SagaRecords{TSaga, TState}" />).
/// </summary>
public interface ISagaInstanceRecord
{
    Guid Id { get; set; }

    string SagaType { get; set; }

    string CorrelationId { get; set; }

    int State { get; set; }

    string? StateData { get; set; }

    int Status { get; set; }

    DateTimeOffset StartedAt { get; set; }

    DateTimeOffset? CompletedAt { get; set; }

    DateTimeOffset? LastStepAt { get; set; }

    DateTimeOffset? TimeoutAt { get; set; }

    string? LastError { get; set; }

    string TenantId { get; set; }

    int Version { get; set; }
}
