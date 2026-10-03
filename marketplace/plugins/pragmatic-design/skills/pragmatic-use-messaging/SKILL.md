---
name: pragmatic-use-messaging
description: Use when adding async cross-boundary events, message handlers, a transactional outbox, sagas, a transport (in-memory, channels, RabbitMQ) or an in-process event reaction — Pragmatic.Messaging.
---

# Pragmatic Use Messaging

**Covers:** Asynchronous cross-boundary work with Pragmatic.Messaging from NuGet — message handler, retry, transactional outbox, saga, transport (in-memory/channels/RabbitMQ). Attribute-first pattern driven by the source generator.

`Pragmatic.Messaging` decouples boundaries: an operation publishes a message, one or more handlers react. The source generator produces each handler's pipeline (retry, idempotency, middleware) inline — no reflection-based dispatch.

## When to use

- React to a cross-boundary event (e.g. Booking confirms → Billing creates invoice).
- Guarantee that a state change and its event are atomic (**transactional outbox**).
- Orchestrate a multi-step workflow with compensation (**saga**).

For scheduled/recurring jobs use `pragmatic-use-jobs` instead.

## Packages

```xml
<PackageReference Include="Pragmatic.Messaging" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Messaging.EFCore" Version="1.0.0-alpha.1" />     <!-- transactional outbox -->
<PackageReference Include="Pragmatic.Messaging.Channels" Version="1.0.0-alpha.1" />   <!-- in-process transport -->
<PackageReference Include="Pragmatic.Messaging.RabbitMQ" Version="1.0.0-alpha.1" />   <!-- distributed transport -->
<PackageReference Include="Pragmatic.Messaging.Saga" Version="1.0.0-alpha.1" />       <!-- workflow -->
```

Always add `Pragmatic.SourceGenerator` as an analyzer. Namespaces: `Pragmatic.Messaging`, `Pragmatic.Messaging.Attributes`, `Pragmatic.Messaging.Saga`, and `Pragmatic.Resilience.Attributes` for `[Retry]`, `[Timeout]` and `[CircuitBreaker]`.

## Mental model

```
   You decorate         Source generator produces          Runtime
  [MessageHandler]  →   {Handler}.Pipeline.g.cs       →  retry+middleware pipeline inline
  [EnableOutbox]    →   __OutboxMessages in the boundary DbContext → delivery pump drains
  [Saga<TState>]    →   {Saga}.Orchestrator.g.cs      →  state machine + timeout runner
```

## Core patterns

### 1. Publishing a message

Inject `IMessageBus` into a mutation/action and publish an event:

```csharp
await _bus.PublishAsync(new ReservationConfirmed(reservation.Id), ct);
```

`IMessageBus`: `PublishAsync` (broadcast to all handlers), `SendAsync` (to a single consumer), `RequestAsync<TRequest,TResponse>` (request/response), `DispatchAsync` (bypasses the transport).

### 2. Handling a message

```csharp
using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;
using Pragmatic.Resilience.Attributes;

[MessageHandler]
[Retry(MaxAttempts = 3, Strategy = BackoffStrategy.ExponentialWithJitter, BaseDelayMs = 500)]
[Timeout(TimeoutSeconds = 60)]
public partial class ReservationConfirmedHandler : IMessageHandler<ReservationConfirmed>
{
    private IRepository<Invoice> _invoices = null!;          // dependency: private field

    public async Task HandleAsync(ReservationConfirmed msg, MessageContext ctx, CancellationToken ct)
    {
        // create the draft invoice
    }
}
```

- The class must be `partial`, implement `IMessageHandler<T>`, and carry `[MessageHandler]` (**PRAG0800/0801**).
- `[Retry]` (`BackoffStrategy`: `Fixed | Exponential | ExponentialWithJitter`), `[Timeout]`, `[CircuitBreaker(FailureThreshold, BreakDurationSeconds)]` configure the generated pipeline.
- Dependencies via uninitialized private fields, same pattern as Actions.
- `[MessageMiddleware]` on an `IMessageMiddleware` class for global or per-type cross-cutting concerns.
- **Request/response**: `[RequestHandler]` on a class implementing `IRequestHandler<TRequest, TResponse>` answers `bus.RequestAsync<TRequest, TResponse>(…)`.
- **Ordering on a partitioned transport (Kafka)**: `[PartitionKey]` on one property of the message — messages with the same key land on the same partition. Without it the key is `CorrelationId ?? MessageId`. It works on a positional record parameter too, written `[property: PartitionKey]`; on a transport without partitions the header travels and routes nothing.

### 2b. Domain events raised by entities (in-process)

