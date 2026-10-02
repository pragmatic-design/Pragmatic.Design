---
title: "Common Mistakes"
description: "> The top 10 mistakes when using Pragmatic.Messaging — and how to fix them."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Messaging/docs/common-mistakes.md
sidebar:
  order: 9
---
> The top 10 mistakes when using Pragmatic.Messaging — and how to fix them.

### 1. Forgetting `partial` on the Handler

**Wrong:**
```csharp
[MessageHandler]
public sealed class OrderCreatedHandler : IMessageHandler<OrderCreated>
{
    public Task HandleAsync(OrderCreated message, MessageContext context, CancellationToken ct) { ... }
}
```

**Right:**
```csharp
[MessageHandler]
public sealed partial class OrderCreatedHandler : IMessageHandler<OrderCreated>
{
    public Task HandleAsync(OrderCreated message, MessageContext context, CancellationToken ct) { ... }
}
```

**Why:** The SG generates a partial declaration of the handler to host the nested `Pipeline` class and its `[LoggerMessage]` methods. Without `partial` the generator emits nothing for the handler — no pipeline, no registration — and **PRAG0801** says so as an error on your declaration.

---

### 2. Not Implementing IMessageHandler<T>

**Wrong:**
```csharp
[MessageHandler]
public sealed partial class InvoicePaidHandler  // Missing interface!
{
    public Task HandleAsync(InvoicePaid message, MessageContext context, CancellationToken ct) { ... }
}
```

**Right:**
```csharp
[MessageHandler]
public sealed partial class InvoicePaidHandler : IMessageHandler<InvoicePaid>
{
    public Task HandleAsync(InvoicePaid message, MessageContext context, CancellationToken ct) { ... }
}
```

**Why:** The SG finds the message type `T` from the `IMessageHandler<T>` interface. Without it, the SG can't determine what message type the handler processes → PRAG0800 error.

---

### 3. Retry with MaxAttempts = 0

**Wrong:**
```csharp
[MessageHandler]
[Retry(MaxAttempts = 0)]  // No retry at all — but why use the attribute?
public sealed partial class PaymentHandler : IMessageHandler<ProcessPayment> { ... }
```

**Right:**
```csharp
[MessageHandler]
[Retry(MaxAttempts = 3, Strategy = BackoffStrategy.ExponentialWithJitter)]
public sealed partial class PaymentHandler : IMessageHandler<ProcessPayment> { ... }
```

**Why:** `MaxAttempts = 0` means zero retries → the handler fails immediately on first error. This triggers PRAG0802 error. If you don't want retry, simply don't add `[Retry]`.

---

### 4. Reporting Batch Progress by Hand, or Throwing to Fail an Item

`EnableBatchProcessing()` registers `BatchProgressMiddleware`, which counts every batch item when its
handler returns (completed) or throws (failed). The handler reports nothing.

**Wrong:**
```csharp
[MessageHandler]
public sealed partial class ImportPartHandler(IBatchProgressStore store) : IMessageHandler<ImportPart>
{
    public async Task HandleAsync(ImportPart part, MessageContext context, CancellationToken ct)
    {
        await ApplyRows(part);
        await BatchTracker.ReportSuccessAsync(context, store, ct);   // counted twice: the middleware counts it too
    }
}
```

**Also wrong**, when the item was processed and only part of its work was refused:
```csharp
if (rejected > 0)
    throw new ImportPartFailedException();   // the valid rows roll back, and the item is nacked
```

**Right:**
```csharp
[MessageHandler]
internal sealed partial class ImportPartHandler(BatchItemOutcome outcome) : IMessageHandler<ImportPart>
{
    public async Task HandleAsync(ImportPart part, MessageContext context, CancellationToken ct)
    {
        var rejected = await ApplyRows(part);   // the valid rows are applied and committed
        if (rejected > 0)
            outcome.Fail();                      // counted as failed, once, when the handler returns
    }
}
```

