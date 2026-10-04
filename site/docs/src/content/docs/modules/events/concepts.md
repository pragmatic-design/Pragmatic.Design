---
title: "Architecture and Core Concepts"
description: "This guide explains **why** Pragmatic.Events exists, how its pieces fit together, and how to think about domain events in your application. Read this before div"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Events/docs/concepts.md
sidebar:
  order: 1
---
This guide explains **why** Pragmatic.Events exists, how its pieces fit together, and how to think about domain events in your application. Read this before diving into the individual feature guides.

---

## The Problem

As applications grow, modules inevitably need to communicate. One boundary does something, and another boundary needs to react. The natural instinct is direct method calls -- and that leads to tight coupling.

### Direct method calls: the coupling trap

```csharp
public class Reservation
{
    private readonly IBillingService _billing;
    private readonly INotificationService _notifications;
    private readonly IAuditService _audit;

    public Reservation(
        IBillingService billing,
        INotificationService notifications,
        IAuditService audit)
    {
        _billing = billing;
        _notifications = notifications;
        _audit = audit;
    }

    public async Task ConfirmAsync()
    {
        Status = ReservationStatus.Confirmed;

        // Booking knows about Billing
        await _billing.CreateDraftInvoiceAsync(Id, GuestId, TotalAmount, Currency);

        // Booking knows about Notifications
        await _notifications.SendConfirmationEmailAsync(GuestId, Id);

        // Booking knows about Audit
        await _audit.LogAsync("ReservationConfirmed", Id);
    }
}
```

Three problems with this code:

1. **The entity depends on services.** `Reservation` needs `IBillingService`, `INotificationService`, and `IAuditService` injected. An entity should model domain state and behavior, not orchestrate infrastructure.

2. **Adding a reaction requires modifying the source.** When a new boundary needs to react to reservation confirmation (analytics, loyalty points, partner API), you modify `Reservation.ConfirmAsync()`. Every new side effect is another constructor parameter and another `await` call.

3. **Failure cascading.** If `_notifications.SendConfirmationEmailAsync()` throws, the entire confirmation fails -- even though the notification is not part of the core business operation. The caller gets an exception for an unrelated side effect.

### Service orchestration: better but still coupled

```csharp
public class ReservationService(
    IReservationRepository reservations,
    IBillingService billing,
    INotificationService notifications,
    IAuditService audit)
{
    public async Task ConfirmAsync(Guid reservationId, CancellationToken ct)
    {
        var reservation = await reservations.GetByIdAsync(reservationId, ct);
        reservation.Confirm();
        await reservations.SaveAsync(reservation, ct);

        // Still: every reaction is an explicit call
        await billing.CreateDraftInvoiceAsync(reservation.Id, reservation.GuestId,
            reservation.TotalAmount, reservation.Currency);
        await notifications.SendConfirmationEmailAsync(reservation.GuestId, reservation.Id);
        await audit.LogAsync("ReservationConfirmed", reservation.Id);
    }
}
```

Moving the orchestration to a service fixes the entity problem, but the service itself now depends on every downstream boundary. The coupling is still there -- it just moved up a level. `ReservationService` is a god-class that knows about billing, notifications, and audit.

### The fundamental issue

Both approaches violate the **Open/Closed Principle**: adding a new reaction to "reservation confirmed" requires modifying existing code. The module that performs the action should not need to know (or change) when new consumers appear.

---

## The Solution

Pragmatic.Events inverts the dependency. The entity **raises** an event describing what happened. Zero knowledge of who listens. Handlers in other boundaries **subscribe** to the event and react independently.

```csharp
// The entity raises an event -- knows nothing about billing, notifications, or audit
[Entity]
[BelongsTo<BookingBoundary>]
public partial class Reservation : DomainEventSource, IEntity
{
    public VoidResult<IError> Confirm()
    {
        var result = TransitionTo(ReservationStatus.Confirmed);
        if (result.IsSuccess)
        {
            RaiseEvent(new ReservationConfirmed(
                Id, GuestId, PropertyId, CheckIn, CheckOut,
                TotalAmount, Currency, DateTimeOffset.UtcNow));
        }
        return result;
    }
}

// Billing boundary reacts -- knows nothing about Booking internals
[EventHandler]
public sealed class ReservationConfirmedHandler(
    IBillingActions billingActions) : IDomainEventHandler<ReservationConfirmed>
{
    public async Task HandleAsync(ReservationConfirmed @event, CancellationToken ct = default)
    {
        await billingActions.CreateDraftInvoice(
            reservationId: @event.ReservationId,
            guestId: @event.GuestId,
            totalAmount: @event.TotalAmount,
            currency: @event.Currency,
            ct: ct).ConfigureAwait(false);
    }
}
```

Adding a new reaction -- analytics, loyalty, partner API -- means adding a new handler class. The `Reservation` entity and the `ReservationConfirmedHandler` never change.

Three guarantees:

1. **Decoupled boundaries.** The entity raises events; handlers react. Neither knows about the other. They share only the event record.
2. **Continue-on-failure.** If the notification handler throws, the billing handler still runs. Independent side effects do not block each other.
3. **Post-persistence dispatch.** Events are dispatched after `SaveChangesAsync()` succeeds. If the database write fails, no handlers fire. You never react to something that did not actually happen.

---

## How It Works: The Event Lifecycle

A domain event flows through four stages, from creation inside an entity to handler execution after persistence.

```
Entity Operation
  |
  v
RaiseEvent(event)                     Event added to entity's DomainEvents list
  |                                   (no dispatch yet)
  v
SaveChangesAsync()                    EF Core persists the entity changes
  |
  v
MutationInvoker / EfCoreUnitOfWork    Post-commit: takes the events off the tracked entities
  |                                   (taking clears them, so a retry does not re-dispatch)
  v
InMemoryEventDispatcher
  |
  +---> Handler A (Order -10)          Sorted by Order, ascending
  |       ICallContext.EnterInternalCall()
  |       handler.HandleAsync(@event)
  |
  +---> Handler B (Order 0)            Continue-on-failure: if A throws, B still runs
  |       ICallContext.EnterInternalCall()
  |       handler.HandleAsync(@event)
  |
  +---> Handler C (Order 100)
          ICallContext.EnterInternalCall()
          handler.HandleAsync(@event)
```

Key points:

- **Events accumulate, not dispatch immediately.** `RaiseEvent()` adds the event to the entity's internal list. Nothing happens until persistence completes.
- **The interceptor collects and clears before dispatching.** This ordering prevents double-dispatch if `SaveChangesAsync()` is retried by a resilience wrapper.
- **Each handler runs as an internal call.** Authorization filters are skipped because handlers are system reactions, not user-initiated operations.
- **`OperationCanceledException` propagates immediately.** All other exceptions are caught, logged, and counted -- but do not stop the dispatch chain.

---

## IDomainEvent

The marker interface for all domain events. Defined in `Pragmatic.Abstractions` so modules can depend on the contract without pulling in the runtime.

```csharp
// Pragmatic.Abstractions
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }

    // Default interface member: returns Guid.Empty unless the event supplies its own.
    Guid EventId => Guid.Empty;
}
```

Events are immutable records that carry all the data downstream consumers need. The event is the contract between boundaries -- handlers should never need to query back into the originating boundary.

`EventId` is a unique per-instance identity used for deduplication, idempotent dispatch, and tracing. It is a **default interface member** returning `Guid.Empty`, so events that predate it keep compiling. When you need it (dedup across at-least-once outbox delivery, correlation in traces), inherit from `DomainEvent`, which assigns `Guid.NewGuid()` per instance. If you implement `IDomainEvent` by hand and do not set `EventId`, it stays `Guid.Empty` and dedup based on it is a no-op; prefer inheriting from `DomainEvent`.

### DomainEvent base record

`Pragmatic.Events` provides an abstract base record for convenience:

```csharp
// Pragmatic.Events
public abstract record DomainEvent(DateTimeOffset OccurredAt) : IDomainEvent
{
    // Fresh identity per instance; override via object-initializer to preserve across replay.
    public Guid EventId { get; init; } = Guid.NewGuid();
}
```

Inherit from `DomainEvent` for automatic `OccurredAt` population and a fresh `EventId`:

```csharp
public sealed record ReservationConfirmed(
    Guid ReservationId,
    Guid GuestId,
    Guid PropertyId,
    DateTimeOffset CheckIn,
    DateTimeOffset CheckOut,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
```

Or implement `IDomainEvent` directly when you want full control over the record shape:

```csharp
public sealed record ReservationConfirmed(
    Guid ReservationId,
    Guid GuestId,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset OccurredAt) : IDomainEvent;
```

### Event design guidelines

| Guideline | Rationale |
|-----------|-----------|
| Use `sealed record` | Immutability by default; value equality for testing |
| Include all data handlers need | Handlers should not query the originating boundary |
| Use `DateTimeOffset`, not `DateTime` | Timezone-safe timestamps |
| Name events in past tense | `ReservationConfirmed`, not `ConfirmReservation` |
| Include entity IDs, not entity references | Events are data snapshots, not live object graphs |
| Keep events focused | One event per business fact; avoid "mega events" with every field |

---

## Integration events (the two-level model)

Domain events (`IDomainEvent`) are **internal** to a boundary. Other boundaries should not consume them
directly: that recreates the coupling events exist to remove. The second level is the **integration
event**: a public, self-contained, denormalized fact that *is* the cross-boundary contract.

Mark an event public by implementing `IIntegrationEvent` or applying `[PublicEvent]` (equivalent; the
contract is owned by the publishing boundary):

```csharp
[PublicEvent]
public sealed record ReservationConfirmed(Guid ReservationId, Guid GuestId, DateTimeOffset OccurredAt)
    : IDomainEvent;
```

