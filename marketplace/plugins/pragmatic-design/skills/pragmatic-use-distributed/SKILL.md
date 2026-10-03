---
name: pragmatic-use-distributed
description: Use when splitting out a service, scaling a host out, adding a gateway, or when instances disagree (stale cache, a job run twice) — [RemoteBoundary], broker, sagas, Agent, YARP gateway. Decide first with pragmatic-architecture.
---

# Pragmatic Use Distributed

**Covers:** Run a Pragmatic app as several services or instances — [RemoteBoundary] calls, broker events and request/reply, cross-service sagas, the Pragmatic Agent, the YARP gateway, discovery, cross-instance cache invalidation.

Stay a modular monolith until something forces the split — independent deployment or scaling of one
area. The modules are already boundaries; splitting changes how they are **wired**, not how they are
written. The examples go one step each: Invoicing is two modules in one host, Casework is two services
over a broker, Warehouse is three services and two instances of one behind a gateway.

## How services talk — choose per interaction

| The caller… | Shape | Mechanism |
|---|---|---|
| needs the answer now and knows who answers | HTTP call to another service's operations | `[RemoteBoundary<TModule>]` on the host |
| needs the answer now, and any instance may answer | request/reply over the broker | `IMessageBus.RequestAsync` + a `[RequestHandler]` |
| tells others something happened and does not wait | an event through the outbox | `[Raises<T>]` / published events + `[MessageHandler]` in the consumer |
| runs a process across services | a saga in the service that owns the process | `pragmatic-use-messaging` |

**Contracts assembly.** What a service publishes lives in `{Service}.Contracts` (events, requests,
replies), referencing only `Pragmatic.Abstractions` and `Pragmatic.Events`; consumers reference that and
nothing of the service. The host of the publisher registers the contracts assembly's generated handler
registry (`{Contracts}.Generated.PragmaticMessageHandlerRegistration.AddPragmaticMessageHandlers(...)`) —
forgetting it is **PRAG1699**.

### `[RemoteBoundary<TModule>]`

On the host, instead of `[Include<TModule, TDatabase>]` (both on one module is **PRAG1685**): the
generator writes HTTP invokers behind the same `I{Boundary}Actions` interface, so calling code does not
change. The base URL is configuration: `Pragmatic:RemoteBoundaries:{Module}:BaseUrl`.

- The remote module's in-process workers (handlers, jobs, sagas) are **not** registered on the caller —
  they run where the module is hosted.
- ⚠️ Only **actions** are invoked across it — there is no remote mutation invoker — and its calls have no
  retry or circuit breaker of their own. A write another service must trigger is an action there, or a
  message.
- ⚠️ A compensator (`[UndoWith]`) behind a remote boundary never runs in the caller: compensation belongs
  to the service that did the work.
- The caller must know the address: that is the reason Warehouse's reservation goes over the broker, where
  both Stock instances consume one queue and nobody needs an address.

### Over the broker

RabbitMQ in production (`msg.UseRabbitMq(...)`), outbox on (`msg.EnableOutbox(...)`) so an event commits
with the change that raised it. Each service consumes on **its own queue** per event, so every service
gets its copy; all instances of one service share it, so each message is handled once per service.
Request/reply: the caller awaits with a timeout (`Messaging:RequestReplyTimeout`) and answers 503 when
nobody replies. A timeout, a responder that threw and a broker that cannot carry the request all arrive
as `RequestReplyException`, so one catch is the whole "no answer" case. The tenant travels on the message and is restored in the consume scope; with
`MultiTenancyOptions.RequireTenant` on, a handler that would write a tenant row with no tenant is
refused instead of writing it onto the shared database.

## Several instances of one service

What breaks the moment there are two:

| Concern | What to do |
|---|---|
| `[Cacheable]` reads | each instance keeps its own copy — add `AddRedisCacheInvalidationBroadcast(redis)` so an invalidation reaches every instance (it carries invalidations, not values) |
| recurring and scheduled jobs | `jobs.UseEfCore()` + `UseEfCorePersistence()` + `[EnableJobPersistence]` on the boundary: the store's lease runs each occurrence once |
| two writers on one row | `[ConcurrencyAware]` on the entity; retry the unit of work on conflict — never last-write-wins by accident |
| configuration and flags changed at runtime | the Agent (below), read through `IOptionsMonitor` / the flag at each request — `IOptions` is read once at startup |
| migrations | two instances starting together is handled by the migration lock; still, run migrations once in the release pipeline for large changes |

## The Pragmatic Agent

A small daemon beside each host (local socket or named pipe; Agents gossip over UDP with an HMAC key):
shared KV, cluster membership, and a control plane.

```csharp
app.UseAgent();   // Pragmatic.Agent.Client; Pragmatic:Agent:SocketPath, AppName, Announce
```

- `config/…` keys become part of the host's `IConfiguration`; `flags/…` back `IFeatureFlagStore`;
  `secret/…` are encrypted at rest (a 32-byte key in `PRAGMATIC_AGENT_SECRET_KEY`, required in Production).
- **Route announcement**: `Pragmatic:Agent:Announce` (`RouteId`, `Path`, `PathRemovePrefix`,
  `RequireAuth`, `Address`) — the host announces itself, the gateway learns the route, and the
  announcement disappears when the host's connection closes: a stopped instance leaves the rotation.
- **Drain**: a command through the control plane takes one instance out of the rotation while it
  finishes what it has in flight; exiting maintenance puts it back. A release without failed requests.
- Unreachable Agent: the host keeps serving with local configuration. It coordinates hosts; it never
  stands between a host and its callers.

## The gateway

YARP-based: routes from the Agent (or static `Gateway:Routes` with `Backends`), round-robin across the
instances of a route, prefix removal, JWT validation at the edge (`Gateway:Jwt`), API keys, CORS, rate
limiting, per-cluster timeout and circuit breaker (`Gateway:Resilience`), maintenance mode.

- ⚠️ **The edge checks who, the service checks what.** The gateway refuses a request without a valid
  token; the service still applies its own permissions to the token it receives. Hosts and gateway share
  key, issuer and audience.
- ⚠️ `X-Tenant-Id` from the client is always stripped; the gateway computes it from the token's tenant
  claim or the subdomain, which is what lets services trust it.

## Topology discovery

`Pragmatic.Discovery` registers what each host hosts (modules, databases, providers — from the
generated metadata) in a shared backend and validates the deployment at startup: the same module on two
hosts with different databases, `[ReadAccess]` that would join across hosts, provider mismatches. Use
`services.UseAgentDiscovery()` (`Pragmatic.Agent.Discovery`) to share it through the Agents; the default
backend is in-memory, one process.

## Limits to know

- ⚠️ **The Agent daemon and the gateway are executables in the repository, not NuGet packages** — an
  application built from packages uses `Pragmatic.Agent.Client`, `Pragmatic.Agent.Discovery` and runs
  the daemon and the gateway built from source.
- `[RemoteBoundary]` resolves its address from configuration, not from discovery, and speaks HTTP only.
- One manifest per generated client — for a separate front end, see `pragmatic-use-client`.

## Testing a distributed shape

Start the real processes — broker, database, each host on its own port, the Agents — as Warehouse's
fixture does; a test double for the broker or the Agent proves nothing about the wiring. Read which
instance answered from a header each host writes, and test the failure paths: one instance stopped,
the broker slow, a request nobody answers.

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Warehouse and Showcase example
applications — code that compiles and that `Warehouse.IntegrationTests` and `Showcase.Billing.Host.Tests`
exercise — and kept identical to it by the gate: a contracts project and a request in it, request/reply
over the broker from both sides, a service's host, the step that says which instance answered, and a
`[RemoteBoundary]`.