For reactions to changes **inside the same boundary**, entities raise domain events and a handler
runs in-process after the mutation/action commits — no message bus needed. Package `Pragmatic.Events`.
For the full in-process story (`[EventHandler]`, handler ordering, the EF Core dispatch interceptor,
declarative lifecycle `[Raises<T>]`, and the domain-event transactional outbox) see **`pragmatic-use-events`**.

```csharp
using Pragmatic.Events;

public partial class Reservation : DomainEventSource, IEntity   // inherit DomainEventSource
{
    public VoidResult<IError> Confirm()
    {
        var r = TransitionTo(ReservationStatus.Confirmed);
        if (r.IsSuccess) RaiseEvent(new ReservationConfirmed(Id, GuestId, /* ... */));   // protected
        return r;
    }
}

public sealed record ReservationConfirmed(Guid ReservationId, Guid GuestId) : IDomainEvent;

[EventHandler]                                  // without it the handler is never registered
public sealed class ReservationConfirmedHandler : IDomainEventHandler<ReservationConfirmed>
{
    public Task HandleAsync(ReservationConfirmed e, CancellationToken ct) { /* react */ return Task.CompletedTask; }
}
```

The mutation/action invoker collects raised events and dispatches them via `IDomainEventDispatcher`
after the transaction commits. **Choose the pattern by reach:** in-process same-boundary reaction →
`IDomainEventHandler<T>` (this section); cross-boundary or async/durable → publish to the bus and
handle with `[MessageHandler] IMessageHandler<T>` (sections 1–2), optionally with the outbox below.

### 3. Transactional outbox

Guarantees that the entity change and the event are atomic (no lost events on crash):

`[EnableOutbox]` goes on the **`[Boundary]`**, not on a DbContext:

```csharp
[Boundary]
[EnableOutbox]                       // requires Pragmatic.Messaging.EFCore
public partial class OrdersBoundary;
```

```csharp
app.UseMessaging(msg =>
{
    // Tuning only — the outbox itself is wired by the attribute above.
    msg.EnableOutbox(o => { o.PollingIntervalSeconds = 5; o.BatchSize = 100; o.Retention = TimeSpan.FromDays(3); });
});
```

The generator maps `__OutboxMessages` into that boundary's DbContext, adds the capture interceptor, and registers the delivery pump plus a retention purge. `OutboxInterceptor` writes the rows in the **same transaction** as `SaveChanges`; the pump publishes them to the transport and an AOT-safe `MessageTypeRegistry` deserializes. Without `Pragmatic.Messaging.EFCore` the attribute is inert → **PRAG0831**.

Pick **one** outbox per boundary: this one publishes to the transport (cross-service); Events' `[EnableEventOutbox]` dispatches in-process. Both capture and clear the same domain events, so applying both → **PRAG0833**.

⚠️ **An `[EnableOutbox]` boundary reacts to its own events with `[MessageHandler]`, never `[EventHandler]`.**
The interceptor takes the entity's domain events during the save — it has to, or one event would be
both a row and an in-process dispatch — and `EfCoreUnitOfWork` dispatches *after* the commit, which is
also deliberate. By then there are none, so every `IDomainEventHandler<T>` of that boundary stays
registered and is never entered: no log, no dead letter. What arrives is the message the pump
publishes from the outbox row. The build says so — **PRAG0837** — and the dangerous moment is adding
`[EnableOutbox]` to a boundary that already has working `[EventHandler]`s: it silences all of them at
once.

### 4. Saga — workflow with compensation

```csharp
using Pragmatic.Messaging.Saga;

[Saga<CheckInState>]                                       // TState MUST be an enum
public partial class CheckInSaga : ISaga<CheckInState>
{
    [SagaStart]
    public Task OnReservationArrived(ReservationArrived e, CancellationToken ct) { /* ... */ }

    [InState(CheckInState.GuestVerified, NextState = CheckInState.RoomAssigned)]
    [CompensateWith<ReleaseRoomAction>]
    public Task AssignRoom(GuestVerified e, CancellationToken ct) { /* ... */ }

    [SagaTimeout(Duration = "00:10:00")]
    public Task OnTimeout(CancellationToken ct) { /* ... */ }
}
```

- `[Saga<TState>]` with `TState` as enum; exactly one `[SagaStart]` (**PRAG0814**).
- `[InState(state, NextState =)]` defines transitions; `[CompensateWith<TAction>]` the compensation on failure; `[SagaTimeout(Duration = "HH:mm:ss")]` the timeout.
- **Correlation**: each event after the start finds its saga instance through `ICorrelatedMessage`, or through `[CorrelationKey]` on one of its properties (positional record parameters too; a non-string value is `ToString()`-ed). An event with neither is **PRAG0820**; two keys on one type, **PRAG0821**.
- The SG generates `{Saga}.Orchestrator.g.cs` with the state machine and validates transitions at compile-time.
- `NextState` is the **default** transition: it is applied only if the handler did not set `State` itself, so a handler that branches (assigning `State`) wins.
- **Rejecting a message**: throw `SagaRejectedException` to reject on a business ground — the orchestrator runs the `[CompensateWith]` chain, marks the saga `Compensated` and does **not** retry. Any other exception compensates too, marks `Faulted`, and is rethrown so the pipeline retries/dead-letters. Simply returning never compensates.
- Enable persistence with the **boundary attribute** — there is no host call and no `.Saga.EFCore` package:

```csharp
[Boundary]
[EnableSagaPersistence]   // maps __SagaInstances/__SagaSteps into this boundary's DbContext
public partial class BookingBoundary;
```

  The boundary project must reference `Pragmatic.Messaging.EFCore`, else the attribute is inert → **PRAG0832**.

### 5. Transport

Default: in-memory bus. For real scenarios, choose a transport in `UseMessaging`:

```csharp
app.UseMessaging(msg =>
{
    msg.UseChannels(c => { c.Capacity = 1000; c.ConsumerCount = 2; });   // in-process, backpressure
    // or distributed:
    msg.UseRabbitMq(r => { r.ConnectionString = config["RabbitMq"]!; r.ConsumerPrefetchCount = 10; });
    msg.EnableIdempotency();                                              // message deduplication
    msg.EnableAuditing();   // handling outcomes on the audit trail (Pragmatic.Messaging.Auditing); retention is the trail's
});
```

`UseChannels` (from `.Channels`) for in-process fan-out with backpressure; `UseRabbitMq` (from `.RabbitMQ`) for distributed messaging. `[OnBus("name")]` on a handler binds it to a named bus.

⚠️ **`[Redelivery]` needs `EnableScheduledMessages()` too, and the two idempotency claims are not the
same claim.** A handler declaring it re-schedules its failure instead of throwing, so the transport
acknowledges — which means the bus's claim on the *bare* message id is marked completed, while the
redelivered copy deliberately keeps that id. `PublishMessageJob` releases that claim before
republishing; a scheduler of your own has to. Without the release the message is neither retried nor
dead-lettered.

A publish right after startup is safe on every transport: Kafka and Service Bus connect before the host reports started, and RabbitMQ and SQL connect in the background while a publish issued meanwhile waits for them (`ConnectWaitTimeout`, default 30 s) — the application still starts with its broker down, and a connect that fails is retried in the background (RabbitMQ: `ReconnectBaseDelayMs`, `MaxReconnectAttempts`) while the host keeps running. Do not add a delay or a retry around the first publish.

### 6. Large payloads — claim check

Offloads a payload above a threshold to `IFileStorage`; the message travels as a stub with an
`x-claim-check` header and is rehydrated transparently before the handler runs.

```csharp
msg.EnableClaimCheck(o =>
{
    o.Threshold = 256 * 1024;           // above this, offload
    o.DeleteAfterConsume = false;       // default — safe for fan-out (many subscribers)
    o.Retention = TimeSpan.FromDays(7); // declarative: mirror in the store's lifecycle rule
});
```

Requires `Pragmatic.Messaging.ClaimCheck` + a registered `IFileStorage`. Know before using it with
sensitive data: **the blob is the complete serialized message**. `[NotLogged]` redacts a member where
`Pragmatic.Logging` writes the object, and nowhere else: it does not strip anything from the blob.
Encryption at rest is the storage provider's job. Set
`DeleteAfterConsume = true` only for competing-consumer topologies (a single logical consumer).

### 7. Batch fan-out

Splits one command into N item messages and tracks progress.

```csharp
msg.EnableBatchProcessing();       // for an EF-backed progress table, mark a [Boundary] with [EnableBatchProgress]

var batchId = await dispatcher.DispatchAsync(batch, label: "import-2026-07");
var progress = await store.GetProgressAsync(batchId);   // Total / Completed / Failed / IsComplete
```

Progress is reported **automatically** by `BatchProgressMiddleware` from each item's real outcome —
do **not** also call `BatchTracker.Report*Async` or items are counted twice. Counting is as exact as
delivery: enable `EnableIdempotency()` so a redelivered duplicate is dropped before it is counted.

### 8. Ops dashboard

```csharp
msg.EnableDashboard(dash => dash.ApiKey = config["Messaging:Dashboard:ApiKey"]);
```

Serves an API + HTML panel under `/_messaging` (status, outbox, dead letters with replay/delete,
sagas, audit). **Always set `ApiKey` outside local development**: with no key the dashboard is
loopback-only, and it refuses any request whose remote IP is not positively loopback (including
anything carrying a forwarding header), because behind a proxy `X-Forwarded-For` is client-controlled.
There is no per-user identity — replay and delete are attributable only to whoever holds the key.

