# Architecture and Core Concepts

This guide explains **why** Pragmatic.Jobs exists, how its pieces fit together, and how to choose the right abstraction for each situation. Read this before diving into the individual feature guides.

---

## The Problem

Background job processing in .NET typically means pulling in Hangfire or Quartz.NET. Both are capable, but both rely on runtime reflection for job discovery, serialization, and invocation. They require separate dashboard packages, separate storage providers, and separate retry configuration that lives outside your job definition.

### The Hangfire approach: config is separate from the job

```csharp
// Job class -- knows nothing about how it will be scheduled
public class DailyReportJob
{
    private readonly IReportService _reports;

    public DailyReportJob(IReportService reports) => _reports = reports;

    public async Task Execute()
        => await _reports.GenerateDailyAsync(DateTime.UtcNow.Date);
}

// Somewhere else -- scheduling config is disconnected from the job
RecurringJob.AddOrUpdate<DailyReportJob>(
    "daily-report",
    x => x.Execute(),                   // Expression tree -> reflection at runtime
    "0 2 * * *",
    new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome") });

// Yet another place -- retry is global or per-filter
GlobalJobFilters.Filters.Add(new AutomaticRetryAttribute { Attempts = 3 });
```

Problems with this approach:

1. **Configuration is scattered.** The job class, the schedule registration, and the retry policy all live in different places. To understand what a job does, you need to look in three locations.

2. **Reflection-heavy.** `RecurringJob.AddOrUpdate` uses expression trees that are evaluated at runtime. `Type.GetType()` is used for deserialization. Neither is AOT-safe.

3. **No compile-time validation.** If you typo a cron expression, rename a job class, or create a cycle between continuations, you find out at runtime -- or not at all.

4. **Retry logic is external.** Different jobs need different retry strategies, but configuring per-job retry requires filter attributes evaluated at runtime.

5. **No distributed locking out of the box.** You need additional configuration for multi-instance deployments.

---

## The Solution

With Pragmatic.Jobs, you declare scheduling, retry, and timeout directly on the job class. The source generator reads these attributes at compile time and produces an invoker with linked cancellation tokens, telemetry, and DI wiring -- zero reflection. Retry is not a loop inside the invoker: the declared policy is surfaced to the runtime and applied by the store, so an attempt is durable.

```csharp
[RecurringJob("0 2 * * *", Id = "daily-report")]
[Retry(MaxAttempts = 3, Strategy = BackoffStrategy.ExponentialWithJitter, BaseDelayMs = 1000)]
[Timeout(TimeoutSeconds = 600)]
public sealed partial class DailyReportJob(IReportService reports) : IJob
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
        => await reports.GenerateDailyAsync(context.ScheduledAt.Date, ct);
}
```

That is the entire job definition. Everything a developer needs to know about how this job is scheduled, retried, and timed out is right here in the class declaration.

The source generator produces:

| Generated File | Purpose |
|---------------|---------|
| `DailyReportJob.Invoker.g.cs` | Nested `Invoker` — DI resolution + timeout + telemetry |
| `_Infra.Jobs.Registration.g.cs` | `AddDiscoveredJobs()` — DI registration, the assembly's `IJobTypeRegistrySource`, recurring provider |
| `_Infra.Jobs.TypeRegistry.g.cs` | AOT-safe `switch` expression (no `Type.GetType`) |
| `_Infra.Jobs.RecurringJobs.g.cs` | `PragmaticRecurringJobProvider` — the declared recurring definitions |
| `_Metadata.Jobs.g.cs` | Job metadata for host aggregation |

### What you gain

- **Single source of truth.** Schedule, retry, timeout, and continuation are all declared on the job class.
- **Durable retry.** The attempt count lives in the job row, not in a worker's memory, so a crash mid-attempt does not restart the budget.
- **Compile-time validation.** Missing job interface (PRAG2500), empty cron expressions (PRAG2501), duplicate recurring IDs (PRAG2503), non-job continuation targets (PRAG2505), continuation cycles (PRAG2506) -- all caught at build time.
- **AOT-safe.** The generated `IJobTypeRegistry` uses a `switch` expression on string FQNs. No `Type.GetType()`, no reflection.
- **Distributed-safe.** Lease-based locking works out of the box with EF Core persistence.
- **Observable.** Every execution creates an OpenTelemetry span with job type, attempt, and correlation ID. Metrics for enqueued, completed, failed, retried, and duration are built in.

---

## How It Works

The job processing pipeline has four stages: **Definition**, **Generation**, **Scheduling**, and **Execution**.

### Stage 1: Definition

You write a class that implements `IJob` (parameterless) or `IJob<T>` (typed parameters) and decorate it with `[Job]` or `[RecurringJob]`:

