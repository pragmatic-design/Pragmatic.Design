---
title: "Architecture and Core Concepts"
description: "`Pragmatic.Gateway` is the **reverse-proxy edge** for Pragmatic.Design applications. Built on YARP, augmented with Agent-driven dynamic routing, tenant-aware pr"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Gateway/docs/concepts.md
sidebar:
  order: 1
---
`Pragmatic.Gateway` is the **reverse-proxy edge** for Pragmatic.Design applications. Built on YARP, augmented with Agent-driven dynamic routing, tenant-aware proxying, maintenance mode, and per-cluster resilience.

> Status: implemented preview subsystem. Functional inside the repo; operational surface still evolving.

---

## The Problem

A multi-service Pragmatic deployment needs an edge:

- route `/api/booking/*` to the booking hosts, `/api/billing/*` to billing
- present a single public endpoint while services scale and move internally
- fail fast (circuit-break) when a backend is unhealthy, without taking the whole edge down
- support **tenant-aware routing**: different tenants to different backend pools
- handle **maintenance mode**: bleed traffic from hosts before deploy
- offer standard edge features (compression, CORS, JWT auth, rate limiting)

You can deploy nginx / Traefik / Envoy, and for many teams that's fine. But you pay:
- a second configuration language (nginx.conf, Traefik labels) separate from your .NET code
- no compile-time coupling to your topology; config drift is a real thing
- limited access to your Pragmatic runtime types for custom middleware

Pragmatic.Gateway is the .NET-native alternative: same process model as your app hosts, driven by the same `Pragmatic.Agent` operational state, extensible with the full ASP.NET middleware stack.

---

## Why YARP underneath

YARP is a Microsoft-built reverse-proxy toolkit for .NET. It handles:

- request forwarding with keep-alive pooling and HTTP/2
- response streaming without buffering
- load balancing (round-robin, random, least-request, power-of-two)
- SSE and WebSocket upgrades
- header transforms

Pragmatic.Gateway doesn't re-invent these. It uses YARP for the **transport** and adds the layers YARP doesn't cover: dynamic config from the Agent, maintenance middleware, tenant extraction, resilience policies built on `Pragmatic.Resilience`.

---

## Pipeline

```
incoming request
    │
    ▼
┌─────────────────────────────────────┐
│ /health /ready endpoints            │ ← short-circuit for Kubernetes probes
├─────────────────────────────────────┤
│ Response compression                │
├─────────────────────────────────────┤
│ CORS                                │
├─────────────────────────────────────┤
│ Maintenance middleware              │ ← drains if agent.gateway/maintenance=on
├─────────────────────────────────────┤
│ Rate limiting                       │ ← per client / per route
├─────────────────────────────────────┤
│ Authentication + Authorization      │ ← JWT bearer, cookie, anonymous allow
├─────────────────────────────────────┤
│ Tenant routing                      │ ← extract tenant from header / subdomain
├─────────────────────────────────────┤
│ YARP reverse proxy                  │ ← resilience wrappers per cluster
├─────────────────────────────────────┤
│ forward to backend                  │
└─────────────────────────────────────┘
```

Every middleware is opt-in via `GatewayOptions`. A minimal gateway is "just the proxy"; a production one typically enables all of them.

---

## Route sources

The route table can come from two places:

### 1. Static configuration

```json
{
  "Gateway": {
    "HttpUrl": "http://*:8080",
    "Routes": [
      {
        "RouteId": "booking",
        "Path": "/booking/{**catch-all}",
        "Backends": ["http://localhost:5010"]
      }
    ]
  }
}
```

Baked-in, loaded at startup, changes require a restart. Fine for simple deployments.

`Backends` names every instance of the service the route fronts. Several addresses are one cluster
whose destinations are taken in turn (round robin), so a service running twice is two entries, not two
routes. A route with no backend fails the gateway's start, naming the route.

