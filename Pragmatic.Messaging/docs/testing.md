# Testing

## MessageBusTestHarness: two modes

**Record-only** (default): replaces `IMessageBus`, records everything, dispatches nothing.

```csharp
var harness = services.AddMessagingTestHarness();

await sut.PlaceOrderAsync(...);          // code under test publishes

harness.HasPublished<OrderPlaced>(o => o.Total == 99m).Should().BeTrue();
harness.SentOf<ChargeCard>().Should().ContainSingle();   // point-to-point tracked separately
```

**Dispatching**: additionally delivers to the registered `IMessageHandler<T>` implementations
and tracks per-handler outcomes. Handler failures are **recorded, not rethrown**: assert on
`Faulted` instead of catching:

```csharp
services.AddDispatchingTestHarness();
await using var sp = services.BuildServiceProvider();
var harness = sp.GetRequiredService<MessageBusTestHarness>();

await harness.PublishAsync(new OrderPlaced(id, 99m));

harness.HasConsumed<OrderPlaced>().Should().BeTrue();
harness.Consumed.Single().HandlerType.Should().Be(typeof(InvoiceHandler));
harness.Faulted.Should().BeEmpty();
```

In dispatching mode `RequestAsync` resolves the real `IRequestHandler<TReq,TRes>`, and
untyped dispatch routes through the SG dispatch tables (AOT-parity with production).

| Recorded | API |
|---|---|
| Published (fan-out) | `Published`, `PublishedOf<T>()`, `HasPublished<T>(…)` |
| Sent (point-to-point) | `Sent`, `SentOf<T>()`, `HasSent<T>()` |
| Consumed (per handler) | `Consumed`, `ConsumedOf<T>()`, `HasConsumed<T>(…)` |
| Faulted (per handler) | `Faulted`, `FaultedOf<T>()`, `HasFaulted<T>()` |

## Broker integration tests: Testcontainers

The Messaging test suite runs its RabbitMQ/Kafka/Azure Service Bus integration tests against
real brokers started by Testcontainers (collection fixtures), so the transport paths execute
in CI; when Docker itself is unavailable they degrade to skip. Reuse the fixtures as
patterns:

| Fixture | Starts | Notes |
|---|---|---|
| `RabbitMqContainerFixture` | rabbitmq | `GetConnectionString()` |
| `KafkaContainerFixture` | kafka | `GetBootstrapAddress()`; AdminClient topology means no pre-provisioning |
| `AzureServiceBusContainerFixture` | official ASB emulator + SQL companion | entities pre-provisioned via `Config.json` (`AutoCreateEntities = false`) |

```csharp
[Collection("RabbitMqBroker")]
public class MyBrokerTests(RabbitMqContainerFixture broker) { ... }
```

## What to test where

- **Handler logic** → plain unit test on the handler class (no harness needed).
- **"Did my code publish the right message?"** → record-only harness.
- **Handler wiring + fan-out + failures** → dispatching harness.
- **Retry/redelivery/circuit breaker** → construct the SG-generated `Handler.Pipeline`
  directly (see `ReliabilityPipelineSample`); the pipeline is real generated code.
- **Transport semantics (DLQ, ordering, redelivery)** → Testcontainers fixtures.
