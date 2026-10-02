using Pragmatic.Jobs;
using Pragmatic.Jobs.Attributes;
using Pragmatic.Resilience.Attributes;

namespace Pragmatic.Jobs.Samples;

/// <summary>
///     Example: delayed job with typed parameters.
///     Scheduled via IJobScheduler.ScheduleAsync&lt;SendReminderJob, ReminderParams&gt;(...).
/// </summary>
public record ReminderParams(Guid ReservationId, string GuestEmail);

[Job]
[Retry(MaxAttempts = 2)]
public sealed partial class SendReminderJob : IJob<ReminderParams>
{
    public Task ExecuteAsync(ReminderParams parameters, JobContext context, CancellationToken ct)
    {
        Console.WriteLine($"Sending reminder to {parameters.GuestEmail} for reservation {parameters.ReservationId}");
        return Task.CompletedTask;
    }
}
