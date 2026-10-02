---
name: pragmatic-use-jobs
description: Use when adding background or scheduled work — [Job], [RecurringJob] cron, retries, timeouts, continuations, distributed lock — with Pragmatic.Jobs.
---

# Pragmatic Use Jobs

**Covers:** Background and recurring work with Pragmatic.Jobs from NuGet — [Job], [RecurringJob] cron, retry, timeout, continuation, distributed lock. Attribute-first pattern driven by the source generator.

`Pragmatic.Jobs` executes work outside the request/response cycle: one-shot, delayed, or cron-based recurring jobs. The source generator produces each job's invoker (DI, retry, timeout) inline.

## When to use

- Scheduled/recurring work (nightly reports, cleanup, reminders).
- Delayed or fire-and-forget work triggered from code.

For cross-boundary event reactions use `pragmatic-use-messaging`. Jobs and Messaging are complementary: a job can publish a message.

## Packages

```xml
<PackageReference Include="Pragmatic.Jobs" Version="1.0.0-alpha.*" />
<PackageReference Include="Pragmatic.Jobs.EFCore" Version="1.0.0-alpha.*" />     <!-- DB store + distributed lock -->
<PackageReference Include="Pragmatic.SourceGenerator" Version="1.0.0-alpha.*">
  <IncludeAssets>analyzers; build; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

Namespaces: `Pragmatic.Jobs`, `Pragmatic.Jobs.Attributes`, and `Pragmatic.Resilience.Attributes` for `[Retry]` and `[Timeout]`.

## Core patterns

### 1. Recurring job (cron)

```csharp
using Pragmatic.Jobs;
using Pragmatic.Jobs.Attributes;
using Pragmatic.Resilience.Attributes;

[RecurringJob("0 2 * * *", TimeZone = "Europe/Rome")]      // 5 or 6 field cron
[Retry(MaxAttempts = 2, Strategy = BackoffStrategy.Exponential)]
[Timeout(TimeoutSeconds = 300)]
public sealed partial class NoShowDetectionJob(
    IRepository<Reservation> reservations) : IJob      // dependencies: primary constructor
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        // ...
    }
}
```

- The class implements `IJob`, is `partial`, and carries `[RecurringJob("cron")]`. The default `Id` is the kebab-case name (`NoShowDetectionJob` → `no-show-detection`); duplicates → **PRAG2503**.
- **A job may carry several `[RecurringJob]`**: one job on two clocks, one `RecurringJobDefinition` each. Only the first may rely on the derived `Id`; the others name themselves or it is **PRAG2507** — a positional suffix would be a name nobody chose, on a persisted key an operator reads. `TimeZone` and `Misfire` belong to each schedule; `Priority` and `MaxConcurrency` describe the job and stay on it.
  ```csharp
  [RecurringJob("0 2 * * *")]                        // id: no-show-detection
  [RecurringJob("0 6 * * 1", Id = "no-show-weekly")]  // the second names itself
  ```
- **Inject through the primary constructor.** The generated invoker resolves the job from the DI container, so constructor parameters are supplied. Never declare a bare `private IFoo _foo = null!;` field — nothing assigns it and the job throws `NullReferenceException` on first use.
- `partial` is mandatory: the generator puts the invoker inside the class. Without it **PRAG2502** is an error and nothing is generated for the job.
- `[Retry]` (`BackoffStrategy`: `Fixed | Exponential | ExponentialWithJitter`) sets the durable attempt budget; `[Timeout]` sets the per-execution deadline.
- `MaxAttempts` is the **total number of executions, including the first**: `MaxAttempts = 2` means one retry.

### Scheduling controls (Priority · MaxConcurrency · Misfire)

Optional properties on `[Job]` / `[RecurringJob]`, all defaulting to no-special-handling:

```csharp
[RecurringJob("0 9 * * *", Misfire = MisfirePolicy.Skip)]   // digest that must not fire late
[Job(Priority = 10)]                                        // due-first ordering
[Job(MaxConcurrency = 2)]                                   // ≤ 2 of this type per host
```

- **`Priority`** (default `0`) — when jobs are due together they are polled by `Priority` descending, then `ScheduledFor` ascending. Persisted on `JobInstance.Priority` (from `IJobTypeRegistry.GetPriority`), so ordering survives a restart. It orders what is *already due*; it does not pull a job ahead of its `ScheduledFor`.
- **`MaxConcurrency`** (default `0` = unbounded) — caps instances of that job type running at once **per host**, independently of `WorkerCount`. An instance over the cap stays `Pending` and is reconsidered next poll — it never blocks a worker. Per host: a 3-host cluster with `MaxConcurrency = 2` runs up to 6.
- **`Misfire`** on `[RecurringJob]` (`MisfirePolicy`, default `RunOnce`) — when the host was down and returns more than `JobsOptions.MisfireThreshold` (default 1 min) past a due occurrence: `RunOnce` runs the missed occurrence once then resumes; `Skip` advances straight to the next future occurrence. Within the threshold it counts as a normal slightly-late run.

### 2. Parameterized job (one-shot / delayed)

```csharp
[Job]
[Retry(MaxAttempts = 3, Strategy = BackoffStrategy.ExponentialWithJitter, BaseDelayMs = 500)]
public sealed partial class SendCheckInReminderJob(
    IEmailService email) : IJob<ReminderParams>
{
    public async Task ExecuteAsync(ReminderParams parameters, JobContext context, CancellationToken ct)
    {
        await email.SendAsync(parameters.Email, "Check-in reminder", ct);
    }
}

