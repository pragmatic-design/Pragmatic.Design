# Common Mistakes

These are the most common issues developers encounter when using Pragmatic.Jobs. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Forgetting `partial` on the Job Class

**Wrong:**

```csharp
[RecurringJob("0 2 * * *", Id = "daily-report")]
public sealed class DailyReportJob(IReportService reports) : IJob
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
        => await reports.GenerateDailyAsync(context.ScheduledAt.Date, ct);
}
```

**Compile result:** Error `PRAG2502` -- "Type 'DailyReportJob' is decorated with [Job]/[RecurringJob] but is not declared as partial, so no invoker is generated and the job never runs."

The nested `Invoker` lives inside your job class, which the generator can only extend if it is `partial`. Without it the generator emits nothing for the job (no invoker, no registration), and `PRAG2502` says so on the declaration.

**Right:**

```csharp
[RecurringJob("0 2 * * *", Id = "daily-report")]
public sealed partial class DailyReportJob(IReportService reports) : IJob
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
        => await reports.GenerateDailyAsync(context.ScheduledAt.Date, ct);
}
```

**Why:** The source generator emits a nested `Invoker` class inside a partial declaration of your job class. Without `partial`, the compiler cannot merge the two declarations. Always include `partial` on every job class.

---

## 2. Wrong Cron Expression Format

Both the 5-field and the 6-field (leading seconds) forms are supported. What is *not* tolerated is a field count that is neither.

**Wrong:**

```csharp
// Missing a field (only 4) -- accepted at build time, skipped at runtime
[RecurringJob("0 2 * *", Id = "daily-report")]
public sealed partial class DailyReportJob : IJob { ... }

// Empty string -- caught at build time
[RecurringJob("", Id = "daily-report")]
public sealed partial class DailyReportJob : IJob { ... }
```

**Compile result:** only the empty expression produces `PRAG2501` -- "[RecurringJob] on 'DailyReportJob' has an empty or missing cron expression." The generator checks that a cron expression is *present*, not that it parses.

A malformed-but-non-empty expression builds cleanly and fails at runtime: the registrar cannot parse it, logs a warning (`"Recurring job '{RecurringId}' not registered: invalid cron expression"`), and skips that definition. Every other recurring job still registers. If a recurring job never fires and there is no build diagnostic, check the startup logs for that warning first.

**Right:**

```csharp
// 5-field: minute hour day-of-month month day-of-week
[RecurringJob("0 2 * * *", Id = "daily-report")]      // Daily at 2:00 AM
public sealed partial class DailyReportJob : IJob { ... }

// 6-field: the leading field is seconds
[RecurringJob("30 0 2 * * *", Id = "daily-report")]   // Daily at 2:00:30 AM
public sealed partial class DailyReportJob : IJob { ... }
```

**Quick reference:**

```
┌─── Minute (0-59)
│ ┌─── Hour (0-23)
│ │ ┌─── Day of month (1-31)
│ │ │ ┌─── Month (1-12)
│ │ │ │ ┌─── Day of week (0-6, Sun=0)
│ │ │ │ │
* * * * *

┌─── Second (0-59)      -- optional leading field
│ ┌─── Minute (0-59)
│ │ ┌─── Hour (0-23)
│ │ │ ┌─── Day of month (1-31)
│ │ │ │ ┌─── Month (1-12)
│ │ │ │ │ ┌─── Day of week (0-6, Sun=0)
│ │ │ │ │ │
* * * * * *
```

Common expressions:

| Expression | Schedule |
|-----------|----------|
| `0 2 * * *` | Daily at 2:00 AM |
| `*/15 * * * *` | Every 15 minutes |
| `0 0 * * 0` | Every Sunday at midnight |
| `0 9 1 * *` | First of every month at 9:00 AM |
| `0 0 * * 1-5` | Every weekday at midnight |
| `30 0 2 * * *` | Daily at 2:00:30 AM (6-field) |

An unknown `TimeZone` behaves the same way: the definition is skipped at startup with a warning, not blocked at build time.

---

## 3. Not Implementing IJob or IJob<T>

**Wrong:**

