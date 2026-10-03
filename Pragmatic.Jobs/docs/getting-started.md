# Getting Started with Pragmatic.Jobs

This guide walks you through creating your first background job, from package installation to seeing it execute. By the end, you will have a recurring cleanup job running on a cron schedule with retry, timeout, and EF Core persistence.

## Prerequisites

- .NET 10.0+
- `Pragmatic.Jobs` package
- `Pragmatic.SourceGenerator` analyzer reference
- `Pragmatic.Temporal` (comes transitively)

Your project file should include:

```xml
<ItemGroup>
  <PackageReference Include="Pragmatic.Jobs" />
  <ProjectReference Include="..\Pragmatic.SourceGenerator\...\Pragmatic.SourceGenerator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

---

## Step 1: Define a Recurring Job

Create a class that implements `IJob` and decorate it with `[RecurringJob]`. The class **must** be `partial` so the source generator can emit the invoker.

```csharp
using Pragmatic.Jobs;
using Pragmatic.Jobs.Attributes;
using Pragmatic.Resilience.Attributes;

namespace MyApp.Infrastructure.Jobs;

/// <summary>
///     Cleans up expired user sessions every day at 3 AM.
/// </summary>
[RecurringJob("0 3 * * *", Id = "cleanup-expired-sessions", TimeZone = "Europe/Rome")]
[Retry(MaxAttempts = 2, Strategy = BackoffStrategy.Exponential, BaseDelayMs = 2000)]
[Timeout(TimeoutSeconds = 120)]
public sealed partial class CleanupExpiredSessionsJob(
    AppDbContext db,
    ILogger<CleanupExpiredSessionsJob> logger) : IJob
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var cutoff = context.ScheduledAt.AddDays(-30);

        var deleted = await db.Sessions
            .Where(s => s.ExpiresAt < cutoff)
            .ExecuteDeleteAsync(ct);

        logger.LogInformation(
            "Cleaned up {Count} expired sessions older than {Cutoff}",
            deleted, cutoff);
    }
}
```

Key points:
- `partial` is mandatory: the generator puts the invoker inside your class. Without it PRAG2502 is an error and nothing is generated for the job.
- `IJob` is the parameterless interface. Use `IJob<T>` if the job needs input data.
- `JobContext.ScheduledAt` gives you the scheduled execution time, not `DateTime.UtcNow`.
- Primary constructor parameters are resolved from DI (the SG handles registration).
- `[Retry(MaxAttempts = 2)]` means two executions in total: the first plus one retry.

### Cron Expression Quick Reference

Both the 5-field form (`minute hour day-of-month month day-of-week`) and the 6-field form (a leading seconds field) are accepted.

| Expression | Schedule |
|-----------|----------|
| `0 3 * * *` | Daily at 3:00 AM |
| `0 0 * * 0` | Every Sunday at midnight |
| `*/15 * * * *` | Every 15 minutes |
| `0 9 1 * *` | First day of every month at 9:00 AM |
| `0 0 * * 1-5` | Every weekday at midnight |
| `30 0 3 * * *` | Daily at 3:00:30 AM (6-field) |

---

## Step 2: Configure the Host

### With Pragmatic.Composition (Recommended)

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    app.UseJobs(jobs =>
    {
        jobs.WithWorkerCount(2);       // 2 concurrent worker tasks
        jobs.WithPollingInterval(5);   // Poll every 5 seconds
    });
});
```

The SG auto-discovers your job classes and generates all registration code, which host aggregation invokes for you. No manual `AddScoped<CleanupExpiredSessionsJob>()` needed.

### Without Composition (Standalone)

Three calls: the module services, the generated discovery registration, and the background services.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPragmaticJobs(jobs =>
{
    jobs.WithWorkerCount(2);
    jobs.WithPollingInterval(5);
});

// SG-generated: registers your job classes, swaps in the generated job type
// registry, and exposes the declared recurring definitions.
builder.Services.AddDiscoveredJobs();

// Register the background processing services
builder.Services.AddJobProcessingServices();