```csharp
[Job]
[Retry(MaxAttempts = 3)]
public sealed partial class SendReminderJob : IJob<ReminderParams>
{
    public async Task ExecuteAsync(ReminderParams p, JobContext context, CancellationToken ct)
    {
        // Your business logic
    }
}
```

### Stage 2: Generation (Compile Time)

The source generator (`JobsFeature` in `Pragmatic.SourceGenerator`) processes every class with `[Job]` or `[RecurringJob]`:

1. **Transform**: `JobTransform` extracts attribute data into an immutable `JobModel` record.
2. **Validate**: `JobsFeature.ValidateJobs` runs aggregate diagnostics (duplicate IDs, continuation cycles).
3. **Template**: `JobInvokerTemplate` generates the per-job invoker. `JobRegistrationTemplate`, `JobTypeRegistryTemplate`, `RecurringJobRegistrationTemplate`, and `JobMetadataTemplate` generate infrastructure files.

### Stage 3: Scheduling (Runtime)

Jobs enter the system in two ways:

- **Recurring**: at startup `RecurringJobSchedulerService` collects every registered `IRecurringJobProvider` and hands each definition to `IRecurringJobRegistrar`, which computes the first occurrence from the cron expression and persists it. It then polls `IRecurringJobStore` every N seconds and, when a definition is due, atomically claims it (`TryClaimDueAsync`) and enqueues a `JobInstance` into `IJobStore`.
- **On-demand**: Your code calls `IJobScheduler.ScheduleAsync<TJob>(...)` which creates a `JobInstance` with the appropriate `ScheduledFor` timestamp.

Persisted scheduling state always wins over the declared definition. Restarting the host does not rewind a schedule that is already running, nor re-enable a definition an operator disabled.

### Stage 4: Execution (Runtime)

`JobProcessorService` is a `BackgroundService` that:

1. Releases expired leases from crashed workers, and purges finished jobs past the retention window.
2. Polls `IJobStore.GetPendingAsync()` for jobs due now.
3. Acquires a lease (optimistic lock) on each job, renewing it by heartbeat while the job runs.
4. Resolves the job from DI via `IJobTypeRegistry.ExecuteAsync()`.
5. Executes the generated invoker (which wraps your `ExecuteAsync` with the timeout and telemetry).
6. Marks the job completed, or failed with the declared retry backoff applied to `ScheduledFor`.
7. If completed and a continuation is declared, enqueues the next job.

Each job runs in its own DI scope, with the job's originating tenant restored onto that scope.

```
[RecurringJob] ──> SG ──> RecurringJobSchedulerService ──> IJobStore
                                                              │
[Job] ──> IJobScheduler.ScheduleAsync() ──────────────────────┘
                                                              │
                                                              v
                                                     JobProcessorService
                                                              │
                                              ┌───────────────┼───────────────┐
                                              v               v               v
                                        Worker 1        Worker 2        Worker N
                                              │               │               │
                                       TryAcquireLease  TryAcquireLease  TryAcquireLease
                                              │               │               │
                                       IJobTypeRegistry.ExecuteAsync()
                                              │
                                       Generated Invoker
                                       (timeout + telemetry)
                                              │
                                       Your ExecuteAsync()
                                              │
                                    MarkCompleted / MarkFailed
                                    (retry backoff applied by the store)
```

---

## Decision Tree: Which Job Type?

### IJob vs IJob<T>

| Scenario | Interface | Example |
|----------|-----------|---------|
| No input data needed | `IJob` | Daily report, session cleanup, health check |
| Needs input data | `IJob<T>` | Send email to specific guest, generate invoice for reservation |

```csharp
// Parameterless -- used for recurring tasks that derive context from the schedule
public sealed partial class CleanupExpiredSessionsJob : IJob
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var cutoff = context.ScheduledAt.AddDays(-30);
        // ...
    }
}

// Typed parameters -- used for on-demand jobs that need specific input
public record ReminderParams(Guid ReservationId, Guid GuestId, string GuestEmail);

public sealed partial class SendCheckInReminderJob : IJob<ReminderParams>
{
    public async Task ExecuteAsync(ReminderParams p, JobContext context, CancellationToken ct)
    {
        // Send email to p.GuestEmail
    }
}
```

### Recurring vs One-Off vs Delayed

| Scenario | Attribute | Scheduling |
|----------|-----------|------------|
| Fixed schedule (cron) | `[RecurringJob("0 2 * * *")]` | Automatic via `RecurringJobSchedulerService` |
| Fire-and-forget (now) | `[Job]` | `scheduler.ScheduleAsync<T>()` |
| Delayed (in N hours) | `[Job]` | `scheduler.ScheduleAsync<T>(delay: TimeSpan.FromHours(24))` |
| At specific time | `[Job]` | `scheduler.ScheduleAtAsync<T>(scheduledFor: ...)` |