```csharp
[Job]
public sealed partial class SendReminderJob
{
    public async Task ExecuteAsync(ReminderParams p, CancellationToken ct)
    {
        // Looks like a job, but doesn't implement the interface
    }
}
```

**Compile result:** Error `PRAG2500` -- "Type 'SendReminderJob' is decorated with [Job]/[RecurringJob] but does not implement IJob or IJob<T>."

**Right:**

```csharp
[Job]
public sealed partial class SendReminderJob : IJob<ReminderParams>
{
    public async Task ExecuteAsync(ReminderParams p, JobContext context, CancellationToken ct)
    {
        // Now implements the correct interface
    }
}
```

**Why:** The SG validates that every class decorated with `[Job]` or `[RecurringJob]` implements one of the two job interfaces. This is an error, not a warning -- the code will not compile. Note also that the `ExecuteAsync` signature must match the interface: `IJob` requires `(JobContext, CancellationToken)` and `IJob<T>` requires `(T, JobContext, CancellationToken)`.

---

## 4. Duplicate Recurring Job IDs

**Wrong:**

```csharp
[RecurringJob("0 2 * * *", Id = "daily-cleanup")]
public sealed partial class CleanupSessionsJob : IJob { ... }

[RecurringJob("0 3 * * *", Id = "daily-cleanup")]    // Same ID!
public sealed partial class CleanupLogsJob : IJob { ... }
```

**Compile result:** Error `PRAG2503` -- "Recurring job ID 'daily-cleanup' is used by both 'CleanupSessionsJob' and 'CleanupLogsJob'."

**Right:**

```csharp
[RecurringJob("0 2 * * *", Id = "cleanup-sessions")]
public sealed partial class CleanupSessionsJob : IJob { ... }

[RecurringJob("0 3 * * *", Id = "cleanup-logs")]
public sealed partial class CleanupLogsJob : IJob { ... }
```

**Why:** The recurring job ID is the primary key in `__RecurringJobs`. Two jobs with the same ID would overwrite each other's definitions. The SG catches this at compile time.

**Tip:** If you omit the `Id` property, the SG generates one from the class name by converting to kebab-case and stripping the "Job" suffix: `CleanupSessionsJob` becomes `cleanup-sessions`. This auto-generated ID is usually sufficient and avoids accidental duplicates.

---

## 5. Retry MaxAttempts <= 0

**Wrong:**

```csharp
[Job]
[Retry(MaxAttempts = 0)]    // Zero retries
public sealed partial class SendNotificationJob : IJob<NotificationParams> { ... }

[Job]
[Retry(MaxAttempts = -1)]   // Negative retries
public sealed partial class SendNotificationJob : IJob<NotificationParams> { ... }
```

**Compile result:** Error `PRAG2504` -- "[Retry] on 'SendNotificationJob' has MaxAttempts = 0. MaxAttempts must be greater than 0."

**Right:**

```csharp
[Job]
[Retry(MaxAttempts = 1)]    // Executes once, no retries (1 attempt total)
public sealed partial class SendNotificationJob : IJob<NotificationParams> { ... }

[Job]
[Retry(MaxAttempts = 3)]    // Executes once + up to 2 retries (3 attempts total)
public sealed partial class SendNotificationJob : IJob<NotificationParams> { ... }
```

**Why:** `MaxAttempts` is the total number of executions, **including the first one**. A value of 0 or negative makes no sense -- the job would never execute. The SG requires `MaxAttempts > 0`.

`MaxAttempts` is what the runtime persists on the job row, so the number you declare is the number an operator sees in `__Jobs.MaxAttempts` and the ceiling the `Attempt` column climbs toward.

**Note:** If you want no retry at all, simply omit the `[Retry]` attribute. The `DefaultMaxRetries` in `JobsOptions` (default: 1) will apply, meaning one execution with no retry. Raise it with `WithMaxRetries(n)` if you want a different fleet-wide default.

---

## 6. Continuation Cycle (A -> B -> A)

**Wrong:**

```csharp
[Job]
[Continuation<SendInvoiceEmailJob>]
public sealed partial class GenerateInvoiceJob : IJob<InvoiceParams> { ... }

[Job]
[Continuation<GenerateInvoiceJob>]    // Cycle!
public sealed partial class SendInvoiceEmailJob : IJob { ... }
```

