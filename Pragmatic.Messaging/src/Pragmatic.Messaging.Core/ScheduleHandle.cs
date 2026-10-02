namespace Pragmatic.Messaging;

/// <summary>
///     Broker handle of a scheduled message: what a transport needs to cancel it later
///     (for ASB, the topic and the broker-assigned sequence number).
/// </summary>
public sealed record ScheduleHandle(Guid ScheduleId, string Topic, long SequenceNumber);
