# Pragmatic.Jobs.Samples

Runnable samples for [Pragmatic.Jobs](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Jobs/samples/Pragmatic.Jobs.Samples/Pragmatic.Jobs.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## Scenarios

| Sample | Demonstrates |
|--------|--------------|
| `ScheduleAndAwaitSample` | Schedule a job, processor drains it |
| `ParameterizedJobSample` | Typed parameters round-tripped via JSON |
| `DelayedSchedulingSample` | Delay / future execution |
| `RetryOnTransientFailureSample` | `[Retry]` re-enqueues until success |
| `ContinuationChainSample` | Chained jobs via `IJobScheduler` |
| `CancellationSample` | `IJobScheduler.CancelAsync` on a pending job |
| `TimeoutEnforcementSample` | `[Timeout]` cancels the job token at the deadline |
| `DiagnosticsSample` | `JobsDiagnostics` meter + activity (OpenTelemetry-ready) |
| `EfCorePersistenceSample` | `EfCoreJobStore` via `UseEfCorePersistence()` (SQLite) |
| `DistributedLockingSample` | Lease contention + expiry recovery on the EF Core store |
| `MessageSchedulerBridgeSample` | Messaging.Jobs bridge: `IMessageScheduler` / `EnableScheduledMessages()` |

## External dependencies

_None by default._ The in-memory scenarios need nothing. `EfCorePersistenceSample` and
`DistributedLockingSample` use an **in-memory SQLite** connection (no Docker) to exercise the
real relational store and atomic lease logic — swap `UseSqlite(...)` for `UseNpgsql(...)` etc.
for PostgreSQL / SQL Server. `MessageSchedulerBridgeSample` is setup-only (the bridge's live
round-trip belongs to the Messaging runtime; a runnable version lives in
`Pragmatic.Messaging/samples/Pragmatic.Messaging.Outbox.Samples`).

## Related

- Module: [Pragmatic.Jobs](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/jobs/
- Source: `Pragmatic.Jobs/src/Pragmatic.Jobs/`
