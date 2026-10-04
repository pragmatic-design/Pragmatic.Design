# Control plane: telling a running host what to do

> **Scope**: `src/Pragmatic.Abstractions/ControlPlane/`, 25 files, 26 public types.
> `IControlPlane` · `IHostIdentity` · `IHostStatus` · `IHostHealthContributor` ·
> `IHostHealthAggregator` + `AggregatedHealthReport` · `IClusterLeadership` ·
> `IHostCommandDispatcher` · `IHostCommandHandler<T>` · `IHostMigrationCoordinator` ·
> `HostInfo` · `HostCommand` and its four commands · `ControlPlaneEvent` and its two events ·
> `ControlPlaneError` · `MigrationStatus` · `HostState` · `HostType` ·
> `ContributorHealthReport` · `ContributorHealthStatus` · `HealthContributorMode`
>
> **Not covered here**: `Abstractions/Maintenance/` (`IMaintenanceMode`,
> `IMigrationProgressStream`) is a neighbouring feature that the host bridges to `IHostStatus` at
> runtime. The implementations (`Pragmatic.Composition.Host` for the monolith and
> `Pragmatic.Agent.Client` for the distributed case) are named where they matter, not opened.

For the member-by-member catalogue, see [interfaces](../interfaces.md#20-control-plane). This
document is about how the pieces fit together, and why they are shaped the way they are.

## The question it answers

A running host is not only a request handler. Something outside it needs to know **which process
this is**, **what state it is in**, and needs a way to say *drain*, *go into maintenance*, *migrate*,
and get an answer back.

The whole namespace is contracts only. That is the point: the same application code runs as a
monolith with no coordinator at all, or as one node under an agent backbone, and nothing in the
domain changes. The host generator registers no-op implementations by default; calling `UseAgent()`
replaces them with the Agent-backed ones.

| Contract | Default (generated into every host) | With `UseAgent()` |
|---|---|---|
| `IControlPlane` | `NoOpControlPlane` | `AgentControlPlane` |
| `IClusterLeadership` | `NoOpClusterLeadership`, always leader | `AgentClusterLeadership`, lease-based |
| `IHostIdentity` | `LocalHostIdentity` | (same, reported to the agent) |
| `IHostStatus` | `LocalHostStatus` | (same) |

A monolith therefore never branches on "am I clustered". It asks `IClusterLeadership` whether it
leads, and the answer is yes.

## Identity and status

`IHostIdentity.HostId` is a version-7 GUID **created in the constructor and regenerated on every
startup**. It identifies a *process instance*, not a deployment slot: two restarts of the same
container are two ids, and that is what makes a heartbeat stream meaningful.

`HostName` comes from the topology metadata the source generator emits: `LocalHostIdentity` reads
`AssemblyMetadataRegistry.FindByCategory(MetadataCategory.HostTopology)` and takes the `host`
property from it, falling back to `Environment.MachineName`. The name a host reports is the name it
was compiled with.

`IHostStatus` is the one mutable object in the feature: `State`, `StateReason`, `StateChangedAt`,
`MigrationStatus`, and two mutators. It is read by a hosted sync service, by the health check, and
written by command handlers, concurrently, by construction. Implementations are required to guard
against that, and `LocalHostStatus` does it with a `System.Threading.Lock` around every getter and
both mutators.

`HostState.Maintenance` is a **reflection, not a command**. The **503** comes from
`IMaintenanceMode`: `Composition.Host`'s maintenance middleware answers on `IsActive`, and
`HostStatusSyncService` polls that same flag every two seconds and moves the state to match. The
state tells you the host is in maintenance; transitioning to it is not what puts it there.

`MigrationStatus` validates in the constructor rather than trusting callers: an inconsistent
`IsError`/`ErrorMessage` pair throws, and so does an out-of-range `ProgressPercent`, with 0 and 100
inclusive. That check is the constructor's alone: both are public `init` properties, so an object
initializer or a `with` expression assigns them unvalidated, and `NaN` passes the range comparison in
either case.

## Commands

`HostCommand` is an abstract record with four concrete commands: `DrainCommand`,
`EnterMaintenanceCommand`, `ExitMaintenanceCommand`, `MigrateCommand`. Every command carries a
`CommandId` and an optional `CorrelationId`.

Two decisions in there are worth stating plainly.

**Dispatch is allow-listed, never activated by name.** `HostCommandDispatcher` resolves the concrete
type through a static `FrozenDictionary` with exactly those four keys; a command type it does not
recognise is dropped with a warning. No `Type.GetType`, so a control message cannot name an
arbitrary type into existence.

**`CommandId` exists so redelivery is safe.** The transport is where deduplication actually happens:
`AgentControlPlane` marks an id as seen before executing, so a command redelivered after a
reconnection is applied once: the control plane is a singleton and it is the connection underneath
it that reconnects, so the seen-set outlives the reconnect. Its limits are the process: the set is
in-memory and bounded at 8192 ids with eviction, so a redelivery after a restart, or after that id
has been evicted, runs again. Delivery stays at-least-once and handlers are required to be
idempotent; the seen-set is a best-effort optimisation over that requirement, not a replacement.

**A command is addressed to an instance.** `SendCommandAsync(targetHostId, …)` takes a `HostId` as
`GetAllHostsAsync` lists it (one running instance), and the Agent delivers it to that connection only;
a command for a whole service is one command per instance.

**A drain leaves the rotation, not the process.** `DrainCommandHandler` reports `Draining` (with the
Agent that report rewrites the instance's announcement out of the rotation, and the gateway stops sending
it new requests), serves what is in flight for the grace period, then reports `Drained` and stays up.
`ExitMaintenanceCommand` puts it back. It does not answer 503 during the drain, and it does not stop
the application afterwards.

Handlers are ordinary DI registrations of `IHostCommandHandler<TCommand>`, and the host generator
registers all four that ship: maintenance in, maintenance out, drain, migrate. What is conditional is
not the handler but the `IHostMigrationCoordinator` behind it: `MigrateCommandHandler` takes a null
coordinator and logs.

`IControlPlane.SendCommandAsync` returns `null` on success and a `ControlPlaneError` on failure,
rather than throwing. `ControlPlaneError` implements `IError` explicitly and reports status **502**,
so `Title` and `StatusCode` are read through the `IError` view of it.

## Migrations, coordinated from the host

`IHostMigrationCoordinator` has no hand-written implementation. The host generator emits
`PragmaticMigrationCoordinator` into `_Migration.Coordinator.g.cs` in the host assembly, and only
when two conditions hold: `Pragmatic.Migrations` is referenced **and** the host actually declares
databases. With no databases declared, no coordinator exists and a `MigrateCommand` logs that fact
instead of failing.

The generated body migrates each declared database in sequence, throwing on the first failure:
fail-fast, so a half-migrated cluster stops rather than continuing.

## Health

Two contracts, deliberately apart. `IHostHealthContributor` is what a component implements to answer
"am I healthy", with a `HealthContributorMode` saying how its answer should be treated, and a
`ContributorHealthReport` carrying the result. `IHostHealthAggregator` runs all registered
contributors in parallel via `Task.WhenAll` and folds them into an `AggregatedHealthReport`.

The host generator registers the **aggregator**. Contributors are registered by the module that owns
them, at the point where that module is configured. `Pragmatic.Messaging` is the worked example:
each transport extension (Channels, RabbitMQ, Kafka, Azure Service Bus, SQL) registers its
`TransportHealthContributor`, and the outbox extension registers `OutboxHealthContributor`. To add
your own, implement `IHostHealthContributor` and register it in DI the same way, from the extension
method that configures your component.

The composed verdict is read by asking the aggregator: contributors fold into one report rather than
into N independent checks. `Composition.Host` also ships `ControlPlaneHealthCheck`, an `IHealthCheck`
that folds that aggregate together with the host lifecycle state; the generated host does not
register it and maps no `/health` route, so an application that wants the aggregate over HTTP
registers the check and maps the endpoint itself.

## Events

`ControlPlaneEvent` has two subclasses, `HostStateChangedEvent` and `ConfigChangedEvent`, which
`IControlPlane.StreamEventsAsync` produces from the agent's key-value watch, forwarding exactly two
prefixes: `state/app:` and `config/`. That watch is the whole subscription, and an operational tool
or a component that must react to another host changing state observes it there. A
`HostStateChangedEvent` carries what the watch can see: `OldState` is always `Starting`, and
`NewState` comes from parsing the raw value stored under the key: `Stopped` when the key was
deleted, otherwise a descriptor-shaped payload that matches no state name and resolves to `Starting`
as well.

`BroadcastEventAsync` is a separate path rather than the publishing side of that subscription: it
writes the event under an `events/` key, carrying `type`, `SourceHostId` and `Timestamp` only, and
`events/` is not one of the prefixes the stream forwards.

Unlike `HostCommand`, `ControlPlaneEvent` carries **no** `[JsonPolymorphic]` discriminator. That
asymmetry is intentional and declared in the contract: commands travel through one known dispatcher
and can fix their discriminator once, while events may cross transports whose serialization strategy
is theirs to define.

## External references

Named here, described where they live:

- **`Pragmatic.Composition.Host`**: `LocalHostIdentity`, `LocalHostStatus`, `NoOpControlPlane`,
  `NoOpClusterLeadership`, `HostCommandDispatcher` and the four command handlers,
  `HostHealthAggregator`, `ControlPlaneHealthCheck`, and `HostStatusSyncService`, the hosted service
  that follows `IMaintenanceMode` into `IHostStatus`.
- **`Pragmatic.Agent.Client`**: `AgentControlPlane`, `AgentClusterLeadership`,
  `AgentHeartbeatService`, wired by `UseAgent()` on the builder.
- **`Pragmatic.Migrations`**: `TenantMigrationOrchestrator` reports progress through
  `IControlPlane.ReportStatusAsync`; `DatabaseLeaderElection` is the *other* election, the one that
  guards schema changes. `IClusterLeadership` is deliberately not used for migrations: a soft,
  lease-based election can hand two nodes the lease across a partition, which is survivable for a
  singleton worker and not for DDL.
- **`Pragmatic.Messaging`**: ships the health contributors described above.
- **`Pragmatic.Result` → `IError`**: the contract `ControlPlaneError` implements.