`PathRemovePrefix` removes the prefix the gateway publishes a service under before forwarding the
request (YARP's transform of the same name): `/booking/{**catch-all}` with `"PathRemovePrefix":
"/booking"` forwards `/booking/health` as `/health`, query string kept, so the service does not know
where it is published. The route's `Path` has to start with the prefix, or the start fails naming the
route. A route from the Agent's KV (below) takes the same property; there, a mismatch is logged and that
one route is skipped, because the gateway is already serving.

### 2. Dynamic from `Pragmatic.Agent`

When `AgentSocketPath` is set, the gateway subscribes to `gateway/routes/*` and `gateway/clusters/*` keys in the Agent's KV store. When an operator updates a route via `pragmatic-agent config set gateway/routes/booking ...`, the gateway picks up the change within a gossip round (~seconds) and reconfigures YARP in-place: zero downtime.

### 3. Announced by the instances themselves

A host with `Pragmatic:Agent:Announce` configured tells its Agent the route it serves and the address it serves it on:

```json
"Pragmatic": {
  "Agent": {
    "Announce": {
      "RouteId": "warehouse",
      "Path": "/warehouse/{**catch-all}",
      "PathRemovePrefix": "/warehouse",
      "RequireAuth": true,
      "Address": "http://10.0.0.5:8080"
    }
  }
}
```

The announcement is written under `gateway/instances/{routeId}/{instanceId}` as an **ephemeral** key. The Agent deletes it when the host's connection closes (a stop, a crash, a dispose), and the delete gossips like the write did. The gateway builds one route per route id and one destination per instance, taken in turn. An instance that stops leaves the rotation with nobody removing it, and when the last instance of a route leaves, the route goes too. `Address` is configured, not read from the server: behind a container network the address a host listens on is not the one it is reached at.

Every instance of a service announces the same route. Each writes its own key, so one instance's departure never takes another's entry with it.

**Precedence**: routes an operator wrote under `gateway/routes/` win, then announced routes, then static routes with the same `RouteId`. Static routes act as a bootstrap and fallback.

---

## Resilience: per-cluster, not global

Each backend cluster gets its own resilience policy:

```json
{
  "Clusters": {
    "booking": {
      "Destinations": { "b1": "http://booking-1:80", "b2": "http://booking-2:80" },
      "Resilience": {
        "TimeoutMs": 5000,
        "CircuitBreaker": {
          "FailureThreshold": 10,
          "BreakDurationMs": 30000,
          "MinimumThroughput": 20
        }
      }
    },
    "billing": { ... }
  }
}
```

Why per-cluster: if billing is unhealthy, booking should keep serving. A global circuit-breaker would trip for both.

The policies are the same `Pragmatic.Resilience` primitives you use inside your app, configured via the same shapes, with the same failure counting semantics.

---

## Tenant routing

When `TenantResolver` is enabled, the gateway extracts the tenant **before** forwarding so:

- the downstream service sees a `X-Tenant-Id` header (or configured equivalent)
- routing can use the tenant to pick a different backend cluster (`tenant=acme` → `cluster=acme-pool`)
- auditing captures the tenant on the edge, not 5 hops in

Resolution strategies available:
- **Header** (`X-Tenant-Id`)
- **Subdomain** (`acme.example.com` → `acme`)
- **JWT claim** (`tenant_id` from the bearer token)
- **Route** (prefix like `/t/acme/...`)

Multiple strategies can be composed with a priority order.

---

## Maintenance mode

Agent key `gateway/maintenance` drives a gateway-wide or per-cluster drain:

```bash
pragmatic-agent config set gateway/maintenance "enabled:true,reason:deploy"
```

The maintenance middleware then:
- accepts new requests on health paths (`/health`, `/ready`)
- returns **503 with `Retry-After`** for application routes
- allows in-flight requests to complete (configurable grace period)

To drain **one cluster** (typical before a rolling deploy):

```bash
pragmatic-agent config set gateway/maintenance/booking "enabled:true"
```

Only the `booking` cluster returns 503; other clusters keep serving normally.

---

## Authentication and authorization

The gateway itself handles JWT Bearer authentication: tokens are validated once at the edge rather than at every downstream service.

```json
{
  "Authentication": {
    "JwtBearer": {
      "Authority": "https://auth.example.com",
      "Audience": "api.example.com",
      "RequireHttpsMetadata": true
    }
  }
}
```

Validated principals are forwarded as trusted headers (`X-User-Id`, `X-User-Roles`) to downstream services, configured via YARP transforms.

For fully public routes, set `AllowAnonymous` on the route.

---

## What the gateway does not do

- **Global load balancing across regions**: use a cloud provider's global LB for that
- **L4 TCP proxying**: HTTP/1.1, HTTP/2, HTTP/3 only
- **TLS termination with cert-manager integration**: configure TLS via Kestrel as you would for any ASP.NET app
- **WAF-style request inspection**: deploy a WAF upstream
- **Service mesh sidecar**: this is an edge gateway, not a sidecar

For those, keep your existing tooling (Cloudflare, Istio, Envoy) and point it at the Pragmatic.Gateway as an origin.

---

## Relationship to other modules

- [Pragmatic.Agent](/modules/agent/overview/), optional but important: dynamic route source, maintenance state, tenant map
- [Pragmatic.Resilience](/modules/resilience/overview/): same policies used inside services are applied here
- [Pragmatic.MultiTenancy](/modules/multi-tenancy/overview/): tenant resolvers are shared between gateway and app hosts

---

## Related

- [getting-started.md](/modules/gateway/getting-started/): minimal config, adding the first route
- [common-mistakes.md](/modules/gateway/common-mistakes/)
- [troubleshooting.md](/modules/gateway/troubleshooting/): route / Agent / forwarding issues
