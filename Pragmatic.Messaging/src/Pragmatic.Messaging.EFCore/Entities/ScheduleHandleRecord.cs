namespace Pragmatic.Messaging.EFCore.Entities;

/// <summary>
///     Persisted broker handle of a scheduled message (see <see cref="Pragmatic.Messaging.ScheduleHandle"/>).
///     Row lifetime: insert on schedule, delete on cancel; rows for messages that were simply
///     delivered linger and are cheap — purge with a retention job if volume matters.
/// </summary>
public sealed class ScheduleHandleRecord
{
    /// <summary>The schedule id returned to the caller.</summary>
    public Guid ScheduleId { get; set; }

    /// <summary>Topic or queue the message was scheduled on.</summary>
    public string Topic { get; set; } = "";

    /// <summary>Broker-assigned sequence number (the ASB cancel token).</summary>
    public long SequenceNumber { get; set; }

    /// <summary>When the schedule was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
