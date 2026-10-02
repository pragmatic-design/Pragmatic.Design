---
title: "Pragmatic.Gateway"
description: "YARP-based API gateway for Pragmatic.Design applications, with Agent-driven routing, maintenance mode, tenant-aware proxying, and per-cluster resilience."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Gateway/README.md
sidebar:
  order: 0
  label: Overview
---
YARP-based API gateway for Pragmatic.Design applications, with Agent-driven routing, maintenance mode, tenant-aware proxying, and per-cluster resilience.

## What It Does

- Reverse proxy based on YARP
- Dynamic routes and clusters loaded from `Pragmatic.Agent`, including routes the running instances announce themselves (an instance that stops leaves the rotation — see [concepts](/modules/gateway/concepts/#3-announced-by-the-instances-themselves))
- Static route fallback from configuration
- Maintenance middleware and graceful drain behavior
- Tenant-aware request routing
- JWT authentication, CORS, response compression, and rate limiting
- Per-cluster timeout and circuit-breaker policies via `Pragmatic.Resilience`

## Quick Start

### 1. Configure the gateway

```json
{
  "Gateway": {
    "HttpUrl": "http://*:8080",
    "AgentSocketPath": "/var/run/pragmatic/agent.sock",
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

`Backends` lists every instance of the service behind the route: several addresses are one cluster,
taken in turn (round robin). A route with no backend fails the start, naming the route.
`PathRemovePrefix` (e.g. `"/booking"`) removes the prefix before forwarding, so `/booking/health` reaches
the service as `/health` and the service does not know where the gateway publishes it.

### 2. Run it

```bash
dotnet run --project Pragmatic.Gateway/src/Pragmatic.Gateway/Pragmatic.Gateway.csproj
```

Useful overrides:

```bash
dotnet run --project Pragmatic.Gateway/src/Pragmatic.Gateway/Pragmatic.Gateway.csproj -- --listen http://*:8081 --agent-socket /tmp/pragmatic/agent.sock
```

### 3. Add resilience

```json
{
  "Gateway": {
    "Resilience": {
      "Default": {
        "Timeout": "00:00:10",
        "FailureThreshold": 5,
        "BreakDuration": "00:00:30"
      },
      "Clusters": {
        "booking": { "Timeout": "00:00:05", "FailureThreshold": 3 }
      }
    }
  }
}
```

Resilience is per-cluster: `Default` applies to every backend, and `Clusters` overrides
individual clusters by YARP cluster ID. Each policy maps to `ClusterResiliencePolicy`
(`Timeout`, `CircuitBreakerEnabled`, `FailureThreshold`, `BreakDuration`,
`FailureStatusCodeMin`/`Max`).

### Circuit-breaker behavior

`ProxyResilienceMiddleware` runs inside the YARP proxy pipeline and maintains a circuit per
cluster (state in `ICircuitBreakerStateStore`, in-memory by default):

- **Closed** — requests flow through. A response whose status falls in the failure range
  (default `500–599`), a timeout, or a backend connection error is counted as a failure.
  Any non-failure response resets the failure count. Once consecutive failures reach
  `FailureThreshold`, the circuit transitions to **Open**.
- **Open** — requests are rejected immediately (fast-fail) with `503 Service Unavailable`,
  a `Retry-After` header, and a JSON body `{"error":"circuit_open",...}`. The backend is not
  contacted. The circuit stays open for `BreakDuration`.
- **Half-open** — after `BreakDuration` elapses, the next request is allowed through as a
  probe. If it succeeds the circuit returns to **Closed**; if it fails the circuit re-opens
  for another `BreakDuration`.

Timeouts are enforced per request via a linked cancellation token; a timed-out request
returns `504 Gateway Timeout` and counts as a failure. Backend connection errors return
`502 Bad Gateway`. Set `CircuitBreakerEnabled = false` on a policy to keep timeouts but
disable the breaker, or `Enabled = false` at the root to bypass resilience entirely.

## Relationship to Pragmatic.Agent

The gateway can run with only static routes, but it becomes much more useful when connected to the Agent:

- route and cluster definitions can be reloaded from Agent KV state
- maintenance state can be coordinated instead of being purely local
- gateway configuration can participate in the broader Pragmatic operational model

If the Agent is unavailable, the gateway continues with its configured static routes.

## Authentication

### JWT

Set `Gateway:Jwt` to enable JWT bearer authentication. The gateway always validates the token
signature; issuer and audience are validated when configured (a startup warning is logged if
either is absent, since leaving them open allows token reuse/forgery across issuers). A
`SigningKey` enables symmetric validation; a `JwksUrl` enables JWKS-based validation.

### API key

`Gateway:ApiKey` binds to `ApiKeyOptions` (`HeaderName`, default `X-Api-Key`; and `ValidKeys`):

```json
{
  "Gateway": {
    "ApiKey": {
      "HeaderName": "X-Api-Key",
      "ValidKeys": ["${API_KEY_1}", "${API_KEY_2}"]
    }
  }
}
```

Load `ValidKeys` from a secrets manager or environment variables — never commit raw keys to
`appsettings.json`.

When `Gateway:ApiKey` is configured with at least one `ValidKeys` entry, `ApiKeyMiddleware` runs in the
pipeline (after rate limiting, before auth) and rejects any request whose `HeaderName` header is missing or
not a configured key with **401 Unauthorized**. Keys are compared in constant time. If `ApiKey` is omitted
(or `ValidKeys` is empty), the gate is a no-op and only JWT authentication applies.

## CORS

Set `Gateway:Cors` to enable a default CORS policy (`CorsOptions`):

```json
{
  "Gateway": {
    "Cors": {
      "Origins": ["https://app.example.com"],
      "Methods": ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"],
      "Headers": ["*"],
      "AllowCredentials": true
    }
  }
}
```

`Origins: ["*"]` allows any origin only when `AllowCredentials` is `false`. Combining a
wildcard origin with `AllowCredentials: true` is invalid (the browser refuses it), so the
gateway fails fast at startup with an actionable message instead of an opaque ASP.NET error.

## Rate limiting

Set `Gateway:RateLimit` to enable a global fixed-window limiter (`RateLimitOptions`). Requests
over the limit get `429 Too Many Requests`:

```json
{
  "Gateway": {
    "RateLimit": {
      "PermitLimit": 1000,
      "Window": "00:01:00",
      "KeyStrategy": "ip"
    }
  }
}
```

`PermitLimit` requests are allowed per `Window`; `KeyStrategy: "ip"` partitions the limit by
client IP.

## Tenant Routing and Header Security

`TenantRoutingMiddleware` runs after authentication and before the proxy. It resolves the
tenant from (1) the authenticated `tenant_id` claim, then (2) the request subdomain
(`acme.myapp.com` → `acme`, excluding reserved infrastructure subdomains such as `www`, `api`,
`admin`), and forwards it to backends as the `X-Tenant-Id` header.

Crucially, the middleware **always strips any client-supplied `X-Tenant-Id` header first**,
before resolution. The gateway is the authoritative source of this header so downstream
services can trust it. If the client value were honored, a user authenticated for tenant A
could read or write tenant B's data simply by setting `X-Tenant-Id: B` — a cross-tenant
isolation breach. Only the gateway-computed value (from the signed claim or DNS-controlled
subdomain) is ever forwarded.

## Key Types

| Type | Purpose |
|------|---------|
| `GatewayOptions` | Full runtime configuration for listen URLs, auth, routes, maintenance, and resilience |
| `AgentRouteProvider` | YARP config provider backed by Agent KV plus static fallback |
| `MaintenanceMiddleware` | Stops normal request flow when maintenance is active |
| `TenantRoutingMiddleware` | Applies tenant-aware routing decisions before proxying |
| `ProxyResilienceMiddleware` | Timeout and circuit-breaker protection per backend cluster |

## Current Scope

Implemented today:

- standalone gateway executable
- Agent-backed and static route loading
- maintenance mode
- health endpoint
- response compression
- JWT auth and CORS
- global rate limiting
- per-cluster resilience

Still evolving:

- broader deployment UX and packaging guidance
- deeper control-plane tooling around route authoring and rollout

## Status

**Preview** within 1.0.0-alpha — the gateway is a standalone executable run from this repository, used by
the Warehouse reference application; it is not published as a NuGet package (`IsPackable` is false). See
the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

## Documentation

Local docs:

- [Concepts](/modules/gateway/concepts/)
- [Getting Started](/modules/gateway/getting-started/)
- [Common Mistakes](/modules/gateway/common-mistakes/)
- [Troubleshooting](/modules/gateway/troubleshooting/)

## Requirements

- .NET 10.0+
- A running [Pragmatic Agent](/modules/agent/overview/) for dynamic routes (optional: static routes
  work without it)

## License

Part of the [Pragmatic.Design](/modules/gateway/overview/) ecosystem — see [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.Gateway is licensed under the **PolyForm Small Business 1.0.0** license (free for small businesses; commercial license above the threshold).
