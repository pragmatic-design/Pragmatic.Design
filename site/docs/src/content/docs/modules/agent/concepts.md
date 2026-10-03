---
title: "Architecture and Core Concepts"
description: "`Pragmatic.Agent` is the **local coordination substrate** for Pragmatic.Design hosts. It's a small sidecar process that provides a shared KV store, cluster memb"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Agent/docs/concepts.md
sidebar:
  order: 1
---
`Pragmatic.Agent` is the **local coordination substrate** for Pragmatic.Design hosts. It's a small sidecar process that provides a shared KV store, cluster membership, and a stable IPC surface, so individual app processes don't need to talk to a centralised service for routine operational concerns.

> Status: implemented preview subsystem. Already used inside the repo; packaging and operational surface are still evolving.

---

## The Problem

A multi-host Pragmatic application needs a place to keep things like:

- **Configuration snapshots**: what's the current `booking-service` feature flag state?
- **Tenant → backend map**: which hosts serve tenant `acme-eu`?
- **Cluster membership**: which hosts are alive right now?
- **Control-plane commands**: "please drain traffic from host-3"

You can centralise this on Redis / etcd / Consul / a custom SignalR hub, and Pragmatic has adapters for all of those. But you pay:

- a network hop for every read
- single-point-of-failure risk if the central store is down
- latency variance for operational hot paths (feature-flag evaluation)
- secrets + TLS management for every outbound call

The Agent solves the **local half** of this problem: every host has a local daemon that apps talk to over a Unix socket / Windows named pipe. The daemon gossips with peer Agents, persists state locally, and serves reads in microseconds.

---

## Three packages

| Package | Role |
|---------|------|
| `Pragmatic.Agent` | The daemon executable + CLI (`pragmatic-agent start`, `pragmatic-agent status`, …) |
| `Pragmatic.Agent.Client` | Runtime integration consumed by Pragmatic hosts (`builder.UseAgent()`) |
| `Pragmatic.Agent.Protocol` | Wire protocol: frames, payloads, JSON serialisation |

Hosts reference `Pragmatic.Agent.Client`. Operators run `Pragmatic.Agent` as a daemon. `Pragmatic.Agent.Protocol` is an implementation detail pulled in transitively.

---

## What `UseAgent()` replaces

The default implementations of these stores are in-memory / file-based; `UseAgent()` swaps them to Agent-backed implementations:

| Interface | Default | Agent-backed |
|-----------|---------|--------------|
| `IControlPlane` | No-op | `AgentControlPlane` (heartbeat, commands) |
| `IConfigurationStore` | `appsettings.json` | Agent KV (`config/{key}`) |
| `IFeatureFlagStore` | In-memory defaults | Agent KV (`flags/{flag}`) |
| `ITenantStore` | Static | Agent KV (`tenants/{tenant-id}`) |

`UseAgent()` also adds the Agent-backed store to the host's configuration, so a value written under `config/{Section}:{Key}` binds to a `[Configuration]` class like an appsettings value, and wins over the sources registered before `UseAgent()` (appsettings, environment variables, command line). A value written before the host connected is loaded when it registers. A change made while it runs reaches `IOptionsMonitor<T>` within a gossip round, and a reader that must see runtime changes injects `IOptionsMonitor<T>` rather than `IOptions<T>`, which is read once.

The Agent-backed versions fall back to **graceful degradation**: if the Agent is unavailable, reads return defaults (or the last known value from a local cache), writes are queued and retried. Your app keeps serving rather than crashing on a socket error.

---

## Storage model

The daemon persists state in a **file-backed KV store** under a data directory:

- `~/.pragmatic/agent/` on desktop Linux / macOS
- `%LOCALAPPDATA%\Pragmatic\Agent\` on Windows
- `/var/lib/pragmatic/agent/` or `$DATA_DIR` in containers and services
- Multiple instances live in sibling directories (`--instance=staging` → `agent-staging/`)

The KV layout is hierarchical:

```
config/
  booking-service/
    retry-max-attempts → "3"
    timeout-ms → "5000"
flags/
  new-checkout-flow → "enabled:true,rollout:50"
tenants/
  acme-eu → "{ 'connectionString': '...', 'region': 'eu-west-1' }"
gateway/
  routes/
    booking → "{ 'backends': ['host-1', 'host-2'] }"
