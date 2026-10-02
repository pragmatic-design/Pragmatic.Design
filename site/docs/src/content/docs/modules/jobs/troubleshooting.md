---
title: "Troubleshooting"
description: "Practical problem/solution guide for Pragmatic.Jobs. Each section covers a common issue, the likely causes, and the fix."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Jobs/docs/troubleshooting.md
sidebar:
  order: 4
---
Practical problem/solution guide for Pragmatic.Jobs. Each section covers a common issue, the likely causes, and the fix.

---

## Job Not Executing

You have a job class that compiles, but it never runs at runtime.

### Checklist

1. **Does the class have `[Job]` or `[RecurringJob]`?** Without the attribute, the SG does not discover the class. It compiles fine but has no invoker, no DI registration, and no type registry entry.

2. **Is the class `partial`?** Without it `PRAG2502` is an error on the declaration, and the generator generates nothing for the job: no invoker, no registration, so it never runs.

3. **Does the class implement `IJob` or `IJob<T>`?** Diagnostic `PRAG2500` fires if the interface is missing. The SG will not generate an invoker.

4. **Is the SG analyzer referenced?** In your `.csproj`, the `Pragmatic.SourceGenerator` must be referenced with `OutputItemType="Analyzer"`:

   ```xml
   <ProjectReference Include="..\Pragmatic.SourceGenerator\...\Pragmatic.SourceGenerator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
   ```

5. **Are processing services registered?**

   With Composition (automatic):
   ```csharp
   await PragmaticApp.RunAsync(args, app =>
   {
       app.UseJobs();
   });
   ```

   Without Composition (manual -- all three calls are required):
   ```csharp
   builder.Services.AddPragmaticJobs();
   builder.Services.AddDiscoveredJobs();         // SG-generated: jobs + type registry + recurring provider
   builder.Services.AddJobProcessingServices();  // Registers the BackgroundServices
   ```

   Missing `AddDiscoveredJobs()` is the most common cause: no recurring definition is declared, and a scheduled job fails with `Unknown job type: … No registry is registered`.

6. **Check the SG output.** In Visual Studio, expand **Dependencies > Analyzers > Pragmatic.SourceGenerator** in Solution Explorer. Look for `{JobType}.Invoker.g.cs` and `_Infra.Jobs.Registration.g.cs`. If these files do not exist, the SG is not processing your class.

7. **Does the registry know the job?** `IJobTypeRegistry` is a composite over one `IJobTypeRegistrySource` per assembly whose `AddDiscoveredJobs()` ran. Resolve `IJobTypeRegistry` and call `Knows(typeof(TJob).FullName!)`; a failed job's error names how many registries were asked.

8. **Is the `ScheduledFor` time in the future?** `JobProcessorService` only fetches jobs where `ScheduledFor <= now`. If you scheduled a delayed job, verify the timestamp.

9. **Check the logs.** `JobProcessorService` logs at startup: `"Job processor started: worker={WorkerId}, concurrency={WorkerCount}"`. If you do not see this message, the service is not running. Look for `"Job processor poll error"` messages that indicate exceptions during polling.

---

## Recurring Job Not Registered

A recurring job exists in code but does not appear in `__RecurringJobs` and never fires.

### Checklist

1. **Does the class have `[RecurringJob("cron")]`?** Not `[Job]` -- only `[RecurringJob]` generates entries in `_Infra.Jobs.RecurringJobs.g.cs`.

2. **Is the cron expression valid?** Only an *empty* expression triggers `PRAG2501` at build time. A malformed but non-empty expression builds cleanly and is skipped at startup -- look for `"Recurring job '{RecurringId}' not registered: invalid cron expression"` in the logs. An unknown `TimeZone` produces the equivalent timezone warning.

3. **Check `_Infra.Jobs.RecurringJobs.g.cs`.** Open the SG output and verify your job appears among the definitions returned by `PragmaticRecurringJobProvider.GetDefinitions()`.

4. **Is the definition persisted at startup?** At startup `RecurringJobSchedulerService` passes each declared definition to `IRecurringJobRegistrar`, which computes the first occurrence from the cron expression and upserts it. Look for `"Registered {Count} declared recurring job definition(s)"`. If the store is EF Core, check the `__RecurringJobs` table -- a row with a null `NextExecutionAt` is never due.

5. **Is `RecurringJobSchedulerService` running?** Check the logs for `"Recurring job scheduler started"`. If absent, `AddJobProcessingServices()` was not called. If it runs and registers nothing, `AddDiscoveredJobs()` was not called, so no recurring provider exists.