public sealed record ReminderParams(Guid ReservationId, string Email);
```

Schedule it by injecting `IJobScheduler`:

```csharp
await scheduler.ScheduleAsync<SendCheckInReminderJob, ReminderParams>(
    new ReminderParams(id, email), delay: TimeSpan.FromHours(24), ct: ct);
```

`IJobScheduler`: `ScheduleAsync<TJob>(delay?)`, `ScheduleAsync<TJob,TParams>(params, delay?)`, `ScheduleAtAsync<TJob>(scheduledFor)`, `ScheduleAtAsync<TJob,TParams>(params, scheduledFor)`, `CancelAsync(jobId)`. Absolute times are normalized to UTC. `JobContext` exposes `JobId`, `JobType`, `ScheduledAt`, `Attempt` (0-based), `MaxAttempts`, `CorrelationId`, `TenantId`.

### 3. Retry (durable)

`[Retry]` is not an in-process loop. The declared policy is exposed via `IJobTypeRegistry.GetRetryPolicy` and applied by the store: a failed job returns to `Pending` with the backoff written to `ScheduledFor`, and the attempt count lives in the job row. So an attempt survives a worker crash, `__Jobs.Attempt` reflects real executions, and `MaxAttempts` is what an operator sees in the table.

A `[Timeout]` expiry marks the job failed and consumes an attempt. Jobs without `[Retry]` fall back to `JobsOptions.DefaultMaxRetries` (default 1 = a single execution).

### 4. Continuation

`[Continuation<TNextJob>]` — note the type is `ContinuationAttribute<T>` — enqueues `TNextJob` after the current job completes successfully. `TNextJob` must implement `IJob`/`IJob<T>` (else **PRAG2505**); cycles A→B→A → **PRAG2506**.

```csharp
[Job]
[Continuation<SendInvoiceEmailJob>]
public sealed partial class GenerateInvoiceJob(IBillingService billing) : IJob<InvoiceParams>
{
    public async Task ExecuteAsync(InvoiceParams p, JobContext context, CancellationToken ct)
        => await billing.GenerateAsync(p.ReservationId, ct);
}
```

The continuation inherits `CorrelationId` and `TenantId`, but not the parameters, and gets its own retry budget. To build one at enqueue time instead, use `JobContinuation.Then<TJob>()` / `JobContinuation.Then<TJob, TParams>(parameters)`.

### 5. Enabling processing

```csharp
app.UseJobs(jobs =>
{
    jobs.WithWorkerCount(2);          // concurrent tasks
    jobs.WithPollingInterval(10);     // seconds
});
```

Other `JobsBuilder` options: `WithLeaseTime`, `WithMaxRetries`, `WithBatchSize`, `WithWorkerId`, `UseEfCore`. Without EF Core the store is in-memory (dev only: jobs do not survive restarts).

Outside `Pragmatic.Composition`, add the generated registration yourself, or nothing runs:

```csharp
builder.Services.AddPragmaticJobs(jobs => jobs.WithWorkerCount(2));
builder.Services.AddDiscoveredJobs();         // SG-generated
builder.Services.AddJobProcessingServices();
```

### 6. EF Core persistence (production)

Two pieces: **declare where the tables live**, and tell the host to use them.

```csharp
[Boundary]
[EnableJobPersistence]              // → __Jobs and __RecurringJobs in this boundary's database
public partial class BillingBoundary;
```

```csharp
using Pragmatic.Jobs.EFCore.Extensions;

