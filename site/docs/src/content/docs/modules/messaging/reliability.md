---
title: "Reliability"
description: "How a message survives failures, end to end. Everything here is generated inline by the"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Messaging/docs/reliability.md
sidebar:
  order: 5
---
How a message survives failures, end to end. Everything here is generated inline by the
source generator (zero reflection) or enforced by the transport — see
[Transports](/modules/messaging/transports/) for the per-broker dead-letter behavior.

## The failure ladder

For a handler decorated with the full set, a failing message climbs this ladder:

1. **In-process retry** — `[Retry]` re-invokes with backoff (process stays up).
2. **Persistent redelivery** — `[Redelivery]` re-schedules through `IMessageScheduler`
   (survives a process restart).
3. **Transport dead-letter** — exhausted: the failure propagates and the transport
   dead-letters (DLX / DLQ topic / native DLQ / dead-letter store).

```csharp
[MessageHandler]
[Retry(MaxAttempts = 3, BaseDelayMs = 200, Strategy = BackoffStrategy.ExponentialWithJitter)]
[Redelivery(MaxAttempts = 3, BaseDelaySeconds = 30)]
[CircuitBreaker(FailureThreshold = 5, BreakDurationSeconds = 30)]
[Timeout(TimeoutSeconds = 20)]
public sealed partial class ChargeCardHandler : IMessageHandler<ChargeCard> { ... }
```

## Persistent redelivery — `[Redelivery]`

When in-process retry is exhausted, the pipeline re-schedules the message with exponential
backoff (`BaseDelaySeconds * 2^n`) through the registered `IMessageScheduler`
(`EnableScheduledMessages()` via Jobs, or the native Azure Service Bus scheduler) instead of
dead-lettering. Semantics that matter:

- The redelivered message keeps its **original MessageId**: sibling handlers that already
  succeeded skip it via their per-handler idempotency claim; only the failed handler re-runs.
- `MessageContext.RetryCount` is the **redelivery counter** — once it reaches `MaxAttempts`
  the failure propagates (→ transport dead-letter).
- Requires **both** an `IMessageScheduler` and `EnableIdempotency()`; without them the
  handler falls back to the normal failure path.

## Honest dispatch outcome

Every handler runs even when a sibling fails (execution isolation), but the dispatch
outcome is honest: `PublishAsync` throws `AggregateException` when ≥ 1 handler failed. This
is what makes the transport dead-letter paths real — an acked-but-failed delivery cannot
exist. On the consume path the per-message idempotency claim is released on failure so the
redelivery is not treated as a duplicate.

## Consumer protection

| Attribute / option | Effect |
|---|---|
| `[ConcurrencyLimit(n)]` | at most n concurrent executions of the handler (per process) |
| `[RateLimit(n, PeriodSeconds = s)]` | at most n executions per window; excess waits for the next window |
| `EnableKillSwitch(o => ...)` | after N consecutive dispatch failures a subscription pauses for a cool-down (half-open), instead of hammering a broken downstream |

## Idempotency (inbox)

`EnableIdempotency()` deduplicates at three levels and now schedules a periodic purge (default:
7-day retention, 6-hour cycle) so the dedup table does not grow without bound. **One store,
three key spaces** — and they are three because the three claims mean different things:

| Level | Key | What it claims |
|---|---|---|
| Outbox delivery | `outbox-publish:{MessageId}` | this row was published |
| Consume side | `{MessageId}` | this delivery was handled |
| Per handler | `{MessageId}:{HandlerFqn}` | this handler ran on it |

⚠️ **The prefix on the first one is not cosmetic.** Publisher and consumer are usually two
processes with two stores, so a shared key space looks harmless — until a service handles a
message **it published itself**, where the delivery comes back to the store that published it.
Under one key space the consume-side claim then finds the publish claim from moments earlier and
drops the message as a duplicate of itself: no error, the outbox row marked processed, the queue
empty, the handler never entered. A store of your own must keep the key it is given
verbatim and never normalise it.

```csharp
msg.EnableIdempotency(o =>
{
    o.Retention = TimeSpan.FromDays(3);
    o.PurgeInterval = TimeSpan.FromHours(1);
});
```

## Outbox