### 9. Testing handlers

```csharp
services.AddDispatchingTestHarness();      // or AddMessagingTestHarness() to record only
// the harness is the registered IMessageBus: sp.GetRequiredService<MessageBusTestHarness>()

await bus.PublishAsync(new OrderPlaced(id));
harness.HasPublished<OrderPlaced>().Should().BeTrue();
harness.ConsumedOf<OrderPlaced>().Should().ContainSingle();
harness.FaultedOf<OrderPlaced>().Should().BeEmpty();
```

The dispatching harness resolves `IMessageHandler<T>`, which is the **SG-generated pipeline** — so
retry, idempotency, circuit-breaker, timeout and middleware all run, as in production. Handler
failures are recorded on `Faulted` rather than rethrown, so assert on `Faulted` instead of expecting
an exception. Untyped publishes only reach handlers when the SG dispatch table is registered.

## What the SG generates

| Trigger | File | Content |
|---|---|---|
| `[MessageHandler]` | `{Handler}.Pipeline.g.cs` | `partial class Pipeline` with retry/idempotency/middleware |
| aggregated | `_Infra.Messaging.Registration.g.cs` | `AddPragmaticMessageHandlers()` |
| `[EnableOutbox]` (on `[Boundary]`) | outbox table + wiring in the boundary DbContext | `EfCoreOutboxSource` + delivery pump + purge |
| `[Saga<TState>]` | `{Saga}.Orchestrator.g.cs` | State machine + timeout runner |

## Common diagnostics

| ID | Sev | Trigger | Fix |
|---|---|---|---|
| **PRAG0800** | Error | `[MessageHandler]` does not implement `IMessageHandler<T>` | Implement the interface |
| **PRAG0801** | Error | Handler not `partial` | Add `partial` |
| **PRAG0802** | Error | `[Retry]` with `MaxAttempts <= 0` | Use a positive value |
| **PRAG0814** | Error | Saga without `[SagaStart]` | Mark an initial method |
| **PRAG0816** | Warning | Event published with no consumer | Add a handler or remove the publish |
| **PRAG0831** | Warning | `[EnableOutbox]` boundary without `Pragmatic.Messaging.EFCore` | Reference the package |
| **PRAG0833** | Warning | Boundary has both `[EnableOutbox]` and `[EnableEventOutbox]` | Keep exactly one |
| **PRAG0837** | Warning | `[EventHandler]` on an `[EnableOutbox]` boundary — registered, never entered | Write it as `[MessageHandler] IMessageHandler<T>` |
| **PRAG1699** | Warning | A referenced assembly declares a message-handler registration and this host does not call it | Call it once in `Program.cs` — typically a contracts project, which is deliberately not a `[Module]` and so is never discovered |

## Troubleshooting

**Handler does not fire** — is the class `partial` with `[MessageHandler]`? Is the library referenced by the host? Is the event actually published (`PublishAsync`, not merely constructed)?

**Events lost on crash** — without the outbox `PublishAsync` is not transactional. Use `[EnableOutbox]` for the atomicity guarantee.

**Messages processed twice** — enable `msg.EnableIdempotency()`.

**Two services subscribe to one event and each sees about half of them** — they are sharing a queue, which makes them competing consumers instead of two subscribers. A subscription's name is `{module}.{message-kebab}` — `intake.verification-requested` beside `verify.verification-requested` — so this should not happen; check that the two really are different modules, and if one deployment must not share a queue with another of the **same** module, give it `msg.SubscribeAs("its-own-name")`. Symptom to recognise: a saga whose start step never runs, or a handler that fires for some messages and not others with no error anywhere. ⚠️ Renaming a subscription leaves the **old queue** bound and holding messages, with nothing reading it — plan it.

**A handler of the publishing service never runs, and nothing says why** — a service may handle a message its own outbox published. The publish side claims `outbox-publish:{MessageId}` and the consume side claims the bare id, so the two never meet. With a **custom** `IIdempotencyStore`, check that it stores the key it is given verbatim: one that normalizes keys makes the delivery look like a duplicate of its own publish.

## Build verification

```powershell
dotnet build                                              # PRAG08xx = messaging/saga issues
dotnet test path\to\App.Tests --no-restore -v minimal
```

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Warehouse example application — code
that compiles and that `Warehouse.IntegrationTests` exercises — and kept identical to it by the gate: an
integration event and the state move that raises it into the outbox, the boundary and the host that
publish, an idempotent handler in another service, a saga with its compensation, and a batch fan-out.

The claim check and the messaging test harness are used by no tested application yet, so there is no
example of them here: the sections above are the reference.
