---
name: pragmatic-use-events
description: Use when an entity raises a domain event and a handler in the same process reacts after commit — Pragmatic.Events, [EventHandler], ordering, lifecycle [Raises<T>], event outbox. Cross-boundary work is pragmatic-use-messaging.
---

# Pragmatic Use Events

**Covers:** In-process domain events with Pragmatic.Events — RaiseEvent, [EventHandler]/IDomainEventHandler<T>, handler ordering, dispatch after commit, lifecycle [Raises<T>], the transactional event outbox.

`Pragmatic.Events` handles **in-process domain events**: an entity raises an event during business logic, and one or more handlers react after the change is persisted — same process, no message bus. The source generator discovers handlers and builds an AOT-safe typed dispatch table.

## When to use

- An entity change should trigger a side effect in the **same boundary/process** (e.g. confirming a reservation sends a notification).
- You want the reaction to run **after a successful save**, not inside the request handler.

For **cross-boundary** or **durable/async** reactions (another service, a queue, retries across restarts) use `pragmatic-use-messaging` — publish to `IMessageBus` and handle with `[MessageHandler] IMessageHandler<T>`. Rule of thumb: same boundary, in-process → `IDomainEventHandler<T>` (this skill); cross-boundary/durable → the bus.

## Packages

```xml
<PackageReference Include="Pragmatic.Events" Version="1.0.0-alpha.*" />
<PackageReference Include="Pragmatic.Events.EFCore" Version="1.0.0-alpha.*" />   <!-- EF interceptor + outbox -->
<PackageReference Include="Pragmatic.SourceGenerator" Version="1.0.0-alpha.*">
  <IncludeAssets>analyzers; build; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

Namespaces: `Pragmatic.Events`, `Pragmatic.Events.Attributes`, `Pragmatic.Events.EFCore.Outbox`, `Pragmatic.Authoring` (for `[Raises<T>]`).

## Core patterns

### 1. Raise an event from an entity

Inherit `DomainEventSource` and call the protected `RaiseEvent`. Events accumulate on the entity and are dispatched after persistence. Inherit `DomainEvent` for a free `EventId` (used for dedup/idempotency/tracing) and the `OccurredAt` timestamp.

```csharp
using Pragmatic.Events;

public sealed record ReservationConfirmed(Guid ReservationId, Guid GuestId, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);

[Entity]
public partial class Reservation : DomainEventSource
{
    public void Confirm(TimeProvider clock)
    {
        Status = ReservationStatus.Confirmed;
        RaiseEvent(new ReservationConfirmed(Id, GuestId, clock.GetUtcNow()));
    }
}
```

> If you implement `IDomainEvent` directly instead of inheriting `DomainEvent`, set `EventId` yourself — the interface default is `Guid.Empty`, which makes deduplication a no-op.

An event other boundaries are meant to consume is marked **`[PublicEvent]`**: it becomes part of the
cross-boundary contract and appears in the generated AsyncAPI. The publishing boundary owns that
contract, so treat a public event as a stable API: an internal one can change with its boundary, a
public one cannot.

⚠️ **An integration event is a domain event that is also published**, and the two forms are **not**
alternatives. `IIntegrationEvent` is declared `IIntegrationEvent : IDomainEvent`, so implementing it
says both things at once; `[PublicEvent]` says only "published" and needs the type to be a domain event
already. On a type that implements neither, the marker is read by nobody — it stays out of the AsyncAPI
document (`AsyncApiFeature` catalogues types implementing `IDomainEvent`) and out of the transactional
outbox (which captures events raised by tracked `IHasDomainEvents` entities). The build says so:
**PRAG0836**.

**So a contract between two services is the publisher's domain event**, raised by its aggregate — which
is what makes it both publishable and transactional. A plain record on the bus gets neither guarantee.

### 2. Handle it

Mark a class `[EventHandler]` and implement `IDomainEventHandler<TEvent>`. Multiple handlers run in ascending `Order` (default 0); a handler failure is logged and does **not** stop the others (continue-on-failure).

```csharp
using Pragmatic.Events;
using Pragmatic.Events.Attributes;

[EventHandler]
public sealed class SendConfirmationEmail(IGuestNotifier notifier) : IDomainEventHandler<ReservationConfirmed>
{
    public int Order => 0;   // lower runs first; IDomainEventHandler<T>.Order, default 0

