namespace Pragmatic.Events.Engines;

/// <summary>
///     An outbox row as the delivery pass reads and marks it, whichever engine stores it (<see cref="Sql.EventOutboxRow" />,
///     <see cref="Documents.EventOutboxDocument" />). Its instants are UTC ticks, as EF Core's <c>__EventOutbox</c> stores them: one table, whichever
///     engine writes it.
/// </summary>
internal interface IEventOutboxRecord
{
    Guid Id { get; }

    string EventType { get; }

    string Payload { get; }

    string? TenantId { get; }

    string? TraceParent { get; }

    long? ProcessedAt { get; set; }

    int Attempts { get; set; }

    string? LastError { get; set; }

    string? ClaimedBy { get; set; }

    long? ClaimedUntil { get; set; }
}
