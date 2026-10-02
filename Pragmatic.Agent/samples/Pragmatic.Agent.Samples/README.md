# Pragmatic.Agent.Samples

Runnable console samples for the **in-process** surface of `Pragmatic.Agent`.

> [!IMPORTANT]
> `Pragmatic.Agent` is a local coordination **daemon** — a KV store, SWIM gossip
> cluster, and IPC over a Unix socket / Windows named pipe. A console sample must
> **not** spin up the daemon or open a socket. These samples therefore exercise
> only the pieces that run purely in-process, from the two **public** libraries:
> `Pragmatic.Agent.Protocol` (the wire protocol) and `Pragmatic.Agent.Client`
> (`AgentOptions`).

## Run

```bash
dotnet run --project Pragmatic.Agent/samples/Pragmatic.Agent.Samples
```

## What is demonstrated (in-process surface)

| Sample | File | Surface |
|--------|------|---------|
| 1 | `WireFormatSample.cs` | `IWireFormat` serialize/deserialize roundtrip through `JsonWireFormat`, the one wire format |
| 2 | `FrameCodecSample.cs` | `FrameCodec` length-prefixed framing: sync `Encode`/`TryDecode` and async `WriteFrameAsync`/`ReadFrameAsync` over a `MemoryStream` (two frames back-to-back + clean EOF) |
| 3 | `AgentMessageSample.cs` | `AgentMessage` envelope + `MessageType` + typed payload extraction (`Payload.Value.Deserialize<T>()`) and a `Response` built via `JsonSerializer.SerializeToElement`, mirroring `AgentMessageHandler` |
| 4 | `AgentOptionsSample.cs` | `AgentOptions` client configuration: OS-sensitive `SocketPath` default, heartbeat cadence, `AutoReconnect` graceful degradation |

## Not covered (requires a running daemon / are `internal` to the daemon)

These live in the `Pragmatic.Agent` **executable** (and are `internal`), so they
cannot be referenced from a separate console app and must run as a daemon:

- `KvStore` — in-memory KV with Lamport-clock versioning, compare-and-swap, prefix queries and watch
- `KvSecretProtector` — AES-256-GCM encrypt/decrypt of `secret/` values at rest (via `Pragmatic.Cryptography`)
- `AgentSocketServer` / `ClientConnection` — the Unix-socket / named-pipe IPC server
- SWIM gossip membership and KV replication (`ClusterManager`, `SwimProtocol`)
- `AgentControlPlane` / Agent-backed configuration, feature-flag and tenant stores wired via `UseAgent()`

To exercise those, run the daemon (`pragmatic-agent start`) and connect a host with `builder.UseAgent()` — see `Pragmatic.Agent/docs/getting-started.md`.
