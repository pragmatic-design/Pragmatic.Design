# Concepts

> Architecture, mental model, and design decisions behind Pragmatic.Messaging.

## The Problem

Without a messaging framework, cross-boundary communication requires manual plumbing:

```csharp
// Without Pragmatic.Messaging: manual event dispatch, no retry, no outbox
public class ReservationService
{
    public async Task ConfirmReservation(Guid id)
    {
        var reservation = await _repo.GetAsync(id);
        reservation.Status = ReservationStatus.Confirmed;
        await _repo.SaveAsync(reservation);

        // Manual event dispatch: no retry, no outbox, no audit
        try
        {
            var @event = new ReservationConfirmed(id, reservation.GuestId, ...);
            var handler = _serviceProvider.GetService<ReservationConfirmedHandler>();
            await handler.HandleAsync(@event);  // What if this fails?
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to notify billing");
            // Lost event. Billing never creates invoice. Guest never gets charged.
        }
    }
}
```

Problems: no retry, no dead letter, no idempotency, no audit trail, no outbox (event lost if dispatch fails after save), no compile-time validation.

## The Solution

```csharp
// With Pragmatic.Messaging: attribute-driven, SG-generated pipeline
[MessageHandler]
[Retry(MaxAttempts = 3, Strategy = BackoffStrategy.ExponentialWithJitter)]
public sealed partial class ReservationConfirmedHandler(
    IBillingActions billing) : IMessageHandler<ReservationConfirmed>
{
    public async Task HandleAsync(ReservationConfirmed @event, MessageContext context, CancellationToken ct)
    {
        await billing.CreateDraftInvoice(@event.ReservationId, @event.GuestId, ...);
    }
}
```

The SG generates a `_Pipeline` wrapper with retry loop, circuit breaker, idempotency check, telemetry, and logging. The outbox ensures the event is never lost. Zero reflection at runtime.

## How It Works

### Message Lifecycle

```
Entity raises DomainEvent
    ↓
OutboxInterceptor (SaveChanges) → persists OutboxMessage atomically
    ↓
OutboxDeliveryService (background) → polls pending messages
    ↓
IMessageTypeRegistry (SG-generated) → deserializes to typed message (AOT-safe)
    ↓
IMessageBus.DispatchAsync() → resolves handler(s)
    ↓
Handler_Pipeline (SG-generated):
  ├── Idempotency check (IIdempotencyStore)
  ├── Circuit breaker gate (static state)
  ├── Retry loop (backoff strategy)
  │   ├── Authorization bypass (ICallContext)
  │   └── Handler.HandleAsync() (your code)
  ├── Metrics (duration, success/failure)
  └── Logging (generated log call sites)
```

### The outbox on the Sql and documents engines