The transactional outbox (see [Concepts](/modules/messaging/concepts/)) is unchanged by this: it retries
DB-side with exponential backoff and dead-letters after `MaxRetries`. With RabbitMQ,
publisher confirms close the outbox→broker window; with the in-memory bus, the honest
dispatch outcome makes the outbox mark failed deliveries as failed.

`EnableOutbox(o => …)` takes five options, and all five reach the services that act on them:

```csharp
msg.EnableOutbox(o =>
{
    o.PollingIntervalSeconds = 2;            // how often the pump sweeps for pending rows
    o.BatchSize = 50;                        // rows per sweep
    o.MaxRetries = 5;                        // before the row is dead-lettered
    o.Retention = TimeSpan.FromDays(7);      // how long a delivered row is kept
    o.PurgeInterval = TimeSpan.FromHours(1); // how often the purge sweeps for expired ones
});
```

⚠️ **Every option has to survive a copy.** They are written onto the builder and copied into the
`MessagingOptions` the purge service resolves; a copy that missed `Retention` would leave an
application that asked for seven days keeping rows for the default three, with nothing thrown and
nothing logged. A test asks `OutboxOptions` for its properties rather than naming them, so an option
added and not copied fails on the day it is written.

## Claim check — large payloads

Brokers have hard payload limits (ASB Standard 256 KB, RabbitMQ practical limits, Kafka
`max.message.bytes`) and large messages hurt broker throughput either way. The claim check
pattern offloads big payloads to blob storage and puts only a reference on the wire:

```csharp
services.AddPragmaticMessaging(msg => msg
    .UseAzureServiceBus(o => o.ConnectionString = cs)
    .EnableClaimCheck(o =>
    {
        o.Threshold = 256 * 1024;          // default: 256 KB
        o.DeleteAfterConsume = true;       // default: FALSE (fan-out safe) — opt in for competing consumers
        o.Retention = TimeSpan.FromDays(7); // declarative: mirror it in the store's lifecycle rule
    }));
```

`EnableClaimCheck` (package `Pragmatic.Messaging.ClaimCheck`) uses the app's registered
`IFileStorage` — local disk, Azure Blob, or S3, whatever Pragmatic.Storage was configured
with — under the `claim-checks` container. Completely transparent end to end:

- **Publish**: a serialized payload above `Threshold` is stored, the message travels as an
  empty stub with the `x-claim-check: {reference}` header. Below threshold nothing changes.
- **Consume**: the binder sees the header, retrieves the payload before deserialization,
  and dispatches as usual. Deletion happens only **after** a successful dispatch — a failed
  handler leaves the blob in place so redeliveries can re-read it.

Failure semantics to know:

- With `DeleteAfterConsume = true` and **multiple subscriptions** on the same topic
  (fan-out), the first consumer to finish deletes the blob and the others fail retrieval.
  Turn it off in fan-out topologies and clean up with storage lifecycle rules instead.
- Blob cleanup is best-effort: a failed delete logs a warning and orphans the blob
  (a storage cost, never a correctness issue).
- The `pragmatic.messaging.claim_checks` counter tracks offloaded payloads.

### Security posture — read before checking sensitive payloads

- **The blob is the complete serialized message.** `[NotLogged]` redaction applies to audit/log
  serialization only; it does **not** strip fields from a claim-checked payload. Any secret or PII
  the message carries is written to the backing store verbatim.
- **Encryption at rest is the storage provider's job** — Azure Blob / S3 server-side encryption, or
  an encrypted volume for local disk. The framework deliberately does not embed key management.
- **Bound the exposure window with `Retention`.** With the fan-out-safe `DeleteAfterConsume = false`
  default nothing deletes blobs on the happy path. `Retention` is *declarative*: `IFileStorage`
  exposes no enumeration, so the framework cannot sweep expired blobs — configure a matching
  lifecycle rule on the store. Keep it longer than the transport's maximum redelivery window.
- **The `x-claim-check` reference is attacker-controllable** (it rides on the wire). The file-storage
  store therefore accepts only the exact `claim-checks/<id>.bin` shape it writes, rejecting path
  traversal, other containers and arbitrary names. A custom `IClaimCheckStore` must validate its
  references the same way.

For a custom backend (Redis, dedicated table), implement `IClaimCheckStore` and register it
before `EnableClaimCheck` — the `TryAdd` registration keeps yours.