**Rule of thumb**: If the job runs on a calendar-based schedule, use `[RecurringJob]`. If the job is triggered by a business event (reservation confirmed, payment received), use `[Job]` and schedule it programmatically.

`ScheduleAsync` takes an offset from now; `ScheduleAtAsync` takes an absolute timestamp and is the right choice when the moment is known up front and must not drift if the enqueue itself is delayed. Absolute times are normalized to UTC before being persisted, so passing a local `DateTimeOffset` is safe across providers.

### When to Use Continuations

Continuations are for sequential job chains where the second job should only run if the first succeeds:

```csharp
[Job]
[Continuation<SendInvoiceEmailJob>]
public sealed partial class GenerateInvoiceJob : IJob<InvoiceParams>
{
    public async Task ExecuteAsync(InvoiceParams p, JobContext context, CancellationToken ct)
    {
        // Generate invoice -- if this fails, the email is NOT sent
    }
}

[Job]
[Retry(MaxAttempts = 2)]
public sealed partial class SendInvoiceEmailJob : IJob
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        // Send email using context.CorrelationId to find the invoice
    }
}
```

The SG validates continuation chains at compile time:
- `PRAG2505`: Continuation type must implement `IJob` or `IJob<T>`.
- `PRAG2506`: No cycles allowed (A->B->A or A->B->C->A are compile errors).

---

## JobContext

Every job execution receives an immutable `JobContext` record with metadata about the current invocation:

```csharp
public sealed record JobContext(
    Guid JobId,              // Unique job instance ID
    string JobType,          // FQN of the job class
    DateTimeOffset ScheduledAt,  // When the job was scheduled to run
    int Attempt,             // Current attempt number (0 = first)
    int MaxAttempts,         // Max retry attempts configured
    string? CorrelationId,   // Optional correlation for tracing
    string? TenantId);       // Optional tenant for multi-tenancy
```

Use `JobContext` for:
- **Logging**: Include `JobId`, `Attempt`, and `ScheduledAt` in log messages.
- **Correlation**: Use `CorrelationId` to link related operations across job chains.
- **Idempotency**: Use `JobId` to ensure at-most-once execution in external systems.
- **Multi-tenancy**: see the section below — `TenantId` is the whole story, and for a declared
  recurring job it is empty.

---

## Jobs in a multi-tenant application

A job runs outside a request, so nothing resolves a tenant for it. Where entities are `ITenantEntity`
the generated filter is fail-closed, which means a job that simply queries reads **zero rows** — not
an error, not an empty database: zero rows, and a job that reports success. A digest written that way
tells every customer their queue is empty.

### A declared `[RecurringJob]` has no tenant, and cannot be given one

`RecurringJobAttribute` takes a cron expression, an id, a time zone, a misfire policy, a priority and
a concurrency cap. The scheduler enqueues each run with the definition's `TenantId`, which for an
attribute-declared job is null. So in a multi-tenant application the declared form is only correct
for work that is genuinely tenant-independent.

**Register per tenant instead.** `RecurringJobDefinition.TenantId` exists for exactly this, and
`IRecurringJobRegistrar` is how it is set:

```csharp
foreach (var tenantId in tenantsYouKnowAbout)
{
    await registrar.RegisterAsync(new RecurringJobDefinition
    {
        Id = $"digest:{tenantId}",
        JobType = typeof(PendingQueueDigestJob).AssemblyQualifiedName!,
        CronExpression = "0 3 * * *",
        TenantId = tenantId,
    }, ct);
}
```

The processor then puts that tenant on `JobContext.TenantId`, and the job opens a scope with it.

### Reading inside one tenant: `TenantScope`

```csharp
public async Task ExecuteAsync(JobContext context, CancellationToken ct)
{
    if (string.IsNullOrEmpty(context.TenantId))
        return;   // declining beats counting to zero: a wrong number looks like good news

    using var scope = TenantScope.BeginScope(context.TenantId);
    // every query in here sees exactly this tenant
}
```

It works because the registered `ITenantContext` is `AmbientTenantContext`: it answers from the
request when there is one and falls back to this scope when there is not. Both the Pragmatic tenant
filter and the EF Core query filter read that same context, so one scope covers both.

⚠️ `TenantScope` is in the `Pragmatic.MultiTenancy` namespace but ships in `Pragmatic.Abstractions`,
beside `ITenantEntity` and `ITenantStore`, so a domain library can use it without referencing the
runtime package.

