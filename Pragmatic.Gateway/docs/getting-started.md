# Getting Started

## 1. Configure the gateway

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

`Backends` lists every instance of the service (taken in turn). Add `"PathRemovePrefix": "/booking"` when
the service answers on its own paths: `/booking/health` then reaches it as `/health`.

## 2. Run the executable

```bash
dotnet run --project Pragmatic.Gateway/src/Pragmatic.Gateway/Pragmatic.Gateway.csproj
```

Optional CLI overrides:

```bash
dotnet run --project Pragmatic.Gateway/src/Pragmatic.Gateway/Pragmatic.Gateway.csproj -- --listen http://*:8081 --agent-socket /tmp/pragmatic/agent.sock
```

## 3. Add JWT or CORS if needed

Enable these only when you actually need them. The gateway can also run with a minimal route-only configuration.

## 4. Add resilience

```json
{
  "Gateway": {
    "Resilience": {
      "Default": {
        "Timeout": "00:00:10",
        "FailureThreshold": 5,
        "BreakDuration": "00:00:30"
      }
    }
  }
}
```

## 5. Connect the Agent for dynamic routing

If the Agent is running and has gateway route data in KV state, the gateway reloads from Agent first and falls back to the static route list when needed.