**Compile result:** Error `PRAG2506` -- "Job 'GenerateInvoiceJob' creates a cycle in continuation chain: GenerateInvoiceJob -> SendInvoiceEmailJob -> GenerateInvoiceJob."

The SG performs DAG traversal on the continuation graph and detects cycles of any length (A->B->A, A->B->C->A, etc.).

**Right:**

```csharp
[Job]
[Continuation<SendInvoiceEmailJob>]
public sealed partial class GenerateInvoiceJob : IJob<InvoiceParams> { ... }

[Job]    // No continuation -- this is the end of the chain
public sealed partial class SendInvoiceEmailJob : IJob { ... }
```

**Why:** A cycle would cause infinite job scheduling. The SG validates the entire continuation graph at compile time to prevent this.

---

## 7. Not Configuring EF Core for Production

**Wrong:**

```csharp
// Using defaults -- InMemory store
await PragmaticApp.RunAsync(args, app =>
{
    app.UseJobs();
});
```

This works fine in development, but in production:
- All pending jobs are lost when the process restarts.
- No distributed locking -- multiple instances process the same job.
- Recurring job next-run timestamps are not persisted.

**Also wrong -- half the wiring:**

```csharp
app.UseJobs(jobs => jobs.UseEfCore());   // no store registered
```

The host refuses to start: `UseEfCore()` drops the in-memory stores but registers nothing in their place. The processor throws with a message naming the missing call.

**Right:**

```csharp
using Pragmatic.Jobs.EFCore.Extensions;

await PragmaticApp.RunAsync(args, app =>
{
    app.UseJobs(jobs =>
    {
        jobs.UseEfCore();             // drop the in-memory stores
        jobs.UseEfCorePersistence();  // register the EF Core stores
    });
});
```

Plus the entity configurations on your `DbContext`, or the tables never exist:

```csharp
modelBuilder.ApplyConfiguration(new JobEntityTypeConfiguration());
modelBuilder.ApplyConfiguration(new RecurringJobEntityTypeConfiguration());
```

**Why:** The in-memory stores (`InMemoryJobStore`, `InMemoryRecurringJobStore`) are designed for development and testing only. They provide no durability and no distributed locking. For any deployment beyond a single-instance development server, use the EF Core stores.

**Tip:** You can use environment-based configuration:

```csharp
app.UseJobs(jobs =>
{
    var isDev = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";

    jobs.WithWorkerCount(isDev ? 1 : 4);

    if (!isDev)
    {
        jobs.UseEfCore();
        jobs.UseEfCorePersistence();
    }
});
```

---

## 8. Blocking Code in ExecuteAsync

**Wrong:**

```csharp
[RecurringJob("*/5 * * * *", Id = "sync-external-data")]
public sealed partial class SyncExternalDataJob(HttpClient http) : IJob
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        // Blocking call -- ties up the thread pool
        var response = http.GetStringAsync("https://api.example.com/data").Result;

        // Long-running synchronous loop
        Thread.Sleep(5000);

        // More blocking
        var result = SomeLibrary.ProcessSync(response);
    }
}
```

**Right:**

```csharp
[RecurringJob("*/5 * * * *", Id = "sync-external-data")]
public sealed partial class SyncExternalDataJob(HttpClient http) : IJob
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var response = await http.GetStringAsync("https://api.example.com/data", ct);

        await Task.Delay(5000, ct);   // If you must wait, do it async

        // Offload CPU-bound work to the thread pool properly
        var result = await Task.Run(() => SomeLibrary.ProcessSync(response), ct);
    }
}
```

**Why:** `JobProcessorService` uses a `SemaphoreSlim` to limit concurrency to `WorkerCount` tasks. If a job blocks its thread with `.Result`, `.Wait()`, or `Thread.Sleep()`, it wastes one of the limited worker slots. All I/O should be `await`-ed, and the `CancellationToken` should be passed through to every async call so that timeouts and shutdown work correctly.

---

## 9. Ignoring the CancellationToken

**Wrong:**

