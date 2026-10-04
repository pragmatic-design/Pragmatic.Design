---
title: "Maintenance: taking the host out of service, on purpose"
description: "> **Scope**: `src/Pragmatic.Abstractions/Maintenance/`, 4 files."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Abstractions/docs/how-it-works/maintenance.md
sidebar:
  order: 16
---
> **Scope**: `src/Pragmatic.Abstractions/Maintenance/`, 4 files.
> `IMaintenanceMode` · `IMaintenanceModeObserver` · `IMigrationProgressStream` ·
> `MigrationProgressEvent`.
>
> **Not covered here**: the implementations, the middleware and the admin endpoints live in
> `Pragmatic.Composition.Host`; the fleet-wide contracts (`IHostStatus`, `HostCommand` and friends)
> are a feature of their own; see [interfaces §20](/modules/abstractions/interfaces/#20-control-plane). Consumers
> outside Abstractions are named where they matter, not opened.

For the member-by-member catalogue, see [interfaces](/modules/abstractions/interfaces/#19-maintenance). This document
is about how the pieces fit together, and why they are shaped that way.

## The question it answers

An application that migrates its own database at startup has a window in which it is running but must
not serve traffic. Two things have to happen in that window: requests get a 503 instead of an error
from a half-migrated schema, and someone watching has to be able to see how far along it is.

`IMaintenanceMode` is the switch. `IMigrationProgressStream` is the window into what the switch is
covering for. They are separate because the first is consulted on every request and the second on
none.

Both contracts sit in Abstractions with no ASP.NET Core dependency, so any module (a job runner, a
message consumer, a background service) can ask "are we in maintenance?" without pulling in the web
stack. The state itself exists only where the host does: the single implementation,
`MaintenanceModeService`, ships in `Pragmatic.Composition.Host`.

## Activation is reference-counted, not a boolean

`Activate(reason, eta)` returns an `IDisposable`; disposing it deactivates. That shape is chosen over
`Enter()`/`Exit()` for one reason: overlapping activations.

Startup migration activates maintenance. An operator, watching, activates it too through the admin
endpoint. If the pair were a boolean, whichever finished first would open the host while the other
was still working. Instead each `Activate` increments a counter and hands back a distinct handle; the
mode stays on until the **last** handle is disposed. `Activate` is deliberately not idempotent: two
calls must return two handles, or the count is wrong.

The other half of that contract is that disposing the *same* handle twice is a no-op.
`MaintenanceHandle` enforces it with an `Interlocked.Exchange` on a disposed flag, with
`Deactivate()` refusing to go below zero as a second line of defence: a `using` block inside a
`try/finally` that also disposes must not silently end someone else's maintenance window.

Implementations must be thread-safe. `MaintenanceModeService` uses a `System.Threading.Lock` for
transitions and a `volatile bool` for the read path, because `IsActive` is on the hot path of every
request and the transitions are not.

## Two registration paths, and why both exist

The generated host registers the service with `TryAddSingleton<IMaintenanceMode>`, a default that
steps aside. Calling `app.UseMaintenanceMode()` registers the same service with `AddSingleton`,
overriding it, and adds what the default cannot know about: the `MaintenanceModeOptions` and the
`IMigrationProgressStream`.

What neither path registers is the pipeline half. The 503 middleware and the admin panel are the job
of `MaintenanceStep`, an `IStartupStep` at `Order -100` that adds `MaintenanceMiddleware` and maps
`MaintenanceAdminEndpoints`; nothing registers that step, so it is not in the pipeline of a generated
host. Both paths register **services only**: what maintenance gives you is a switch any code can read
(and one a control-plane `EnterMaintenanceCommand` does flip, through the `IHostCommandHandler` pair
the host template emits), not a change in what an HTTP request gets back.

Both paths register through a **factory**, not a pre-built instance, and the factory does two things
the instance could not: it resolves the logger, and it enumerates
`IEnumerable<IMaintenanceModeObserver>` from the container to attach them. A bare instance left the
observer seam unreachable and the logger null; the comment recording that is still in
`PragmaticBuilderMaintenanceExtensions`.

**To be notified when maintenance turns on or off, register an `IMaintenanceModeObserver`.** Both
registration paths pick it up; `OnActivatedAsync(reason, eta)` and `OnDeactivatedAsync()` are called
on transitions, not on every stacked activation. The host's own status reporting does not use it:
`HostStatusSyncService` is a `BackgroundService` that polls `IsActive` and `Reason`, so an observer
you register is yours alone and sees every transition.

## The progress stream

`IMigrationProgressStream` is a plain producer/consumer channel: `Report(evt)` is non-blocking
fire-and-forget, `StreamAsync(ct)` is the `IAsyncEnumerable` the consumer iterates, `Complete()` ends
it.

`ReportFailure(message, exception, databaseName)` is a default interface method composing
`Report(MigrationProgressEvent.Failed(...))` and `Complete()`. It is a DIM rather than an abstract
member because the ordering is the contract: consumers must see the failure as the *last* event
before the stream ends, and an implementer who wrote the two calls by hand could get it backwards.
Overriding it is for transport-level fault semantics, nothing else.

The single implementation, `MigrationProgressStream` in `Pragmatic.Composition.Host`, is a bounded
channel: capacity 100 by default, `DropOldest` on full. Dropping is the right failure mode here: a
progress feed nobody is reading must not block the migration that produces it, and a late reader
wants the most recent state, not the first hundred lines. The capacity is a constructor parameter, so
it is a default rather than a limit.

The only consumer written against it is `MaintenanceAdminEndpoints`, which iterates `StreamAsync(ct)`
and writes `text/event-stream`, the SSE feed behind the maintenance panel, reachable in a host that
maps those endpoints through `MaintenanceStep`.

## `MigrationProgressEvent`

A positional record: `Phase`, `Message`, `ProgressPercent`, `DatabaseName`, `IsError`, `ErrorDetail`,
`Timestamp`.

Two members do work in their initializers. `Timestamp` is declared nullable so callers can omit it,
but resolves to `DateTimeOffset.UtcNow` at construction; the nullable type is an API affordance, not
a signal that the value may be missing. Pass an explicit value when replaying or batching, so the
timestamp reflects when the thing happened rather than when the record was built. `ProgressPercent`
throws `ArgumentOutOfRangeException` outside the inclusive range `[0, 100]`: a percentage arriving at
a progress bar is worth validating at the source, where the producer's stack trace is still available.

Both are **constructor** invariants, and only that. The setters are `init`, so an object initializer
or a `with` expression assigns over the validated value without re-running the check, and the range
pattern `< 0 or > 100` is false for `double.NaN`, which therefore passes. Build these events
positionally.

`MigrationProgressEvent.Failed(message, exception, phase, databaseName)` is the factory for the error
case, setting `IsError` and filling `ErrorDetail` from `exception.ToString()`. It is what
`ReportFailure` calls.

## What the generator emits

There are no attributes in this folder: three interfaces and a record, nothing for the generator to
*read*. What it writes, in two templates, is code that depends on them.

`PragmaticHostTemplate.Infrastructure` emits the maintenance registration block: the
`TryAddSingleton<IMaintenanceMode>` factory with the observer attach, plus `MaintenanceHandleHolder`
and the `IHostCommandHandler<EnterMaintenanceCommand>` / `<ExitMaintenanceCommand>` pair that lets a
control-plane command reach the same switch an operator would use.

`PragmaticEntryTemplate` emits the database initialization bootstrap. It resolves both
`IMaintenanceMode` and `IMigrationProgressStream` with `GetService` (optionally), and every call
site is null-conditional: `progressStream?.Report(...)` for the per-database steps,
`progressStream?.ReportFailure(...)` in the `catch`, and, in the `finally` when nothing failed, a
final `Report` at `ProgressPercent: 100` followed by `progressStream?.Complete()`. The stream is
terminated exactly once on either path. An application that never called `UseMaintenanceMode()` runs
its migrations exactly the same way, reporting to nobody; the worker host never resolves the stream
at all: it declares `progressStream` as `null`, so the same calls are there and do nothing. The
generated flow punctuates itself with `MigrationProgressEvent`s whose percentages are derived from
the number of databases being initialized.

Neither template introduces a type: both write method bodies inside the generated host.

## External references

Named here, described where they live:

- **`Pragmatic.Composition.Host` → `MaintenanceModeService`**: the reference-counted implementation;
  **`MaintenanceMiddleware`**: serves 503 while active; **`MaintenanceAdminEndpoints`**: the admin
  panel and the SSE consumer; **`MaintenanceStep`**: the startup step that adds those two to the
  pipeline, which no generated host registers; **`MigrationProgressStream`**: the bounded channel;
  **`PragmaticBuilderMaintenanceExtensions`**: `UseMaintenanceMode()`.
- **`Pragmatic.Composition.Host/ControlPlane` → `HostStatusSyncService`,
  `MaintenanceCommandHandler`, `MaintenanceHandleHolder`**: how a fleet-level command becomes a
  local activation, and how local state is reported back.
- **`Pragmatic.Migrations` → `MigrationRunner`**: the main producer of `MigrationProgressEvent`,
  including its leader-election phase.
