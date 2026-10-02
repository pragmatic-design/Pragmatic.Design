# Pragmatic.Jobs

AOT-safe background job scheduling for .NET 10 — recurring cron jobs, delayed fire-and-forget,
continuation chains, and lease-based distributed locking, all source-generated at compile time.

## The Problem

Background jobs in .NET usually mean Hangfire or Quartz.NET. Both rely on runtime reflection for job
discovery, serialization, and invocation, require separate dashboards/storage, and keep retry config
*outside* the job definition — so the job class has no idea how it'll be scheduled, retried, or timed
out.

```csharp
// Without Pragmatic: config separate from the job
RecurringJob.AddOrUpdate<DailyReportJob>("daily-report", x => x.Execute(), "0 2 * * *");
GlobalJobFilters.Filters.Add(new AutomaticRetryAttribute { Attempts = 3 });  // global, not per-job
```

## The Solution

Declare scheduling, retry, and timeout **on the job class**. The generator produces the invoker with
linked cancellation tokens, telemetry, and DI wiring — zero reflection. Retries are applied durably by
the store, so an attempt survives a worker crash.

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

`[Retry]` and `[Timeout]` are the `Pragmatic.Resilience.Attributes` ones — the same declaration a message
handler uses; each engine reads it its own way. That's the whole job. The generator emits the invoker (timeout + telemetry), the DI registration, an
AOT-safe type registry (no `Type.GetType`), the recurring definitions, and metadata.

## Installation

```bash
dotnet add package Pragmatic.Jobs
dotnet add package Pragmatic.Jobs.EFCore       # optional: durable, distributed job store
dotnet add package Pragmatic.SourceGenerator    # the unified analyzer
```

## Quick Start

```csharp
// A delayed, fire-and-forget job
[Job]
[Retry(MaxAttempts = 5)]
public sealed partial class SendWelcomeEmailJob(IEmailService email) : IJob<Guid>
{
    public async Task ExecuteAsync(Guid userId, JobContext context, CancellationToken ct)
        => await email.SendWelcomeAsync(userId, ct);
}

// Schedule it
await scheduler.ScheduleAsync<SendWelcomeEmailJob, Guid>(userId, delay: TimeSpan.FromMinutes(5));
```

Enable processing in the host with `app.UseJobs(jobs => jobs.WithWorkerCount(2))`. Full walkthrough:
[Getting Started](docs/getting-started.md).

## What you can declare

- **`[Job]` / `[RecurringJob(cron)]`** — fire-and-forget or recurring (5- or 6-field cron) jobs.
- **`[Retry]`** — durable per-job retry: the attempt count lives in the job row and the store
  re-schedules the job with the declared backoff.
- **`[Timeout]`** — a per-execution deadline via a linked cancellation token; expiry marks the job
  failed and consumes an attempt.
- **`Priority` on `[Job]` / `[RecurringJob]`** — scheduling priority (default `0`): due jobs with a
  higher value are polled first, ties broken by scheduled time. The value is persisted on the job row.
- **`MaxConcurrency` on `[Job]` / `[RecurringJob]`** — cap (default `0` = unbounded) on how many
  instances of that job type run at once **per host**; an instance over the cap stays `Pending` for
  the next poll instead of holding a worker.
- **`Misfire` on `[RecurringJob]`** — what to do with an occurrence missed while the host was down past
  `JobsOptions.MisfireThreshold`: `RunOnce` (default) runs it once then resumes, `Skip` jumps straight
  to the next future occurrence.
- **`[Continuation<T>]`** — continuation chains, enqueued after the job completes successfully.
- **EF Core persistence** — durable jobs with **lease-based distributed locking** (one worker per job
  across instances), plus automatic retention of finished jobs.
- **Messaging bridge** — `Pragmatic.Messaging.Jobs` for scheduled message delivery.

## Durable persistence

The in-memory stores are the default and are for development only. For production, add
`Pragmatic.Jobs.EFCore`, ask for durable persistence, and apply the two entity configurations to your
`DbContext`:

```csharp
app.UseJobs(jobs =>
{
    jobs.UseEfCore();             // drops the in-memory stores
    jobs.UseEfCorePersistence();  // registers EfCoreJobStore + EfCoreRecurringJobStore
});

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfiguration(new JobEntityTypeConfiguration());          // __Jobs
    modelBuilder.ApplyConfiguration(new RecurringJobEntityTypeConfiguration()); // __RecurringJobs
}
```

If `UseEfCore()` is set but no durable store is registered, the processor fails at startup with an
actionable message rather than running on the in-memory store.

## Status

Recurring/delayed jobs, retry/timeout, continuations, EF Core persistence, and distributed locking are
functional within 1.0.0-alpha. See the [roadmap](../docs/ROADMAP.md).

## Documentation

| Guide | What you'll learn |
|-------|-------------------|
| [Concepts](docs/concepts.md) | Job types, scheduler/store model, the invoker pipeline, continuation chains, distributed locking |
| [Getting Started](docs/getting-started.md) | Your first recurring and delayed jobs, host wiring |
| [Common Mistakes](docs/common-mistakes.md) | The most frequent job pitfalls |
| [Troubleshooting](docs/troubleshooting.md) | Problem/solution guide with diagnostics |

## Requirements

- .NET 10.0+
- `Pragmatic.SourceGenerator` analyzer

## License

Part of the [Pragmatic.Design](../README.md) ecosystem — see [Licensing](../docs/LICENSING.md).
Pragmatic.Jobs is licensed under the **PolyForm Small Business 1.0.0** license (free for small
businesses; commercial license above the threshold).
