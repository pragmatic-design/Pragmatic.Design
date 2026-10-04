---
title: "Common Mistakes"
description: "These are the most common issues developers encounter when using Pragmatic.Events. Each section shows the wrong approach, the correct approach, and explains why"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Events/docs/common-mistakes.md
sidebar:
  order: 4
---
These are the most common issues developers encounter when using Pragmatic.Events. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Raising Events That Lack Sufficient Data

**Wrong:**

```csharp
public sealed record ReservationConfirmed(
    Guid ReservationId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

[EventHandler]
public sealed class ReservationConfirmedHandler(
    IReservationRepository reservations,
    IBillingActions billingActions) : IDomainEventHandler<ReservationConfirmed>
{
    public async Task HandleAsync(ReservationConfirmed @event, CancellationToken ct = default)
    {
        // Handler queries back into the originating boundary to get the data it needs
        var reservation = await reservations.GetByIdAsync(@event.ReservationId, ct);
        await billingActions.CreateDraftInvoice(
            reservationId: reservation!.Id,
            guestId: reservation.GuestId,
            totalAmount: reservation.TotalAmount,
            currency: reservation.Currency,
            ct: ct).ConfigureAwait(false);
    }
}
```

**Right:**

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

[EventHandler]
public sealed class ReservationConfirmedHandler(
    IBillingActions billingActions) : IDomainEventHandler<ReservationConfirmed>
{
    public async Task HandleAsync(ReservationConfirmed @event, CancellationToken ct = default)
    {
        // All data comes from the event -- no cross-boundary query needed
        await billingActions.CreateDraftInvoice(
            reservationId: @event.ReservationId,
            guestId: @event.GuestId,
            totalAmount: @event.TotalAmount,
            currency: @event.Currency,
            ct: ct).ConfigureAwait(false);
    }
}
```

**Why:** The event is the contract between boundaries. If a handler in the Billing boundary needs `IReservationRepository` from the Booking boundary, the boundaries are no longer decoupled -- Billing depends on Booking's data access layer. Include all data handlers need in the event record. If you find a handler injecting a repository from another boundary, the event is missing fields.

---

## 2. Dispatching Events Before Persistence

**Wrong:**

```csharp
public class ReservationService(
    IReservationRepository reservations,
    IDomainEventDispatcher dispatcher)
{
    public async Task ConfirmAsync(Guid id, CancellationToken ct)
    {
        var reservation = await reservations.GetByIdAsync(id, ct);
        reservation.Confirm();

        // Dispatch events BEFORE saving -- if save fails, handlers already ran
        await dispatcher.DispatchAsync(
            reservation.DomainEvents, ct).ConfigureAwait(false);

        await reservations.SaveAsync(reservation, ct);
    }
}
```

**Runtime result:** If `SaveAsync` fails (concurrency conflict, constraint violation, connection timeout), the event handlers have already executed. An invoice was created for a reservation that was never actually confirmed.

**Right:**

```csharp
// Save through the unit of work -- the events are taken and dispatched AFTER the commit,
// and only when it succeeded.
services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString);
    options.UseDomainEvents();  // raises [Raises<T>]; dispatch belongs to the unit of work
});

