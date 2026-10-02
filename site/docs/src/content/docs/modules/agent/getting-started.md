---
title: "Getting Started"
description: "From the repo root:"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Agent/docs/getting-started.md
sidebar:
  order: 2
---
## 1. Run the daemon

From the repo root:

```bash
dotnet run --project Pragmatic.Agent/src/Pragmatic.Agent/Pragmatic.Agent.csproj -- start
```

Optional overrides:

```bash
dotnet run --project Pragmatic.Agent/src/Pragmatic.Agent/Pragmatic.Agent.csproj -- start --instance booking --socket /tmp/pragmatic/booking.sock
```

## 2. Connect an application

```csharp
using Pragmatic.Agent.Client;

await PragmaticApp.RunAsync(args, builder =>
{
    builder.UseAgent(agent =>
    {
        agent.SocketPath = "/var/run/pragmatic/agent.sock";
        agent.HeartbeatInterval = TimeSpan.FromSeconds(15);
        agent.AutoReconnect = true;
    });
});
```

## 3. Use the CLI

```bash
dotnet run --project Pragmatic.Agent/src/Pragmatic.Agent/Pragmatic.Agent.csproj -- status
dotnet run --project Pragmatic.Agent/src/Pragmatic.Agent/Pragmatic.Agent.csproj -- config set Mail:Host smtp.example.com
dotnet run --project Pragmatic.Agent/src/Pragmatic.Agent/Pragmatic.Agent.csproj -- flag set bookings.checkout-v2 true
```

## 4. Development auto-start

In Development environments, `UseAgent()` attempts to auto-start the daemon if it is not already listening and the project can be located from the current solution layout.

This is a convenience for local work, not a deployment mechanism for production.

## 5. Next steps

- wire configuration or feature flags to Agent-backed stores
- point `Pragmatic.Gateway` at the same Agent socket
- explore multi-instance setups with `--instance`