```csharp
[Job]
[Timeout(TimeoutSeconds = 30)]
public sealed partial class GenerateReportJob(IReportService reports) : IJob
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        // Ignores ct -- timeout has no effect
        await reports.GenerateAsync(context.ScheduledAt.Date);
        await Task.Delay(10000);                    // Not cancellable
        await reports.ExportToPdfAsync("report.pdf"); // Not cancellable
    }
}
```

**Right:**

```csharp
[Job]
[Timeout(TimeoutSeconds = 30)]
public sealed partial class GenerateReportJob(IReportService reports) : IJob
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        await reports.GenerateAsync(context.ScheduledAt.Date, ct);
        await Task.Delay(10000, ct);
        await reports.ExportToPdfAsync("report.pdf", ct);
    }
}
```

**Why:** The `[Timeout]` attribute makes the SG generate a linked `CancellationTokenSource` with `CancelAfter`. But if your code never checks the token, the timeout has no effect. The job will keep running until it finishes or the process shuts down. Always pass `ct` to every `await`-ed operation.

When the token *is* observed, the deadline marks the job failed and consumes an attempt, so a job that keeps timing out reaches `Failed` after `MaxAttempts` instead of running indefinitely.

---

## 10. Using [RecurringJob] on IJob<T>

**Subtle mistake:**

```csharp
[RecurringJob("0 2 * * *", Id = "send-daily-summary")]
public sealed partial class SendDailySummaryJob : IJob<SummaryParams>
{
    public async Task ExecuteAsync(SummaryParams p, JobContext context, CancellationToken ct)
    {
        // Where does SummaryParams come from on a recurring schedule?
    }
}
```

This technically compiles and generates code, but the `SummaryParams` will always be `null` at runtime because recurring jobs have no parameter source. The `RecurringJobSchedulerService` enqueues instances with `ParametersJson = null`.

**Right for recurring:** Use `IJob` (parameterless) and derive context from `JobContext.ScheduledAt`:

```csharp
[RecurringJob("0 2 * * *", Id = "send-daily-summary")]
public sealed partial class SendDailySummaryJob(ISummaryService summaries) : IJob
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        // Derive the date from the schedule
        var reportDate = context.ScheduledAt.Date;
        await summaries.GenerateAndSendAsync(reportDate, ct);
    }
}
```

**Right for parametric:** Use `[Job]` and schedule programmatically:

```csharp
[Job]
public sealed partial class SendDailySummaryJob : IJob<SummaryParams>
{
    public async Task ExecuteAsync(SummaryParams p, JobContext context, CancellationToken ct)
    {
        await summaries.GenerateAndSendAsync(p.ReportDate, p.RecipientEmail, ct);
    }
}

// Schedule with parameters
await scheduler.ScheduleAsync<SendDailySummaryJob, SummaryParams>(
    new SummaryParams(DateTime.UtcNow.Date, "manager@company.com"));
```

---

## 11. Not Passing CorrelationId in Continuation Chains

**Wrong:**

```csharp
// Scheduling without correlationId
await scheduler.ScheduleAsync<GenerateInvoiceJob, InvoiceParams>(
    new InvoiceParams(reservationId, amount, "EUR"));

// In the continuation, CorrelationId is null
[Job]
public sealed partial class SendInvoiceEmailJob : IJob
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var invoiceId = context.CorrelationId;   // null!
    }
}
```

**Right:**

```csharp
await scheduler.ScheduleAsync<GenerateInvoiceJob, InvoiceParams>(
    new InvoiceParams(reservationId, amount, "EUR"),
    correlationId: $"reservation-{reservationId}");

[Job]
public sealed partial class SendInvoiceEmailJob : IJob
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        // CorrelationId is carried forward through the continuation chain
        var correlationId = context.CorrelationId;   // "reservation-{guid}"
    }
}
```

**Why:** When a job has `[Continuation<T>]`, the `CorrelationId` (and `TenantId`) are automatically forwarded to the continuation job. But if the original job was scheduled without a `correlationId`, the continuation also gets `null`. Always provide a meaningful correlation ID when scheduling jobs that participate in continuation chains.

---

## 12. Forgetting `AddDiscoveredJobs()` Outside Composition

**Wrong:**