```

Apps read with `AgentClient.GetAsync("config/booking-service/retry-max-attempts")`; the daemon serves from RAM (state is fully loaded at startup) and persists writes to disk.

---

## Cluster model: SWIM gossip

Multiple Agents form a cluster through **SWIM-based gossip**:

- membership: each agent maintains a view of live/suspect/dead peers
- failure detection: periodic indirect pings; an unreachable peer is marked suspect then dead after a grace period
- dissemination: KV changes piggyback on gossip messages, propagating through the cluster in O(log N) rounds
- piggybacking: the same gossip packets carry state updates plus health info, minimising traffic
- anti-entropy: every 2 s each Agent pushes its whole KV state to one random peer and pulls that peer's back. Piggybacking is the fast path, but it spends a fixed budget per update and can die out before it reaches every Agent; the exchange guarantees a missed update does not stay missed. An Agent that joins receives the whole state at once.
- tombstones: a delete leaves the version it happened at (kept 10 minutes), so a write an Agent pushes back after missing the delete is refused instead of resurrecting the key

An Agent joins with `--peers host:port`; the peer learns it from the datagram it sends, so an Agent bound to `0.0.0.0` does not need to know its own address. Replication needs the same `--gossip-key` on every Agent: without one, an Agent applies nothing it hears.

A client can write a key as **ephemeral** (`AgentConnection.KvSetEphemeralAsync`): the Agent deletes it when that client disconnects. It is for what describes a running process and must not outlive it, such as the route and address an instance announces to the gateway. The roster entry an instance registers behaves the same way.

The key also dies with the Agent that holds the client. The write records that Agent's id, and the id gossips with the key. When the membership declares an Agent dead, every other Agent deletes the keys it owned, so an instance whose machine is lost leaves the rotation within the suspect timeout (5 s by default) plus a gossip round. An Agent that restarts drops the ephemeral keys it had saved to disk, because no client of the previous process is connected to it. ⚠️ A partition can declare dead an Agent that is still running. Its clients' keys are then deleted, and an instance announces again only when its rotation changes.

This gives you cluster-wide propagation of config / flag / tenant changes **without a central store**. Each write lands in one Agent; within a few gossip rounds (~seconds on LAN), every Agent has the new value.

SWIM was chosen because it scales, tolerates partitions, and needs no leader. The tradeoff: state convergence is eventually consistent; a reader in a different host might see stale values for a few seconds.

For soft, cluster-wide singleton work (advisory leader election), the Agent exposes `IClusterLeadership`, a KV-lease that any host can steal once it expires (eventually consistent, not linearisable). For a **hard** mutual-exclusion guarantee (e.g. database migrations, where two writers must never proceed), use `DatabaseLeaderElection` (a DB advisory lock), which is split-brain-safe by construction and independent of the gossip fabric.

---

## IPC transport

Apps talk to the Agent over a **local-only** transport:

- **Linux / macOS**: Unix domain socket at `$XDG_RUNTIME_DIR/pragmatic-agent.sock` (or configurable)
- **Windows**: named pipe at `\\.\pipe\pragmatic-agent`

Both transports are machine-local: no network exposure, no TLS needed, filesystem permissions gate who can talk to the daemon.

The wire protocol is length-prefixed frames with JSON bodies. Clients reconnect automatically on daemon restart.

---

## Lifecycle

1. Daemon starts (manually, systemd, launchd, Docker `CMD`, or auto-start from `UseAgent()` in dev)
2. Daemon binds its local socket, loads persisted KV state, joins the gossip cluster
3. Apps open connections on startup via `UseAgent()`; each app sends a heartbeat every few seconds
4. Apps publish initial state (tenant mapping, flag defaults) if they own it
5. Apps read runtime state on demand; reads are sub-millisecond via in-memory cache on the daemon
6. Daemon persists KV mutations to disk as they happen
7. On shutdown, daemon unregisters from gossip (if graceful), flushes state, closes sockets

---

## CLI

The same socket is used by the CLI as by app clients:

```bash
pragmatic-agent start --data-dir=/var/lib/pragmatic/agent
pragmatic-agent status
pragmatic-agent config get booking-service.retry-max-attempts
pragmatic-agent config set booking-service.retry-max-attempts 5
pragmatic-agent flag set new-checkout-flow enabled=true rollout=100
pragmatic-agent cluster members
```

Operators don't need to install a separate tool: the daemon ships the CLI in the same binary.

---

## What the Agent does not do

- **Application delivery**: the Agent doesn't deploy your app, it just coordinates it
- **Hard mutual exclusion**: the Agent's `IClusterLeadership` is a soft KV-lease (advisory singletons); for a split-brain-safe guarantee (e.g. migrations) use `DatabaseLeaderElection` (DB advisory lock)
- **External gateway**: routing decisions happen in `Pragmatic.Gateway`, which consumes from the Agent
- **Service discovery across WAN**: the gossip cluster is LAN-oriented; for WAN-scale discovery use an external tool (Consul, etcd)
- **A secrets vault**: values under `secret/` are encrypted at rest (AES-256-GCM, key from `PRAGMATIC_AGENT_SECRET_KEY` or `{dataDir}/secret.key`) and masked in listings; beyond that it is a KV namespace. When you need what a vault offers, use a dedicated secrets store and keep references in Agent KV

---

## Integration points

- [Pragmatic.Gateway](/modules/gateway/overview/): reads routes and clusters from Agent KV
- [Pragmatic.Composition.Host](/modules/composition/overview/): hosts the app that calls `UseAgent()`
- [Pragmatic.Migrations](/modules/migrations/overview/): `DatabaseLeaderElection` for split-brain-safe migration leadership (independent of the gossip fabric)

---

## Related

- [getting-started.md](/modules/agent/getting-started/): starting the daemon, wiring a host
- [common-mistakes.md](/modules/agent/common-mistakes/)
- [troubleshooting.md](/modules/agent/troubleshooting/): socket / connection issues
