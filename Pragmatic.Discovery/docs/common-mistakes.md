# Common Mistakes

These are the most common issues developers encounter when using Pragmatic.Discovery. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Forgetting to Reference Pragmatic.Composition

**Wrong:**

```
Host project references:
  - Pragmatic.Discovery        ✓
  - Pragmatic.Endpoints         ✓
  - Pragmatic.Actions           ✓
  (no Pragmatic.Composition)    ✗
```

**Startup log:**

```
warn: No HostTopology metadata found in entry assembly.
      Discovery registration skipped.
```

**Right:**

```
Host project references:
  - Pragmatic.Discovery         ✓
  - Pragmatic.Composition       ✓   ← required
  - Pragmatic.Endpoints         ✓
```

And the host must have at least one `[Module]` class with `[Include<T>]` attributes:

```csharp
[Module]
[Include<BookingModule, ShowcaseDatabase>]
[Include<CatalogModule, ShowcaseDatabase>]
public static class ShowcaseHost;
```

**Why:** Discovery reads its topology from the `[assembly: PragmaticMetadata(HostTopology, ...)]` attribute, which is emitted by the Composition source generator. Without `Pragmatic.Composition` and a `[Module]` class, no metadata is emitted and Discovery has nothing to register. The `DiscoveryHostedService` logs a warning and skips registration silently.

---

## 2. Calling UseDiscoveryBackend Before AddDiscovery

**Wrong:**

```csharp
// Program.cs
services.UseDiscoveryBackend<RedisDiscoveryBackend>();  // No default registration exists yet!
services.AddDiscovery();
```

**Runtime result:** The `UseDiscoveryBackend<T>()` method searches for an existing `IDiscoveryBackend` registration to remove it. Since `AddDiscovery()` has not been called yet, there is nothing to remove. Then `AddDiscovery()` registers `InMemoryDiscoveryBackend` via `TryAddSingleton`, which succeeds because no registration exists -- overwriting the Redis backend.

**Right:**

```csharp
// Program.cs
services.AddDiscovery();                                // Registers InMemory as default
services.UseDiscoveryBackend<RedisDiscoveryBackend>();   // Replaces InMemory with Redis
```

**Why:** `AddDiscovery()` uses `TryAddSingleton<IDiscoveryBackend, InMemoryDiscoveryBackend>()`, which only registers if no `IDiscoveryBackend` is already registered. `UseDiscoveryBackend<T>()` explicitly removes the existing registration and adds a new one. The correct order is: register the default first, then replace it.

---

## 3. Not Enabling ThrowOnValidationFailure in Production

**Wrong:**

```csharp
// Same configuration for all environments
services.AddDiscovery();  // ThrowOnValidationFailure defaults to false
```

**Runtime result:** In production, two hosts accidentally include the same module. Discovery logs an error but the host starts anyway. Both hosts write to the same database independently, causing data corruption that surfaces hours later.

**Right:**

```csharp
services.AddDiscovery(opts =>
{
    opts.ThrowOnValidationFailure = !app.Environment.IsDevelopment();
});
```

Or via `appsettings.Production.json`:

```json
{
  "Pragmatic": {
    "Discovery": {
      "ThrowOnValidationFailure": true
    }
  }
}
```

**Why:** The default `false` is appropriate for local development where you are iterating quickly with InMemory backends and single-host setups. In staging and production, topology conflicts are deployment errors that should prevent the host from starting. Setting `ThrowOnValidationFailure = true` causes `DiscoveryHostedService` to throw `InvalidOperationException` on Error-severity issues, blocking startup.

---

## 4. Expecting Cross-Host Validation with InMemory Backend

**Wrong:**

```csharp
// Host A (process 1)
services.AddDiscovery();  // InMemory backend

// Host B (process 2)
services.AddDiscovery();  // InMemory backend (separate process!)
```