### Reading across tenants: `FilterMode.Background`

```csharp
using (filters.UseMode(FilterMode.Background))   // IQueryFilterToggle
{
    // tenant filters off, soft-delete still on
}
```

⚠️ This lifts the tenant rule at **both** levels — the Pragmatic filter and the EF Core named query
filter the generated `DbContext` installs. Lifting only the first would leave a background job that
asked for it reading nothing.

### Do not enumerate `ITenantStore` expecting to find anything

The generated host registers an empty `InMemoryTenantStore`, and header-based tenant resolution never
writes to it — a tenant exists, as far as the store is concerned, only if the application put it
there. A job that iterates the store to find its tenants will iterate nothing. Take the list from
somewhere that actually has it: your own rows read in `FilterMode.Background`, or the table where
your application records its customers.

---

## Retry and Attempts

`MaxAttempts` is the **total number of executions, including the first**. `[Retry(MaxAttempts = 3)]` means the job runs at most three times: the initial execution plus two retries.

Retry is durable. When an execution throws, the processor records the new attempt number and asks the store to mark the job failed:

- If the attempt count is still below `MaxAttempts`, the job returns to `Pending` with the retry delay applied to `ScheduledFor`, so it is not re-selected on the very next poll.
- Once the attempt count reaches `MaxAttempts`, the job becomes terminal `Failed`.

Because the counter lives in the job row rather than in a worker's memory, an attempt survives a worker crash, and the `Attempt` column reflects the executions that actually happened.

`JobContext.Attempt` is zero-based: `0` on the first execution, `1` on the first retry.

A job's own `[Retry(MaxAttempts = n)]` determines `JobInstance.MaxAttempts` at enqueue time. Jobs without `[Retry]` fall back to `JobsOptions.DefaultMaxRetries` (default `1` — a single execution, no retry).

### Backoff

The delay before a retry comes from the declared `[Retry]` policy, capped at 30 minutes:

| Strategy | Delay |
|----------|-------|
| `Fixed` | `BaseDelayMs` |
| `Exponential` | `BaseDelayMs * 2^attempt` |
| `ExponentialWithJitter` | `BaseDelayMs * 2^attempt + Random(0, BaseDelayMs)` |

Jitter spreads a fleet's retries so a shared dependency coming back online is not hit by every failed job at the same instant. Jobs without `[Retry]` use a shared default of `2^attempt` seconds, capped the same way.

### Timeout

`[Timeout]` generates a linked `CancellationTokenSource` with `CancelAfter`. When the deadline fires, the job is marked failed and **consumes an attempt**, so a job that always times out eventually reaches `Failed` instead of retrying forever. Your `ExecuteAsync` must observe the `CancellationToken` for the deadline to have any effect.

---

## Scheduling Controls

Three properties on `[Job]` / `[RecurringJob]` shape *when* and *how many* of a job type run.

### Priority

When several jobs are due at the same moment, `Priority` decides which the workers pick up first. The pending selection is ordered by **`Priority` descending, then `ScheduledFor` ascending**, so a higher number wins and same-priority jobs run oldest-first.

```csharp
[Job(Priority = 10)]
public sealed partial class ProcessPaymentJob(IPaymentGateway gateway) : IJob<PaymentParams>
{
    public async Task ExecuteAsync(PaymentParams p, JobContext context, CancellationToken ct)
        => await gateway.CaptureAsync(p.PaymentId, ct);
}
```

The value is captured onto `JobInstance.Priority` at enqueue time (from `IJobTypeRegistry.GetPriority`), so it is durable and sortable in the store. Default `0`; negative values push a job behind the default tier. Priority orders *what is already due* — it does not make a job run before its `ScheduledFor`.

### Per-type concurrency

`MaxConcurrency` caps how many instances of one job type run at once **on a single host**, independently of the global `WorkerCount`. Use it to stop one heavy job type from occupying every worker.

```csharp
[Job(MaxConcurrency = 2)]
public sealed partial class RebuildSearchIndexJob(ISearchIndexer indexer) : IJob<IndexParams>
{
    public async Task ExecuteAsync(IndexParams p, JobContext context, CancellationToken ct)
        => await indexer.RebuildAsync(p.TenantId, ct);
}
```

At most two `RebuildSearchIndexJob` instances run concurrently per host even with `WorkerCount = 8`; the rest of the pool stays free for other work. An instance picked up over the cap is **not** started — it stays `Pending` and is reconsidered on the next poll, without blocking a worker. The count is per host, so a three-host cluster runs up to six at once. Default `0` means only `WorkerCount` bounds the type.

### Misfire policy