**Why:** the middleware is the one place an item is counted. `BatchTracker.Report*Async` is for
topologies that opt out of it, and on top of it counts the item a second time. A throw is a real failure:
the transaction rolls back, and the RabbitMQ transport nacks without requeue, so the item goes to the
dead-letter exchange, or is dropped when none is configured. `BatchItemOutcome` (scoped, one per message)
fails an item that was processed, without either. To start a batch from an operation, depend on
`IBatchDispatcher<TBatch, TItem>`; the generator does not inject the concrete `BatchDispatcher` (PRAG0419).

---

### 5. Putting [EnableOutbox] anywhere but the [Boundary]

**Wrong:**
```csharp
[EnableOutbox]
public class BookingRepository { ... }          // not a boundary

[EnableOutbox]
public class BookingDbContext : PragmaticDbContext { ... }   // the DbContext is generated
```

**Right:**
```csharp
[Boundary]
[EnableOutbox]                                  // requires Pragmatic.Messaging.EFCore
public partial class BookingBoundary;
```

**Why:** the attribute is read off the **boundary marker** (`MessagingOutboxBoundaryReader`), and the
generator maps `__OutboxMessages` into that boundary's generated DbContext and adds the capture
interceptor. Anywhere else it is silently inert — nothing generated, nothing said. ⚠️ It does not go on
the DbContext: a boundary whose DbContext the generator writes has no class of yours to decorate. A boundary marked `[EnableOutbox]` without a `Pragmatic.Messaging.EFCore` reference *is*
caught, as `PRAG0831`.

---

### 5-bis. An [EventHandler] on an [EnableOutbox] boundary

**Wrong:**
```csharp
[Boundary]
[EnableOutbox]
public partial class OrdersBoundary;

[EventHandler]                                   // registered, never entered → PRAG0837
public sealed class WhenAnOrderIsPlaced : IDomainEventHandler<OrderPlaced> { ... }
```

**Right:**
```csharp
[MessageHandler]                                 // what arrives is the published message
public sealed class WhenAnOrderIsPlaced : IMessageHandler<OrderPlaced> { ... }
```

**Why:** `OutboxInterceptor` takes the entity's domain events **during** the save — it has to, or one
event would be both an outbox row and an in-process dispatch — and `EfCoreUnitOfWork` dispatches
*after* the commit, which is equally deliberate: an event announcing a write that failed is worse than
one never sent. By then there are none, so the handler is registered and never called: no log, no dead
letter, nothing. ⚠️ The dangerous path is the upgrade — adding `[EnableOutbox]` to a boundary with
working `[EventHandler]`s would silence every one of them, with a green build and a green suite, so
the build reports **PRAG0837** and names `[MessageHandler]`.

---

### 6. Saga Without [SagaStart]

**Wrong:**
```csharp
[Saga<OrderState>]
public partial class OrderSaga : ISaga<OrderState>
{
    [InState(OrderState.Created, NextState = OrderState.Paid)]
    public ProcessPaymentAction Handle(PaymentReceived e) { ... }
}
```

**Right:**
```csharp
[Saga<OrderState>]
public partial class OrderSaga : ISaga<OrderState>
{
    [SagaStart]
    public ValidateOrderAction Handle(OrderCreated e) { ... }

    [InState(OrderState.Created, NextState = OrderState.Paid)]
    public ProcessPaymentAction Handle(PaymentReceived e) { ... }
}
```

**Why:** Every saga needs exactly one entry point. The SG-generated orchestrator looks for `[SagaStart]` to know which event creates a new saga instance. Without it → PRAG0814 error.

---

### 7. Catching OperationCanceledException in Handlers

**Wrong:**
```csharp
public async Task HandleAsync(OrderCreated message, MessageContext context, CancellationToken ct)
{
    try
    {
        await ProcessOrder(message, ct);
    }
    catch (Exception ex)  // Catches OperationCanceledException too!
    {
        _logger.LogError(ex, "Failed");
    }
}
```