// Or dispatch manually AFTER saving
public async Task ConfirmAsync(Guid id, CancellationToken ct)
{
    var reservation = await reservations.GetByIdAsync(id, ct);
    reservation.Confirm();
    await reservations.SaveAsync(reservation, ct);

    // Only dispatch after persistence succeeds
    var events = reservation.DomainEvents.ToList();
    reservation.ClearDomainEvents();
    await dispatcher.DispatchAsync(events, ct).ConfigureAwait(false);
}
```

**Why:** Events represent things that happened. If the database write fails, the thing did not happen. The EF Core interceptor handles this correctly by design: it hooks into `SavedChangesAsync` (post-commit), ensuring events are only dispatched after persistence succeeds. Manual dispatch should follow the same principle.

---

## 3. Throwing Exceptions in Handlers to Signal Business Errors

**Wrong:**

```csharp
[EventHandler]
public sealed class ReservationConfirmedHandler(
    IBillingActions billingActions) : IDomainEventHandler<ReservationConfirmed>
{
    public async Task HandleAsync(ReservationConfirmed @event, CancellationToken ct = default)
    {
        if (@event.TotalAmount <= 0)
            throw new InvalidOperationException("Cannot create invoice for zero amount");

        await billingActions.CreateDraftInvoice(/* ... */).ConfigureAwait(false);
    }
}
```

**Runtime result:** The exception is caught by the dispatcher's continue-on-failure policy, logged at `Error` level, counted as a handler failure, and **swallowed**. The next handler still runs. The caller of `SaveChangesAsync()` is not notified. The failing handler silently produces nothing.

**Right:**

```csharp
[EventHandler]
public sealed class ReservationConfirmedHandler(
    IBillingActions billingActions,
    ILogger<ReservationConfirmedHandler> logger) : IDomainEventHandler<ReservationConfirmed>
{
    public async Task HandleAsync(ReservationConfirmed @event, CancellationToken ct = default)
    {
        if (@event.TotalAmount <= 0)
        {
            logger.LogWarning("Skipping invoice creation for reservation {ReservationId}: amount is {Amount}",
                @event.ReservationId, @event.TotalAmount);
            return;  // Skip gracefully, do not throw
        }

        await billingActions.CreateDraftInvoice(/* ... */).ConfigureAwait(false);
    }
}
```

**Why:** The dispatcher uses continue-on-failure by design. Handler exceptions are logged but swallowed so independent side effects do not block each other. If a handler encounters a business condition where it cannot proceed, handle it gracefully with a log and early return. Reserve exceptions for truly unexpected failures (database down, serialization error) that should appear in error metrics and traces.

---

## 4. Forgetting to Register AddInMemoryDomainEvents

**Wrong:**

```csharp
// Program.cs
builder.Services.AddDomainEventHandler<ReservationConfirmedHandler, ReservationConfirmed>();

// Missing: builder.Services.AddInMemoryDomainEvents();
```

**Runtime result:** `InvalidOperationException` at runtime when the EF Core interceptor or any code tries to resolve `IDomainEventDispatcher`. The handlers are registered, but there is no dispatcher to invoke them.

**Right:**

```csharp
// Program.cs
builder.Services.AddInMemoryDomainEvents();  // Register the dispatcher FIRST
builder.Services.AddDomainEventHandler<ReservationConfirmedHandler, ReservationConfirmed>();
```

**Why:** `AddInMemoryDomainEvents()` registers `InMemoryEventDispatcher` as the `IDomainEventDispatcher` implementation (scoped). Without it, there is no dispatcher in the DI container. Handler registration alone is not enough.

---

## 5. Using DomainEventSource with an Existing Base Class

**Wrong:**

```csharp
// Entity already has a base class for TPH hierarchy
public class Reservation : BookingEntity, DomainEventSource  // Compile error: C# single inheritance
{
}
```

**Compile result:** C# does not support multiple class inheritance. `Reservation` cannot inherit from both `BookingEntity` and `DomainEventSource`.

**Right:**

```csharp
// Implement the interface instead
public class Reservation : BookingEntity, IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void RaiseEvent(IDomainEvent @event) => _domainEvents.Add(@event);
}
```

**Why:** `DomainEventSource` is a convenience base class. When your entity already has a base class (TPH/TPC hierarchy, shared audit base, etc.), implement `IHasDomainEvents` directly. The EF Core interceptor scans for `IHasDomainEvents`, not `DomainEventSource`, so both approaches work identically for dispatch.

---

## 6. Creating Mutable Events

**Wrong:**

```csharp
public class ReservationConfirmed : IDomainEvent  // class, not record
{
    public Guid ReservationId { get; set; }     // mutable
    public decimal TotalAmount { get; set; }    // mutable
    public DateTimeOffset OccurredAt { get; set; }
}