**Runtime result:** Host A and Host B each have their own `InMemoryDiscoveryBackend` in separate processes. Host A registers itself but never sees Host B. Host B registers itself but never sees Host A. Cross-host validation runs but finds zero existing hosts -- all checks pass trivially.

**Right:**

```csharp
// Both hosts use a shared backend
services.AddDiscovery(opts =>
{
    opts.ThrowOnValidationFailure = true;
});
services.UseAgentDiscovery();   // Pragmatic.Agent.Discovery, after UseAgent()
```

**Why:** `InMemoryDiscoveryBackend` uses a `ConcurrentDictionary` that exists only within a single process. For cross-host validation to work, both hosts must register against the same backend instance. Use the Agent backend, or a distributed store of your own behind `IDiscoveryBackend` (see [Custom Backends](topology-validation.md)), so that when Host B registers, it can see Host A's topology and vice versa. InMemory is designed for monoliths and tests, not multi-host deployments.

---

## 5. Building Topology Manually Instead of Using SG Metadata

**Wrong:**

```csharp
// Hardcoded topology that drifts from the actual [Module] declarations
var topology = new HostTopologyInfo
{
    HostName = "MyHost",
    Modules =
    [
        new ModuleDeploymentInfo { ModuleName = "BookingModule", DatabaseName = "BookingDb" },
        // Forgot to add CatalogModule, which was added to the [Module] class last sprint
    ]
};

await discoveryService.RegisterAsync(topology);
```

**Runtime result:** The manually constructed topology does not include `CatalogModule`, even though the host's `[Module]` class has `[Include<CatalogModule>]`. Validation does not detect the overlap because the registration is incomplete.

**Right:**

```csharp
// Let the hosted service read from the compiled assembly metadata
services.AddDiscovery();  // DiscoveryHostedService handles everything
```

Or if you need to register manually:

```csharp
var topology = HostTopologyInfo.FromEntryAssembly();
if (topology is not null)
    await discoveryService.RegisterAsync(topology);
```

**Why:** The Composition SG emits topology metadata that always matches the actual `[Module]` and `[Include<T>]` declarations. Manual construction introduces drift -- the topology description diverges from the code. Always prefer `HostTopologyInfo.FromEntryAssembly()` or the automatic `DiscoveryHostedService` flow.

---

## 6. Ignoring DISC003 Info Messages About ReadAccess

**Wrong:**

```
info: [DISC003] Boundary 'Booking' in 'Showcase.Host' declares ReadAccess on [Property, RoomType].
```

Developer ignores the info message. Later, the team splits `CatalogModule` to a separate host. The `Booking` boundary still declares `[ReadAccess<Property>]`, but `Property` now lives in a different database. SQL joins fail at runtime.

**Right:**

When you see DISC003, verify that the owning module for the ReadAccess entities is co-located on the same database as the boundary. If you plan to split modules to separate hosts, review all ReadAccess declarations first.

```
Boundary 'Booking' declares ReadAccess on [Property, RoomType]
  → Property is owned by CatalogModule
  → CatalogModule is on ShowcaseDatabase
  → Booking boundary is on ShowcaseDatabase
  → OK: same database, SQL joins will work

If CatalogModule moves to a separate host/database:
  → ReadAccess becomes invalid
  → Switch to application-level data fetching or co-locate the modules
```

**Why:** DISC003 is informational by design because runtime-only validation cannot determine exact entity-to-module ownership. But it serves as an early warning. When planning a module split, DISC003 messages identify the boundaries that will break.

---

## 7. Registering Discovery in a Class Library Instead of the Host

**Wrong:**

```csharp
// In Pragmatic.MyModule (class library)
public static class MyModuleExtensions
{
    public static IServiceCollection AddMyModule(this IServiceCollection services)
    {
        services.AddDiscovery();  // Wrong place!
        return services;
    }
}
```

**Runtime result:** `HostTopologyInfo.FromEntryAssembly()` reads metadata from the entry assembly -- the top-level host executable. When `AddDiscovery()` is called from a library, the entry assembly is still the host, so this technically works. But calling `AddDiscovery()` from a library creates a hidden dependency: the host must reference `Pragmatic.Composition` to emit metadata, but the library does not enforce this. If the host is built without Composition, Discovery registers silently with no topology.

