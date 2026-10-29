namespace Pragmatic.Events.Engines;

/// <summary>One event as an outbox row holds it, before an engine's type: its instants in UTC ticks (<see cref="EventOutboxCapture" />).</summary>
internal readonly record struct CapturedEvent(Guid Id, string EventType, string Payload, long OccurredAt, string? TenantId, string? TraceParent, long CreatedAt);
