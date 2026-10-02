# Architecture and Core Concepts

This guide explains **why** Pragmatic.Discovery exists, how its pieces fit together, and how it enables safe distributed deployments. Read this before diving into the getting-started guide.

---

## The Problem

A modular monolith splits business logic into isolated modules -- Booking, Billing, Catalog -- each with its own database assignment. In a single host, everything works: the Composition source generator knows the full topology at compile time and validates it.

### Splitting to microservices: topology becomes invisible

When you split modules across hosts, compile-time validation disappears:

```
Host A (Booking + Catalog)  →  BookingDb (PostgreSQL)
Host B (Billing)            →  BillingDb (PostgreSQL)
```

Host A has no idea what Host B deploys. You cannot know at compile time whether:
- Two hosts accidentally include the same module (dual writes, data corruption)
- A module declares `[ReadAccess]` on entities that live in a different host (SQL joins across hosts are impossible)
- Two hosts assign the same database name but use different providers (PostgreSQL vs. InMemory -- likely a misconfiguration)

Without runtime topology awareness, these issues surface as silent data corruption, failed queries, or mysterious production errors days after deployment.

### Manual service registries: maintenance burden

Teams often solve this with Consul, etcd, or custom service registries. But those systems do not understand your module topology. They know "Host B is alive at port 5002" but not "Host B owns BillingModule on BillingDb with PostgreSQL." You end up maintaining a separate configuration map that drifts from the actual code.

### The gap: compile-time topology meets runtime reality

The Pragmatic Composition SG already emits a `[assembly: PragmaticMetadata(HostTopology, ...)]` attribute with the full module-to-database-to-provider mapping. This metadata exists in every compiled host assembly. What is missing is a runtime service that reads it, stores it, shares it across hosts, and validates it.

---

## The Solution

Pragmatic.Discovery bridges compile-time topology and runtime deployment. It reads the SG-emitted metadata at startup, registers it in a shared backend, and validates the deployment against other hosts.

```
Compile Time (SG)                    Runtime (Discovery)
─────────────────                    ───────────────────
[Module] classes with                DiscoveryHostedService reads
[Include<T>] attributes              PragmaticMetadata attribute
        │                                    │
        v                                    v
SG emits HostTopology JSON ───────> HostTopologyInfo.FromEntryAssembly()
  in assembly metadata                       │
                                             v
                                    RegisterAsync() → IDiscoveryBackend
                                             │
                                             v
                                    ValidateAsync() → cross-host checks
                                             │
                                             v
                                    DISC001/002/003 issues logged or thrown
```

The same "get guest by ID" scenario in a distributed deployment:

```csharp
// Showcase.Host registers: [Booking, Catalog] → ShowcaseDb (PostgreSQL)
// Showcase.Billing.Host registers: [Billing] → BillingDb (PostgreSQL)

// At startup, both hosts register with the discovery backend.
// If both accidentally include CatalogModule, validation catches it:
//   DISC001 Error: CatalogModule deployed in multiple hosts

// At runtime, FindHostsForModuleAsync locates the correct host:
var billingHosts = await discovery.FindHostsForModuleAsync("BillingModule");
// → [Showcase.Billing.Host]
```

No manual configuration. No external service registry that drifts from the code. The topology metadata travels with the compiled assembly, and Discovery reads it automatically.

---

## The Topology Model

Discovery operates on three immutable records that describe a host's deployment.

### HostTopologyInfo

The top-level record representing a single host:

```csharp
public sealed record HostTopologyInfo
{
    public required string HostName { get; init; }
    public IReadOnlyList<ModuleDeploymentInfo> Modules { get; init; } = [];
    public IReadOnlyList<BoundaryReadAccessInfo> Boundaries { get; init; } = [];
    public DateTimeOffset RegisteredAt { get; init; } = DateTimeOffset.UtcNow;
}
```

| Property | Description |
|----------|-------------|
| `HostName` | Logical host name, derived from the root namespace of the host project |
| `Modules` | All modules deployed in this host, with their database assignments |
| `Boundaries` | ReadAccess declarations per boundary (cross-boundary SQL join intent) |
| `RegisteredAt` | UTC timestamp when this topology was registered |

### ModuleDeploymentInfo

Describes a single module's deployment within a host:

```csharp
public sealed record ModuleDeploymentInfo
{
    public required string ModuleName { get; init; }
    public string? DatabaseName { get; init; }
    public string? Provider { get; init; }
}
```

| Property | Description |
|----------|-------------|
| `ModuleName` | The module class name (e.g., `"BillingModule"`) |
| `DatabaseName` | The database class name (e.g., `"ShowcaseFinancialDatabase"`), or `null` if the module has no database |
| `Provider` | The database provider (e.g., `"SqlServer"`, `"InMemory"`), or `null` if not assigned |

### BoundaryReadAccessInfo

Describes cross-boundary read access declarations:

```csharp
public sealed record BoundaryReadAccessInfo
{
    public required string BoundaryName { get; init; }
    public IReadOnlyList<string> EntityTypes { get; init; } = [];
}
```

