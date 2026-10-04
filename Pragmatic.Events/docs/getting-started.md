# Getting Started with Pragmatic.Events

This guide walks through raising and handling your first domain event, from event definition to EF Core auto-dispatch.

## Prerequisites

Add the package references to your module's `.csproj`:

```xml
<!-- Core events (required) -->
<PackageReference Include="Pragmatic.Events" />

<!-- EF Core auto-dispatch (optional, for automatic dispatch after SaveChanges) -->
<PackageReference Include="Pragmatic.Events.EFCore" />
```

## Step 1: Define a Domain Event

A domain event is an immutable record that captures what happened. Inherit from `DomainEvent` (which implements `IDomainEvent`) and include all data consumers need:

```csharp
using Pragmatic.Events;

namespace MyApp.Booking.Events;

public sealed record ReservationConfirmed(
    Guid ReservationId,
    Guid GuestId,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
```

**Key design rule:** include enough data in the event so that handlers never need cross-boundary entity access. The event is the contract between boundaries.

If you prefer not to use the base record, implement `IDomainEvent` directly:

```csharp
public sealed record ReservationConfirmed(
    Guid ReservationId,
    Guid GuestId,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset OccurredAt) : IDomainEvent;
```

## Step 2: Raise Events from Your Entity

Make your entity inherit from `DomainEventSource` and call `RaiseEvent()` during domain operations:

```csharp
using Pragmatic.Events;
using Pragmatic.Persistence.Entity;

[Entity]
public partial class Reservation : DomainEventSource, IEntity
{
    public ReservationStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string Currency { get; private set; } = "EUR";

    public VoidResult<IError> Confirm()
    {
        var result = TransitionTo(ReservationStatus.Confirmed);
        if (result.IsSuccess)
        {
            RaiseEvent(new ReservationConfirmed(
                Id, GuestId, TotalAmount, Currency, DateTimeOffset.UtcNow));
        }
        return result;
    }
}
```

Events accumulate inside the entity. They are **not dispatched** until the entity is persisted and the EF Core interceptor fires.

You can also raise events during entity creation with a factory method:

```csharp
public static Reservation Create(Guid guestId, decimal amount, string currency)
{
    var reservation = new Reservation
    {
        GuestId = guestId,
        TotalAmount = amount,
        Currency = currency,
        Status = ReservationStatus.Pending
    };

    reservation.RaiseEvent(new ReservationCreated(
        reservation.Id, guestId, amount, currency, DateTimeOffset.UtcNow));

    return reservation;
}
```

### Alternative: IHasDomainEvents

If your entity already has a base class (e.g., TPH/TPC hierarchy), implement `IHasDomainEvents` manually:

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

The EF Core interceptor works with any entity that implements `IHasDomainEvents`.

## Lifecycle Domain Events

When you need to raise an event **on a persistence lifecycle transition** (created / updated / deleted)
and the event's data comes straight from the entity, you can skip the hand-written `RaiseEvent()` call.
Declare it with `[Raises<TEvent>(on: ...)]` (from `Pragmatic.Authoring`) on an entity that derives from
`DomainEventSource`: the source generator wires the raise, and the `LifecycleEventsInterceptor` fires it
at the right moment, filling the event constructor from entity members that match **by name**.

```csharp
using Pragmatic.Authoring;
using Pragmatic.Events;

[Entity]
[Raises<OrderPlaced>]                                 // On = EntityLifecycle.Created (the default)
[Raises<OrderCancelled>(EntityLifecycle.Deleted)]     // stackable: one attribute per event
public partial class Order : DomainEventSource, IEntity
{
    public Guid CustomerId { get; private set; }
    public decimal Total { get; private set; }
    // OrderPlaced(Guid OrderId, Guid CustomerId, decimal Total, ...) is filled from Id/CustomerId/Total.
}
```

When the entity is inserted, `OrderPlaced` is raised and dispatched automatically after the commit, with no
handler-side or entity-side code beyond the attribute. (A soft delete is treated as `Deleted`, not
`Updated`.)

**How `On` behaves:**

- On an **entity** deriving from `DomainEventSource`, the generator raises the event at the `On`
  transition, filling the constructor from matching entity members (no custom body).