Public events surface in the generated **AsyncAPI** document (`PragmaticAsyncApi.Json`) tagged
`x-pragmatic-public: true`, with their payload schema, so the AsyncAPI is a real, snapshot-testable
contract. Treat a public event as a stable API: evolve it **additively**.

### Evolving events

Pragmatic events are **transient** (state-based, dispatched via the outbox, not a persisted event
stream), so version *upcasting* does not apply. Detect breaking changes by snapshotting the generated
AsyncAPI; to phase an event out, mark it `[ObsoleteEvent(removeBy: "…")]` (surfaced as
`x-pragmatic-obsolete`) and remove it once consumers have migrated.

---

## IDomainEventHandler\<T\>

The typed handler interface. Each handler processes one event type. Defined in `Pragmatic.Abstractions`.

```csharp
// Pragmatic.Abstractions
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    int Order => 0;
    Task HandleAsync(TEvent @event, CancellationToken ct = default);
}
```

### The Order property

Handlers execute in ascending `Order` (lower values run first). The default is `0`. Within the same `Order` value, execution follows DI registration order.

```csharp
// Runs first -- audit is the highest priority
[EventHandler]
public sealed class AuditHandler : IDomainEventHandler<ReservationConfirmed>
{
    public int Order => -10;
    public Task HandleAsync(ReservationConfirmed @event, CancellationToken ct) { /* ... */ }
}

// Runs second -- default Order is 0
[EventHandler]
public sealed class InvoiceHandler : IDomainEventHandler<ReservationConfirmed>
{
    public Task HandleAsync(ReservationConfirmed @event, CancellationToken ct) { /* ... */ }
}

// Runs last -- notifications are least critical
[EventHandler]
public sealed class NotificationHandler : IDomainEventHandler<ReservationConfirmed>
{
    public int Order => 100;
    public Task HandleAsync(ReservationConfirmed @event, CancellationToken ct) { /* ... */ }
}
```

**Design guidance:** handlers should be independent. If you need guaranteed sequencing between two side effects (e.g., "create invoice then send invoice email"), combine them into a single handler. Do not rely on `Order` for transactional coordination.

### The [EventHandler] attribute

Mark handler classes with `[EventHandler]` for source generator discovery. The Composition SG generates AOT-safe DI registration code (`AddPragmaticEventHandlers()`) for all classes with this attribute.

```csharp
using Pragmatic.Events.Attributes;

[EventHandler]
public sealed class ReservationConfirmedHandler(
    IBillingActions billingActions) : IDomainEventHandler<ReservationConfirmed>
{
    public async Task HandleAsync(ReservationConfirmed @event, CancellationToken ct = default)
    {
        // React to the event
    }
}
```

The attribute is optional for runtime correctness (the handler works with manual DI registration), but it is the recommended approach for AOT safety and zero-ceremony registration.

---

## DomainEventSource and IHasDomainEvents

Entities raise events through two mechanisms. Both work with the EF Core interceptor.

### DomainEventSource (base class)

Inherit from `DomainEventSource` when your entity has no other base class requirement:

```csharp
[Entity]
public partial class Reservation : DomainEventSource, IEntity
{
    public VoidResult<IError> Confirm()
    {
        var result = TransitionTo(ReservationStatus.Confirmed);
        if (result.IsSuccess)
        {
            RaiseEvent(new ReservationConfirmed(/* ... */));
        }
        return result;
    }

    public static Reservation Create(/* params */)
    {
        var reservation = new Reservation { /* ... */ };
        reservation.RaiseEvent(new ReservationCreated(/* ... */));
        return reservation;
    }
}
```

`DomainEventSource` provides:

| Member | Description |
|--------|-------------|
| `DomainEvents` | `IReadOnlyList<IDomainEvent>` -- accumulated events, read-only |
| `ClearDomainEvents()` | Removes all pending events (called by the interceptor) |
| `RaiseEvent(IDomainEvent)` | Adds a single event to the list |
| `RaiseEvents(IEnumerable<IDomainEvent>)` | Adds multiple events |

Events accumulate in the entity and are **not dispatched** until the EF Core interceptor fires after `SaveChangesAsync()`.

### IHasDomainEvents (interface)

When your entity already has a base class (EF Core TPH/TPC hierarchy, a shared base for audit fields, etc.), implement `IHasDomainEvents` directly:

```csharp
public class Reservation : MyExistingBaseClass, IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void ClearDomainEvents() => _domainEvents.Clear();

    public VoidResult<IError> Confirm()
    {
        // ...
        _domainEvents.Add(new ReservationConfirmed(/* ... */));
        // ...
    }
}
```

The EF Core interceptor scans for `IHasDomainEvents` (not `DomainEventSource`), so both approaches work identically for dispatch.

### When to use which

| Situation | Use |
|-----------|-----|
| Entity has no base class | `DomainEventSource` -- less boilerplate |
| Entity already has a base class | `IHasDomainEvents` -- implement the interface manually |
| Entity in TPH/TPC hierarchy | `IHasDomainEvents` -- cannot change the hierarchy root |
| Lightweight value object that raises events | `IHasDomainEvents` -- value objects should not inherit from a class |

