---
title: "Advanced Patterns"
description: "Beyond basic publish/subscribe (covered in Getting Started) and"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Messaging/docs/advanced-patterns.md
sidebar:
  order: 3
---
Beyond basic publish/subscribe (covered in [Getting Started](/modules/messaging/getting-started/)) and
[Sagas](/modules/messaging/saga-guide/), Pragmatic.Messaging supports point-to-point request/reply, isolated named
buses, scheduled (future) delivery, and message auditing.

## Request/Reply

Point-to-point request with a typed response (queue semantics — single consumer).

### Define a handler

```csharp
[RequestHandler]
public sealed partial class GetPriceHandler
    : IRequestHandler<GetPriceRequest, PriceResponse>
{
    public Task<PriceResponse> HandleAsync(
        GetPriceRequest request, MessageContext context, CancellationToken ct)
    {
        return Task.FromResult(new PriceResponse(request.ProductId, 29.99m));
    }
}
```

### Call

```csharp
var response = await bus.RequestAsync<GetPriceRequest, PriceResponse>(
    new GetPriceRequest(productId));
```

### Local vs distributed

`RequestAsync` resolves a **local** `IRequestHandler` first (in-process, no broker
roundtrip). When none is registered, the request goes **over the transport**:

- The request is sent point-to-point to its convention queue
  (`requests.{kebab-name}`, e.g. `requests.get-price-request`) with reply-to/request-id
  headers.
- The responder side is SG-generated: every `[RequestHandler]` gets a typed executor
  (deserialize → execute → serialize, zero reflection) that the transport consumer binds at
  startup.
- Replies come back on a process-unique reply queue and are correlated by request id.
- Responder exceptions and timeouts (default 30s) surface as `RequestReplyException` —
  the caller fails fast instead of hanging.

For cross-*service* calls over HTTP, `RemoteBoundary` (typed HTTP invokers) remains the
recommended default — see [Composition](/modules/composition/overview/); transport
request/reply fits broker-only topologies.

## Multi-Bus (isolated transports)

Isolate message flows by routing handlers to named buses (e.g. keep analytics traffic off the
business-critical bus). Each named bus runs its OWN transport with its own consumer service.

```csharp
[MessageHandler]
[OnBus("analytics")]
public sealed partial class PageViewedHandler : IMessageHandler<PageViewed>
{
    public async Task HandleAsync(PageViewed message, MessageContext context, CancellationToken ct)
    {
        // Runs on the "analytics" bus, isolated from business-critical traffic
    }
}
```

```csharp
msg.UseRabbitMq(o => o.ConnectionString = "amqp://business-rmq:5672"); // default bus
msg.AddBus("analytics", bus => bus.UseTransport(
    sp => new RabbitMqTransport(
        new RabbitMqOptions { ConnectionString = "amqp://analytics-rmq:5672" },
        sp.GetRequiredService<ILogger<RabbitMqTransport>>()),
    transportType: "rabbitmq"));
```

- Handlers without `[OnBus]` consume from the default bus; `[OnBus("analytics")]` handlers
  consume ONLY on the analytics transport (the SG emits per-bus subscriptions and the bus
  resolver).
- Publish to a named bus with the `bus.name` header, or resolve its bus directly:

```csharp
await bus.PublishAsync(evt, MessageContext.New() with
{
    Headers = new Dictionary<string, string> { ["bus.name"] = "analytics" },
});
// or: sp.GetRequiredKeyedService<IMessageBus>("analytics")
```

## Scheduled Messages

Two `IMessageScheduler` implementations:

- **Jobs bridge** (`Pragmatic.Messaging.Jobs`, any transport): persisted in the Jobs store,
  delivered by the job runner — restart-safe scheduling AND cancellation.