6. **Did the schedule already advance?** Persisted state wins over the declared definition. Changing the cron expression in code does not rewind a definition that already has a `NextExecutionAt`, and re-deploying does not re-enable a definition that was disabled via `DisableAsync()`. Update or delete the `__RecurringJobs` row to adopt a new schedule.

7. **Is the timezone correct?** If you specify `TimeZone = "America/New_York"`, the cron expression is evaluated in that timezone. A job scheduled for `"0 2 * * *"` in New York might not fire at the UTC time you expect.

8. **Is the definition disabled?** `RecurringJobDefinition.IsEnabled` defaults to `true`, but if something calls `IRecurringJobStore.DisableAsync()`, the job will not be picked up -- and a redeploy will not turn it back on.

---

## Lease Conflicts (Multiple Workers)

The `pragmatic.jobs.lease_conflicts` counter is high, meaning workers are competing for the same jobs.

### Checklist

1. **Is the batch size too large?** If `BatchSize = 100` and `WorkerCount = 2`, each poll fetches far more jobs than the workers can process before the next poll. Reduce `BatchSize` to `WorkerCount * 2` or `WorkerCount * 3`.

2. **Is the polling interval too short?** If multiple instances poll every second, lease conflicts increase. The default 5 seconds is usually sufficient.

3. **Are leases expiring too fast?** If `LeaseTimeSeconds` is shorter than the typical job execution time, the lease expires mid-execution. Another worker picks up the same job. Increase `LeaseTimeSeconds` to be at least 2x the expected maximum job duration.

---

## Diagnostics Reference

The source generator emits the following diagnostics for common mistakes:

| ID | Severity | Title | Message |
|----|----------|-------|---------|
| PRAG2500 | Error | Job class must implement IJob or IJob<T> | Type '{0}' is decorated with [Job]/[RecurringJob] but does not implement IJob or IJob<T> |
| PRAG2501 | Error | Invalid cron expression | [RecurringJob] on '{0}' has an empty or missing cron expression |
| PRAG2502 | Error | Job class must be partial | Type '{0}' is decorated with [Job]/[RecurringJob] but is not declared as partial, so no invoker is generated and the job never runs |
| PRAG2503 | Error | Duplicate recurring job ID | Recurring job ID '{0}' is used by both '{1}' and '{2}' |
| PRAG2504 | Error | Invalid retry configuration | [Retry] on '{0}' has MaxAttempts = {1}. MaxAttempts must be greater than 0 |
| PRAG2505 | Error | Continuation must be a job | [Continuation<{0}>] on '{1}': type '{0}' does not implement IJob or IJob<T> |
| PRAG2506 | Error | Continuation cycle detected | Job '{0}' creates a cycle in continuation chain: {1} |

### Reading the Diagnostics

- **PRAG2500**: Add `: IJob` or `: IJob<TParams>` to your class declaration. No invoker is generated for the class, so this must be fixed before anything else compiles.
- **PRAG2501**: Provide a cron expression. This fires only when the expression is empty or whitespace -- field-count and syntax validation happen at runtime, where a bad expression is logged and skipped.
- **PRAG2502**: Add the `partial` keyword to your class declaration. The "Make class partial" code fix does it.
- **PRAG2503**: Specify a unique `Id` in `[RecurringJob(cron, Id = "unique-id")]`, or rely on auto-generated IDs by omitting the `Id` property.
- **PRAG2504**: Set `MaxAttempts` to a positive integer. If you want no retry, omit `[Retry]` entirely.
- **PRAG2505**: The type argument in `[Continuation<T>]` must implement `IJob` or `IJob<T>`. Check the target class declaration.
- **PRAG2506**: Remove `[Continuation<T>]` from one of the jobs in the cycle to break it. The diagnostic message shows the full chain path.

---

## FAQ

### Q: Can I use `[RecurringJob]` and `[Job]` on the same class?

No. A class should have either `[RecurringJob]` (for cron-based scheduling) or `[Job]` (for on-demand scheduling), not both. If you need the same job logic to run on a schedule and also on demand, extract the logic into a shared service and create two separate job classes.

### Q: What happens if my job throws an exception?

The exception propagates out of the generated invoker to `JobProcessorService`, which records the attempt and asks the store to mark the job failed. The error message is stored in `JobInstance.Error`.

If the new attempt count is below `MaxAttempts`, the job returns to `Pending` with the retry delay applied to `ScheduledFor` and is picked up again on a later poll. Once the count reaches `MaxAttempts`, the job is terminal `Failed`.

Retry is durable: the attempt count lives in the job row, so it is not lost if the worker crashes mid-attempt.

