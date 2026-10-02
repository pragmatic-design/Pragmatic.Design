using Microsoft.Extensions.Logging;
using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Jobs.Services;

/// <inheritdoc cref="IRecurringJobRegistrar"/>
public sealed partial class RecurringJobRegistrar(
    IRecurringJobStore store,
    IClock clock,
    ILogger<RecurringJobRegistrar> logger) : IRecurringJobRegistrar
{
    /// <inheritdoc/>
    public async Task<bool> RegisterAsync(RecurringJobDefinition definition, CancellationToken ct = default)
    {
        // Persisted scheduling state wins: re-registering on every startup must not rewind a
        // schedule that is already running, nor re-enable a definition an operator disabled.
        var existing = await store.GetAsync(definition.Id, ct).ConfigureAwait(false);

        if (existing?.NextExecutionAt is not null)
        {
            definition.NextExecutionAt = existing.NextExecutionAt;
            definition.LastExecutedAt = existing.LastExecutedAt;
            definition.IsEnabled = existing.IsEnabled;
        }
        else if (!TrySeedFirstOccurrence(definition))
        {
            return false;
        }

        await store.UpsertAsync(definition, ct).ConfigureAwait(false);
        LogRecurringRegistered(definition.Id, definition.JobType, definition.NextExecutionAt);
        return true;
    }

    // A definition whose cron or timezone does not parse is skipped rather than thrown: one bad
    // declaration must not prevent every other recurring job in the host from being registered.
    private bool TrySeedFirstOccurrence(RecurringJobDefinition definition)
    {
        TimeZoneInfo tz;
        try
        {
            tz = !string.IsNullOrEmpty(definition.TimeZoneId)
                ? TimeZoneInfo.FindSystemTimeZoneById(definition.TimeZoneId)
                : TimeZoneInfo.Utc;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            LogRegistrationSkippedBadTimezone(definition.Id, definition.TimeZoneId!, ex);
            return false;
        }

        CronExpression cron;
        try
        {
            cron = CronExpression.Parse(definition.CronExpression);
        }
        catch (Exception ex)
        {
            LogRegistrationSkippedBadCron(definition.Id, definition.CronExpression, ex);
            return false;
        }

        definition.NextExecutionAt = cron.GetNextOccurrence(clock.UtcNow, tz);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Recurring job '{RecurringId}' ({JobType}) registered, next execution at {NextExecution}")]
    partial void LogRecurringRegistered(string recurringId, string jobType, DateTimeOffset? nextExecution);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Recurring job '{RecurringId}' not registered: timezone '{TimeZoneId}' not found on this host")]
    partial void LogRegistrationSkippedBadTimezone(string recurringId, string timeZoneId, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Recurring job '{RecurringId}' not registered: invalid cron expression '{CronExpression}'")]
    partial void LogRegistrationSkippedBadCron(string recurringId, string cronExpression, Exception ex);
}