### Lifecycle events via `[Raises<T>]`

Instead of calling `RaiseEvent()` by hand, an entity that derives from `DomainEventSource` can **declare** the events its lifecycle raises with `[Raises<TEvent>(on: ...)]` (from `Pragmatic.Authoring`). The source generator wires the raise, and the `LifecycleEventsInterceptor` fires the event at the matching transition, filling the event's constructor from entity members that match by name, with no custom body. The full dev walk-through is in [Getting Started](/modules/events/getting-started/#lifecycle-domain-events).

```csharp
[Entity]
[Raises<OrderPlaced>]                                 // On = Created (default)
[Raises<OrderCancelled>(EntityLifecycle.Deleted)]     // stackable, one per event
public partial class Order : DomainEventSource, IEntity { /* ... */ }
```

Diagnostics: **PRAG2750**, an entity carrying `[Raises<T>]` must derive from `DomainEventSource`; **PRAG2751**, an event constructor parameter that matches no entity member is passed `default` (warning); **PRAG2753**, the attribute on an entity's **method** generates nothing and is refused (declare it on the class, use `[RaisesEvent<T>]` on the state machine's target member, or `RaiseEvent(...)` in the body).

---

## Event Dispatching

### InMemoryEventDispatcher

The built-in dispatcher resolves handlers from DI and invokes them in-process. Registered as **scoped** by `AddInMemoryDomainEvents()`.

Two dispatch paths:

| Method | Use Case |
|--------|----------|
| `DispatchAsync<TEvent>(event)` | Typed -- resolves `IDomainEventHandler<TEvent>` directly |
| `DispatchAsync(IEnumerable<IDomainEvent>)` | Batch (untyped) -- used by the EF Core interceptor when event types are mixed |

The batch path bridges each event to the typed path through the SG-generated typed dispatch tables (`ITypedEventDispatchTable`, one per composed module): a compile-time event→`DispatchAsync<TEvent>` switch, no reflection. The dispatcher probes each registered table in order; the first that recognises the concrete type routes it. Only when no table covers the type (no module referenced the generator, or the event has no handler) does it fall back to a `dynamic` (DLR, not reflection) dispatch. See [Internals](/modules/events/internals/) for the full mechanism.

### Continue-on-failure strategy

The dispatcher catches all exceptions except `OperationCanceledException`:

```csharp
catch (Exception ex) when (ex is not OperationCanceledException)
{
    handlerFailures++;
    EventsDiagnostics.HandlerFailures.Add(1, ...);
    LogHandlerFailed(eventName, handlerName, ex);
    activity?.RecordException(ex);
}
```

This means:

- A failing handler does **not** block subsequent handlers.
- All handlers are invoked regardless of individual failures.
- The dispatcher itself does **not** throw (except on cancellation).
- Failures are observable via logs, metrics, and OTel traces.

### Why continue-on-failure?

In a multi-handler scenario, independent side effects (create invoice, send email, update analytics) should not block each other. If the notification handler fails, the billing handler should still run. Each side effect is independent.

If you need transactional guarantees across multiple handlers:

- **Combine them into a single handler.** One handler, one transaction.
- **Use the built-in transactional outbox** (`Pragmatic.Events.EFCore.Outbox`). Events are written to `__EventOutbox` in the same transaction as the domain write, then delivered by a background worker with at-least-once semantics and retries. See [Transactional Outbox](#transactional-outbox).

### OperationCanceledException

`OperationCanceledException` propagates immediately. Within a single event's handlers, it stops the handler chain. In the batch dispatch loop, it stops before the next event. This respects cancellation tokens (request aborted, shutdown).

---

## EF Core Integration

The `Pragmatic.Events.EFCore` package provides automatic event dispatch after `SaveChangesAsync()`. No manual dispatch calls needed.

### LifecycleEventsInterceptor: raising, not dispatching

The one interceptor in this package **raises** the events an entity declares with `[Raises<TEvent>(on: ...)]`, during `SavingChanges`, while the change-tracking state still says whether the row is being added, updated or deleted. It stops there: nothing is dispatched from inside a save.

No interceptor dispatches in `SavedChangesAsync`. To avoid a captive dependency on the scoped dispatcher it would need a scope of its own, and that scope is the defect: a fresh one resolves a fresh tenant context, no tenant is resolved in it, and every fail-closed query filter then hides the rows the handler was called to act on. The handler runs, finds nothing, writes nothing, and reports success.

### Who dispatches

| Path | Who | When |
|---|---|---|
| A mutation or a domain action | the generated invoker | after the commit, with the commit claim suspended |
| A composition (`[Transactional]`, a batch) | `BatchContext`, fed by `EfCoreUnitOfWork` | after the whole batch commits |
| A repository write, or any plain `IUnitOfWork.SaveChangesAsync` | `EfCoreUnitOfWork` itself | right after a successful save |

All three run **in the scope that asked for the write**, so a handler sees the tenant and the user of the request that caused it.

### Dispatch flow step by step

```
IUnitOfWork.SaveChangesAsync()
  |
  v
LifecycleEventsInterceptor raises [Raises<T>] events onto the entities (SavingChanges)
  |
  v
The database commits
  |                                       <-- a failed save stops here: nothing is taken,
  v                                           and nothing announces a write that did not happen
Take the events off every tracked IHasDomainEvents entity
  |                                       (taking clears them, so a retry does not re-dispatch)
  v
Someone owns this commit?
  |                       \
  | yes                    \ no
  v                         v
batch.DeferEvent(each)      IDomainEventDispatcher.DispatchAsync(all, ct)
  |                         (or, with no dispatcher registered, the events are
  v                          left on the entity rather than silently destroyed)
flushed after the commit,
outside the commit claim
```

### Why take after the save, and only on success?

Two rules, and both were learned the hard way:

- **After.** The lifecycle interceptor raises during `SavingChanges`. Collecting first found an empty list every time.
- **Only on success.** A save that failed wrote nothing, and an event announcing a write that did not happen is worse than one never sent. The refused entity also keeps its events, so a retry can still announce the write if it lands.

Taking is what clears them, so there is no window in which the same event is both pending on the entity and already handed over.

### Registration

```csharp
using Pragmatic.Events.EFCore;

// The generated registration also registers the in-memory dispatcher (TryAdd, so an
// application that wants an outbox instead registers its own and wins).
services.AddPragmaticEventHandlers();

services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString);
    options.UseDomainEvents();  // LifecycleEventsInterceptor: raising only
});
```

`UseDomainEvents()` takes no service provider: the one interceptor it adds has no dependencies, because dispatch is not its job.

---

## Internal Call Context

Event handlers run as **internal calls**. The `InMemoryEventDispatcher` resolves `ICallContext` (from `Pragmatic.Abstractions`, namespace `Pragmatic.Pipeline`) and calls `EnterInternalCall()` before each handler.

```csharp
var callContext = _serviceProvider.GetService<Pragmatic.Pipeline.ICallContext>();

foreach (var handler in handlers)
{
    var scope = callContext?.EnterInternalCall();
    try
    {
        await handler.HandleAsync(@event, ct).ConfigureAwait(false);
    }
    finally
    {
        scope?.Dispose();
    }
}
```

### What this means in practice

| Filter Level | Behavior During Event Handlers |
|-------------|-------------------------------|
| Authorization (L1-L3: permissions, policies, ABAC) | **Skipped** -- `IsInternalCall` is true |
| Data-level filters (L4: tenant isolation, soft delete) | **Active** -- user context is preserved |

This design ensures that system reactions to domain events (e.g., creating an invoice after reservation confirmation) are not blocked by the HTTP user's permissions. The handler runs in the same DI scope as the original request, so tenant isolation and soft delete filters still apply correctly.

### When ICallContext is not registered

If `Pragmatic.Actions` is not referenced in your project, `ICallContext` is not registered in DI. `GetService<ICallContext>()` returns `null`, `scope` is `null`, and `?.Dispose()` is a no-op. Handlers execute normally without any call context -- and since no authorization filters exist either, there is nothing to skip.

---

## Cross-Boundary Events

Domain events are the primary mechanism for cross-boundary communication in Pragmatic applications. An event raised in one boundary triggers handlers registered in another boundary.

### How it works

```
Booking boundary                    Billing boundary
-----------------                   ------------------
Reservation.Confirm()
  -> RaiseEvent(ReservationConfirmed)
      -> SaveChangesAsync()
          -> post-commit hand-over (invoker, batch, or unit of work)
              -> InMemoryEventDispatcher
                  -> ReservationConfirmedHandler  (creates draft invoice)
                  -> AuditHandler                 (audit trail)

Invoice.MarkAsPaid()
  -> RaiseEvent(InvoicePaid)
      -> SaveChangesAsync()
          -> post-commit hand-over (invoker, batch, or unit of work)
              -> InMemoryEventDispatcher
                  -> InvoicePaidHandler           (notifies Booking)
```

### The contract is the event

Boundaries share only the event record. Handlers interact with other boundaries through typed boundary interfaces (e.g., `IBillingActions`, `IBookingActions`), never through direct entity or repository access. This keeps boundaries decoupled.

```csharp
// Billing handler reacts to Booking event
// Knows about: ReservationConfirmed (the event) and IBillingActions (its own boundary)
// Does NOT know about: Reservation entity, IReservationRepository, BookingDbContext
[EventHandler]
public sealed class ReservationConfirmedHandler(
    IBillingActions billingActions) : IDomainEventHandler<ReservationConfirmed>
{
    public async Task HandleAsync(ReservationConfirmed @event, CancellationToken ct = default)
    {
        await billingActions.CreateDraftInvoice(
            reservationId: @event.ReservationId,
            guestId: @event.GuestId,
            totalAmount: @event.TotalAmount,
            currency: @event.Currency,
            ct: ct).ConfigureAwait(false);
    }
}
```

### Event data self-sufficiency

Include all the data handlers need in the event. If `ReservationConfirmedHandler` needs the reservation amount and currency, those fields must be in `ReservationConfirmed`. If you find yourself injecting a repository from another boundary to look up data, the event is missing fields.

```csharp
// Good: event carries all needed data
public sealed record ReservationConfirmed(
    Guid ReservationId,
    Guid GuestId,
    Guid PropertyId,
    DateTimeOffset CheckIn,
    DateTimeOffset CheckOut,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

// Bad: event is too slim, handler needs to query Booking
public sealed record ReservationConfirmed(
    Guid ReservationId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
// Handler would need IReservationRepository to look up amount, currency, etc.
```

---

## EntityPropertyChanged\<TEntity\>

A built-in generic event for cascade property change propagation. Defined in `Pragmatic.Abstractions`.

```csharp
public sealed record EntityPropertyChanged<TEntity> : IDomainEvent
    where TEntity : class
{
    public required object EntityId { get; init; }
    public required string PropertyName { get; init; }
    public object? NewValue { get; init; }
    public object? OldValue { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public Guid EventId { get; init; } = Guid.NewGuid();

    public static EntityPropertyChanged<TEntity> Create(
        object entityId, string propertyName, object? oldValue, object? newValue)
        => new()
        {
            EntityId = entityId,
            PropertyName = propertyName,
            OldValue = oldValue,
            NewValue = newValue
        };
}
```

### Use case: dependent entity updates

When a parent entity's property changes and dependent entities need to react. For example, when `RoomType.BaseRate` changes, reservations priced against that room type may need updating:

```csharp
// Entity raises the property change event
public partial class RoomType : DomainEventSource
{
    public void UpdateBaseRate(decimal newRate)
    {
        var oldRate = BaseRate;
        BaseRate = newRate;
        RaiseEvent(EntityPropertyChanged<RoomType>.Create(Id, nameof(BaseRate), oldRate, newRate));
    }
}

// Handler reacts to the change
[EventHandler]
public sealed class RoomTypeRateChangedHandler(
    IReservationRepository reservations)
    : IDomainEventHandler<EntityPropertyChanged<RoomType>>
{
    public async Task HandleAsync(
        EntityPropertyChanged<RoomType> @event, CancellationToken ct = default)
    {
        if (@event.PropertyName == nameof(RoomType.BaseRate))
        {
            // Update future reservations with the new rate
        }
    }
}
```

`EntityPropertyChanged<T>` is also used by SG-generated setters in `Pragmatic.Persistence` for automatic change propagation.

---

## Handler Ordering

Handlers for the same event type are sorted by `Order` (ascending, lower first):

```csharp
var handlers = _serviceProvider.GetServices<IDomainEventHandler<TEvent>>()
    .OrderBy(h => h.Order)
    .ToList();
```

| Order Value | Convention | Example |
|-------------|-----------|---------|
| Negative (e.g., -10) | High-priority side effects (audit, logging) | `AuditHandler` |
| 0 (default) | Normal business reactions | `InvoiceHandler` |
| Positive (e.g., 100) | Low-priority, non-critical | `NotificationHandler` |

Within the same `Order` value, execution follows DI registration order. The SG-generated `AddPragmaticEventHandlers()` registers handlers in a deterministic order (sorted by full type name), so the sequence is stable across builds; register by hand if you need a different one. Give handlers distinct `Order` values whenever the sequence actually matters: relying on registration order makes the contract invisible at the handler.

---

## DI Lifetimes

| Type | Lifetime | Registration Method |
|------|----------|---------------------|
| `IDomainEventDispatcher` / `InMemoryEventDispatcher` | Scoped | `AddInMemoryDomainEvents()` via `TryAddScoped` |
| `IDomainEventHandler<T>` | Scoped | `AddDomainEventHandler<THandler, TEvent>()` |
| `LifecycleEventsInterceptor` | Singleton (implicit) | Created once per `DbContextOptions` by `UseDomainEvents()` |

The dispatcher is **scoped** because it resolves handlers from the current DI scope, and dispatch happens in the scope that asked for the write, so a handler sees the tenant and the user of the request that caused it. The interceptor is effectively singleton because it is attached to `DbContextOptions`; it is stateless and holds nothing, which is possible only because it raises and never dispatches.

---

## Observability

`InMemoryEventDispatcher` uses `[LoggerMessage]` for zero-allocation structured logging.

### Distributed tracing

Each dispatch creates an `Activity` named `Event.{EventName}` on the `Pragmatic.Events` ActivitySource, tagged with:

| Tag | Value |
|-----|-------|
| `pragmatic.event.name` | Event type name (e.g., `ReservationConfirmed`) |
| `pragmatic.event.handler_count` | Number of handlers resolved |
| `event.handler_failures` | Number of failed handlers (only when > 0) |

### Metrics

Three instruments on the `Pragmatic.Events` Meter:

| Instrument | Type | Description |
|------------|------|-------------|
| `pragmatic.events.dispatched` | Counter | Total domain events dispatched (tagged by `event.name`) |
| `pragmatic.events.dispatch_duration` | Histogram (ms) | Duration of event dispatch across all handlers (tagged by `event.name`) |
| `pragmatic.events.handler_failures` | Counter | Total handler failures (tagged by `event.name` and `handler.name`) |

### Logging levels

| Level | What Is Logged |
|-------|---------------|
| Debug | Dispatching event, handler count, handler executing/completed, events collected/cleared |
| Warning | Dispatch completed with failures (includes failure count) |
| Error | Handler failure (with exception), dispatch failure in interceptor |

---

## What Gets Generated

The event infrastructure is driven by the source generator on three fronts:

### Composition SG: handler registration + typed dispatch table

When `Pragmatic.Composition` is referenced, the Composition source generator discovers all classes marked with `[EventHandler]` and generates AOT-safe DI registration:

```csharp
// Generated: _Infra.Events.Registration.g.cs
public static class EventsRegistrationExtensions
{
    public static IServiceCollection AddPragmaticEventHandlers(this IServiceCollection services)
    {
        services.AddScoped<IDomainEventHandler<ReservationConfirmed>, ReservationConfirmedHandler>();
        services.AddScoped<IDomainEventHandler<InvoicePaid>, InvoicePaidHandler>();
        // ... one registration per [EventHandler] class
        return services;
    }
}
```

The same generator emits, per assembly with handlers, a `GeneratedEventDispatchTable : ITypedEventDispatchTable`: a typed event→`DispatchAsync<TEvent>` switch registered additively by `AddPragmaticEventHandlers()`. This is the AOT-safe mechanism the untyped/batch path uses to avoid reflection; see [Internals](/modules/events/internals/).

### Lifecycle SG: `[Raises<T>]`

An entity that derives from `DomainEventSource` can declare `[Raises<TEvent>(on: ...)]`. The generator wires the raise so the event fires automatically at the given lifecycle transition, with no handler body. See the lifecycle section in [Getting Started](/modules/events/getting-started/).

### Persistence SG: EntityPropertyChanged

When `Pragmatic.Persistence` generates setters for entity properties, it can emit `EntityPropertyChanged<T>` events automatically for properties that participate in cascade propagation.

### What you register manually

Without `Pragmatic.Composition`, register handlers manually:

```csharp
services.AddDomainEventHandler<ReservationConfirmedHandler, ReservationConfirmed>();
```

There is no assembly-scanning overload: discovering handlers by reflection is the thing the generator exists to replace, and keeping a second way to do it made the reflection-free path optional.

---

## Registration Methods

Two ways to register handlers, and one of them also brings the dispatcher:

```csharp
using Pragmatic.Events.Extensions;

// A. SG-generated (recommended). Registers every [EventHandler] in the assembly, plus the
//    in-memory dispatcher via TryAdd, so an application that wants an outbox registers its
//    own dispatcher first and wins.
services.AddPragmaticEventHandlers();

// B. By hand. The dispatcher is not implied here, so register it yourself.
services.AddInMemoryDomainEvents();
services.AddDomainEventHandler<ReservationConfirmedHandler, ReservationConfirmed>();
services.AddDomainEventHandler<InvoicePaidHandler, InvoicePaid>();
```

| Method | AOT-Safe | Discovery | Registers the dispatcher | When to Use |
|--------|----------|-----------|--------------------------|-------------|
| `[EventHandler]` + `AddPragmaticEventHandlers()` | Yes | Compile-time | Yes (`TryAdd`) | Production (recommended) |
| `AddDomainEventHandler<THandler, TEvent>()` | Yes | Manual | No | Explicit control, tests |

---

## Ecosystem Integration Overview

Pragmatic.Events integrates with other Pragmatic modules. Each integration is opt-in.

### Actions

Event handlers commonly invoke domain actions through typed boundary interfaces. The internal call context ensures that handler-triggered actions bypass authorization filters.

### Persistence

`DomainEventSource` entities accumulate events during domain operations. The EF Core interceptor dispatches them after `SaveChangesAsync()`. SG-generated setters can emit `EntityPropertyChanged<T>` for cascade propagation.

### Composition

The Composition SG discovers `[EventHandler]` classes and generates AOT-safe registration. When using `PragmaticApp.RunAsync()`, handler registration is automatic.

### Authorization

Event handlers run as internal calls (`ICallContext.EnterInternalCall()`). This means:
- Permission checks (L1), policy evaluation (L2), and ABAC (L3) are **skipped**.
- Data-level filters (L4: tenant isolation, soft delete) remain **active**.

### Messaging

`Pragmatic.Messaging` adds a message broker transport (Channels, RabbitMQ), sagas, and cross-service delivery. It reuses the same `IDomainEvent` records and `IDomainEventHandler<T>` interface. The **transactional outbox itself does not belong to Messaging**: it ships in `Pragmatic.Events.EFCore` (see below).

---

## Transactional Outbox

The `InMemoryEventDispatcher` is synchronous and in-process: it dispatches *after* the commit, but between the commit and the dispatch the process can die and the events are lost. The **transactional outbox** closes that gap, and it is **included** in the `Pragmatic.Events.EFCore` package (namespace `Pragmatic.Events.EFCore.Outbox`); you do not need `Pragmatic.Messaging` for it.

### What it does

- The `EventOutboxInterceptor` captures the domain events **before** the commit (`SavingChanges`) and writes them into the `__EventOutbox` table **in the same transaction** as the entity change. The event can never be lost relative to a committed change, and a rolled-back change never emits an event. It then clears the events from the entity, so the post-commit hand-over finds nothing left to take, and the two are safe to combine: the outbox wins and the event is delivered once.
- The `EventOutboxDeliveryService<TContext>` is a `BackgroundService` that polls the table, **atomically claims** rows (columns `ClaimedBy`/`ClaimedUntil`, a compare-and-swap via `ExecuteUpdate`) so multiple replicas do not deliver the same entry twice, dispatches each event through `IDomainEventDispatcher`, and marks it `ProcessedAt`.

### Delivery semantics

- **At-least-once.** An event may be dispatched more than once if the process crashes between dispatch and the mark-processed write. **Handlers must be idempotent**: this is where `IDomainEvent.EventId` earns its keep as a dedup key.
- **Poison messages.** After `MaxAttempts` failed attempts a row is abandoned (left un-processed, no longer retried). There is no separate dead-letter table; inspect the row's `LastError` column.
- **Fail-closed type resolution.** Only event types that have a registered handler are deserializable: the resolver builds an allowlist from the registered `IDomainEventHandler<T>` services. There is no `Type.GetType` on an arbitrary string from the database, so a tampered `EventType` value cannot load an unexpected type (no gadget-chain surface).
- **Context propagation.** The W3C trace context (`TraceParent`) and the originating tenant (`TenantId`) are captured at write time and restored across the async delivery boundary, so handler work shows under the request that raised the event and tenant-scoped queries resolve the correct filter.

### Options: `EventOutboxOptions`

| Option | Default | Constraint |
|--------|---------|-----------|
| `BatchSize` | 100 | at least 1 (validated on set) |
| `PollingInterval` | 5 seconds | must be positive (validated on set) |
| `MaxAttempts` | 5 | at least 1 (validated on set) |

### Enabling the outbox

On a **source-generated** boundary DbContext, mark the boundary `[EnableEventOutbox]`: the generator maps `__EventOutbox`, adds the interceptor, registers the delivery service, and includes the table in the schema metadata. The boundary project must reference `Pragmatic.Events.EFCore` (otherwise the generator emits **PRAG2752** rather than silently doing nothing). On a **hand-written** DbContext, use the three-step manual wiring (see the outbox Quick Start in [Getting Started](/modules/events/getting-started/)). Two boundaries that share one physical database share a single `__EventOutbox` table; the atomic claim keeps delivery safe.

---

## Thread Safety

| Type | Thread Safety | Notes |
|------|---------------|-------|
| `InMemoryEventDispatcher` | Safe | Stateless per-call; holds only the injected dispatch tables (read-only after construction) |
| `LifecycleEventsInterceptor` | Safe | Stateless; holds nothing at all |
| `DomainEventSource` | Not thread-safe | Entity instances are scoped to a single request/DbContext |

`DomainEventSource` is not thread-safe because entity instances are expected to be used within a single DI scope (one DbContext, one request). Do not share entities across threads.

---

## Testable Timestamps

Use `TimeProvider` for deterministic timestamps in tests. Pass `TimeProvider.GetUtcNow()` to event constructors instead of `DateTimeOffset.UtcNow`:

```csharp
// Production: use real time
RaiseEvent(new ReservationConfirmed(Id, GuestId, PropertyId,
    CheckIn, CheckOut, TotalAmount, Currency, DateTimeOffset.UtcNow));

// Test: use FakeTimeProvider for deterministic assertions
var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero));
RaiseEvent(new ReservationConfirmed(Id, GuestId, PropertyId,
    CheckIn, CheckOut, TotalAmount, Currency, timeProvider.GetUtcNow()));
```

---

## See Also

- [Getting Started](/modules/events/getting-started/) -- Install and raise your first domain event in 5 minutes
- [Internals](/modules/events/internals/) -- Dispatcher mechanics, handler ordering, error handling, and observability details
- [Common Mistakes](/modules/events/common-mistakes/) -- Frequent pitfalls with Wrong/Right/Why format
- [Troubleshooting](/modules/events/troubleshooting/) -- Problem/checklist format for common issues
- [Showcase: Booking + Billing](https://github.com/pragmatic-design/Pragmatic.Design/tree/main/examples/showcase) -- Real-world cross-boundary event examples
