---
title: "Troubleshooting"
description: "> Checklists, diagnostics reference, and FAQ for Pragmatic.Messaging."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Messaging/docs/troubleshooting.md
sidebar:
  order: 10
---
> Checklists, diagnostics reference, and FAQ for Pragmatic.Messaging.

## Handler Not Invoked

- [ ] Class has `[MessageHandler]` attribute
- [ ] Class implements `IMessageHandler<T>` (not just the method signature)
- [ ] Class is `partial` (for `[LoggerMessage]` integration)
- [ ] Message type `T` matches the event being published
- [ ] Module is referenced by the host project (SG runs per-project)
- [ ] `dotnet clean && dotnet build` (stale SG cache)

## Outbox Messages Not Delivered

- [ ] `EnableOutbox()` called in `UseMessaging()` config
- [ ] DbContext has `[EnableOutbox]` attribute
- [ ] `SagaEntityTypeConfiguration` / `OutboxEntityTypeConfiguration` applied to DbContext
- [ ] `OutboxDeliveryService` is running (check logs for `Pragmatic.Messaging.OutboxDeliveryService`)
- [ ] `IMessageTypeRegistry` can deserialize the message type (check `_Infra.Messaging.TypeRegistry.g.cs`)
- [ ] Database has `__OutboxMessages` table (migration applied)

## Retry Not Working

- [ ] `[Retry]` attribute is on the handler class (not the method)
- [ ] `MaxAttempts > 0` (PRAG0802 if zero)
- [ ] Exception is NOT `OperationCanceledException` (excluded from retry)
- [ ] Check generated `_Pipeline.g.cs` for `for (var __attempt` loop
- [ ] Verify metrics: `pragmatic.messaging.retry_attempts` counter

## Circuit Breaker Always Open

- [ ] Check `FailureThreshold`: lower values trip faster
- [ ] `BreakDurationSeconds` controls how long the circuit stays open
- [ ] Circuit state is per-handler-type (static); resets on success
- [ ] After `BreakDurationSeconds`, next call is "half-open" (if it succeeds, circuit closes)

## Saga Not Progressing

- [ ] Saga class has `[Saga<TState>]` attribute
- [ ] Exactly one method has `[SagaStart]` (PRAG0814 if missing)
- [ ] `[InState]` values match enum members, and every non-terminal state has a handler (PRAG0811 lists the ones that do not)
- [ ] Handler methods return the next action to dispatch (not null)
- [ ] Repository is registered (`EnableSagas()`; for durable EF persistence mark the `[Boundary]` with `[EnableSagaPersistence]`)
- [ ] Correlation ID is consistent across all events in the workflow

## Batch Progress at 0%

- [ ] `EnableBatchProcessing()` called in `UseMessaging()` config (it registers the middleware that counts each item)
- [ ] The items were published by `IBatchDispatcher` with the batch headers (check `context.Headers["batch.id"]`); a message published by hand carries none and is not counted
- [ ] The batch exists in the progress store: the middleware ignores a `batch.id` it does not know
- [ ] Handlers do **not** call `BatchTracker.Report*Async` as well (the item would count twice); to fail a processed item, call `BatchItemOutcome.Fail()`

---

## Diagnostics Reference

### Handler Diagnostics

| ID | Severity | Message | Fix |
|----|----------|---------|-----|
| PRAG0800 | Error | `[MessageHandler]` on non-`IMessageHandler<T>` | Add `IMessageHandler<T>` interface |
| PRAG0801 | Warning | Handler not `partial` | Add `partial` keyword |
| PRAG0802 | Error | `[Retry]` with `MaxAttempts <= 0` | Set positive value |
| PRAG0803 | Error | `[MessageMiddleware]` on non-`IMessageMiddleware` | Add `IMessageMiddleware` interface |

### Saga Diagnostics

