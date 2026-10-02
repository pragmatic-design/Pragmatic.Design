---
title: "Getting Started with Pragmatic.Discovery"
description: "This guide walks through setting up topology discovery in a Pragmatic.Design host."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Discovery/docs/getting-started.md
sidebar:
  order: 2
---
This guide walks through setting up topology discovery in a Pragmatic.Design host.

## Prerequisites

- `Pragmatic.Discovery` NuGet package referenced
- `Pragmatic.Composition` with at least one `[Module]` class using `[Include<T>]`
- The Pragmatic source generator configured (generates `PragmaticMetadata` assembly attributes)

## How It Works

1. The **Composition source generator** emits `[assembly: PragmaticMetadata(HostTopology, ...)]` with a JSON payload describing which modules, databases, and boundaries the host includes.
2. At **startup**, `DiscoveryHostedService` reads this metadata from the entry assembly.
3. It **registers** the topology with the `IDiscoveryBackend` (InMemory by default).
4. It optionally **validates** the topology against other registered hosts.

## Step 1: Add Discovery Services

In your `Program.cs` or `IStartupStep`:

```csharp
using Pragmatic.Discovery.Extensions;

services.AddDiscovery();
```

This registers:
- `IDiscoveryService` (singleton)
- `IDiscoveryBackend` (InMemoryDiscoveryBackend, singleton)
- `DiscoveryHostedService` (auto-registers topology on startup)

## Step 2: Verify Auto-Registration

When the host starts, you should see log messages:

```
info: Pragmatic.Discovery.Services.DiscoveryService
  Registering host topology: Showcase.Host with 3 module(s)
```

If you see a warning instead:

```
warn: No HostTopology metadata found in entry assembly.
```

This means the Composition source generator is not producing metadata. Ensure your host project has `[Module]` classes with `[Include<T>]` attributes.

## Step 3: Configure Validation

For development, the default settings are fine (log warnings, don't block startup). For production:

```csharp
services.AddDiscovery(opts =>
{
    opts.ThrowOnValidationFailure = true;  // Block startup on conflicts
});
```

Or via `appsettings.json`:

```json
{
    "Pragmatic": {
        "Discovery": {
            "ThrowOnValidationFailure": true,
            "ValidateOnStartup": true
        }
    }
}
```

## Step 4: Query Topology at Runtime

Inject `IDiscoveryService` to query the topology:

```csharp
public class TopologyHealthCheck(IDiscoveryService discovery) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        var hosts = await discovery.GetAllHostsAsync(ct);

        if (hosts.Count == 0)
            return HealthCheckResult.Degraded("No hosts registered");

        var data = new Dictionary<string, object>();
        foreach (var host in hosts)
        {
            data[host.HostName] = new
            {
                Modules = host.Modules.Select(m => m.ModuleName).ToList(),
                RegisteredAt = host.RegisteredAt
            };
        }

        return HealthCheckResult.Healthy($"{hosts.Count} host(s) registered", data);
    }
}
```

### Find a Specific Module

```csharp
var billingHosts = await discovery.FindHostsForModuleAsync("BillingModule", ct);

if (billingHosts.Count == 0)
    logger.LogWarning("No host owns BillingModule");
else
    logger.LogInformation("BillingModule is hosted by: {Hosts}",
        string.Join(", ", billingHosts.Select(h => h.HostName)));
```

## Step 5: Read Topology Manually

You can also read the topology metadata directly without the hosted service:

```csharp
// From the running host
var topology = HostTopologyInfo.FromEntryAssembly();

if (topology is not null)
{
    Console.WriteLine($"Host: {topology.HostName}");
    foreach (var module in topology.Modules)
        Console.WriteLine($"  Module: {module.ModuleName} -> DB: {module.DatabaseName}");
    foreach (var boundary in topology.Boundaries)
        Console.WriteLine($"  Boundary: {boundary.BoundaryName} reads [{string.Join(", ", boundary.EntityTypes)}]");
}
```

## Distributed Deployment

In a multi-host deployment, each host registers independently. Replace the InMemory backend with a distributed one:

```csharp
services.AddDiscovery(opts =>
{
    opts.ThrowOnValidationFailure = true;
});

// Replace InMemory with the Agent's replicated store (Pragmatic.Agent.Discovery; needs UseAgent())
services.UseAgentDiscovery();
```

Any other store works the same way: implement `IDiscoveryBackend` and register it with
`UseDiscoveryBackend<T>()` — see [Custom Backends](/modules/discovery/topology-validation/).

When Host B registers, it validates against Host A's already-registered topology:

- **DISC001** (Info): Same module in multiple hosts on different databases -- allowed, just logged
- **DISC002** (Warning): Same module + database with different providers -- likely misconfiguration
- **DISC003** (Info): ReadAccess declarations flagged for awareness

## Disabling Auto-Registration

If you want to register manually (e.g., in tests):

```csharp
services.AddDiscovery(opts =>
{
    opts.AutoRegisterOnStartup = false;
    opts.ValidateOnStartup = false;
});
```

Then register when ready:

```csharp
var topology = new HostTopologyInfo
{
    HostName = "TestHost",
    Modules =
    [
        new ModuleDeploymentInfo { ModuleName = "CatalogModule", DatabaseName = "TestDb", Provider = "InMemory" }
    ]
};

await discoveryService.RegisterAsync(topology);
```

## Integration with Remote Boundaries

`Pragmatic.Composition.Host` uses `IDiscoveryService` to locate remote modules when `[RemoteBoundary<T>]` is declared. The discovery service tells the composition layer which host URL to call for a given module.

```
Showcase.Host             --> registers: [Booking, Catalog]
Showcase.Billing.Host     --> registers: [Billing]

Showcase.Host wants to call Billing:
  1. FindHostsForModuleAsync("BillingModule") --> Showcase.Billing.Host
  2. HTTP call to configured base URL
```