| Property | Description |
|----------|-------------|
| `BoundaryName` | The boundary name (e.g., `"Booking"`) |
| `EntityTypes` | Entity type names this boundary needs to read via SQL join (declared with `[ReadAccess<T>]` on the boundary class) |

### How Topology Is Parsed

The Composition SG emits JSON metadata as an assembly attribute:

```json
{
  "host": "Showcase.Host",
  "includes": [
    { "module": "BookingModule", "database": "ShowcaseDatabase", "provider": "InMemory" },
    { "module": "CatalogModule", "database": "ShowcaseDatabase", "provider": "InMemory" }
  ],
  "boundaries": [
    { "name": "Booking", "readAccess": ["Property", "RoomType"] }
  ]
}
```

`HostTopologyInfo` provides three parsing entry points:

| Method | Use Case |
|--------|----------|
| `FromEntryAssembly()` | Read metadata from the running host's entry assembly. Used by `DiscoveryHostedService` automatically. |
| `FromAssembly(assembly)` | Read from a specific assembly. Useful for testing or analyzing external assemblies. |
| `Parse(json)` | Parse raw JSON. Useful for custom backends or manual topology construction. |

All three return `null` if no valid metadata is found, rather than throwing.

---

## Validation

The `DiscoveryValidator` checks for cross-host topology issues when a new host registers. It compares the incoming topology against all previously registered topologies.

### Validation Flow

```
Host registers → DiscoveryHostedService.StartAsync()
                         │
                         v
              HostTopologyInfo.FromEntryAssembly()
                         │
                         v
              RegisterAsync() → stored in backend
                         │
                         v
              ValidateAsync() → compare against existing
                         │
                         v
              DiscoveryValidationResult
                ├── IsValid (no Error-severity issues)
                ├── Errors   → logged as Error
                ├── Warnings → logged as Warning
                └── Info     → logged as Debug
```

### Diagnostic Codes

| Code | Severity | Trigger | Description |
|------|----------|---------|-------------|
| **DISC001** | Info | Same module in multiple hosts with different databases | Multi-deployment (scale-out). Allowed but flagged: ensure consistency via messaging. |
| **DISC002** | Warning | Same module + same database name with different providers | Likely misconfiguration. One host uses PostgreSQL, another uses InMemory for the same database name. |
| **DISC003** | Info | Boundary declares ReadAccess on entities from another boundary | Flagged for awareness. Runtime validation cannot resolve exact entity-to-module ownership, so treat it as a signal to review the topology, not as a verdict. |

### DiscoveryValidationResult

```csharp
public sealed record DiscoveryValidationResult
{
    public static readonly DiscoveryValidationResult Ok = new() { IsValid = true };

    public bool IsValid { get; init; }
    public IReadOnlyList<DiscoveryValidationIssue> Issues { get; init; } = [];

    public IEnumerable<DiscoveryValidationIssue> Errors =>
        Issues.Where(i => i.Severity == IssueSeverity.Error);

    public IEnumerable<DiscoveryValidationIssue> Warnings =>
        Issues.Where(i => i.Severity == IssueSeverity.Warning);
}
```

### DiscoveryValidationIssue

```csharp
public sealed record DiscoveryValidationIssue
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public IssueSeverity Severity { get; init; }
}

public enum IssueSeverity
{
    Info,
    Warning,
    Error
}
```

### Validation Behavior by Environment

| Environment | Recommended `ThrowOnValidationFailure` | Rationale |
|-------------|---------------------------------------|-----------|
| Development | `false` (default) | Log warnings, do not block startup. InMemory backend, single host -- few real conflicts. |
| Staging | `true` | Catch topology issues before production. Block startup on Error-severity issues. |
| Production | `true` | Prevent known-broken deployments from receiving traffic. |

---

## Backends

Discovery uses a pluggable backend for topology storage. The backend abstraction is `IDiscoveryBackend`:

```csharp
public interface IDiscoveryBackend
{
    Task StoreAsync(HostTopologyInfo topology, CancellationToken ct = default);
    Task<HostTopologyInfo?> GetByHostNameAsync(string hostName, CancellationToken ct = default);
    Task<IReadOnlyList<HostTopologyInfo>> GetAllAsync(CancellationToken ct = default);
}
```

### InMemoryDiscoveryBackend (default)

A thread-safe `ConcurrentDictionary` implementation. Suitable for:
- Single-process monoliths (discovery is self-referential)
- Integration tests
- Local development

Registered automatically by `AddDiscovery()`. All data lives for the lifetime of the application -- no persistence, no sharing across processes.

### Custom Backends

For distributed deployments, replace the InMemory backend with a shared store:

```csharp
services.AddDiscovery();
services.UseDiscoveryBackend<RedisDiscoveryBackend>();
```

`UseDiscoveryBackend<T>()` removes the default InMemory registration and replaces it with your implementation.