| ID | Severity | Message | Fix |
|----|----------|---------|-----|
| PRAG0811 | Info | State has no handler | Expected for terminal states (Completed, Cancelled) |
| PRAG0813 | Error | `[Saga<T>]` where T is not enum | Use enum type parameter |
| PRAG0814 | Error | Saga without `[SagaStart]` | Add `[SagaStart]` to entry method |
| PRAG0820 | Error | Saga event has neither `ICorrelatedMessage` nor `[CorrelationKey]` | Correlate the event so it can reach a saga instance |
| PRAG0821 | Warning | Several `[CorrelationKey]` properties | Keep one; the first ordinal wins |

### Routing and Event Diagnostics

| ID | Severity | Message | Fix |
|----|----------|---------|-----|
| PRAG0816 | Warning | Event type has no consumers | Add handler or remove event |
| PRAG0819 | Warning | Several `[PartitionKey]` properties | Keep one per message type |
| PRAG0822 | Warning | Domain-event cascade cycle | Make a handler idempotent/terminal, or guard the re-raise |

### Outbox, Saga Persistence and Batch Diagnostics

Each of these fires when a boundary opts into an EF-backed capability whose package is missing. The
build succeeds and the attribute does nothing, which is exactly why the diagnostic exists.

| ID | Severity | Message | Fix |
|----|----------|---------|-----|
| PRAG0831 | Warning | `[EnableOutbox]` without `Pragmatic.Messaging.EFCore`: no outbox table, no delivery pump | Add the NuGet reference |
| PRAG0832 | Warning | `[EnableSagaPersistence]` without `Pragmatic.Messaging.EFCore`: saga tables not mapped | Add the NuGet reference |
| PRAG0833 | Warning | `[EnableOutbox]` and `[EnableEventOutbox]` on one boundary: both clear the same events, one silently wins | Keep exactly one |
| PRAG0834 | Warning | `[EnableBatchProgress]` on more than one boundary | Only one may host `__BatchProgress` |
| PRAG0835 | Warning | `[EnableBatchProgress]` without `Pragmatic.Messaging.Batch` | Add the NuGet reference |

---

## FAQ

### How do I test handlers without a real transport?

Use `InMemoryMessageBus` (the default). In tests, resolve `IMessageBus` and publish directly:

```csharp
var bus = serviceProvider.GetRequiredService<IMessageBus>();
await bus.PublishAsync(new OrderCreated { OrderId = Guid.NewGuid() });
```

The handler executes synchronously in the same thread.

### Can I use multiple transports simultaneously?

Yes. Use `[OnBus("analytics")]` on handlers and `msg.AddBus("analytics", bus => bus.UseRabbitMq(...))` in config. Handlers without `[OnBus]` use the default bus.

### How do I replay dead-lettered messages?

Inject `IDeadLetterStore`, call `GetAllAsync()`, deserialize, and re-publish:

```csharp
var deadLetters = await deadLetterStore.GetAllAsync();
foreach (var dl in deadLetters)
{
    var message = JsonSerializer.Deserialize(dl.Payload, Type.GetType(dl.MessageType)!);
    await messageBus.PublishAsync(message!, dl.Context);
}
```

### Is Pragmatic.Messaging AOT-compatible?

The handler pipeline is 100% SG-generated (zero reflection). Serialization uses `System.Text.Json` which requires `JsonSerializerContext` for full AOT. The `IMessageTypeRegistry` switch expression is AOT-safe.

### How do I monitor messaging health?

Register health checks:

```csharp
services.AddHealthChecks()
    .AddCheck<OutboxHealthCheck>("outbox")
    .AddCheck<ChannelTransportHealthCheck>("channels")
    .AddCheck<RabbitMqHealthCheck>("rabbitmq");
```

Use the `Pragmatic.Messaging` ActivitySource and Meter for OpenTelemetry integration.

### Can I use Pragmatic.Messaging without Pragmatic.Composition?

Yes. Call `services.AddPragmaticMessaging(msg => { ... })` directly and add `services.AddPragmaticMessageHandlers()` (SG-generated) for handler registration.

---

## Getting Help

- **Showcase examples**: `examples/showcase/src/Showcase.Billing/Events/Handlers/`
- **GitHub Issues**: [github.com/pragmatic-design/Pragmatic.Design/issues](https://github.com/pragmatic-design/Pragmatic.Design/issues)
