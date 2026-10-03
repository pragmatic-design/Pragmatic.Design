---
title: "Topology Validation and Custom Backends"
description: "When multiple hosts deploy the same modules, inconsistencies can arise: overlapping module ownership, missing database assignments, or conflicting read-access d"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Discovery/docs/topology-validation.md
sidebar:
  order: 3
---
When multiple hosts deploy the same modules, inconsistencies can arise: overlapping module ownership, missing database assignments, or conflicting read-access declarations. The Discovery module detects these issues at startup before they cause runtime failures.

---

## Validation Flow

When `ValidateOnStartup` is enabled (default), the `DiscoveryHostedService` performs two steps:

1. **Register**: reads `[assembly: PragmaticMetadata(HostTopology, ...)]` from the current host and calls `IDiscoveryService.RegisterAsync()`.
2. **Validate**: calls `ValidateAsync()` comparing the local topology against all registered hosts.

```csharp
services.AddDiscovery(opts =>
{
    opts.AutoRegisterOnStartup = true;       // Register this host's topology
    opts.ValidateOnStartup = true;           // Compare against other hosts
    opts.ThrowOnValidationFailure = false;   // Log warnings instead of crashing
});
```

---

## Validation Issues

`DiscoveryValidationResult` contains a list of `DiscoveryValidationIssue` entries, each with a code, message, and severity.

| Code | Severity | Meaning |
|------|----------|---------|
| `DISC001` | Error | A module is deployed in multiple hosts without `[RemoteBoundary]` declaration |
| `DISC002` | Warning | A module has no database assignment in any host |
| `DISC003` | Info | Cross-boundary read access declared but target boundary is in a different host |

### DISC001: Module Overlap

Two hosts both include the same module without marking one as remote. This means both hosts run the module's business logic independently, which can cause data inconsistency.

```
Host "Booking.Host" includes CatalogModule
Host "Catalog.Host" includes CatalogModule
→ DISC001 Error: CatalogModule deployed in multiple hosts
```

**Fix:** Use `[RemoteBoundary<CatalogModule>]` on the host that should invoke remotely, and `[Include<CatalogModule>]` only on the owning host.

### DISC002: Missing Database

A module is included but has no database assignment. This is a warning because some modules (e.g., utility modules) legitimately don't need a database.

**Fix:** Use `[Include<TModule, TDatabase>]` to assign a database, or ignore if the module has no entities.

### DISC003: Cross-Host Read Access

A boundary declares `[ReadAccess]` for entities in another boundary, but that boundary is deployed in a different host. SQL joins across hosts are not possible.

**Fix:** Either co-locate the modules in the same host, or use application-level data fetching instead of SQL joins.

---

## HostTopologyInfo

The `HostTopologyInfo` record captures a host's deployment topology. It is populated automatically from SG-generated assembly metadata.

```csharp
public sealed record HostTopologyInfo
{
    public required string HostName { get; init; }
    public IReadOnlyList<ModuleDeploymentInfo> Modules { get; init; }
    public IReadOnlyList<BoundaryReadAccessInfo> Boundaries { get; init; }
    public DateTimeOffset RegisteredAt { get; init; }
}
```

### Building Topology Manually

For testing or non-standard scenarios, you can build topology manually:

```csharp
var topology = new HostTopologyInfo
{
    HostName = "Booking.Host",
    Modules =
    [
        new ModuleDeploymentInfo { ModuleName = "BookingModule", DatabaseName = "BookingDb", Provider = "PostgreSql" },
        new ModuleDeploymentInfo { ModuleName = "CatalogModule", DatabaseName = "BookingDb", Provider = "PostgreSql" }
    ],
    Boundaries = []
};
```

### Reading from Assembly

In production, topology is read from SG-generated metadata:

```csharp
var topology = HostTopologyInfo.FromEntryAssembly();
```

---

## Custom Discovery Backends

The default `InMemoryDiscoveryBackend` works for single-process and testing scenarios. For distributed deployments, implement `IDiscoveryBackend` with a shared store.

### IDiscoveryBackend Interface

```csharp
public interface IDiscoveryBackend
{
    Task StoreAsync(HostTopologyInfo topology, CancellationToken ct = default);
    Task<HostTopologyInfo?> GetByHostNameAsync(string hostName, CancellationToken ct = default);
    Task<IReadOnlyList<HostTopologyInfo>> GetAllAsync(CancellationToken ct = default);
}
```

### Example: Redis Backend

```csharp
public sealed class RedisDiscoveryBackend(IConnectionMultiplexer redis) : IDiscoveryBackend
{
    private const string KeyPrefix = "pragmatic:discovery:";

    public async Task StoreAsync(HostTopologyInfo topology, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var json = JsonSerializer.Serialize(topology);
        await db.StringSetAsync($"{KeyPrefix}{topology.HostName}", json);
    }

    public async Task<HostTopologyInfo?> GetByHostNameAsync(string hostName, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var json = await db.StringGetAsync($"{KeyPrefix}{hostName}");
        return json.HasValue ? JsonSerializer.Deserialize<HostTopologyInfo>(json!) : null;
    }

    public async Task<IReadOnlyList<HostTopologyInfo>> GetAllAsync(CancellationToken ct)
    {
        var server = redis.GetServers().First();
        var keys = server.Keys(pattern: $"{KeyPrefix}*").ToArray();
        var db = redis.GetDatabase();
        var results = new List<HostTopologyInfo>();

        foreach (var key in keys)
        {
            var json = await db.StringGetAsync(key);
            if (json.HasValue)
                results.Add(JsonSerializer.Deserialize<HostTopologyInfo>(json!)!);
        }

        return results;
    }
}
```

### Registration

```csharp
services.AddDiscovery();
services.UseDiscoveryBackend<RedisDiscoveryBackend>();
```

---

## Configuration Reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AutoRegisterOnStartup` | `bool` | `true` | Register this host's topology on startup |
| `ValidateOnStartup` | `bool` | `true` | Validate topology against other hosts |
| `ThrowOnValidationFailure` | `bool` | `false` | Throw exception on validation errors (vs. log warning) |

Configuration section: `Pragmatic:Discovery`

```json
{
  "Pragmatic": {
    "Discovery": {
      "AutoRegisterOnStartup": true,
      "ValidateOnStartup": true,
      "ThrowOnValidationFailure": false
    }
  }
}
```