**Right:**

```csharp
// In the host project's Program.cs or IStartupStep
services.AddDiscovery(opts =>
{
    opts.ThrowOnValidationFailure = true;
});
```

**Why:** Discovery is a host-level concern, not a module concern. The topology metadata lives in the host's assembly, and the configuration (which backend, whether to throw on failure) is a deployment decision. Keep `AddDiscovery()` in the host where the developer can see and configure it explicitly.

---

## 8. Assuming Module Names Match Class Names Exactly

**Wrong:**

```csharp
// Module class is named "BillingModule"
var hosts = await discovery.FindHostsForModuleAsync("Billing");  // Missing "Module" suffix
// Returns empty list -- no match found
```

**Right:**

```csharp
var hosts = await discovery.FindHostsForModuleAsync("BillingModule");
```

**Why:** `FindHostsForModuleAsync` performs a case-sensitive (`StringComparison.Ordinal`) match on the `ModuleName` property. The module name in the topology JSON comes from the class name as declared in the `[Module]` / `[Include<T>]` attributes. If your module class is `BillingModule`, the name is `"BillingModule"`, not `"Billing"`.

---

## 9. Not Disposing Test InMemory Backends Between Tests

**Wrong:**

```csharp
public class DiscoveryTests
{
    private readonly IDiscoveryService _discovery;

    public DiscoveryTests()
    {
        // Same static backend shared across all tests
        var services = new ServiceCollection();
        services.AddDiscovery();
        var sp = services.BuildServiceProvider();
        _discovery = sp.GetRequiredService<IDiscoveryService>();
    }

    [Fact]
    public async Task Test1()
    {
        await _discovery.RegisterAsync(new HostTopologyInfo { HostName = "Host1", Modules = [...] });
        // Registers Host1
    }

    [Fact]
    public async Task Test2()
    {
        var hosts = await _discovery.GetAllHostsAsync();
        // May see Host1 from Test1 if tests run in the same order!
    }
}
```

**Right:**

```csharp
public class DiscoveryTests
{
    private static (IDiscoveryService service, ServiceProvider sp) CreateDiscovery()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDiscovery();
        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<IDiscoveryService>(), sp);
    }

    [Fact]
    public async Task Test1()
    {
        var (discovery, sp) = CreateDiscovery();
        await using var _ = sp;  // Fresh backend per test
        await discovery.RegisterAsync(new HostTopologyInfo { HostName = "Host1", Modules = [] });
    }

    [Fact]
    public async Task Test2()
    {
        var (discovery, sp) = CreateDiscovery();
        await using var _ = sp;
        var hosts = await discovery.GetAllHostsAsync();
        // Always starts empty -- isolated from Test1
    }
}
```

**Why:** `InMemoryDiscoveryBackend` is registered as a singleton. If the same `ServiceProvider` is reused across tests, the `ConcurrentDictionary` retains registrations from previous tests, causing non-deterministic test behavior depending on execution order.

---

## Quick Reference

| Mistake | Symptom |
|---------|---------|
| Missing Pragmatic.Composition | "No HostTopology metadata found" warning at startup |
| UseDiscoveryBackend before AddDiscovery | InMemory backend used instead of custom backend |
| ThrowOnValidationFailure = false in prod | Topology conflicts silently logged, host starts broken |
| InMemory backend in multi-host deployment | Cross-host validation always passes (no shared state) |
| Manual topology construction | Topology drifts from actual [Module] declarations |
| Ignoring DISC003 before module split | SQL joins break when modules move to separate hosts |
| AddDiscovery in library instead of host | Hidden dependency, no enforcement of Composition reference |
| Wrong module name casing | FindHostsForModuleAsync returns empty (case-sensitive) |
| Shared InMemory backend in tests | Non-deterministic test results |