If `[Retry]` is not configured, `JobsOptions.DefaultMaxRetries` applies (default: 1, meaning no retry) and the job is marked `Failed` on the first exception.

### Q: How is `MaxAttempts` counted?

It is the total number of executions, **including the first**. `[Retry(MaxAttempts = 3)]` allows the initial run plus two retries. `JobContext.Attempt` is zero-based, so it reads `0` on the first execution and `2` on the last permitted one.

### Q: My job takes longer than the lease. Will it run twice?

No. While a job executes, the processor renews the lease by heartbeat at half the lease interval, so a long-running job keeps its lease. `MarkCompletedAsync` and `MarkFailedAsync` are additionally fenced on the worker ID, so a worker that did lose its lease cannot overwrite the outcome recorded by the new owner.

### Q: What happens to a running job when the host shuts down?

Its lease is released without counting an attempt, so it returns to `Pending` and is picked up immediately -- by another instance, or by this one after restart -- instead of waiting for the lease to expire.

### Q: How long are finished jobs kept?

`JobsOptions.RetentionDays` (default 30). Terminal jobs older than that are deleted in batches of `PurgeBatchSize` (default 1000), roughly once an hour. Setting `RetentionDays = 0` disables the purge.

Keep in mind that a job row retains its serialized parameters, which frequently contain personal data. Disabling the purge keeps that payload indefinitely.

### Q: How do I cancel a scheduled job?

Use `IJobScheduler.CancelAsync(jobId)`. The `jobId` is the `Guid` returned by `ScheduleAsync` or `ScheduleAtAsync`. This marks the job as `Cancelled` in the store. If the job is already running, cancellation depends on whether your `ExecuteAsync` observes the `CancellationToken`.

```csharp
var jobId = await scheduler.ScheduleAsync<SendReminderJob, ReminderParams>(
    new ReminderParams(reservationId, email),
    delay: TimeSpan.FromHours(24));

// Later, if the reservation is cancelled:
await scheduler.CancelAsync(jobId);
```

### Q: Can I have multiple workers processing different job types?

The current architecture uses a single `JobProcessorService` that processes all job types. Workers are generic -- any worker can process any job. This is by design: the lease mechanism ensures each job is processed by exactly one worker. If you need dedicated workers for specific job types, deploy separate app instances with different job assemblies.

### Q: How does the InMemory store behave with multiple workers?

`InMemoryJobStore` and `InMemoryRecurringJobStore` are singletons within a single process. They work with `WorkerCount > 1` (multiple tasks in the same process) but do NOT work across multiple processes or containers. For multi-instance deployments, use `UseEfCore()`.

### Q: What happens if the cron timezone is invalid?

`RecurringJobSchedulerService` calls `TimeZoneInfo.FindSystemTimeZoneById()`, which throws `TimeZoneNotFoundException` for invalid IANA names on systems that do not support them. On Linux, IANA names are natively supported. On Windows, .NET 10 supports IANA names via ICU. If you see timezone errors, ensure your runtime supports the specified timezone.

### Q: How do I see what jobs are pending/running/failed?

Query `IJobStore` directly. In EF Core mode, query the `__Jobs` table:

```sql
-- Pending jobs
SELECT * FROM __Jobs WHERE Status = 0 ORDER BY ScheduledFor;

-- Failed jobs with error details
SELECT Id, JobType, Error, Attempt, MaxAttempts FROM __Jobs WHERE Status = 3;

-- Currently running (leased) jobs
SELECT Id, JobType, LeasedBy, LeaseExpiresAt FROM __Jobs WHERE Status = 1;
```

### Q: My continuation job does not receive the parameters from the parent job. Why?

Continuation jobs receive the `CorrelationId` and `TenantId` from the parent job, not its parameters. The continuation also gets its own declared retry budget rather than inheriting the parent's. If the continuation needs data from the parent job, either:
- Use `CorrelationId` to look up the data in a shared store.
- Store the result of the parent job in a database and query it in the continuation.

---

## Observability Debugging

### OpenTelemetry Spans Not Appearing

1. **Is the ActivitySource configured?** Add `Pragmatic.Jobs` to your OpenTelemetry tracing configuration:

   ```csharp
   builder.Services.AddOpenTelemetry()
       .WithTracing(tracing => tracing
           .AddSource("Pragmatic.Jobs"));
   ```

2. **Are spans being sampled out?** Check your sampler configuration. With `AlwaysOnSampler`, all spans are captured.

### Metrics Not Appearing

1. **Is the Meter configured?** Add `Pragmatic.Jobs` to your OpenTelemetry metrics configuration:

   ```csharp
   builder.Services.AddOpenTelemetry()
       .WithMetrics(metrics => metrics
           .AddMeter("Pragmatic.Jobs"));
   ```