A recurring occurrence is *misfired* when the host is down (or saturated) and comes back more than `JobsOptions.MisfireThreshold` (default 1 minute) past the scheduled time. Below that threshold the run is treated as a normal, slightly-late execution — the threshold is what separates a real misfire from ordinary polling lag.

`Misfire` decides what happens to that missed occurrence:

| `MisfirePolicy` | Behavior |
|-----------------|----------|
| `RunOnce` (default) | Enqueue the missed occurrence once, then resume from the next future occurrence. Intermediate missed occurrences are not replayed. |
| `Skip` | Do not run any missed occurrence — advance straight to the next future occurrence. |

```csharp
// A 9am digest that must not fire at noon after an outage
[RecurringJob("0 9 * * *", Misfire = MisfirePolicy.Skip)]
public sealed partial class MorningDigestJob(IDigestService digest) : IJob
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
        => await digest.SendAsync(ct);
}
```

Choose `Skip` when a late run is worse than a skipped one; keep the default `RunOnce` when the occurrence still needs to happen even if delayed.

---

## Registration Model

### With Pragmatic.Composition (Recommended)

When using `Pragmatic.Composition`, the SG auto-detects `Pragmatic.Jobs` via `FeatureDetector` and generates host registration in `PragmaticHost.g.cs`:

```csharp
// Auto-generated -- no manual registration needed
services.AddPragmaticJobs();
services.AddJobProcessingServices();
```

Host aggregation also invokes each module's generated `AddDiscoveredJobs()` through the job metadata. That call is what makes jobs actually run: it registers the job classes, adds that assembly's generated registry as one `IJobTypeRegistrySource` of the composite `IJobTypeRegistry` that `AddPragmaticJobs()` registers, and registers the recurring job provider.

Configure options via `UseJobs()`:

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    app.UseJobs(jobs =>
    {
        jobs.WithWorkerCount(4);
        jobs.WithPollingInterval(5);
        jobs.UseEfCore();
    });
});
```

### Standalone (Without Composition)

Outside Composition, call the generated `AddDiscoveredJobs()` yourself — without it the composite registry has no source, and every job fails with `Unknown job type: … No registry is registered`.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPragmaticJobs(jobs =>
{
    jobs.WithWorkerCount(2);
    jobs.WithPollingInterval(10);
});

// SG-generated: job classes, the generated type registry, the recurring job provider
builder.Services.AddDiscoveredJobs();

// Register processing services manually
builder.Services.AddJobProcessingServices();
```

### JobsBuilder Methods

| Method | Sets |
|--------|------|
| `WithWorkerCount(int)` | Concurrent worker tasks |
| `WithPollingInterval(int)` | Seconds between polls |
| `WithLeaseTime(int)` | Lease duration in seconds |
| `WithBatchSize(int)` | Max pending jobs fetched per poll |
| `WithMaxRetries(int)` | Default attempt budget for jobs without `[Retry]` |
| `WithRetention(int)` | Days a finished job is kept before purging (`0` disables) |
| `WithMisfireThreshold(TimeSpan)` | How late a recurring occurrence may be before it counts as a misfire |
| `WithWorkerId(string)` | Explicit worker ID used for lease fencing |
| `UseEfCore()` | Switches away from the in-memory stores |

### Configuration Options

| Option | Default | Description |
|--------|---------|-------------|
| `WorkerCount` | 2 | Number of concurrent job processing tasks |
| `PollingIntervalSeconds` | 5 | Seconds between polling for pending jobs |
| `LeaseTimeSeconds` | 300 | Lease duration for distributed locking (5 min) |
| `BatchSize` | 10 | Maximum pending jobs to fetch per poll cycle |
| `DefaultMaxRetries` | 1 | Attempt budget for jobs without `[Retry]` |
| `RetentionDays` | 30 | How long a finished job is kept before deletion; `0` disables purging |
| `PurgeBatchSize` | 1000 | Maximum rows deleted per purge pass |
| `MisfireThreshold` | 1 minute | How far past its scheduled time a recurring occurrence may be before it counts as a misfire |
| `WorkerId` | auto-generated | Worker ID for lease acquisition |
| `UseEfCore` | false | Enable EF Core persistence |

---

## What Gets Generated

For a project with two jobs -- a recurring `DailyReportJob` and an on-demand `SendReminderJob` -- the SG produces five files:

### Per-Job: Invoker

`DailyReportJob.Invoker.g.cs` emits a nested `Invoker` inside a partial declaration of your job class, so Go-to-Definition on the job type surfaces both. The invoker resolves the job from the caller's DI scope and applies the declared timeout:

```csharp
// Simplified illustration
partial class DailyReportJob
{
    internal sealed class Invoker
    {
        public static async Task ExecuteAsync(
            string? parametersJson, JobContext context, IServiceProvider serviceProvider, CancellationToken ct)
        {
            using var activity = JobsDiagnostics.ActivitySource.StartActivity("Job.DailyReportJob");
            activity?.SetTag("job.type", "DailyReportJob");
            activity?.SetTag("job.id", context.JobId.ToString());

            var job = serviceProvider.GetRequiredService<DailyReportJob>();
            var logger = serviceProvider.GetRequiredService<ILogger<Invoker>>();

            try
            {
                // [Timeout] only: linked CancellationToken with CancelAfter
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(600));

                await job.ExecuteAsync(context, cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw;
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
        }
    }
}
```

Key details:
- The method is **static**; dispatch goes through the generated registry, not a DI-resolved invoker instance.
- The job itself is resolved from the scope the processor opened, so constructor dependencies come from DI.
- `[Timeout]` generates the linked `CancellationTokenSource`. There is no retry loop here — retry is the store's job.
- The invoker records the span; outcome metrics and duration are recorded by `JobProcessorService`, which sees the real result.
- For `IJob<T>`, the invoker deserializes `parametersJson` through the shared `PragmaticJsonOptions` seam before calling your `ExecuteAsync`.

### Aggregate: Registration

`_Infra.Jobs.Registration.g.cs` exposes `AddDiscoveredJobs()`:

```csharp
public static class PragmaticJobRegistration
{
    public static IServiceCollection AddDiscoveredJobs(this IServiceCollection services)
    {
        services.TryAddScoped<DailyReportJob>();
        services.TryAddScoped<SendReminderJob>();

        // Contributes this assembly's job types to the composite AddPragmaticJobs() registers.
        services.TryAddEnumerable(ServiceDescriptor
            .Singleton<IJobTypeRegistrySource, PragmaticJobTypeRegistry>(static sp =>
                new PragmaticJobTypeRegistry(sp.GetService<PragmaticJsonOptions>())));

        // One provider per assembly; the scheduler seeds and persists them at startup.
        services.TryAddEnumerable(ServiceDescriptor
            .Singleton<IRecurringJobProvider, PragmaticRecurringJobProvider>());

        return services;
    }
}
```

### Aggregate: Type Registry

`_Infra.Jobs.TypeRegistry.g.cs` maps job FQN strings to typed operations -- AOT-safe `switch` expressions that replace `Type.GetType()`. Alongside `ExecuteAsync` it exposes the declared `[Retry]` policy and `[Continuation<T>]` target, which is how the runtime applies both without reflection:

```csharp
internal sealed class PragmaticJobTypeRegistry : IJobTypeRegistry
{
    public JobRetryPolicy? GetRetryPolicy(string jobTypeFqn)
        => jobTypeFqn switch
        {
            "MyApp.Jobs.DailyReportJob" => new JobRetryPolicy(3, TimeSpan.FromMilliseconds(1000), BackoffStrategy.ExponentialWithJitter),
            _ => null
        };

    public string? GetContinuationJobType(string jobTypeFqn)
        => jobTypeFqn switch
        {
            "MyApp.Jobs.GenerateInvoiceJob" => "MyApp.Jobs.SendInvoiceEmailJob",
            _ => null
        };

    public Task ExecuteAsync(string jobTypeFqn, string? parametersJson,
        JobContext context, IServiceProvider serviceProvider, CancellationToken ct)
        => jobTypeFqn switch
        {
            "MyApp.Jobs.DailyReportJob" => DailyReportJob.Invoker.ExecuteAsync(parametersJson, context, serviceProvider, ct),
            "MyApp.Jobs.SendReminderJob" => SendReminderJob.Invoker.ExecuteAsync(parametersJson, context, serviceProvider, ct),
            _ => throw new InvalidOperationException($"Unknown job type: {jobTypeFqn}")
        };

    // DeserializeParameters / SerializeParameters follow the same switch shape.
}
```

### Aggregate: Recurring Jobs

`_Infra.Jobs.RecurringJobs.g.cs` exposes the declared definitions through `IRecurringJobProvider`. The generator emits declarations only -- computing the first occurrence and persisting it is the runtime registrar's job, because it depends on the wall clock and the host's timezone database:

```csharp
public sealed class PragmaticRecurringJobProvider : IRecurringJobProvider
{
    public IReadOnlyList<RecurringJobDefinition> GetDefinitions()
        =>
        [
            new RecurringJobDefinition
            {
                Id = "daily-report",
                JobType = "MyApp.Jobs.DailyReportJob",
                CronExpression = "0 2 * * *",
                IsEnabled = true,
            },
        ];
}
```

### Aggregate: Metadata

