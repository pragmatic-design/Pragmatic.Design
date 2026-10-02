---
title: "Pragmatic.Messaging"
description: "Event-driven messaging for .NET 10: zero reflection, AOT-safe handler pipelines, transactional"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Messaging/README.md
sidebar:
  order: 0
  label: Overview
---
Event-driven messaging for .NET 10: zero reflection, AOT-safe handler pipelines, transactional
outbox, saga orchestration, batch processing, and multi-transport support, all source-generated at
compile time.

## The Problem

Messaging in .NET usually means MassTransit or NServiceBus. Both are powerful, but both rely on runtime
assembly scanning for handler discovery, dynamic dispatch, and runtime-evaluated middleware pipelines.
Retry lives in a separate Polly config; sagas are discovered by reflection; the link between a handler
and its resilience policy is implicit and scattered.

```csharp
// Without Pragmatic: config scattered across files; the handler has no idea about retry/CB/timeout
services.AddMassTransit(x =>
{
    x.AddConsumer<OrderCreatedConsumer>();              // runtime discovery
    x.UsingRabbitMq((ctx, cfg) => cfg.ReceiveEndpoint("order-created", e =>
    {
        e.UseMessageRetry(r => r.Intervals(200, 500, 1000));   // separate from the handler
        e.UseCircuitBreaker(cb => cb.TrackingPeriod = TimeSpan.FromMinutes(1));
        e.ConfigureConsumer<OrderCreatedConsumer>(ctx);
    }));
});
```

## The Solution

Resilience is declared **on the handler**. The generator produces the complete pipeline (retry loop,
circuit breaker, timeout, idempotency, telemetry) as inline code at compile time. Zero Polly, zero
reflection.

```csharp
[MessageHandler]
[Retry(MaxAttempts = 3, Strategy = BackoffStrategy.ExponentialWithJitter, BaseDelayMs = 200)]
[CircuitBreaker(FailureThreshold = 5, BreakDurationSeconds = 30)]
[Timeout(TimeoutSeconds = 60)]
public sealed partial class OrderCreatedHandler(IOrderService service)
    : IMessageHandler<OrderCreated>
{
    public async Task HandleAsync(OrderCreated message, MessageContext context, CancellationToken ct)
        => await service.ProcessAsync(message.OrderId, ct);
}
```

The generator emits `OrderCreatedHandler.Pipeline.g.cs` (a nested `Pipeline` class) with the retry loop, circuit-breaker state,
timeout token, idempotency check, and telemetry, all inline.

## Installation

```bash
dotnet add package Pragmatic.Messaging         # bridge to Events + EF Core: the outbox
dotnet add package Pragmatic.Messaging.Core     # interfaces, attributes, in-memory bus
dotnet add package Pragmatic.SourceGenerator    # the unified analyzer
```

Add transports and bridges as needed:

```bash
dotnet add package Pragmatic.Messaging.Channels   # in-process async
dotnet add package Pragmatic.Messaging.RabbitMQ    # distributed (AMQP)
dotnet add package Pragmatic.Messaging.Kafka       # event streaming
dotnet add package Pragmatic.Messaging.Jobs        # scheduled (future) delivery
dotnet add package Pragmatic.Messaging.Auditing    # message audit trail
```

(Building inside this monorepo? See [Monorepo Structure](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/howto/monorepo-structure.md).)

## Quick Start

**1. Define a message and handler** (the generator auto-discovers handlers by `[MessageHandler]`; no
manual registration):

```csharp
public record OrderCreated(Guid OrderId, decimal Total, string CustomerId);

[MessageHandler]
[Retry(MaxAttempts = 3)]
public sealed partial class OrderCreatedHandler(INotificationService notifications)
    : IMessageHandler<OrderCreated>
{
    public async Task HandleAsync(OrderCreated message, MessageContext context, CancellationToken ct)
        => await notifications.SendOrderConfirmationAsync(message.OrderId, ct);
}
```