    public Task HandleAsync(ReservationConfirmed e, CancellationToken ct = default)
        => notifier.ConfirmationAsync(e.GuestId, ct);
}
```

Handlers run in an **internal call context**: L1–L3 authorization filters are skipped (a system reaction is not the HTTP user's action), while data-level filters and the user context are preserved.

### 3. Register and dispatch

**In a composed host there is nothing to write.** The host registers every module's event handlers, and the generated boundary `DbContext` carries the interceptor that raises declarative lifecycle events. The calls below are what that amounts to, and what a project **without** the composed host — a hand-written `DbContext`, a test harness — writes itself.

The generator emits `AddPragmaticEventHandlers()` (AOT-safe, no reflection), and that method registers the in-memory dispatcher itself. Wire the interceptor that *raises* declarative lifecycle events, and dispatch happens on its own after `SaveChangesAsync`:

```csharp
services.AddPragmaticEventHandlers();          // SG-generated — every [EventHandler] + the dispatcher

services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString);
    options.UseDomainEvents();                  // adds LifecycleEventsInterceptor — raising only
});
```

**Who dispatches.** `EfCoreUnitOfWork` does, after a successful `SaveChangesAsync` — it takes the events off the tracked entities (so a retry does not re-dispatch) and either hands them to whoever owns the commit, or dispatches them itself. The generated repository saves through it too, so a write made by hand behaves like the same write made by a mutation. Two consequences worth knowing:

- Handlers run **in the scope that asked for the write**, so the tenant and the user of the request are the ones a handler sees — and the fail-closed tenant filters show the handler the rows it was called to act on.
- Under a composition or a mutation, dispatch happens **after the commit and outside it** — a handler that writes is an operation in its own right, not a participant in the transaction it is reacting to.

A save that fails hands over nothing: the events stay on the entity rather than announcing a write that never happened. With no dispatcher registered at all they are left there too, untouched — never taken and silently dropped.

⚠️ **A handler that throws does not fail the request.** The exception is logged at `Error`, counted on
`pragmatic.events.handler_failures` and recorded on the trace — and the caller gets its 200. Alert on
that metric. A side effect that must happen when the write happens is not an in-process handler: put it
in the operation itself, or make it durable with the outbox below.

⚠️ **Only `SaveChangesAsync` through the unit of work dispatches.** A synchronous `dbContext.SaveChanges()`
writes the rows and leaves the events on the entities; nobody takes them.

### 4. Declarative lifecycle events

Let the generator raise an event at a persistence transition instead of writing it by hand. Put `[Raises<TEvent>(on: ...)]` on an entity that derives from `DomainEventSource`; the lifecycle interceptor raises the event at that transition, filling the event constructor from entity members **matched by name**.

```csharp
using Pragmatic.Authoring;

[Entity]
[Raises<DrugCreated>]                                  // On defaults to Created
[Raises<DrugDeleted>(EntityLifecycle.Deleted)]
public partial class Drug : DomainEventSource { public string Name { get; set; } }
```

⚠️ **On an entity's method it generates nothing — and the build refuses it, `PRAG2753`.** The attribute
accepts `AttributeTargets.Method`, but the generated `{Entity}.LifecycleEvents.g.cs` carries only the
lifecycle branches from the attribute on the **class**, and a generator cannot add statements to a body
you wrote. The diagnostic names the declaration and the two shapes below.

The target is still `AttributeTargets.Method`, on purpose: on a type that is **not** an entity the
member-level declaration is what the host's event graph reads to attribute a raise to its origin —
that is where `PRAG0816` gets `RecallDrugAction.Execute()` from.

For a move that is not a lifecycle transition, the two shapes that work are `[RaisesEvent<T>]` on the
**state machine's** target member, or `RaiseEvent(new TEvent(...))` **in the method**, by hand. On a
`[Mutation]` class, `[Raises<T>]` does work.

**The payload is read after the save, so a generated value is in it.** On a create mutation a
`[GeneratedValue("…{SEQ}…")]` property, an audit stamp and a tenant id are all filled by the unit of
work as part of the commit, not by the operation's body: an event parameter named after one of them
carries the committed value. Take it from the event — there is no need to read the row back.

**One handler may handle several events.** A class implementing three `IDomainEventHandler<T>` is
registered for all three.

### 5. Transactional event outbox

`AddInMemoryDomainEvents` dispatches in-process only — an event is lost if the process crashes between commit and dispatch. The outbox (in `Pragmatic.Events.EFCore`) writes each event into the `__EventOutbox` table **in the same transaction** as the entity change, then a background loop delivers it. Three steps:

```csharp
// 1. Map the table (in OnModelCreating):
protected override void OnModelCreating(ModelBuilder b) => b.AddEventOutbox();

// 2. Register the capture interceptor on the context:
services.AddDbContext<AppDbContext>((sp, o) => o
    .UseNpgsql(cs)
    .AddInterceptors(sp.GetRequiredService<EventOutboxInterceptor>()));