// Handler accidentally modifies the event
[EventHandler]
public sealed class AuditHandler : IDomainEventHandler<ReservationConfirmed>
{
    public Task HandleAsync(ReservationConfirmed @event, CancellationToken ct = default)
    {
        @event.TotalAmount = 0; // Mutates the event!
        // All subsequent handlers see TotalAmount = 0
        return Task.CompletedTask;
    }
}
```

**Runtime result:** The `AuditHandler` (Order -10) mutates the event. The `InvoiceHandler` (Order 0) sees `TotalAmount = 0` and creates a zero-amount invoice. Since all handlers receive the same event instance, mutations are visible across the handler chain.

**Right:**

```csharp
public sealed record ReservationConfirmed(
    Guid ReservationId,
    decimal TotalAmount,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
// sealed record: immutable, value equality, cannot be subclassed
```

**Why:** Events represent historical facts -- they should never change after creation. Use `sealed record` to get immutability by default (`init`-only properties), value equality (useful in tests), and compiler protection against accidental mutation. All handlers receive the same event instance; immutability guarantees they all see the same data.

---

## 7. Relying on Handler Order for Transactional Coordination

**Wrong:**

```csharp
// Handler A creates the invoice (Order 0)
[EventHandler]
public sealed class CreateInvoiceHandler : IDomainEventHandler<ReservationConfirmed>
{
    public async Task HandleAsync(ReservationConfirmed @event, CancellationToken ct = default)
    {
        await billingActions.CreateDraftInvoice(/* ... */).ConfigureAwait(false);
    }
}

// Handler B sends the invoice email (Order 10) -- depends on Handler A's invoice
[EventHandler]
public sealed class SendInvoiceEmailHandler : IDomainEventHandler<ReservationConfirmed>
{
    public int Order => 10;

    public async Task HandleAsync(ReservationConfirmed @event, CancellationToken ct = default)
    {
        var invoice = await invoices.GetByReservationIdAsync(@event.ReservationId, ct);
        // If Handler A failed (continue-on-failure), invoice is null!
        await emailService.SendInvoiceEmailAsync(invoice!.Id, ct);
    }
}
```

**Runtime result:** If `CreateInvoiceHandler` throws, `SendInvoiceEmailHandler` still runs (continue-on-failure), but the invoice does not exist. The email handler gets a null invoice and fails with a `NullReferenceException`.

**Right:**

```csharp
// Combine dependent side effects into a single handler
[EventHandler]
public sealed class BillingHandler : IDomainEventHandler<ReservationConfirmed>
{
    public async Task HandleAsync(ReservationConfirmed @event, CancellationToken ct = default)
    {
        var invoiceId = await billingActions.CreateDraftInvoice(/* ... */).ConfigureAwait(false);
        await emailService.SendInvoiceEmailAsync(invoiceId, ct).ConfigureAwait(false);
    }
}
```

**Why:** Continue-on-failure means each handler is independent. If Handler A fails, Handler B still runs. Do not create dependencies between handlers in the same event chain. If two side effects must happen atomically or in sequence, put them in one handler. `Order` is for priority (audit first, notifications last), not for transactional sequencing.

---

## 8. Forgetting UseDomainEvents on DbContext Configuration

**Wrong:**

```csharp
// Dispatcher registered, handlers registered, entity declares [Raises<ReservationConfirmed>]...
services.AddPragmaticEventHandlers();

// But the interceptor is never added!
services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString);
    // Missing: options.UseDomainEvents();
});
```

**Runtime result:** `SaveChangesAsync()` persists the entity changes and nothing raises the declared events, so there is nothing to dispatch and no handler fires. `[Raises<T>]` produces code that nobody calls: the quietest failure in the module, because the attribute is right there in the source saying otherwise.

**Right:**

```csharp
services.AddPragmaticEventHandlers();

