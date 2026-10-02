---
title: "Transports"
description: "Four transports share one abstraction (`IMessageTransport`): pick one with `Use{Transport}()`"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Messaging/docs/transports.md
sidebar:
  order: 8
---
Four transports share one abstraction (`IMessageTransport`): pick one with `Use{Transport}()`
inside `UseMessaging(...)`. Publish is fan-out (topic/exchange), Send is point-to-point
(queue). **On every transport a failed handler cannot lose the message silently** —
that is a construction-level guarantee.

| | Channels | RabbitMQ | Kafka | Azure Service Bus | SQL (Postgres/SqlServer) |
|---|---|---|---|---|
| Scope | in-process | broker | broker (log) | broker (cloud/emulator) | database (no broker) |
| Topology auto-created | n/a | ✅ exchange/queue/binding | ✅ AdminClient (`AutoCreateTopics`) | ✅ management API (graceful pass-through) | ✅ DDL idempotente (`AutoCreateSchema`) |
| Failed handler | → `IDeadLetterStore` | nack → **DLX** (`pragmatic.dlx` default) | → **DLQ topic** (`{topic}.dlq` default) | abandon → redelivery → **native DLQ** | backoff → **`__TransportDeadLetters`** |
| Redelivery (broker) | ❌ | via DLX policy | on rebalance/restart | ✅ up to `MaxDeliveryCount` | ✅ lease scaduto / nack, fino a `MaxDeliveryCount` |
| Ordering | FIFO per channel | per queue | ✅ per partition key | per session/partition key | per-queue best effort (id crescente) |
| Scheduled messages | via Jobs | via Jobs | via Jobs | ✅ **native** (`ScheduleMessageAsync`) | ✅ **native** (`VisibleAt`, cancel restart-safe) |
| Local testing | none needed | Testcontainers | Testcontainers (or Redpanda) | **official emulator** via Testcontainers | Sqlite/Testcontainers |
| Connect at startup | n/a | background; a publish waits (`ConnectWaitTimeout`) | before the host reports started | before the host reports started | background; a publish waits (`ConnectWaitTimeout`) |

**Connecting at startup.** A publish issued as soon as the host has started — a startup step, the
first request, a test — never finds its transport "not connected". Kafka and Service Bus connect
before the host reports started: building their clients opens no connection. RabbitMQ and SQL
connect with I/O, so their consumer service begins the connect at startup without waiting for it
— the application starts with its broker or database down — and a publish, send or subscribe
issued meanwhile waits for it, failing only if the connect fails or `ConnectWaitTimeout`
(default 30 s) passes.

## RabbitMQ

```csharp
app.UseMessaging(m => m.UseRabbitMq(o =>
{
    o.ConnectionString = "amqp://guest:guest@localhost:5672";
    o.QueueType = "quorum";          // replicated queues (production clusters); default "classic"
    // o.DeadLetterExchange = null;  // opt OUT of the default DLX — failed messages get DROPPED
}));
```

- **Topology**: one durable topic exchange per boundary (`{boundary}.events`), one durable
  queue per handler, declared idempotently at startup/first use. The compile-time picture is
  in the generated `_Infra.Messaging.Topology.g.cs`.
- **Dead-lettering is ON by default**: queues are declared with
  `x-dead-letter-exchange: pragmatic.dlx`; the DLX and a durable `pragmatic.dlx.dlq` are
  auto-provisioned. Setting `DeadLetterExchange = null` reverts to discard-on-failure (loud
  warning at subscribe time).
- **Publisher confirms are ON by default** (`PublisherConfirms`): a publish completes only
  after the broker confirms it — the outbox cannot silently lose messages between DB and broker.
- Queue arguments are immutable on RabbitMQ: changing `QueueType`/DLX on existing queues
  requires deleting and re-declaring them.

## Kafka

```csharp
app.UseMessaging(m => m.UseKafka(o =>
{
    o.BootstrapServers = "localhost:9092";
    o.DefaultPartitions = 6;         // for auto-created topics (default 3)
    // o.AutoCreateTopics = false;   // pre-provisioned topics (IaC)
}));
```

- **Topology**: topics are created explicitly via AdminClient before first use (publish,
  subscribe, dead-letter) — works where `auto.create.topics.enable` is off.
- **Dead-letter topic is ON by default**: a failed handler's message is published to
  `{topic}.dlq` (with failure headers) and its offset committed. Without it, the failed
  offset would be implicitly committed by the next successful message on the partition —
  silent loss.
- **Ordering**: the message key decides the partition. Precedence:
  `[PartitionKey]` property (SG-resolved, `x-partition-key` header) → `CorrelationId` →
  `MessageId`. Write it wherever the key already is — a positional parameter or a body property:

```csharp
public sealed record StockMoved(
    [property: PartitionKey] Guid ProductId,       // per-product ordering
    int Delta);
```

  The resolver is generated in the assembly that **declares** the message, which is the one the
  publisher references — a contract in its own project works, and so does the positional form. ⚠️ A
  resolver collected from syntax, or generated by a scan in the consumer, would produce nothing at all
  for a contracts assembly.

## Azure Service Bus

```csharp
app.UseMessaging(m => m.UseAzureServiceBus(o =>
{
    o.ConnectionString = "<namespace or emulator connection string>";
    o.MaxDeliveryCount = 5;          // then ASB dead-letters natively
}));
```