var app = builder.Build();
app.Run();
```

Skipping `AddDiscoveredJobs()` leaves the job type registry with no source: no recurring definition is declared, and a scheduled job fails with `Unknown job type: … No registry is registered`.

---

## Step 3: Verify the Generated Output

Build the project. The source generator produces five files in `obj/GeneratedFiles/Pragmatic.SourceGenerator/`:

```
CleanupExpiredSessionsJob.Invoker.g.cs      # Nested Invoker: DI resolution + timeout + telemetry
_Infra.Jobs.Registration.g.cs               # AddDiscoveredJobs(): DI, registry source, recurring provider
_Infra.Jobs.TypeRegistry.g.cs               # AOT-safe type mapping, retry policy, continuation target
_Infra.Jobs.RecurringJobs.g.cs              # PragmaticRecurringJobProvider with the cron definitions
_Metadata.Jobs.g.cs                         # Host aggregation metadata
```

In Visual Studio, expand **Dependencies > Analyzers > Pragmatic.SourceGenerator** in Solution Explorer to inspect these files.

In Rider, look under **Dependencies > Source Generators > Pragmatic.SourceGenerator**.

If these files are missing, check that the `<ProjectReference>` has `OutputItemType="Analyzer"`.

---

## Step 4: Add a Delayed Job with Parameters

Not every job runs on a cron schedule. For jobs triggered by business events, use `[Job]` and schedule them programmatically.

### Define the Job

```csharp
using Pragmatic.Jobs;
using Pragmatic.Jobs.Attributes;
using Pragmatic.Resilience.Attributes;

namespace MyApp.Infrastructure.Jobs;

public record SendReminderParams(Guid ReservationId, Guid GuestId, string GuestEmail, DateTimeOffset CheckIn);

[Job]
[Retry(MaxAttempts = 3, Strategy = BackoffStrategy.ExponentialWithJitter, BaseDelayMs = 500)]
public sealed partial class SendCheckInReminderJob(
    IEmailService email,
    ILogger<SendCheckInReminderJob> logger) : IJob<SendReminderParams>
{
    public async Task ExecuteAsync(SendReminderParams p, JobContext context, CancellationToken ct)
    {
        logger.LogInformation(
            "Sending check-in reminder to {Email} for reservation {Id}",
            p.GuestEmail, p.ReservationId);

        await email.SendAsync(
            p.GuestEmail,
            $"Reminder: your check-in is at {p.CheckIn:g}",
            $"Dear guest, your reservation {p.ReservationId} check-in is tomorrow.",
            ct);
    }
}
```

### Schedule the Job

Inject `IJobScheduler` and schedule when a reservation is confirmed:

```csharp
public class ReservationConfirmedHandler(IJobScheduler scheduler)
{
    public async Task HandleAsync(ReservationConfirmed evt, CancellationToken ct)
    {
        // Schedule reminder 24 hours before check-in
        var delay = evt.CheckIn - DateTimeOffset.UtcNow - TimeSpan.FromHours(24);
        if (delay > TimeSpan.Zero)
        {
            await scheduler.ScheduleAsync<SendCheckInReminderJob, SendReminderParams>(
                new SendReminderParams(evt.ReservationId, evt.GuestId, evt.GuestEmail, evt.CheckIn),
                delay: delay,
                correlationId: $"reservation-{evt.ReservationId}",
                ct: ct);
        }
    }
}
```

Scheduling methods:

| Method | When the job runs |
|--------|-------------------|
| `ScheduleAsync<TJob>()` | Immediately (fire-and-forget) |
| `ScheduleAsync<TJob>(delay: ...)` | After the specified delay |
| `ScheduleAtAsync<TJob>(scheduledFor: ...)` | At the specified time (normalized to UTC) |
| `CancelAsync(jobId)` | Cancels a pending job |

---

## Step 5: Add EF Core Persistence for Production

By default, jobs are stored in memory -- fine for development, but lost on restart. For production, switch to EF Core:

### Install the Package

```bash
dotnet add package Pragmatic.Jobs.EFCore
```

### Enable in Configuration

```csharp
using Pragmatic.Jobs.EFCore.Extensions;