- On a **mutation or action class** the generator wires the raise as well, so do not raise the event in
  the body too or it goes out twice; `On` is ignored there.
- On an **entity's method** it generates nothing, and the build refuses it (see **PRAG2753**). The
  attribute on a mutation is read from the **class**; there is no method the generator wires.
- `On` defaults to `EntityLifecycle.Created`.

**Diagnostics:**

- **PRAG2750**: an entity with `[Raises<T>]` must derive from `DomainEventSource` (otherwise the
  generated events cannot be raised).
- **PRAG2751**: an event constructor parameter that matches no entity member is passed `default`
  (warning); rename the parameter to match, or raise the event from a domain method instead.
- **PRAG2753**: `[Raises<T>]` on an **entity's method**, where nothing generates that raise. Declare it on
  the entity class, use `[RaisesEvent<T>]` on the state machine's target member, or call
  `RaiseEvent(...)` in the body.

For events that carry data not present on the entity, keep raising them explicitly with `RaiseEvent()`
(Step 2) or from a domain method.

## Step 3: Create an Event Handler

Implement `IDomainEventHandler<T>` and mark the class with `[EventHandler]` for source generator discovery:

```csharp
using Pragmatic.Events;
using Pragmatic.Events.Attributes;

namespace MyApp.Billing.EventHandlers;

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

Handlers can inject any service from DI. They run as **internal calls** (authorization filters are skipped), so handler-triggered actions are not blocked by the HTTP user's permissions.

## Step 4: Register Services

```csharp
using Pragmatic.Events.Extensions;

// Register the in-memory dispatcher
builder.Services.AddInMemoryDomainEvents();

// Option A (recommended, AOT-safe): [EventHandler] attribute + SG-generated registration.
// The Composition SG generates AddPragmaticEventHandlers() for every class marked [EventHandler]
// (and, per module, the typed dispatch table). With Pragmatic.Composition + PragmaticApp.RunAsync()
// this call is wired for you.
builder.Services.AddPragmaticEventHandlers();

// Option B (AOT-safe): register handlers individually, for small projects or explicit control.
builder.Services.AddDomainEventHandler<ReservationConfirmedHandler, ReservationConfirmed>();
```

> Option A also registers the in-memory dispatcher (via `TryAdd`), so an application that wants an
> outbox instead registers its own first and wins. Option B does not: call
> `AddInMemoryDomainEvents()` yourself alongside it, or the handlers sit in DI with nothing to call
> them.

## Step 5: Wire Up EF Core Auto-Dispatch

Add the interceptor to your DbContext configuration:

```csharp
using Pragmatic.Events.EFCore;

services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString);
    options.UseDomainEvents();  // LifecycleEventsInterceptor: raises [Raises<T>], nothing more
});
```

The interceptor **raises**; it does not dispatch. After `SaveChangesAsync()` commits, the events are taken off the tracked `IHasDomainEvents` entities and dispatched by whoever performed the write: the generated invoker of a mutation or action, the batch of a composition, or `EfCoreUnitOfWork` itself for a plain save. All three run in the scope that asked for the write, so a handler sees the tenant and the user of the request. No manual dispatch code needed.

## Transactional Outbox (at-least-once delivery)

Dispatch above happens *after* the commit, but if the process dies in that window, the events
are lost. When you need them to survive a crash (or to fan out reliably across replicas), turn on the
**transactional outbox**. It ships in `Pragmatic.Events.EFCore` (namespace
`Pragmatic.Events.EFCore.Outbox`); you do **not** need `Pragmatic.Messaging`.

The outbox writes each event into an `__EventOutbox` table **in the same transaction** as the entity
change, and a background service delivers them. Wiring is three steps:

**1. Map the table in `OnModelCreating`:**

```csharp
using Pragmatic.Events.EFCore.Outbox;

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.AddEventOutbox();   // maps the __EventOutbox table
}
```

**2. Add the capture interceptor to the DbContext options** (use the `(sp, options)` overload so the
interceptor can be resolved):

```csharp
services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseNpgsql(connectionString);
    options.AddInterceptors(sp.GetRequiredService<EventOutboxInterceptor>());
});
```

**3. Register the outbox (interceptor, options, and the background delivery service):**

```csharp
using Pragmatic.Events.EFCore.Outbox;