**Right:**
```csharp
public async Task HandleAsync(OrderCreated message, MessageContext context, CancellationToken ct)
{
    try
    {
        await ProcessOrder(message, ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        _logger.LogError(ex, "Failed");
    }
}
```

**Why:** The generated retry loop uses `when (ex is not OperationCanceledException)` to avoid retrying cancellations. If your handler swallows `OperationCanceledException`, the `[Timeout]` attribute can't cancel the handler, and shutdown becomes slow.

---

### 8. Publishing Messages Without EnableOutbox in Cross-Boundary Scenarios

**Wrong:**
```csharp
// Booking module saves entity, then manually publishes
await _repo.SaveAsync(reservation);
await _messageBus.PublishAsync(new ReservationConfirmed(...));
// If app crashes between these two lines → event lost!
```

**Right:**
```csharp
// Entity raises domain event — outbox ensures atomicity
reservation.RaiseDomainEvent(new ReservationConfirmed(...));
await _repo.SaveAsync(reservation);
// OutboxInterceptor persists event in same TX → guaranteed delivery
```

**Why:** Without the outbox, there's a window between `SaveAsync` and `PublishAsync` where a crash loses the event. The outbox interceptor persists the event atomically with the entity changes.

---

### 9. Using InMemory Stores in Production

**Wrong:**
```csharp
app.UseMessaging(msg =>
{
    msg.UseRabbitMq(rmq => { rmq.ConnectionString = "..."; });
    msg.EnableIdempotency();   // InMemoryIdempotencyStore — lost on restart!
    msg.EnableSagas();         // InMemorySagaRepository — lost on restart!
});
```

**Right:**
```csharp
// Host wiring:
app.UseMessaging(msg =>
{
    msg.UseRabbitMq(rmq => { rmq.ConnectionString = "..."; });
    msg.EnableIdempotency();
    msg.EnableBatchProcessing();     // dispatcher + progress tracking
    // For an EF-backed batch progress table, mark a [Boundary] with [EnableBatchProgress]
    // (the generator maps __BatchProgress and registers the EF store) — see the batch guide.
    // Implement IIdempotencyStore backed by Redis/DB for production dedup
});

// Durable sagas are opt-in per boundary, not a host call:
[Boundary]
[EnableSagaPersistence]              // EF Core-backed saga repository (needs Pragmatic.Messaging.EFCore)
public partial class BookingBoundary;
```

**Why:** `InMemoryIdempotencyStore`, `InMemorySagaRepository`, and `InMemoryBatchProgressStore` lose all state on process restart. In production with RabbitMQ, messages may be redelivered after restart → duplicates without persistent idempotency.

---

### 10. Accessing DbContext Directly in Cross-Boundary Handlers

**Wrong:**
```csharp
[MessageHandler]
public sealed partial class ReservationConfirmedHandler(
    BookingDbContext dbContext) : IMessageHandler<ReservationConfirmed>
{
    public async Task HandleAsync(ReservationConfirmed @event, MessageContext context, CancellationToken ct)
    {
        // Direct DB access to another boundary!
        var reservation = await dbContext.Reservations.FindAsync(@event.ReservationId);
        // Creates tight coupling, breaks boundary isolation
    }
}
```

**Right:**
```csharp
[MessageHandler]
public sealed partial class ReservationConfirmedHandler(
    IBillingActions billing) : IMessageHandler<ReservationConfirmed>
{
    public async Task HandleAsync(ReservationConfirmed @event, MessageContext context, CancellationToken ct)
    {
        // Use boundary interface — works in monolith AND distributed
        await billing.CreateDraftInvoice(@event.ReservationId, @event.GuestId, ...);
    }
}
```

**Why:** The event should contain all data the handler needs. If you access another boundary's DbContext directly, you break the boundary isolation — the code won't work when you switch to distributed deployment with `BoundaryMode.Remote`.
