# Pragmatic.Discovery

Runtime topology registration and discovery for Pragmatic.Design hosts.

## The Problem

A modular monolith splits logic into modules, each with its own database. In a single host, the
Composition generator validates the whole topology at compile time. But split modules across multiple
hosts and that validation disappears: you can't know at build time whether two hosts deploy the same
module, whether `ReadAccess` spans hosts (breaking SQL joins), or whether DB providers mismatch.
External registries (Consul, etcd) know "Host B is alive at :5002" — not "Host B owns BillingModule on
BillingDb with PostgreSQL." So you maintain a separate config that drifts from the code.

## The Solution

Pragmatic.Discovery reads the SG-emitted topology metadata that already lives in every compiled host
assembly, stores it in a shared backend, and validates the deployment against the other hosts —
automatically, at startup. No manual config, no drifting registry; the metadata travels with the
assembly.

```
Host A (Booking + Catalog) ── register ──▶ discovery backend
Host B (Billing)           ── register ──▶ discovery backend
Host A  ── "which host owns Billing?" ──▶  backend ──▶ call Host B over HTTP
```

## Installation

```bash
dotnet add package Pragmatic.Discovery
```

`IDiscoveryService` is the high-level API (register / query / validate); `IDiscoveryBackend` is the
pluggable store. Two exist: `InMemoryDiscoveryBackend` (the default, one process) and
`AgentDiscoveryBackend`, in `Pragmatic.Agent.Discovery` (`services.UseAgentDiscovery()`), which shares
the topology between hosts through the [Pragmatic Agent](../Pragmatic.Agent/README.md). There is no Redis or Consul backend; a
store of your own implements `IDiscoveryBackend`. Validation runs at startup and flags duplicate
modules, cross-host `ReadAccess`, and provider mismatches.

## Status

**Functional** within 1.0.0-alpha — topology registration, querying, validation, and the in-memory and
Agent backends. See the [roadmap](../docs/ROADMAP.md).

## Documentation

| Guide | What you'll learn |
|-------|-------------------|
| [Concepts](docs/concepts.md) | Topology metadata, the backend model, register/query/validate |
| [Getting Started](docs/getting-started.md) | Register a host and query the topology |
| [Topology Validation](docs/topology-validation.md) | Cross-host checks: duplicate modules, ReadAccess, provider mismatch |
| [Common Mistakes](docs/common-mistakes.md) | The most frequent discovery pitfalls |
| [Troubleshooting](docs/troubleshooting.md) | Problem/solution guide |

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](../README.md) ecosystem — see [Licensing](../docs/LICENSING.md).
Pragmatic.Discovery is **MIT-licensed**.