`_Metadata.Jobs.g.cs` exposes job metadata for host aggregation and tooling.

---

## Attributes Reference

### [Job]

Marks a class as a background job. Required for SG discovery on non-recurring jobs.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Priority` | `int` | 0 | Higher values are polled first (see [Priority](#priority)) |
| `MaxConcurrency` | `int` | 0 | Max instances of this type running at once per host; 0 = unbounded (see [Per-type concurrency](#per-type-concurrency)) |

### [RecurringJob]

Marks a class as a recurring job with cron scheduling.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `CronExpression` | `string` | (required) | Cron expression, 5 or 6 fields |
| `Id` | `string?` | auto-generated (kebab-case) | Unique recurring job ID |
| `TimeZone` | `string?` | UTC | IANA timezone for cron evaluation |
| `Misfire` | `MisfirePolicy` | `RunOnce` | What to do with an occurrence missed while the host was down (see [Misfire policy](#misfire-policy)) |
| `Priority` | `int` | 0 | Higher values are polled first (see [Priority](#priority)) |
| `MaxConcurrency` | `int` | 0 | Max instances of this type running at once per host; 0 = unbounded (see [Per-type concurrency](#per-type-concurrency)) |

The 5-field form is `minute hour day-of-month month day-of-week`; the 6-field form prefixes a seconds field. `*`, ranges (`1-5`), lists (`1,3,5`), steps (`*/5`), `L`, `W` and `#` are supported.

### [Retry]

Declares the job's durable attempt budget and backoff. See [Retry and Attempts](#retry-and-attempts).

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxAttempts` | `int` | 3 | Total executions allowed, including the first |
| `Strategy` | `BackoffStrategy` | `Exponential` | `Fixed`, `Exponential`, `ExponentialWithJitter` |
| `BaseDelayMs` | `int` | 1000 | Base delay between retries (ms) |

### [Timeout]

Sets an execution deadline. Expiry marks the job failed and consumes an attempt.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `TimeoutSeconds` | `int` | 300 | Timeout per execution (seconds), minimum 1 |

### [Continuation<TNextJob>]

Declares automatic job chaining on successful completion. The next job is enqueued by the processor with the parent's `CorrelationId` and `TenantId`, and gets its **own** declared retry budget rather than the parent's.

```csharp
[Continuation<SendInvoiceEmailJob>]
public sealed partial class GenerateInvoiceJob : IJob<InvoiceParams> { ... }
```

Because the continuation target is resolved from the generated registry rather than from the job row, adding `[Continuation<T>]` also applies to jobs that were enqueued before it was declared.

To build a continuation at enqueue time instead of declaring it, use the `JobContinuation` factory:

```csharp
var next = JobContinuation.Then<SendInvoiceEmailJob>();
var withParams = JobContinuation.Then<SendReminderJob, ReminderParams>(new ReminderParams(id, email), jsonOptions);
```

The factories are `static`, so they cannot resolve the host's JSON configuration themselves. Pass the
registered `PragmaticJsonOptions` — resolve it from DI — whenever parameters are involved: the
overload without it falls back to `PragmaticJsonOptions.Default`, which does not carry the contexts
added through `UseJson(...)` and does not honour `DisableReflectionFallback()`, so an AOT publish
fails on it.

---

## Persistence Stores

| Store | Package | Use Case |
|-------|---------|----------|
| `InMemoryJobStore` | `Pragmatic.Jobs` | Development and testing (default) |
| `InMemoryRecurringJobStore` | `Pragmatic.Jobs` | Development and testing (default) |
| `EfCoreJobStore` | `Pragmatic.Jobs.EFCore` | Production with database persistence |
| `EfCoreRecurringJobStore` | `Pragmatic.Jobs.EFCore` | Production with database persistence |

### Enabling EF Core persistence

Two calls, plus the entity configurations on your `DbContext`:

```csharp
app.UseJobs(jobs =>
{
    jobs.UseEfCore();             // drops the in-memory stores
    jobs.UseEfCorePersistence();  // from Pragmatic.Jobs.EFCore: registers the EF Core stores
});
```

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfiguration(new JobEntityTypeConfiguration());
    modelBuilder.ApplyConfiguration(new RecurringJobEntityTypeConfiguration());
}
```

`UseEfCore()` removes any in-memory store already registered, so the ordering between it and `AddPragmaticJobs()` does not matter. If it is set but no durable store is registered by the time the host starts, `JobProcessorService` throws with an actionable message rather than quietly running on the in-memory store.

The EF Core stores are registered `Scoped` and depend on the base `DbContext`; forward your own context if it is registered under its concrete type.

### Distributed Locking