```csharp
builder.Services.AddPragmaticJobs(jobs => jobs.WithWorkerCount(2));
builder.Services.AddJobProcessingServices();
// No recurring job is declared, and a scheduled job fails with "Unknown job type".
```

**Right:**

```csharp
builder.Services.AddPragmaticJobs(jobs => jobs.WithWorkerCount(2));
builder.Services.AddDiscoveredJobs();        // SG-generated
builder.Services.AddJobProcessingServices();
```

**Why:** `AddPragmaticJobs()` registers a composite `IJobTypeRegistry` with no source of its own. The generated `AddDiscoveredJobs()` is what registers your job classes, adds your assembly's registry as a source of that composite, and registers the recurring job provider. Without it no recurring definition exists, and a job you schedule is refused with `Unknown job type: … No registry is registered`.

Under `Pragmatic.Composition` this call is made for you through host aggregation; it is only manual wiring that can miss it.

---

## 13. Letting a `[RecurringJob]` Query Tenant-Scoped Rows

**Wrong:**

```csharp
[RecurringJob("0 3 * * *")]
public sealed class PendingDigestJob(IRepository<Candidate> candidates) : IJob
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var pending = await candidates.CountAsync(Specs.Pending(), ct);
        // pending is 0. Always. For every customer.
    }
}
```

**Right:**

```csharp
public async Task ExecuteAsync(JobContext context, CancellationToken ct)
{
    if (string.IsNullOrEmpty(context.TenantId))
        return;                                   // a declared recurring run carries none

    using var scope = TenantScope.BeginScope(context.TenantId);
    var pending = await candidates.CountAsync(Specs.Pending(), ct);
}
```

...registered per tenant, where the tenant can actually be supplied:

```csharp
await registrar.RegisterAsync(new RecurringJobDefinition
{
    Id = $"digest:{tenantId}", JobType = /* … */, CronExpression = "0 3 * * *", TenantId = tenantId,
}, ct);
```

**Why:** a job runs outside a request, so nothing resolves a tenant for it, and the generated filter
on an `ITenantEntity` is fail-closed. The query does not throw and the job does not fail: it reads
**zero rows and reports success**, which is indistinguishable from a queue that is genuinely empty.
`RecurringJobAttribute` has no `TenantId` and cannot be given one; `RecurringJobDefinition` does, and
`IRecurringJobRegistrar` is how it gets set.

Returning early when the tenant is missing is deliberate: counting to zero produces a wrong number
that looks like good news, and nobody investigates good news.

To work **across** tenants instead of within one, use `FilterMode.Background` through
`IQueryFilterToggle`: it lifts the tenant rule at both levels and keeps soft-delete. And do not
enumerate `ITenantStore` looking for the tenant list: the generated host registers an empty
`InMemoryTenantStore` and header-based resolution never writes to it.

---

## Summary Table

| # | Mistake | Diagnostic | Severity |
|---|---------|-----------|----------|
| 1 | Missing `partial` | PRAG2502 | Error; nothing generated for the job |
| 2 | Empty cron expression | PRAG2501 | Error |
| 2b | Malformed (non-empty) cron or unknown timezone | (runtime) | Definition skipped, warning logged |
| 3 | Not implementing `IJob`/`IJob<T>` | PRAG2500 | Error |
| 4 | Duplicate recurring job IDs | PRAG2503 | Error |
| 5 | `MaxAttempts <= 0` | PRAG2504 | Error |
| 6 | Continuation cycle | PRAG2506 | Error |
| 7 | No EF Core in production | (runtime) | Data loss risk |
| 7b | `UseEfCore()` without `UseEfCorePersistence()` | (runtime) | Startup failure |
| 8 | Blocking code in `ExecuteAsync` | (runtime) | Thread pool starvation |
| 9 | Ignoring `CancellationToken` | (runtime) | Timeout ineffective |
| 10 | `[RecurringJob]` on `IJob<T>` | (runtime) | Null parameters |
| 11 | Missing `CorrelationId` in chains | (runtime) | Lost tracing context |
| 12 | Missing `AddDiscoveredJobs()` | (runtime) | No job ever runs |
| 13 | A `[RecurringJob]` querying tenant-scoped rows | (runtime) | **Zero rows, reported as success** |