services.AddEventOutbox<AppDbContext>(o =>
{
    o.BatchSize = 100;                          // entries per poll (>= 1, default 100)
    o.PollingInterval = TimeSpan.FromSeconds(5); // between polls (> 0, default 5s)
    o.MaxAttempts = 5;                          // before a row is left as poison (>= 1, default 5)
});
```

What you get and what to keep in mind:

- **At-least-once delivery.** An event can be dispatched more than once (crash between dispatch and
  mark-processed). **Write idempotent handlers**: `IDomainEvent.EventId` is a natural dedup key, which is
  another reason to inherit from `DomainEvent`.
- **Multi-replica safe.** Rows are claimed atomically (`ClaimedBy`/`ClaimedUntil`), so two workers never
  deliver the same entry.
- **Poison messages.** After `MaxAttempts` the row is abandoned: no dead-letter table; inspect its
  `LastError` column.
- **Trace + tenant propagation.** The W3C trace context and the originating tenant are restored across the
  async delivery boundary.
- **Combines cleanly with `UseDomainEvents`.** The outbox interceptor clears the entity's events at
  capture time, so the post-commit hand-over finds nothing left to take: the event is delivered once,
  through the outbox.

> **Source-generated DbContext?** You don't hand-wire the three steps above. Mark the boundary
> `[EnableEventOutbox]` and the generator maps `__EventOutbox`, adds the interceptor, registers the
> delivery service, and includes the table in the schema metadata. The boundary project must reference
> `Pragmatic.Events.EFCore` (otherwise the generator emits **PRAG2752** instead of silently doing nothing).
> Two boundaries sharing one physical database share a single `__EventOutbox` table.
>
> ```csharp
> [Boundary]
> [EnableEventOutbox]
> public sealed class OrdersBoundary;
> ```

## Step 6: Test Your Events

**Test that the entity raises the event:**

```csharp
[Fact]
public void Confirm_RaisesReservationConfirmedEvent()
{
    var reservation = Reservation.Create(guestId, 300m, "EUR");
    reservation.ClearDomainEvents(); // clear creation event

    reservation.Confirm();

    reservation.DomainEvents.Should().ContainSingle()
        .Which.Should().BeOfType<ReservationConfirmed>()
        .Which.TotalAmount.Should().Be(300m);
}
```

**Test the handler in isolation:**

```csharp
[Fact]
public async Task ReservationConfirmedHandler_CreatesDraftInvoice()
{
    var billingActions = Substitute.For<IBillingActions>();
    var handler = new ReservationConfirmedHandler(billingActions);

    await handler.HandleAsync(new ReservationConfirmed(
        Guid.NewGuid(), Guid.NewGuid(), 300m, "EUR", DateTimeOffset.UtcNow));

    await billingActions.Received(1).CreateDraftInvoice(
        Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Is(300m), Arg.Is("EUR"),
        Arg.Any<CancellationToken>());
}
```

## Cross-Boundary Pattern

Events are the recommended mechanism for cross-boundary communication:

```
Booking boundary                    Billing boundary
-----------------                   ------------------
Reservation.Confirm()
  -> RaiseEvent(ReservationConfirmed)
      -> SaveChangesAsync()
          -> post-commit hand-over (invoker, batch, or unit of work)
              -> ReservationConfirmedHandler (creates invoice)

Invoice.MarkAsPaid()
  -> RaiseEvent(InvoicePaid)
      -> SaveChangesAsync()
          -> post-commit hand-over (invoker, batch, or unit of work)
              -> InvoicePaidHandler (notifies Booking)
```

Handlers interact with other boundaries through typed boundary interfaces (`IBillingActions`, `IBookingActions`), never through direct entity or repository access. Events carry all the data handlers need.

## Next Steps

- See [internals.md](internals.md) for dispatcher mechanics, error handling, and observability details.
- See the Showcase application (`examples/showcase/`) for a full working example with Booking and Billing boundaries.