await PragmaticApp.RunAsync(args, app =>
{
    app.UseJobs(jobs =>
    {
        jobs.WithWorkerCount(4);
        jobs.WithPollingInterval(5);
        jobs.WithLeaseTime(300);      // 5 minute lease
        jobs.WithBatchSize(10);       // Fetch 10 jobs per poll
        jobs.UseEfCore();             // Drop the in-memory stores
        jobs.UseEfCorePersistence();  // Register EfCoreJobStore + EfCoreRecurringJobStore
    });
});
```

Both calls are needed. `UseEfCore()` states the intent and removes the in-memory stores;
`UseEfCorePersistence()` (from `Pragmatic.Jobs.EFCore`) registers the EF Core ones. If `UseEfCore()` is
set but no durable store is registered, the processor fails at startup with a message telling you which
call is missing, instead of silently running on the in-memory store.

### Map the Tables on Your DbContext

The stores persist through your own `DbContext`. Apply the two shipped entity configurations:

```csharp
using Pragmatic.Jobs.EFCore.Entities;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new JobEntityTypeConfiguration());
        modelBuilder.ApplyConfiguration(new RecurringJobEntityTypeConfiguration());
    }
}
```

The EF Core stores take a base `DbContext` dependency. If your context is registered under its concrete
type, forward it:

```csharp
builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>());
```

| Table | Purpose |
|-------|---------|
| `__Jobs` | Job instances with status, lease, retry tracking |
| `__RecurringJobs` | Recurring definitions with next-run timestamps |

Include both tables in your migrations.

### Distributed Locking

With EF Core, multiple app instances can safely process jobs. Lease acquisition is an atomic conditional
update -- only one worker acquires each job -- and the lease is renewed by heartbeat while the job runs,
so a job that takes longer than `LeaseTimeSeconds` is not picked up twice. If a worker crashes, the lease
expires and another worker picks the job up, consuming an attempt.

### Retention

Finished jobs are deleted once they are older than `RetentionDays` (default 30), in batches of
`PurgeBatchSize` (default 1000):

```csharp
app.UseJobs(jobs => jobs
    .UseEfCore().UseEfCorePersistence()
    .WithRetention(90));   // keep finished jobs 90 days; WithRetention(0) keeps them indefinitely
```

Job rows keep their serialized parameters, which often contain personal data. Disabling the purge retains
that payload forever -- decide the window deliberately rather than switching it off.

---

## Step 6: Add a Continuation Chain

When one job must run after another succeeds, declare it with `[Continuation<TNextJob>]`:

```csharp
[Job]
[Retry(MaxAttempts = 3)]
[Continuation<SendInvoiceEmailJob>]
public sealed partial class GenerateInvoiceJob(
    IBillingService billing) : IJob<InvoiceParams>
{
    public async Task ExecuteAsync(InvoiceParams p, JobContext context, CancellationToken ct)
        => await billing.GenerateAsync(p.ReservationId, p.Amount, p.Currency, ct);
    // On success, SendInvoiceEmailJob is automatically enqueued
}