- **Topology**: queues/topics/subscriptions are created via the management API. Where
  management is unavailable — the **local emulator** (entities come from its `Config.json`)
  or an IaC-provisioned namespace with a restricted SAS — the transport warns once and works
  against the pre-provisioned entities.
- **Failed handlers are abandoned**: the broker redelivers with a growing `DeliveryCount`
  (mapped into `MessageContext.RetryCount`) and dead-letters natively after
  `MaxDeliveryCount`. Nothing to configure, nothing to lose.
- **Scheduled messages are broker-native**: `UseAzureServiceBus` registers an
  `IMessageScheduler` backed by `ScheduleMessageAsync` — no database polling.
- `[PartitionKey]` maps to the native message `PartitionKey` (sessions/partitioned entities).
- v1 limitation: point-to-point queues can be **sent to** but the subscription binder
  consumes **topics** — consume queues with a raw `ServiceBusReceiver` if needed.

### Local emulator

The official emulator runs in Docker (AMQP only, no management API, entities from
Config.json). With Testcontainers:

```csharp
var container = new ServiceBusBuilder()
    .WithAcceptLicenseAgreement(true)
    .WithResourceMapping(configJsonBytes, "/ServiceBus_Emulator/ConfigFiles/Config.json")
    .Build();
```

See `AzureServiceBusContainerFixture` in the Messaging test suite for a complete example
(pre-provisioned topic + subscription + queue, `AutoCreateEntities = false`).

## SQL (PostgreSQL / SQL Server)

`UseSqlTransport()` (package `Pragmatic.Messaging.Sql`) — durable queues on THREE tables
(`__TransportMessages`, `__TransportSubscriptions`, `__TransportDeadLetters`), no broker to
operate. The provider comes from the options:

```csharp
msg.UseSqlTransport(o =>
{
    o.ConfigureDbContext = db => db.UseNpgsql(connectionString);   // or UseSqlServer(...)
    o.AutoCreateSchema = true;        // idempotent DDL under advisory lock (default)
    o.PollingInterval = TimeSpan.FromSeconds(1);   // adaptive up to MaxPollingInterval
    o.LockDuration = TimeSpan.FromMinutes(5);      // lease per competing consumers
    o.MaxDeliveryCount = 10;          // then transactional move to __TransportDeadLetters
    o.UseNotifications = true;        // PostgreSQL: pg_notify wakeups (28-80ms latency)
});
```

Semantics:

- **Publish = fan-out at INSERT**: subscriptions are DURABLE rows (upsert on subscribe,
  dispose does NOT remove — ASB semantics); publish resolves subscribers (cached, TTL 30s)
  and inserts one row per subscription in one transaction. Send = single row on the queue.
- **Competing consumers via lease**: 3-phase portable CAS claim (select ids → CAS update
  with token → re-select); `DeliveryCount` increments AT CLAIM so a crash mid-handler
  consumes an attempt (no poison loop). Ack = DELETE guarded by token; nack = exponential
  backoff on `VisibleAt` (2^n, cap 30min). Expired leases redeliver via the claim predicate.
- **Latency**: adaptive polling (1s → 10s when idle, drain on full batch). On PostgreSQL,
  `pg_notify` in the SAME transaction as the INSERT wakes consumers cross-connection at
  commit — polling stays on as safety net. SQL Server: polling only.
- **Scheduler nativo**: `VisibleAt = scheduledAt` with a `SchedulingTokenId`; `CancelAsync`
  is a DELETE by token — **durable and restart-safe** (unlike ASB without the handle store).
- Co-locate the tables in your own DbContext with
  `SqlTransportDbContext.ApplyTransportConfigurations(modelBuilder)` (Pragmatic.Migrations).

Operational notes: DELETE-heavy tables benefit from autovacuum tuning on PostgreSQL;
handler slower than `LockDuration` can cause duplicate delivery (no lease auto-renew — keep
handlers idempotent); ordering under concurrency is best-effort like every other transport.

## Channels (in-process)

`UseChannels()` — bounded `System.Threading.Channels` with backpressure (`Capacity`,
`FullMode`, `ConsumerCount`). Failed handlers are persisted to the registered
`IDeadLetterStore`. The full transport path (serialize → route → consume in a fresh DI
scope) runs in-process: ideal for modular monoliths and for tests that need transport
semantics without a broker.

**A channel per subscription, not per address**, so the two addressing modes mean what they mean:

| | |
|---|---|
| `PublishAsync` (topic) | **every** subscription of that topic gets a copy |
| `SendAsync` (queue) | **one** consumer, round-robin among the address's subscriptions |
| `ConsumerCount` | N reader tasks for **one** subscription, competing for its own messages |

⚠️ **A publish nobody subscribes to is discarded**, as a topic exchange with no bound queue discards it.
Publishing before a consumer has bound therefore loses the message. `ChannelConsumerService` binds every
subscription inside `StartAsync`, so once the host has started — or a test has awaited the service's
`StartAsync` — a publish finds its subscribers; nothing needs to wait or sleep. `Capacity` is per **subscription**, so N subscribers to one topic hold up to
N × `Capacity`, and with `FullMode = Wait` one slow subscriber blocks the publisher for that address.

⚠️ Why not **one** channel per topic, shared by every subscription: `System.Threading.Channels` readers
compete, so a published message would reach exactly one subscriber. Two subscribers to one topic would be
competing consumers, and the one that received a message of a type it does not handle would **drop** it —
`TransportSubscriptionBinder` acknowledges what belongs to "another subscription, already on its way
there", which is true only where a topic fans out. Nothing logged, nothing dead-lettered.
