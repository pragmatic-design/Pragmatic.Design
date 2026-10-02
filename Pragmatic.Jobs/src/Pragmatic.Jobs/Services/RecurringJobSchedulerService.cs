using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pragmatic.Jobs.Configuration;
using Pragmatic.Jobs.Diagnostics;
using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Jobs.Services;

/// <summary>
///     BackgroundService that checks recurring job definitions against their cron schedule
///     and enqueues job instances when due.
/// </summary>
public sealed partial class RecurringJobSchedulerService(
    IServiceScopeFactory scopeFactory,
    IJobTypeRegistry registry,
    IClock clock,
    JobsOptions options,
    ILogger<RecurringJobSchedulerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogSchedulerStarted();

        // Wait for host startup to complete before polling
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);

        // Persist the compile-time definitions before the first poll. Without this the store is
        // empty and GetDueAsync never returns anything, so no [RecurringJob] would ever fire.
        await RegisterDeclaredDefinitionsAsync(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDuePassAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogSchedulerError(ex);
            }

            await Task.Delay(TimeSpan.FromSeconds(options.PollingIntervalSeconds), stoppingToken).ConfigureAwait(false);
        }

        LogSchedulerStopped();
    }

    /// <summary>One scheduling pass: read what is due now, and act on each definition.</summary>
    /// <remarks>
    ///     A named pass rather than the body of the loop, so a test can drive one and assert on the
    ///     call returning instead of waiting for its effect to appear.
    ///     <para>
    ///         ⚠️ Polling for that effect was not a stylistic problem. The loop waits two seconds
    ///         before its first pass and then one interval between passes, so a test had to outlast
    ///         both, and under a saturated thread pool — 106 suites at once — even a thirty-second
    ///         budget was not always enough: one gate run in five was red on
    ///         <c>RunOnce_OnMisfire_EnqueuesTheMissedOccurrence</c> with no defect behind it.
    ///     </para>
    /// </remarks>
    internal async Task RunDuePassAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;

        // Read due definitions under a short-lived scope; the scoped IRecurringJobStore
        // (EF DbContext) must not be captured by this singleton hosted service.
        IReadOnlyList<RecurringJobDefinition> dueJobs;
        using (var readScope = scopeFactory.CreateScope())
        {
            var recurringStore = readScope.ServiceProvider.GetRequiredService<IRecurringJobStore>();
            dueJobs = await recurringStore.GetDueAsync(now, ct).ConfigureAwait(false);
        }

        // Process all due jobs in parallel so a slow store write for one job does not delay
        // scheduling of others. Each task opens its OWN scope (below), so the parallel writes never
        // share a DbContext.
        var tasks = dueJobs.Select(definition => EnqueueRecurringJobAsync(definition, now, ct));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task RegisterDeclaredDefinitionsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var providers = scope.ServiceProvider.GetServices<IRecurringJobProvider>().ToList();
            if (providers.Count == 0)
                return;

            var registrar = scope.ServiceProvider.GetRequiredService<IRecurringJobRegistrar>();

            var registered = 0;
            foreach (var definition in providers.SelectMany(p => p.GetDefinitions()))
            {
                if (await registrar.RegisterAsync(definition, ct).ConfigureAwait(false))
                    registered++;
            }

            LogDefinitionsRegistered(registered);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host is shutting down before the first poll — nothing to report.
        }
        catch (Exception ex)
        {
            // Registration failing must not kill the scheduler: already-persisted definitions
            // from a previous run remain schedulable, and the next restart retries.
            LogDefinitionRegistrationError(ex);
        }
    }

    private async Task EnqueueRecurringJobAsync(RecurringJobDefinition definition, DateTimeOffset now, CancellationToken ct)
    {
        // Compute next occurrence — guard each parse step individually so a bad
        // timezone or cron expression only skips this job, not the entire batch.
        TimeZoneInfo tz;
        try
        {
            tz = !string.IsNullOrEmpty(definition.TimeZoneId)
                ? TimeZoneInfo.FindSystemTimeZoneById(definition.TimeZoneId)
                : TimeZoneInfo.Utc;
        }
        catch (TimeZoneNotFoundException ex)
        {
            LogRecurringSkippedBadTimezone(definition.Id, definition.TimeZoneId!, ex);
            return;
        }

        CronExpression cron;
        try
        {
            cron = CronExpression.Parse(definition.CronExpression);
        }
        catch (Exception ex)
        {
            LogRecurringSkippedBadCron(definition.Id, definition.CronExpression, ex);
            return;
        }

        var nextOccurrence = cron.GetNextOccurrence(now, tz);

        // Capture before claiming: the in-memory store hands out live references and the CAS
        // advances NextExecutionAt in place, so reading it afterwards yielded the NEXT occurrence
        // there and the CURRENT one on EF Core — the same job landed a whole cron period apart
        // depending on which store was configured.
        var occurrence = definition.NextExecutionAt ?? now;

        // A misfire: the occurrence is further in the past than a normal slightly-late poll. On the
        // Skip policy we still advance the schedule (claim), but do not enqueue the stale run.
        var isMisfire = now - occurrence > options.MisfireThreshold;
        var skipEnqueue = isMisfire && definition.MisfirePolicy == Attributes.MisfirePolicy.Skip;

        // Dedicated scope per definition: this method runs concurrently (Task.WhenAll above), so a
        // shared scoped store (EF DbContext) would trigger "second operation on this context".
        using var scope = scopeFactory.CreateScope();
        var recurringStore = scope.ServiceProvider.GetRequiredService<IRecurringJobStore>();
        var jobStore = scope.ServiceProvider.GetRequiredService<IJobStore>();

        // Distributed guard: atomically claim the due definition by advancing NextExecutionAt
        // via compare-and-swap on the value we observed. Every host runs this BackgroundService,
        // so without the claim two instances would both enqueue the same due definition per tick.
        // Only the host that wins the CAS proceeds to enqueue.
        var claimed = await recurringStore.TryClaimDueAsync(
            definition.Id,
            expectedNextExecution: definition.NextExecutionAt,
            nextExecution: nextOccurrence,
            lastExecuted: now,
            ct).ConfigureAwait(false);

        if (!claimed)
        {
            LogRecurringClaimLost(definition.Id, definition.JobType);
            return;
        }

        // Skip policy on a misfire: the claim already advanced the schedule past the stale
        // occurrence, so there is nothing left to enqueue.
        if (skipEnqueue)
        {
            LogRecurringMisfireSkipped(definition.Id, definition.JobType, occurrence);
            return;
        }

        // Enqueue the job instance — ScheduledFor is the actual cron occurrence (not 'now')
        // so that scheduling information is preserved in the job record.
        var job = new JobInstance
        {
            Id = Guid.NewGuid(),
            JobType = definition.JobType,
            ParametersJson = definition.ParametersJson,
            ParameterType = definition.ParameterType,
            Status = JobStatus.Pending,
            ScheduledFor = occurrence,
            Priority = registry.GetPriority(definition.JobType),
            // Same rule as one-off jobs: the job's own [Retry] wins over the global default.
            MaxAttempts = registry.GetRetryPolicy(definition.JobType)?.MaxAttempts ?? options.DefaultMaxRetries,
            TenantId = definition.TenantId,
            CreatedAt = now,
        };

        await jobStore.EnqueueAsync(job, ct).ConfigureAwait(false);

        JobsDiagnostics.RecurringJobsTriggered.Add(1);
        LogRecurringTriggered(definition.Id, definition.JobType, nextOccurrence);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Recurring job scheduler started")]
    partial void LogSchedulerStarted();

    [LoggerMessage(Level = LogLevel.Information, Message = "Recurring job scheduler stopped")]
    partial void LogSchedulerStopped();

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Recurring job '{RecurringId}' triggered: {JobType}, next at {NextOccurrence}")]
    partial void LogRecurringTriggered(string recurringId, string jobType, DateTimeOffset? nextOccurrence);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recurring job scheduler error")]
    partial void LogSchedulerError(Exception ex);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Registered {Count} declared recurring job definition(s)")]
    partial void LogDefinitionsRegistered(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to register declared recurring job definitions")]
    partial void LogDefinitionRegistrationError(Exception ex);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Recurring job '{RecurringId}' ({JobType}) claimed by another host this tick; skipping enqueue")]
    partial void LogRecurringClaimLost(string recurringId, string jobType);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Recurring job '{RecurringId}' ({JobType}) missed occurrence {Occurrence} skipped by misfire policy")]
    partial void LogRecurringMisfireSkipped(string recurringId, string jobType, DateTimeOffset occurrence);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Recurring job '{RecurringId}' skipped: timezone '{TimeZoneId}' not found on this host")]
    partial void LogRecurringSkippedBadTimezone(string recurringId, string timeZoneId, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Recurring job '{RecurringId}' skipped: invalid cron expression '{CronExpression}'")]
    partial void LogRecurringSkippedBadCron(string recurringId, string cronExpression, Exception ex);
}