2. Metrics available: `pragmatic.jobs.enqueued`, `pragmatic.jobs.completed`, `pragmatic.jobs.failed`, `pragmatic.jobs.retried`, `pragmatic.jobs.duration`, `pragmatic.jobs.lease_acquisitions`, `pragmatic.jobs.lease_conflicts`, `pragmatic.jobs.recurring_triggered`.

---

## Job Stuck in Running State

A job appears as `Running` (Status = 1) in `__Jobs` but no worker is actively processing it.

### Checklist

1. **Did the worker crash mid-execution?** When a worker crashes, the lease remains until `LeaseExpiresAt`. `JobProcessorService` calls `IJobStore.ReleaseExpiredLeasesAsync()` at the start of each poll cycle. Once the lease expires, the job becomes eligible for reprocessing and the crashed attempt is counted -- so a job that repeatedly kills its worker ends up `Failed` rather than looping forever.

2. **Is `LeaseTimeSeconds` too long?** The default is 300 (5 minutes). If the worker crashed, you must wait for the full lease duration before another worker picks up the job. For fast jobs, reduce the lease time. Note that a long-running job does not need a lease longer than its runtime: the lease is renewed by heartbeat while the job executes.

3. **Check `LeasedBy` in the database.** If the value matches a worker that no longer exists, wait for `LeaseExpiresAt` to pass. Alternatively, manually reset the job:

   ```sql
   UPDATE __Jobs
   SET Status = 0, LeasedBy = NULL, LeaseExpiresAt = NULL
   WHERE Id = 'your-job-id' AND Status = 1;
   ```

4. **Is the job genuinely long-running?** Check the `StartedAt` timestamp. If the job started recently and your `[Timeout]` is high, it may still be executing normally.

---

## Job Parameters Deserialization Failure

A parametric job (`IJob<T>`) fails with a JSON deserialization error.

### Checklist

1. **Is the parameter type JSON-serializable?** The SG-generated `IJobTypeRegistry` uses `System.Text.Json` for serialization. Ensure your parameter record/class has a parameterless constructor or uses `record` types (which serialize correctly by default).

2. **Did the parameter type change after jobs were already enqueued?** If you rename properties or change the type structure, already-enqueued jobs in `__Jobs` have the old JSON format. They will fail on deserialization. Clear or migrate old job records.

3. **Are there unsupported types in the parameters?** Avoid `interface` properties, `Func<>`, `Expression<>`, or circular references. Stick to simple records with primitive types, `Guid`, `DateTimeOffset`, `string`, and `decimal`.

---

## EF Core Migration Issues

### Tables Not Created

The `__Jobs` and `__RecurringJobs` tables are created by `EfCoreJobStore` and `EfCoreRecurringJobStore` entity configurations. If they do not appear:

1. **Are both calls present?** `UseEfCore()` drops the in-memory stores; `UseEfCorePersistence()` (from `Pragmatic.Jobs.EFCore`) registers the EF Core ones:
   ```csharp
   app.UseJobs(jobs =>
   {
       jobs.UseEfCore();
       jobs.UseEfCorePersistence();
   });
   ```
   With only the first, the host refuses to start and names the missing call.

2. **Are the entity configurations applied?** The tables exist only if your `DbContext` maps them:
   ```csharp
   modelBuilder.ApplyConfiguration(new JobEntityTypeConfiguration());
   modelBuilder.ApplyConfiguration(new RecurringJobEntityTypeConfiguration());
   ```

3. **Was `EnsureCreated` or a migration run?** Ensure your database migration includes `__Jobs` and `__RecurringJobs`.

4. **Is the base `DbContext` resolvable?** The EF Core stores depend on `DbContext`. If your context is registered under its concrete type only, forward it: `services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>())`.

5. **Check the connection string.** The EF Core stores use the same `DbContext` as your application. Verify the connection string points to the correct database.

---

## Getting Help

- **Showcase examples**: See `NoShowDetectionJob` and `SendCheckInReminderJob` in `examples/showcase/src/Showcase.Booking/Infrastructure/Jobs/` for working examples of both recurring and delayed jobs.
- **Integration tests**: `examples/showcase/tests/Showcase.IntegrationTests/Jobs/` contains tests for job infrastructure and EF Core stores.
- **Architecture**: Read [concepts.md](/modules/jobs/concepts/) for the full pipeline explanation and decision tree.
- **Common mistakes**: Read [common-mistakes.md](/modules/jobs/common-mistakes/) for the most frequent issues with code examples.
