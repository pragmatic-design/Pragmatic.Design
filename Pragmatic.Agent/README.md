# Pragmatic.Agent

Local coordination daemon and client libraries for Pragmatic.Design applications.

`Pragmatic.Agent` gives distributed hosts a shared KV store, SWIM-based membership, local IPC, and operational primitives for configuration, feature flags, tenant data, and gateway/control coordination.

> Status: implemented preview subsystem. The runtime exists today and is used inside the repo; this is not a shape-only placeholder. Native AOT for the daemon is planned, but the current daemon runs as a normal .NET executable.

## What Ships

| Component | Type | Purpose |
|-----------|------|---------|
| `Pragmatic.Agent` | daemon executable project | Local process hosting socket/pipe IPC, KV persistence, gossip membership, CLI entrypoint |
| `Pragmatic.Agent.Client` | runtime package | `UseAgent()` integration for Pragmatic hosts, heartbeat service, Agent-backed stores |
| `Pragmatic.Agent.Protocol` | runtime package | Wire protocol, frames, payloads, the JSON wire format |

## Core Capabilities

- Local socket or named-pipe connection between app and Agent
- Persistent KV store with file-backed state
- SWIM-based cluster membership and gossip
- Agent-backed `IControlPlane`, `IConfigurationStore`, `IFeatureFlagStore`, and `ITenantStore`
- Development auto-start from `UseAgent()`
- Built-in CLI for status, config, and feature flags
- Platform adapters for IIS, nginx, Docker, Kubernetes, Azure Container Apps, and AWS ECS

## Quick Start

### 1. Start the daemon

From the repo root:

```bash
dotnet run --project Pragmatic.Agent/src/Pragmatic.Agent/Pragmatic.Agent.csproj -- start
```

Useful overrides:

```bash
dotnet run --project Pragmatic.Agent/src/Pragmatic.Agent/Pragmatic.Agent.csproj -- start --instance booking --socket /tmp/pragmatic/booking.sock
```

### 2. Connect a Pragmatic host

```csharp
using Pragmatic.Agent.Client;

await PragmaticApp.RunAsync(args, builder =>
{
    builder.UseAgent(agent =>
    {
        agent.SocketPath = "/var/run/pragmatic/agent.sock";
        agent.HeartbeatInterval = TimeSpan.FromSeconds(15);
    });
});
```

When the daemon is unreachable, the app degrades gracefully and keeps serving with local or in-memory defaults.

### 3. Use the CLI

```bash
dotnet run --project Pragmatic.Agent/src/Pragmatic.Agent/Pragmatic.Agent.csproj -- status
dotnet run --project Pragmatic.Agent/src/Pragmatic.Agent/Pragmatic.Agent.csproj -- config set Mail:Host smtp.example.com
dotnet run --project Pragmatic.Agent/src/Pragmatic.Agent/Pragmatic.Agent.csproj -- flag set bookings.new-checkout true
```

## Wire Format and Transport Security

`Pragmatic.Agent.Protocol` frames are length-prefixed: `[4-byte big-endian length][payload]`, capped at 16 MB (`FrameCodec`). The body serializer sits behind `IWireFormat`, and the one implementation is `JsonWireFormat` (`FrameCodec.DefaultFormat`: System.Text.Json, camelCase, null-omitting). `DefaultFormat` is immutable; a format is passed per call to `FrameCodec.Encode`/`ReadFrameAsync`/`WriteFrameAsync`, never swapped globally.

There is no TLS layer, and none is needed on the two links that exist:

- **App ↔ daemon** is local IPC — a Unix socket, owner-only (file 0600, directory 0700), or a named pipe on Windows. Nothing crosses the network.
- **Agent ↔ agent gossip** is UDP, and every datagram carries an HMAC-SHA256 over its JSON payload with the cluster-shared key (`GossipAuthenticator`); a datagram whose tag does not verify, in constant time, is dropped. All agents of a cluster need the same key.

A binary wire format and a TLS transport were prototyped and removed: the message model is JSON-native, and gossip over UDP would need DTLS, not a stream wrapper.

## Secret Encryption at Rest

Values stored under the `secret/` KV namespace are encrypted at rest via `IKvSecretProtector` (`Pragmatic.Agent.KV`). The daemon resolves the protector at startup with `KvSecretProtector.CreateFromEnvironment(dataDir)` and the handler encrypts on `KvSet` / decrypts on `KvGet` only for keys that start with `secret/`.

| Implementation | Behavior |
|----------------|----------|
| `KvSecretProtector` | AES-256-GCM via `Pragmatic.Cryptography`. Format: `base64([0x01][keyIdLen][keyId][nonce 12][tag 16][ciphertext])` — the embedded key id is what makes rotation possible. Requires a 32-byte key. |
| `RejectEncryption` | Fail-closed: any secret read/write throws "no encryption key configured". Used outside production when no key is present. |
| `NoEncryption` | Pass-through (development only, must be explicitly opted in). |

The cryptography itself is not the agent's: `KvSecretProtector` adapts `Pragmatic.Cryptography.ISecretEncryptor` to the store's string-in/string-out port, base64-encoding the packed ciphertext so it survives JSON persistence and gossip replication.

**Values written before this migration** used a different framing — `base64(nonce[12] + ciphertext + tag[16])`, with the tag last. They stay readable: a value that fails to decrypt under the current framing is retried under the retired one, and the next write stores it in the current format.

Key resolution order in `CreateFromEnvironment`:

1. `PRAGMATIC_AGENT_SECRET_KEY` environment variable (base64, must decode to exactly 32 bytes)
2. `{dataDir}/secret.key` file (base64, 32 bytes)
3. No key found → **throws** in Production (`ASPNETCORE_ENVIRONMENT` / `DOTNET_ENVIRONMENT` = `Production`); otherwise returns `RejectEncryption` so the daemon still starts for non-secret workflows while secret operations fail closed.

A configured-but-invalid key (bad base64 or wrong length) always throws — it is treated as operator misconfiguration, not silently ignored. Secret values are also masked (`***`) from prefix listings, so they are only readable via an explicit `KvGet` on the exact key.

## How It Fits

The Agent is the local coordination substrate. Other modules consume it rather than re-implementing their own stores or control channels:

- `Pragmatic.Configuration` can read and write config through the Agent KV store
- `Pragmatic.FeatureFlags` can use Agent-backed flag storage with fallback behavior
- `Pragmatic.MultiTenancy` can source tenant data from the Agent
- `Pragmatic.Gateway` can load routes from Agent state

## Current Scope

Implemented today:

- daemon process and CLI
- socket/pipe transport
- persistent KV store
- gossip cluster primitives
- client connection and heartbeat
- Agent-backed store replacements in `UseAgent()`
- platform detection and adapters

Still evolving:

- daemon AOT publishing
- broader cloud sync and deployment workflows
- wider operational tooling around the Agent ecosystem

## Documentation

Local docs:

- [Concepts](docs/concepts.md)
- [Getting Started](docs/getting-started.md)
- [Common Mistakes](docs/common-mistakes.md)
- [Troubleshooting](docs/troubleshooting.md)


## License

Part of the [Pragmatic.Design](../README.md) ecosystem — see [Licensing](../docs/LICENSING.md).
Pragmatic.Agent is licensed under the **PolyForm Small Business 1.0.0** license (free for small businesses; commercial license above the threshold).
