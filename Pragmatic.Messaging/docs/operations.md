# Operations

Running messaging in production: the ops dashboard, dead-letter replay, and the
observability surface.

## Dashboard (`Pragmatic.Messaging.Dashboard`)

```csharp
msg.EnableDashboard(o =>
{
    o.Path = "/_messaging";          // default
    o.ApiKey = configuration["Messaging:DashboardKey"];  // null = localhost-only
    o.ApiKeys["ci"] = configuration["Messaging:CiKey"];  // optional: extra labelled keys
    o.MaxItems = 100;                // page size of list endpoints
});
```

Auth mirrors the maintenance panel: with a key configured, every request must carry
`X-Messaging-Key` (compared constant-time). You can issue **multiple labelled keys** via
`ApiKeys` (`label → key`) alongside (or instead of) the single `ApiKey` — any accepted key
authenticates, and the matching label is logged on replay/delete so an operation is
attributable. Rotate one key without touching the others.

Without any key the endpoints answer **loopback only**. In that mode mutating calls
(replay/delete) must additionally carry the non-simple `X-Messaging-Csrf` header (the panel
sends it automatically): being non-simple it forces a CORS preflight that same-origin policy
blocks, defeating a drive-by cross-site POST to `localhost`. In keyed mode the
`X-Messaging-Key` header already forces that preflight. All routes are excluded from OpenAPI.

The `status` counter and the `outbox` list are **read-only**: they use the outbox source's
`InspectPendingAsync`/`PeekPendingAsync`, which never lease rows — so an open, auto-refreshing
dashboard cannot starve the delivery pump (the delivery worker's `GetPendingAsync` claims a
5-minute lease; the dashboard must not).

| Endpoint | What it returns |
|----------|-----------------|
| `GET {path}/status` | transport name + status, dead-letter / outbox-pending / active-saga counters |
| `GET {path}/outbox?boundary=` | pending outbox messages per boundary (retry count, next attempt, last error) |
| `GET {path}/dead-letters` | dead-lettered messages, newest first |
| `POST {path}/dead-letters/{id}/replay` | re-publishes through the bus (same MessageId, fresh retry budget), then removes |
| `DELETE {path}/dead-letters/{id}` | drops a dead letter |
| `GET {path}/sagas` | active instances per saga type (from the SG-generated `SagaDescriptor` registry) |
| `GET {path}/audit?type=&direction=&from=&to=` | audit trail query (requires `EnableAuditing`) |
| `GET {path}/panel` | embedded HTML panel (auto-refresh, replay/delete buttons) |

### Replay semantics

Replay resolves the payload back to a typed message via the SG-generated
`IMessageTypeRegistry` (an AOT-safe switch per module assembly — no reflection) and
publishes through the ACTIVE transport with the ORIGINAL MessageId: idempotency stores
never marked the id as processed (the dispatch failed), so dedup does not swallow the
replay, while handlers that already succeeded on a fan-out still dedupe correctly.
A type not in any registry returns 422 and the dead letter stays.

Replay is **at-least-once**: it re-publishes and then removes the dead letter in two steps,
not one transaction, so a crash between them can leave the message both re-published and
still in the store — a second replay would publish it again. Enable consumer idempotency
(`EnableIdempotency`) so the preserved MessageId lets a duplicate be deduped.

### Tenant isolation & the dashboard

In a multi-tenant host, messaging propagates the tenant end to end: the bus stamps the
ambient tenant onto every published message, and the consumer restores it before handlers
run (background delivery has no ambient tenant otherwise). Saga rows are tenant-owned —
stamped on create, filtered on read by a fail-closed EF query filter, and keyed on the
tenant in the active-saga unique index so two tenants can run the same
`(SagaType, CorrelationId)` — and claim-check blobs are stored under a per-tenant path and
rejected on retrieve if the reference's tenant does not match the caller.

**The dashboard is an operator/admin surface and is deliberately cross-tenant**: it reports
sagas, dead letters, outbox and audit across all tenants (its background reads bypass the
tenant filter). Protect it with `ApiKey` (or loopback) — it is not a per-tenant view and
must not be exposed to tenant users.

## Counters

All counters live on the `Pragmatic.Messaging` meter:

| Instrument | Meaning |
|------------|---------|
| `pragmatic.messaging.messages_published` | messages published |
| `pragmatic.messaging.handler_failures` | handler exceptions |
| `pragmatic.messaging.handler_duration` | handler latency histogram |
| `pragmatic.messaging.dead_lettered` | messages moved to dead letter |
| `pragmatic.messaging.retry_attempts` | retry attempts |
| `pragmatic.messaging.circuit_breaker_trips` | circuit breaker opens |
| `pragmatic.messaging.idempotency_duplicates` | duplicates skipped |
| `pragmatic.messaging.claim_checks` | payloads offloaded to the claim check store |

## Health

Every transport registers a health check (`AddHealthChecks()` integration) and a
`HealthContributor`; the SQL transport also verifies schema reachability.