- **Azure Service Bus native** (registered automatically by `UseAzureServiceBus`): the broker
  holds the message (`ScheduleMessageAsync`), no database polling. With
  `EnableEfCorePersistence()` the cancel tokens are persisted in `__ScheduleHandles`
  (`IScheduleHandleStore`) and **cancellation is restart-safe**; without it, ids are
  in-process only (delivery itself is always durable on the broker).
- **SQL transport native** (registered automatically by `UseSqlTransport`): `VisibleAt`-based,
  restart-safe scheduling AND cancellation with no extra setup.

```csharp
msg.EnableScheduledMessages();  // Jobs bridge — requires Pragmatic.Messaging.Jobs
```

This registers `IMessageScheduler`:

```csharp
public interface IMessageScheduler
{
    Task<Guid> ScheduleAsync<T>(T message, TimeSpan delay, CancellationToken ct = default) where T : notnull;
    Task<Guid> ScheduleAsync<T>(T message, DateTimeOffset scheduledAt, CancellationToken ct = default) where T : notnull;
    Task CancelAsync(Guid scheduleId, CancellationToken ct = default);
}
```

```csharp
// Deliver in 24 hours
await scheduler.ScheduleAsync(new CheckoutReminder(reservationId), delay: TimeSpan.FromHours(24));

// Deliver at a specific time
await scheduler.ScheduleAsync(new CheckoutReminder(reservationId), scheduledAt: checkout.AddHours(-2));
```

Internally it creates a `PublishMessageJob` executed by the Jobs scheduler.

## Auditing

Record lifecycle events (Published / Handled / Failed / DeadLettered) for every message.

```csharp
msg.EnableAuditing();          // no options: see below
```

Messaging is a producer on the framework's shared trail, not the owner of one — register the trail
itself with `AddAuditTrail()` from `Pragmatic.Audit.EFCore`, once for the whole application. A Pragmatic
host with an `[Audited]` entity already does, on that entity's database.

**There is no payload option, and no `[NotLogged]` redaction here.** There is no payload field:
storing the serialized message is what put personal data in the old trail, and it is the reason the
shared one exists. Retention belongs to the trail as well (`AuditRetentionService`), which discards
whole sealed segments — deleting individual entries would change a segment's hash and make retention
indistinguishable from tampering. See [Pragmatic.Audit](/modules/audit/overview/).

`AuditMiddleware` (Order -100) wraps every handler and records duration, handler name, correlation ID,
and tenant. Query the trail:

```csharp
var entries = await auditStore.QueryAsync(new AuditQuery
{
    MessageType = "MyApp.Orders.OrderPlaced",
    From = DateTimeOffset.UtcNow.AddDays(-7),
    Limit = 100,
});
```

`AuditQuery` filters on `TenantId`, `MessageType`, `Direction`, `From`/`To`, with `Limit`/`Offset`.
Correlation ID is *captured* on every entry but is not a filter — narrow by type and time window.

| Store | Package | Use case |
|-------|---------|----------|
| `InMemoryAuditStore` | `Pragmatic.Messaging.Auditing` | Development (default) |
| `EfCoreAuditStore` | `Pragmatic.Messaging.EFCore` | Production |

## Claim check (large payloads)

`EnableClaimCheck()` (package `Pragmatic.Messaging.ClaimCheck`) offloads payloads above a
threshold to `IFileStorage` and puts only a reference on the wire — transparent on both
sides. Details: [Reliability](/modules/messaging/reliability/#claim-check--large-payloads).

## Operational dashboard

`EnableDashboard()` (package `Pragmatic.Messaging.Dashboard`) exposes the ops API + HTML
panel under `/_messaging`: status counters, outbox, dead-letters with replay, active sagas,
audit. Details: [Operations](/modules/messaging/operations/).

## Related

- [Getting Started](/modules/messaging/getting-started/) · [Concepts](/modules/messaging/concepts/) · [Sagas](/modules/messaging/saga-guide/)
- [Common Mistakes](/modules/messaging/common-mistakes/) · [Troubleshooting](/modules/messaging/troubleshooting/)