services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString);
    options.UseDomainEvents();  // Add the interceptor that raises them
});
```

**Why:** `UseDomainEvents()` adds `LifecycleEventsInterceptor`, and that is the only thing that calls the `RaiseLifecycleEvents` method the generator puts on a `[Raises<T>]` entity. It takes no service provider: dispatch is not its job, so it has nothing to resolve. Events raised by hand with `RaiseEvent(...)` do not depend on it; declared ones do entirely.

---

## 9. Handling Events Synchronously When They Need Async

**Wrong:**

```csharp
[EventHandler]
public sealed class ReservationConfirmedHandler(
    IBillingActions billingActions) : IDomainEventHandler<ReservationConfirmed>
{
    public Task HandleAsync(ReservationConfirmed @event, CancellationToken ct = default)
    {
        // Blocking async call with .Result -- potential deadlock!
        billingActions.CreateDraftInvoice(
            reservationId: @event.ReservationId,
            guestId: @event.GuestId,
            totalAmount: @event.TotalAmount,
            currency: @event.Currency,
            ct: ct).Result;

        return Task.CompletedTask;
    }
}
```

**Runtime result:** Calling `.Result` on an async method can deadlock when the synchronization context is not free (ASP.NET Core's `SynchronizationContext` is typically null, so this often works by luck, but it blocks a thread pool thread unnecessarily and can deadlock in some hosting scenarios).

**Right:**

```csharp
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

**Why:** `HandleAsync` returns `Task` by design. Use `async`/`await` and `ConfigureAwait(false)` for all async operations. Never block with `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` inside a handler. The dispatcher awaits each handler's `Task` -- it already handles the async flow correctly.

---

## 10. Not Passing CancellationToken Through Handlers

**Wrong:**

```csharp
[EventHandler]
public sealed class ReservationConfirmedHandler(
    IBillingActions billingActions) : IDomainEventHandler<ReservationConfirmed>
{
    public async Task HandleAsync(ReservationConfirmed @event, CancellationToken ct = default)
    {
        // Ignores the cancellation token!
        await billingActions.CreateDraftInvoice(
            reservationId: @event.ReservationId,
            guestId: @event.GuestId,
            totalAmount: @event.TotalAmount,
            currency: @event.Currency).ConfigureAwait(false);
    }
}
```

**Runtime result:** If the request is cancelled (client disconnects, server shutdown), the handler continues executing because the cancellation token is not forwarded. The handler wastes resources processing a request that will never produce a visible result.

**Right:**

```csharp
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
            ct: ct).ConfigureAwait(false);  // Forward the token
    }
}
```

**Why:** The dispatcher passes the cancellation token to each handler. `OperationCanceledException` is the only exception type that propagates immediately (not swallowed by continue-on-failure). Always forward `ct` to downstream async calls. This ensures graceful shutdown and request cancellation work correctly throughout the handler chain.

---

## Quick Reference

| Mistake | Symptom |
|---------|---------|
| Event lacks data | Handler injects repository from another boundary |
| Dispatch before persistence | Side effects for uncommitted changes |
| Throwing for business errors | Exception swallowed, handler silently produces nothing |
| Missing `AddInMemoryDomainEvents()` | `InvalidOperationException` resolving `IDomainEventDispatcher` |
| `DomainEventSource` with existing base class | Compile error: cannot inherit from two classes |
| Mutable events | Handler mutation visible to subsequent handlers |
| Handler order for transactional coordination | Dependent handler sees incomplete state on failure |
| Missing `UseDomainEvents()` | `[Raises<T>]` events are never raised, so no handler fires |
| Blocking async with `.Result` | Deadlock risk or thread pool starvation |
| Not forwarding `CancellationToken` | Handler continues after request cancellation |