Any class implementing `IDiscoveryBackend` can serve as a backend. The interface has three methods -- `StoreAsync`, `GetByHostNameAsync`, `GetAllAsync` -- keeping the implementation surface minimal.

### Backend Packages

| Package | Backend | Status |
|---------|---------|--------|
| `Pragmatic.Agent.Discovery` | The Agent's gossip-replicated KV — `services.UseAgentDiscovery()`, after `UseAgent()` | Available |
| `Pragmatic.Discovery.Redis` | Redis-backed distributed registry | Planned |
| `Pragmatic.Discovery.Consul` | HashiCorp Consul service catalog | Planned |

---

## Registration

### Automatic Registration

By default, `DiscoveryHostedService` runs at startup and:

1. Reads `[assembly: PragmaticMetadata(HostTopology, ...)]` from the entry assembly
2. Calls `IDiscoveryService.RegisterAsync()` to store the topology
3. Calls `IDiscoveryService.ValidateAsync()` to check for cross-host issues

This is fully automatic -- no code needed beyond `services.AddDiscovery()`.

### Manual Registration

For testing or custom scenarios, disable auto-registration and register manually:

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
        new ModuleDeploymentInfo
        {
            ModuleName = "CatalogModule",
            DatabaseName = "TestDb",
            Provider = "InMemory"
        }
    ]
};

await discoveryService.RegisterAsync(topology);
```

### IDiscoveryService

The high-level service for topology operations:

```csharp
public interface IDiscoveryService
{
    Task RegisterAsync(HostTopologyInfo topology, CancellationToken ct = default);
    Task<IReadOnlyList<HostTopologyInfo>> GetAllHostsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<HostTopologyInfo>> FindHostsForModuleAsync(string moduleName, CancellationToken ct = default);
    Task<DiscoveryValidationResult> ValidateAsync(HostTopologyInfo localTopology, CancellationToken ct = default);
}
```

| Method | Description |
|--------|-------------|
| `RegisterAsync` | Stores this host's topology in the backend. Logs the host name and module count. |
| `GetAllHostsAsync` | Returns all registered host topologies from the backend. |
| `FindHostsForModuleAsync` | Finds all hosts that include (own) the specified module. Case-sensitive match on module name. |
| `ValidateAsync` | Compares the given topology against all registered hosts. Returns issues with codes, messages, and severities. |

### Configuration Options

```csharp
public sealed class DiscoveryOptions
{
    public bool AutoRegisterOnStartup { get; set; } = true;
    public bool ValidateOnStartup { get; set; } = true;
    public bool ThrowOnValidationFailure { get; set; } = false;
    public const string SectionName = "Pragmatic:Discovery";
}
```

Configure via code:

```csharp
services.AddDiscovery(opts =>
{
    opts.ThrowOnValidationFailure = true;
});
```

Or via `appsettings.json`:

```json
{
  "Pragmatic": {
    "Discovery": {
      "AutoRegisterOnStartup": true,
      "ValidateOnStartup": true,
      "ThrowOnValidationFailure": true
    }
  }
}
```

---

## Ecosystem Integration

### Composition Source Generator

Discovery depends on the Composition SG for its metadata source. The SG reads `[Module]` classes with `[Include<T>]` and `[BelongsTo<T>]` attributes at compile time and emits:

```csharp
[assembly: PragmaticMetadata(MetadataCategory.HostTopology, """{"host":"Showcase.Host","includes":[...]}""")]
```

Without `Pragmatic.Composition` and at least one `[Module]` class, no metadata is emitted and Discovery has nothing to register.

### Remote Boundaries

`Pragmatic.Composition.Host` uses `IDiscoveryService` to locate remote modules when `[RemoteBoundary<T>]` is declared. Discovery tells the composition layer which host URL to call for a given module:

```
Showcase.Host             → registers: [Booking, Catalog]
Showcase.Billing.Host     → registers: [Billing]

Showcase.Host wants to call Billing:
  1. FindHostsForModuleAsync("BillingModule") → Showcase.Billing.Host
  2. HTTP call to configured base URL
```

### Health Checks

Inject `IDiscoveryService` into custom health checks to monitor the deployment topology:

```csharp
public class TopologyHealthCheck(IDiscoveryService discovery) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        var hosts = await discovery.GetAllHostsAsync(ct);
        return hosts.Count > 0
            ? HealthCheckResult.Healthy($"{hosts.Count} host(s) registered")
            : HealthCheckResult.Degraded("No hosts registered");
    }
}
```

### Auto-Registration in Pragmatic Host

When `Pragmatic.Discovery` is referenced in a host project, the `FeatureDetector` in the Composition SG detects it and auto-registers Discovery services via `RegisterAllPragmaticServices()`. This means `AddDiscovery()` is called automatically in hosts that use `PragmaticApp.RunAsync()`.

---

## See Also

- [Getting Started](getting-started.md) -- Set up discovery in a Pragmatic.Design host
- [Topology Validation and Custom Backends](topology-validation.md) -- Validation rules, issue codes, custom backends (Redis, DB)
