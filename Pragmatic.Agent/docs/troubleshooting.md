# Troubleshooting

## App cannot connect to the Agent

Checklist:

- confirm the daemon is running
- confirm the app and daemon use the same socket or pipe path
- verify file-system permissions on the socket directory
- check whether you accidentally started a different `--instance`

## Development auto-start does not happen

`UseAgent()` only auto-starts in Development and only when it can locate the Agent project from the current solution layout.

Check:

- `ASPNETCORE_ENVIRONMENT` or `DOTNET_ENVIRONMENT`
- whether the current process can find `Pragmatic.Agent/src/Pragmatic.Agent/Pragmatic.Agent.csproj`

## KV state is not where you expect

The data directory is environment-aware. Container and host paths differ. If you need explicit placement, set a socket or data path override instead of assuming a default location.

## Cluster peers do not appear

Verify:

- `--port`
- `--peers`
- `--discovery`
- firewall and container networking rules

If local IPC works but cluster membership does not, the problem is in gossip networking, not in the app-to-Agent connection.

