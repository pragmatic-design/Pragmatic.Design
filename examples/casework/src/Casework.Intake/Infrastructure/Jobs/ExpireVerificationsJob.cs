using Microsoft.Extensions.Logging;
using Pragmatic.Jobs;
using Pragmatic.Jobs.Attributes;
using Pragmatic.Resilience.Attributes;
using Pragmatic.Temporal.Clock;

namespace Casework.Intake.Infrastructure.Jobs;

/// <summary>
///     Every hour, the cases whose verification deadline has passed stop waiting.
/// </summary>
/// <remarks>
///     <para>
///         Hourly and not daily: the deadline is a date, so the precision that matters is "the day
///         after", and an hour is close enough to it while keeping the sweep cheap. A cron expression and
///         a time zone are all <c>[RecurringJob]</c> takes — it cannot carry a tenant, which is why the
///         work it delegates to says at its own call site how it crosses them.
///     </para>
///     <para>
///         The clock is injected and read <b>here</b>, then passed down: the unit that does the work
///         takes the instant as an argument, so a test seals time by handing it a value instead of
///         replacing a service inside the sweep. <c>DateTimeOffset.UtcNow</c> in either of them would be
///         the one time source no container can replace.
///     </para>
/// </remarks>
[RecurringJob("0 * * * *", TimeZone = "Europe/Rome")]
[Timeout(TimeoutSeconds = 300)]
public sealed partial class ExpireVerificationsJob(
    IExpireOverdueVerifications expiry,
    IClock clock,
    ILogger<ExpireVerificationsJob> logger) : IJob
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var expired = await expiry.RunAsync(clock.UtcNow, ct).ConfigureAwait(false);

        LogRan(expired);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "The deadline sweep expired {Expired} case(s).")]
    private partial void LogRan(int expired);
}
