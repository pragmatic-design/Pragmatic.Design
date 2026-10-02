using Pragmatic.Jobs;
using Pragmatic.Jobs.Attributes;
using Pragmatic.Resilience.Attributes;

namespace Showcase.Booking.Infrastructure.Jobs;

/// <summary>
///     Runs every hour to detect no-show reservations.
///     A reservation with CheckIn in the past and status Confirmed is a no-show candidate.
/// </summary>
[RecurringJob("0 * * * *", Id = "no-show-detection")]
[Retry(MaxAttempts = 2)]
[Timeout(TimeoutSeconds = 120)]
// What follows a no-show is putting the room back on sale. Declared here instead of scheduled from
// inside the body: the processor enqueues it only after this job completes, so a detection pass that
// fails and is retried never releases anything, and the release carries its own retry budget.
[Continuation<ReleaseNoShowRoomsJob>]
public sealed partial class NoShowDetectionJob : IJob
{
    /// <summary>
    ///     Deliberately does nothing: this job exists to demonstrate the scheduling attributes above —
    ///     cron, retry and timeout — not no-show detection. A real one would load the confirmed
    ///     reservations past their check-in and mark them, through the repository and the boundary
    ///     actions.
    /// </summary>
    public Task ExecuteAsync(JobContext context, CancellationToken ct) => Task.CompletedTask;
}