**2. Configure the host:**

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    app.UseMessaging(msg =>
    {
        msg.UseChannels(ch => { ch.Capacity = 1000; ch.ConsumerCount = 2; });
        msg.EnableIdempotency();
    });
});
```

**3. Publish:**

```csharp
public class CheckoutService(IMessageBus bus)
{
    public Task CompleteCheckoutAsync(Order order, CancellationToken ct)
        => bus.PublishAsync(new OrderCreated(order.Id, order.Total, order.CustomerId), ct);
}
```

Full walkthrough: [Getting Started](/modules/messaging/getting-started/).

## Packages

| Package | Role |
|---------|------|
| `Pragmatic.Messaging` | Bridge to Events + EF Core: outbox interceptor, delivery service, event adapter |
| `Pragmatic.Messaging.Core` | Interfaces, attributes, in-memory bus, middleware, routing, distributed request/reply; no dependency on Events or EF Core |
| `Pragmatic.Messaging.Channels` / `.RabbitMQ` / `.Kafka` / `.AzureServiceBus` / `.Sql` | Transports (in-process / AMQP / streaming / cloud+emulator / PostgreSQL or SQL Server tables, no broker) |
| `Pragmatic.Messaging.EFCore` | EF Core stores: outbox source, idempotency, audit persistence |
| `Pragmatic.Messaging.Saga` | Saga orchestration over domain actions: compensation, timeouts, generated orchestrator |
| `Pragmatic.Messaging.Batch` | Scatter/gather with progress tracking and rate limiting |
| `Pragmatic.Messaging.ClaimCheck` | Large payloads moved to Pragmatic.Storage; the message carries the reference |
| `Pragmatic.Messaging.Jobs` | Scheduled (future) message delivery, via Pragmatic.Jobs |
| `Pragmatic.Messaging.Auditing` | Every handled message on the Pragmatic.Audit trail, by reference, never by payload |
| `Pragmatic.Messaging.Dashboard` | Ops API and embedded panel: status, outbox, dead-letter replay, sagas, audit |
| `Pragmatic.Messaging.Testing` | `MessageBusTestHarness` (record-only + dispatching modes) |

## Operational note

The outbox is **at-least-once**: a message can be delivered more than once (e.g. after a retry or a
crash between commit and dispatch). Make handlers **idempotent**: enable `EnableIdempotency()` and/or
guard side effects by a business key. In cross-boundary scenarios, publish through the outbox
(`[EnableOutbox]`) so the message commits in the same transaction as your data. See
[Common Mistakes](/modules/messaging/common-mistakes/).

## Status

**Functional** within 1.0.0-alpha: the handler pipeline, the outbox, sagas, the transports, and batch.
See the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

| [Concepts](/modules/messaging/concepts/) | Message lifecycle, handler pipeline, transport architecture, registration, Events vs Messaging |
| [Getting Started](/modules/messaging/getting-started/) | Define a message, handler, publish, configure a transport |
| [Transports](/modules/messaging/transports/) | RabbitMQ / Kafka / Azure Service Bus / SQL (Postgres/SqlServer) / Channels: topology, dead-lettering, ordering, local testing (incl. ASB emulator) |
| [Reliability](/modules/messaging/reliability/) | The failure ladder: `[Retry]` → `[Redelivery]` → transport dead-letter; kill switch, rate/concurrency limits, idempotency purge, claim check |
| [Sagas](/modules/messaging/saga-guide/) | `ISaga<T>`, orchestration vs choreography, compensation, timeouts, generated Mermaid diagrams |
| [Advanced Patterns](/modules/messaging/advanced-patterns/) | Distributed request/reply, multi-bus with isolated transports, scheduled messages (Jobs / ASB / SQL native), auditing, claim check |
| [Operations](/modules/messaging/operations/) | Ops dashboard (status, outbox, dead-letter replay, sagas, audit), counters, health |
| [Testing](/modules/messaging/testing/) | Harness modes (record-only / dispatching), Testcontainers broker fixtures |
| [Common Mistakes](/modules/messaging/common-mistakes/) | The most frequent messaging pitfalls (idempotency, outbox, retry, batch) |
| [Troubleshooting](/modules/messaging/troubleshooting/) | Handler/outbox/retry/saga checklists, diagnostics reference, FAQ |

## Cross-module integration

Publishes [Events](/modules/events/overview/) across boundaries, commits the outbox in the same
[Persistence](/modules/persistence/overview/) transaction, bridges to [Jobs](/modules/jobs/overview/)
for scheduled delivery, and is wired by [Composition](/modules/composition/overview/)'s `UseMessaging()`.

## Requirements

- .NET 10.0+
- `Pragmatic.SourceGenerator` analyzer

## License

Part of the [Pragmatic.Design](/modules/messaging/overview/) ecosystem. See [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.Messaging is licensed under the **PolyForm Small Business 1.0.0** license (free for small
businesses; commercial license above the threshold).