A boundary the Sql or documents engine persists enables the outbox with the same `[EnableOutbox]`; its project references `Pragmatic.Messaging.Engines` (PRAG0831 names the package the boundary's engine needs). The generated `Add{Boundary}Persistence` then registers, for that boundary only, the capture and the source the delivery pump reads:

| | Sql engine | Documents engine |
|---|---|---|
| Capture | `SqlMessagingOutboxPhase`: the events of the entities the save writes become `OutboxMessageRow`s in the save's batch, its transaction | `DocMessagingOutboxPhase`: `OutboxMessageDocument`s written through the save's session, in its transaction where MongoDB runs one |
| Store | `__OutboxMessages`, EF Core's table column for column (instants in UTC ticks), in the database's generated schema | the `__OutboxMessages` collection, in the boundary's `{Boundary}DocSchema` |
| Source | `SqlOutboxSource` | `DocOutboxSource` |

Everything after the capture is shared with EF Core. A message carries what `OutboxMessageCapture` takes from the save: the tenant, the user, the correlation and the trace headers. The sources claim, back off and release under `OutboxClaimPolicy`'s rules. The one `OutboxDeliveryService`, the retention purge and the `Messaging.Outbox` health contributor read every boundary's source, whatever its store.

Where they differ from EF Core:

- **The phase sees the entities the save writes.** An entity that raised an event and changed nothing is not written, and its events are dispatched in process after the commit, as without the outbox. EF Core's interceptor reads every tracked entity.
- **On a standalone MongoDB server and on Cosmos DB** a save is not one transaction: the outbox documents are written after the entities, and a failure in between leaves the entities written and their events lost.
- **The package does not bring the engines' bridges.** The application references the bridge its boundaries use (`Pragmatic.Persistence.Sql`, `Pragmatic.Persistence.Documents`), as it does without the outbox; bringing both would make it reference two engines.

### Handler Pipeline (SG-Generated)

For each `[MessageHandler]` class, the SG generates `{Handler}_Pipeline.g.cs`:

| Layer | When Generated | Purpose |
|-------|----------------|---------|
| Idempotency | Always (null-safe) | Skips duplicate MessageIds |
| Circuit Breaker | `[CircuitBreaker]` present | Rejects when failure threshold exceeded |
| Retry | `[Retry]` present | Retries with configurable backoff |
| Timeout | `[Timeout]` present | Linked CancellationToken |
| Auth Bypass | Authorization detected | `ICallContext.EnterInternalCall()` |
| Telemetry | Always | Activity span + metrics counters |
| Logging | Always | Generated log call sites, written in the same pass as the pipeline |

### Transport Architecture

```
                    IMessageBus
                        │
         ┌──────────────┼──────────────┐
         ▼              ▼              ▼
  InMemoryBus    ChannelTransport  RabbitMqTransport
  (dev/test)     (in-process)      (distributed)
         │              │              │
         └──────────────┼──────────────┘
                        ▼
                 IMessageRouter (SG-generated)
                 ├── GetTopic<T>() → exchange name
                 ├── GetSendQueue() → point-to-point queue
                 └── GetBusName() → named bus

                 SubscriptionName.For(subscriber, message, bus)
                 └── → {module}.{message-kebab}: the queue a consumer binds
```

⚠️ **A subscription's name is not the router's job**, and that is the one thing to understand here: the
router maps *types* to addresses, and a subscription needs to say **who is listening**. The two queues
`intake.verification-requested` and `verify.verification-requested` are the same event read by two
services, each getting its own copy, which is what a topic is for. A name like
`{transport}-{messageType}` says nothing of the subscriber: the two services would share one queue, and
each request would reach one of them. The subscriber is the **module**, not the handler: one subscription per message type serves
every handler of it in the process. Override it with `MessagingBuilder.SubscribeAs(...)` when the
deployment's identity is not the module's.

## Decision Tree

| Scenario | Transport | Outbox | Pattern |
|----------|-----------|--------|---------|
| Unit tests | InMemory | No | Direct dispatch |
| Monolith, async decoupling | Channels | Optional | Background processing |
| Cross-service events | RabbitMQ | Yes | Outbox + delivery |
| Event streaming, analytics | Kafka (P3) | Yes | Topic partitions |
| Multi-step workflow | Any | Yes | Saga orchestration |
| Large data processing | Any | No | Batch split + dispatch |

## What Gets Generated

| Trigger | Generated File | Content |
|---------|---------------|---------|
| `[MessageHandler]` | `{Handler}.Pipeline.g.cs` | Retry, CB, idempotency, telemetry wrapper |
| Aggregate | `_Infra.Messaging.Registration.g.cs` | `AddPragmaticMessageHandlers()` DI extension |
| Aggregate | `_Infra.Messaging.TypeRegistry.g.cs` | AOT-safe message type → deserializer map |
| Aggregate | `_Infra.Messaging.Metadata.g.cs` | Assembly metadata for host composition |
| Transport ref | `_Infra.Messaging.Topology.g.cs` | Exchange/queue bindings |
| Transport ref | `_Infra.Messaging.Routing.g.cs` | Message type → topic/queue router |
| `[EnableOutbox]` | `_Infra.Outbox.{Boundary}.g.cs` | OutboxSource + health check |
| `[Saga<TState>]` | `{Saga}.Orchestrator.g.cs` | State machine router + compensation |
| Aggregate | `_Infra.Messaging.SagaRegistration.g.cs` | Saga DI registration |

## Registration Model

### With Pragmatic.Composition (recommended)

```csharp
// Program.cs: transport + features
await PragmaticApp.RunAsync(args, app =>
{
    app.UseMessaging(msg =>
    {
        msg.UseChannels(ch => { ch.Capacity = 1000; });
        msg.EnableOutbox();
        msg.EnableIdempotency();
        msg.EnableSagas();
        msg.EnableBatchProcessing();
        msg.EnableAuditing();
    });
});
```

The SG auto-registers handlers, type registry, routing: no manual DI needed.

### Without Composition (standalone)

```csharp
builder.Services.AddPragmaticMessaging(msg =>
{
    msg.UseChannels();
});
// SG-generated extension:
builder.Services.AddPragmaticMessageHandlers();
```

## Pragmatic.Events vs Pragmatic.Messaging

These are **separate modules** with different scopes:

| | Pragmatic.Events | Pragmatic.Messaging |
|---|---|---|
| **Scope** | In-process, same boundary | Cross-boundary, cross-service, async |
| **Handler** | `IDomainEventHandler<T>` | `IMessageHandler<T>` |
| **Dispatch** | `InMemoryEventDispatcher` (sync, same TX) | `IMessageBus` (async, outbox, transport) |
| **SG** | Composition feature | Messaging feature (pipeline, resilience) |

**Bridge**: The SG generates `MessageHandlerEventAdapter<T>` so domain events can also reach `IMessageHandler<T>`. This is optional and automatic when both packages are referenced.

## Ecosystem Integration

| Module | Integration |
|--------|-------------|
| **Events** | Optional bridge: domain events reach message handlers via adapter |
| **Actions** | Saga steps dispatch `DomainAction` via `IMessageBus` |
| **Persistence.EFCore** | `OutboxInterceptor` on `SaveChanges`, `[EnableOutbox]` on DbContext |
| **Persistence.Sql / Documents** | `Pragmatic.Messaging.Engines`: a save phase per `[EnableOutbox]` boundary, and its `IOutboxSource` |
| **Authorization** | Pipeline generates `ICallContext.EnterInternalCall()` bypass |
| **MultiTenancy** | `MessageContext.TenantId` propagated to Activity tags |
| **Composition** | `PragmaticHostTemplate` auto-calls `AddPragmaticMessageHandlers()` |