[Job]
[Retry(MaxAttempts = 2)]
public sealed partial class SendInvoiceEmailJob(
    IEmailService email) : IJob
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
        => await email.SendInvoiceAsync(context.CorrelationId!, ct);
    // Uses CorrelationId to find the invoice
}
```

The SG validates at compile time:
- `PRAG2505` if the continuation type does not implement `IJob` or `IJob<T>`.
- `PRAG2506` if the chain creates a cycle (A->B->A).

---

## Step 7: Tune Scheduling (Priority, Concurrency, Misfire)

Three properties on `[Job]` / `[RecurringJob]` control ordering and volume. All are optional and default to the "no special handling" behavior.

### Run the important jobs first

When several jobs are due at once, `Priority` (default `0`) decides the order: due jobs are polled by priority descending, then scheduled time ascending. Give latency-sensitive work a higher number.

```csharp
[Job(Priority = 10)]
public sealed partial class ProcessPaymentJob(IPaymentGateway gateway) : IJob<PaymentParams>
{
    public async Task ExecuteAsync(PaymentParams p, JobContext context, CancellationToken ct)
        => await gateway.CaptureAsync(p.PaymentId, ct);
}
```

A due `ProcessPaymentJob` is picked up before default-priority jobs waiting in the same poll. The value is persisted on the job row, so ordering survives a restart.

### Throttle a heavy job type

`MaxConcurrency` (default `0` = unbounded) caps how many instances of that type run at once **per host**, independently of `WithWorkerCount`. Use it so one expensive job type cannot occupy the whole pool.

```csharp
[Job(MaxConcurrency = 2)]
public sealed partial class RebuildSearchIndexJob(ISearchIndexer indexer) : IJob<IndexParams>
{
    public async Task ExecuteAsync(IndexParams p, JobContext context, CancellationToken ct)
        => await indexer.RebuildAsync(p.TenantId, ct);
}
```

With `WithWorkerCount(8)` at most two of these run concurrently on each host; the other workers stay free for other jobs. An instance over the cap is left `Pending` and reconsidered next poll; it never holds a worker idle.

### Skip missed recurring occurrences

If the host is down when a recurring job was due and comes back more than `JobsOptions.MisfireThreshold` (default 1 minute) late, `Misfire` decides what happens to the missed occurrence:

- `MisfirePolicy.RunOnce` (default): run the missed occurrence once, then resume from the next future occurrence.
- `MisfirePolicy.Skip`: run nothing for the missed occurrence, jump straight to the next future one.

```csharp
// A 9am digest that must not fire at noon after an overnight outage
[RecurringJob("0 9 * * *", Misfire = MisfirePolicy.Skip)]
public sealed partial class MorningDigestJob(IDigestService digest) : IJob
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
        => await digest.SendAsync(ct);
}
```

Reach for `Skip` when a late run is worse than a skipped one; leave the default `RunOnce` when the work still needs to happen even if delayed.

---

## What Happens at Runtime

Here is the lifecycle for the recurring `CleanupExpiredSessionsJob`:

1. **Startup**: `RecurringJobSchedulerService` reads the generated `PragmaticRecurringJobProvider` and hands each definition to `IRecurringJobRegistrar`, which computes the first occurrence from the cron expression and upserts it into `IRecurringJobStore`. Already-persisted scheduling state wins, so a restart never rewinds a running schedule or re-enables a disabled job.
2. **Every 5 seconds**: `RecurringJobSchedulerService` polls for due recurring jobs.
3. **At 3:00 AM Rome time**: The cron expression matches. One host wins the atomic claim and enqueues a `JobInstance` with `Status = Pending`.
4. **Next poll cycle**: `JobProcessorService` finds the pending job. A worker acquires the lease and keeps it renewed while the job runs.
5. **Execution**: The generated invoker resolves the job from DI and runs `ExecuteAsync` under a 120-second deadline.
6. **Success**: The job is marked `Completed`. Metrics are emitted (`pragmatic.jobs.completed`, `pragmatic.jobs.duration`).
7. **Failure**: The attempt is recorded and the job returns to `Pending` with the exponential backoff applied to `ScheduledFor`. Once the attempt count reaches `MaxAttempts` (2 here), the job is marked `Failed` and `pragmatic.jobs.failed` increments.

For the delayed `SendCheckInReminderJob`:

1. **Business event**: A reservation is confirmed. Your handler calls `scheduler.ScheduleAsync(...)`.
2. **Job created**: A `JobInstance` is stored with `ScheduledFor = now + delay` and `Status = Pending`.
3. **When due**: `JobProcessorService` picks it up (it only fetches jobs where `ScheduledFor <= now`).
4. **Execution**: The generated invoker deserializes `SendReminderParams` from JSON, calls `ExecuteAsync`, and handles retry/telemetry.

---

## Next Steps

- Read [Architecture and Core Concepts](concepts.md) for the full pipeline details and decision tree.
- Read [Common Mistakes](common-mistakes.md) to avoid the most frequent issues.
- Read [Troubleshooting](troubleshooting.md) for diagnostic IDs and debugging guidance.
- Explore the Showcase examples: `NoShowDetectionJob` and `SendCheckInReminderJob` in `examples/showcase/src/Showcase.Booking/Infrastructure/Jobs/`.