app.UseJobs(jobs =>
{
    jobs.UseEfCore();             // drops the in-memory stores
    jobs.UseEfCorePersistence();  // registers EfCoreJobStore + EfCoreRecurringJobStore
});
```

The attribute is what maps the two tables into the generated `DbContext` and puts them in the migration,
and it also registers the bare `DbContext` the stores resolve. The context's `OnModelCreatingPartial` hook
is not a substitute: it maps into the model EF holds in memory, and the migration would not create the
tables. The boundary library that carries the attribute references
`Pragmatic.Jobs.EFCore` (**PRAG2508** otherwise), exactly one boundary carries it (**PRAG2509**), and the
host references it too, because the generated context names its two configurations.

⚠️ **Writing `OnModelCreating` yourself is not the way in a Pragmatic host** — that method belongs to the
generated context. Outside one, on a hand-written `DbContext`, the two configurations are applied
directly:

```csharp
using Pragmatic.Jobs.EFCore.Entities;

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfiguration(new JobEntityTypeConfiguration());          // __Jobs
    modelBuilder.ApplyConfiguration(new RecurringJobEntityTypeConfiguration()); // __RecurringJobs
}
```

…and there the stores' bare `DbContext` is forwarded by hand:
`services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>())`.

`UseEfCore()` without `UseEfCorePersistence()` fails at startup with an explicit message instead of falling back to in-memory.

### 7. Retention

`JobsOptions.RetentionDays` (default 30) and `PurgeBatchSize` (default 1000): terminal jobs older than the window are deleted periodically. `RetentionDays = 0` disables purging — note that job rows keep their serialized parameters, which often carry personal data, so disabling retention keeps that payload forever.

## Store and distributed work

With `Pragmatic.Jobs.EFCore` the store uses a **lease-based distributed lock**: each job is leased by a single worker via an atomic conditional UPDATE, so multiple app instances run in parallel without executing the same job twice. The lease is renewed by heartbeat while the job runs, so a job longer than `LeaseTimeSeconds` is not picked up twice; completion and failure are fenced on the lease holder. On graceful shutdown the lease is released without consuming an attempt. The in-memory store does not provide these guarantees across processes.

⚠️ **Once per healthy run, not exactly once.** A worker that dies mid-job leaves the lease to expire
(`LeaseTimeSeconds`, 300 by default), and the job runs **again** from the start on another worker — the
crashed attempt counts, so a job that keeps killing its worker ends `Failed` instead of looping. Write
jobs to be safe to repeat: check before acting, write with a key the database refuses twice, send
through the outbox rather than directly.

Recurring definitions are claimed with a compare-and-swap (`TryClaimDueAsync`), so one host per tick enqueues them.

## Recurring job lifecycle

The generator emits `PragmaticRecurringJobProvider : IRecurringJobProvider` with the declared definitions. At startup `RecurringJobSchedulerService` hands each to `IRecurringJobRegistrar`, which computes the first occurrence from the cron expression and persists it.

**Persisted state wins over the declaration**: restarting the host does not rewind a running schedule, and does not re-enable a definition disabled via `DisableAsync()`. To adopt a changed cron expression, update or delete the `__RecurringJobs` row.

## Multi-tenant applications

⚠️ **A job runs outside a request, so nothing resolves a tenant for it.** Where entities are
`ITenantEntity` the generated filter is fail-closed: a job that simply queries reads **zero rows** —
not an error, not an empty database, zero rows and a job reporting success. A nightly digest written
that way tells every customer their queue is empty.

**`[RecurringJob]` cannot carry a tenant.** The attribute takes a cron expression, an id, a time
zone, a misfire policy, a priority and a concurrency cap. Each run is enqueued with the definition's
`TenantId`, which for an attribute-declared job is null. The declared form is correct only for work
that is genuinely tenant-independent.

Within one tenant — register per tenant, and open a scope:

```csharp
// registration, once per tenant, through IRecurringJobRegistrar
await registrar.RegisterAsync(new RecurringJobDefinition
{
    Id = $"digest:{tenantId}", JobType = typeof(DigestJob).AssemblyQualifiedName!,
    CronExpression = "0 3 * * *", TenantId = tenantId,
}, ct);