Multiple app instances can process jobs safely. Lease acquisition is an atomic conditional update: only one worker acquires each job. While a job runs, the processor renews the lease by heartbeat at half the lease interval, so a job that outlives `LeaseTimeSeconds` is not picked up a second time.

`MarkCompletedAsync` and `MarkFailedAsync` are fenced on the worker ID, so a worker whose lease expired and was taken over cannot stamp an outcome underneath the new owner. Jobs that are already completed or cancelled cannot be re-acquired.

On graceful shutdown an in-flight job's lease is released without counting an attempt, so it is retried immediately instead of waiting out the lease. A worker that crashes consumes an attempt when its lease expires, so a job that repeatedly kills its worker ends up `Failed` rather than retrying forever.

### Retention

Finished jobs (`Completed`, `Failed`, `Cancelled`) are deleted once they are older than `RetentionDays` (default 30), at most `PurgeBatchSize` rows (default 1000) per pass, roughly hourly.

Setting `RetentionDays = 0` disables purging and keeps job history indefinitely. Consider this carefully: a job row retains its serialized parameters, which commonly carry personal data. Disabling the purge keeps that payload forever.

### Database Tables

| Table | Purpose |
|-------|---------|
| `__Jobs` | Job instances (pending, running, completed, failed, cancelled) |
| `__RecurringJobs` | Recurring job definitions and next-run tracking |

---

## Background Services

Two hosted services drive the job infrastructure:

| Service | Responsibility |
|---------|---------------|
| `JobProcessorService` | Polls `IJobStore` for pending jobs, acquires and renews leases, invokes via `IJobTypeRegistry`, records outcomes, enqueues continuations, purges old jobs |
| `RecurringJobSchedulerService` | Persists the declared recurring definitions at startup, then evaluates cron schedules against `IRecurringJobStore` and enqueues when due |

Both are registered by `AddJobProcessingServices()` (or automatically by the SG in Composition mode). They read `IJobTypeRegistry`, a composite over one source per assembly that declares a job; when the generated `AddDiscoveredJobs()` was never called it has no source, and a job fails with `Unknown job type: … No registry is registered`.

---

## Observability

### OpenTelemetry

ActivitySource: `Pragmatic.Jobs`

Each execution produces two nested spans:

| Span | Emitted by | Tags |
|------|-----------|------|
| `Job.Execute.{JobTypeFqn}` | `JobProcessorService` | `job.id`, `job.type`, `job.attempt` |
| `Job.{JobTypeName}` | the generated invoker | `job.type`, `job.id` |

### Metrics

| Metric | Type | Description |
|--------|------|-------------|
| `pragmatic.jobs.enqueued` | Counter | Total jobs enqueued |
| `pragmatic.jobs.completed` | Counter | Total jobs completed successfully |
| `pragmatic.jobs.failed` | Counter | Total jobs that failed permanently |
| `pragmatic.jobs.retried` | Counter | Total retry attempts |
| `pragmatic.jobs.duration` | Histogram (ms) | Job execution duration |
| `pragmatic.jobs.lease_acquisitions` | Counter | Total lease acquisitions |
| `pragmatic.jobs.lease_conflicts` | Counter | Lease conflicts (another worker won) |
| `pragmatic.jobs.recurring_triggered` | Counter | Recurring job triggers |

---

## Ecosystem Integration

### Messaging.Jobs Bridge

`Pragmatic.Messaging.Jobs` bridges the two systems, enabling scheduled message delivery:

```csharp
app.UseMessaging(msg =>
{
    msg.EnableScheduledMessages();  // Requires Pragmatic.Messaging.Jobs
});

// Schedule a message for future delivery
var scheduleId = await messageScheduler.ScheduleAsync(
    new CheckoutReminder(reservationId),
    delay: TimeSpan.FromHours(24));
```

Internally, this creates a `PublishMessageJob` that publishes the message to the bus when the scheduled time arrives.

### Showcase Examples

The Showcase application demonstrates both job types:

- **`NoShowDetectionJob`** (`examples/showcase/src/Showcase.Booking/Infrastructure/Jobs/`) -- recurring hourly job that detects no-show reservations.
- **`SendCheckInReminderJob`** (`examples/showcase/src/Showcase.Booking/Infrastructure/Jobs/`) -- delayed job with typed parameters, scheduled 24h before check-in.

---

## Packages

| Package | Description |
|---------|-------------|
| `Pragmatic.Jobs` | Core: `IJob`, `IJob<T>`, `IJobScheduler`, InMemory stores, background services, attributes |
| `Pragmatic.Jobs.EFCore` | EF Core persistence: `EfCoreJobStore`, `EfCoreRecurringJobStore`, entity config |