// 3. Register the delivery loop + options:
services.AddEventOutbox<AppDbContext>(o =>
{
    o.BatchSize = 100;
    o.PollingInterval = TimeSpan.FromSeconds(5);
    o.MaxAttempts = 5;                 // then the row is abandoned (inspect LastError)
    o.ClaimLeaseDuration = TimeSpan.FromMinutes(5);
});
```

Delivery is **at-least-once** (a crash between dispatch and mark-processed re-delivers) — **handlers must be idempotent**. Rows are claimed atomically across replicas (`ClaimedBy`/`ClaimedUntil`) so an event is delivered once per healthy run; the W3C trace context and tenant are propagated across the async boundary. Deserialization is **fail-closed**: only event types with a registered handler are resolvable (no `Type.GetType` on a DB string).

### Enabling the outbox on a generated DbContext

On a source-generated boundary DbContext you don't hand-wire the three steps — mark the boundary `[EnableEventOutbox]` and the generator does all of it (maps `__EventOutbox`, adds the interceptor, registers the delivery service, and includes the table in the schema metadata so migrations create it):

```csharp
using Pragmatic.Events.Attributes;

[Boundary]
[EnableEventOutbox]                 // this boundary's domain events go through the outbox
public partial class OrdersBoundary;
```

Requires the boundary project to reference `Pragmatic.Events.EFCore` — otherwise the generator emits **PRAG2752** (rather than silently doing nothing). Two boundaries sharing one physical database share a single `__EventOutbox` table. The manual three-step wiring above stays the path for a hand-written DbContext.

## What the SG generates

| Trigger | Output | Purpose |
| --- | --- | --- |
| `[EventHandler]` on `IDomainEventHandler<T>` | `AddPragmaticEventHandlers()` | Registers each handler (scoped) |
| `[EventHandler]` (per assembly) | `GeneratedEventDispatchTable : ITypedEventDispatchTable` | AOT-safe typed dispatch for the untyped batch path (registered additively per module; probed by `InMemoryEventDispatcher`) |
| `[Raises<T>(on: ...)]` on a `DomainEventSource` entity | `IRaisesLifecycleEvents` partial | Raises the event at the lifecycle transition |
| Assembly metadata | `_Metadata.EventHandlers.g.cs` | Lets the host discover and call the registration method |

## Common diagnostics

| ID | Level | Meaning | Fix |
| --- | --- | --- | --- |
| **PRAG1670** | Error | `[EventHandler]` on a class that does not implement `IDomainEventHandler<T>` | Implement the interface (or remove the attribute) |
| **PRAG0816** | Warning | An event is raised via `[Raises<T>]` but no handler exists anywhere | Add a handler or remove the raise |
| **PRAG0822** | Warning | Cascade cycle: event → handler → operation re-raises another event, looping | Make a handler terminal/idempotent or guard the re-raise |
| **PRAG2750** | Error | `[Raises<T>]` on an entity that is not a `DomainEventSource` | Inherit `DomainEventSource` |
| **PRAG2751** | Warning | An event constructor parameter matches no entity member (passed `default`) | Rename to match, or set it in a domain method instead |
| **PRAG2753** | Error | `[Raises<T>]` on an **entity's method** — nothing generates that raise | Declare it on the entity class (a lifecycle transition), use `[RaisesEvent<T>]` on the state machine's target member, or `RaiseEvent(...)` in the body |

## Troubleshooting

**Handler does not fire** — is it `[EventHandler]` and does it implement `IDomainEventHandler<T>`? Is the handler's library referenced — and hosted — by the host (outside a composed host: is `AddPragmaticEventHandlers()` called)? Is the boundary `[EnableOutbox]` (then it is `PRAG0837`, see `pragmatic-use-messaging`)? Was the event actually raised (`RaiseEvent`, not merely constructed) and the entity saved?

**Events lost on crash** — plain `AddInMemoryDomainEvents` is not durable. Use the transactional outbox (pattern 5) for the atomicity guarantee.

**Duplicate handling** — with the outbox, delivery is at-least-once; make handlers idempotent (e.g. key off `IDomainEvent.EventId`).

**Outbox row never delivered, `LastError` says "No registered handler"** — the delivery allowlist is built from registered `IDomainEventHandler<T>` types; register a handler for that event, or the type was renamed since it was written.

## Build verification

```bash
dotnet build -c Release --warnaserror      # PRAG diagnostics above surface here
dotnet test
```

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Invoicing example application — code
that compiles and that `Invoicing.IntegrationTests` exercises — and kept identical to it by the gate: a
domain event, the state machine that raises it on the move, `[Raises<T>]` on a create mutation and the
event built after the save, and two handlers.

The transactional event outbox (`[EnableEventOutbox]`) is used by no tested application yet, so there is
no example of it here: the sections above are the reference.