// the job
public async Task ExecuteAsync(JobContext context, CancellationToken ct)
{
    if (string.IsNullOrEmpty(context.TenantId))
        return;                                   // declining beats counting to zero

    using var scope = TenantScope.BeginScope(context.TenantId);
    // every query here sees exactly this tenant
}
```

`TenantScope` works because the registered `ITenantContext` is `AmbientTenantContext`: it answers
from the request when there is one and falls back to the scope when there is not, and both the
Pragmatic filter and the EF Core query filter read that same context.

Across tenants — `FilterMode.Background` through `IQueryFilterToggle`; it lifts the tenant rule at
both levels and keeps soft-delete.

⚠️ **Reading across tenants does not let you write across them.** The lift is for the read; the write
belongs to one organisation, and `TenantInterceptor` throws `TenantNotResolvedException` when an
`ITenantEntity` is inserted or updated with no tenant resolved (`MultiTenancyOptions.RequireTenant`,
on by default). Two mistakes follow from thinking of the sweep as one pass:

- a `TenantScope` around the in-memory change and a `SaveChangesAsync` **after** the loop writes with
  nothing resolved — a refusal;
- one context for several tenants cannot be fixed by nesting scopes: the connection is chosen when the
  context opens it.

So the shape is a scope **per tenant**, container scope included, with the read, the change and the
save inside it:

```csharp
foreach (var tenantId in overdue.Select(row => row.TenantId).Distinct())
{
    using var tenant = TenantScope.BeginScope(tenantId);
    using var scope = scopes.CreateScope();          // IServiceScopeFactory

    var owned = scope.ServiceProvider.GetRequiredService<IRepository<Case>>();
    var unitOfWork = scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(IntakeBoundary));
    // read again (ordinary tenant-filtered read), change, then save — all in here
}
```

⚠️ **Do not enumerate `ITenantStore` to find the tenants.** The generated host registers an empty
`InMemoryTenantStore` and header-based resolution never writes to it, so iterating it iterates
nothing. Take the list from your own rows read in `Background` mode, or from wherever the application
records its customers.

`TenantScope` is in the `Pragmatic.MultiTenancy` **namespace** and ships in `Pragmatic.Abstractions`,
beside `ITenantEntity` and `ITenantStore` — so a domain library that has those has the scope too, and
needs a `using`, not a package.

## What the SG generates

| Trigger | File | Content |
|---|---|---|
| `[Job]` / `[RecurringJob]` | `{Job}.Invoker.g.cs` | Nested `Invoker` with a static `ExecuteAsync`: DI resolution, `[Timeout]` linked token, Activity |
| aggregated | `_Infra.Jobs.Registration.g.cs` | `AddDiscoveredJobs()`: job classes, this assembly's registry as one `IJobTypeRegistrySource`, recurring provider |
| aggregated | `_Infra.Jobs.TypeRegistry.g.cs` | AOT-safe dispatch, parameter (de)serialization, retry policy, continuation target |
| `[RecurringJob]` | `_Infra.Jobs.RecurringJobs.g.cs` | `PragmaticRecurringJobProvider` with the cron definitions |
| aggregated | `_Metadata.Jobs.g.cs` | Host aggregation metadata |

The background services (`JobProcessorService`, `RecurringJobSchedulerService`) are registered automatically by host aggregation when `UseJobs` is called. The registry they read is a composite over one source per assembly that declares a job — each module's, and any package that ships one — so jobs from several assemblies all run.

## Common diagnostics

| ID | Sev | Trigger | Fix |
|---|---|---|---|
| **PRAG2500** | Error | `[Job]`/`[RecurringJob]` does not implement `IJob`/`IJob<T>` | Implement the interface |
| **PRAG2501** | Error | `[RecurringJob]` with missing/empty cron | Specify the cron expression |
| **PRAG2502** | Error | Job not `partial` | Add `partial` (without it nothing is generated and the job never runs) |
| **PRAG2503** | Error | Two `[RecurringJob]` with the same `Id` | Set unique `Id` values |
| **PRAG2504** | Error | `[Retry]` with `MaxAttempts <= 0` | Use a positive value |
| **PRAG2505** | Error | `[Continuation<T>]` where `T` is not a job | Point it at an `IJob`/`IJob<T>` type |
| **PRAG2506** | Error | Cycle in the continuation chain | Break the cycle |

**Cron validation is split**: PRAG2501 only fires on an empty expression. A malformed but non-empty cron (or an unknown `TimeZone`) builds cleanly, then that single definition is logged and skipped at startup. Both 5-field and 6-field (leading seconds) expressions are valid.

## Troubleshooting

**A job fails with `Unknown job type: …`** — no registry knows it. The message says how many were asked; "No registry is registered" means, outside Composition, that `AddDiscoveredJobs()` is missing, or that a package shipping the job registers its own and was not asked to.

**Recurring job does not start** — is `UseJobs` called in `Program.cs`? Does the cron parse (check startup warnings, not the build)? Does the `__RecurringJobs` row have a `NextExecutionAt`, and is it enabled?

**Recurring job ignores my new cron expression** — persisted state wins; update or delete the `__RecurringJobs` row.

**Job executed multiple times in a cluster** — you are using the in-memory store: switch to `UseEfCore()` + `UseEfCorePersistence()` for distributed leasing.

**Job does not survive a restart** — in-memory store; `Pragmatic.Jobs.EFCore` is required.

**`NullReferenceException` on a dependency** — the job declares a `= null!` field instead of injecting through the primary constructor.

## Build verification

```powershell
dotnet build                                              # PRAG25xx = job issues
dotnet test path\to\App.Tests --no-restore -v minimal
```

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Invoicing and Warehouse example
applications — code that compiles and that `Invoicing.IntegrationTests` and `Warehouse.IntegrationTests`
exercise — and kept identical to it by the gate: a recurring job that fans out one tenant at a time, the
boundary that holds the job store's tables, the host's `UseJobs`, a parameterized job scheduled for a
moment from inside the transaction that needs it, and a test that drives the work on a day the clock
chooses.
