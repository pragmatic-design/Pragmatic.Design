using Pragmatic.Jobs;
using Pragmatic.Jobs.Attributes;

namespace Pragmatic.Jobs.Samples;

/// <summary>
///     Example: recurring cleanup job with timezone awareness.
///     Runs every Sunday at midnight CET — good for maintenance windows.
/// </summary>
[RecurringJob("0 0 * * 0", Id = "cleanup-expired-sessions", TimeZone = "Europe/Rome")]
public sealed partial class CleanupExpiredSessionsJob : IJob
{
    public Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        Console.WriteLine($"[{context.ScheduledAt:u}] Cleaning up expired sessions...");
        // In real code: dbContext.Sessions.Where(s => s.ExpiresAt < now).ExecuteDeleteAsync()
        return Task.CompletedTask;
    }
}
