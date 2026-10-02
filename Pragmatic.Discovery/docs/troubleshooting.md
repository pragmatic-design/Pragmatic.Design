# Troubleshooting

Practical problem/solution guide for Pragmatic.Discovery. Each section covers a common issue, the likely causes, and the fix.

---

## "No HostTopology metadata found in entry assembly"

The `DiscoveryHostedService` logs this warning and skips registration.

### Checklist

1. **Is `Pragmatic.Composition` referenced in the host project?** The Composition SG emits the `[assembly: PragmaticMetadata(HostTopology, ...)]` attribute. Without it, no metadata exists.

2. **Does the host have a `[Module]` class with `[Include<T>]` attributes?** The SG needs at least one module declaration to generate topology metadata:

   ```csharp
   [Module]
   [Include<BookingModule, ShowcaseDatabase>]
   public static class ShowcaseHost;
   ```

3. **Is the Pragmatic source generator configured?** Check that the `.csproj` references the generator with `OutputItemType="Analyzer"`.

4. **Are you running a test host?** In integration tests, the entry assembly may be the test runner, not the host. Use `HostTopologyInfo.FromAssembly()` with the specific assembly, or disable auto-registration and register manually.

---

## Validation Always Passes (No Issues Found)

You expect topology conflicts but validation reports zero issues.

### Checklist

1. **Are you using InMemory backend in a multi-host deployment?** InMemory is per-process. Each host validates against its own empty registry. Use a shared backend for cross-host validation — `UseAgentDiscovery()` from `Pragmatic.Agent.Discovery`, or your own `IDiscoveryBackend`.

2. **Is the other host registered before validation runs?** Validation compares the incoming host against already-registered hosts. If both hosts start simultaneously with InMemory backends, neither sees the other.

3. **Are the module names exactly matching?** `DiscoveryValidator` uses `StringComparison.Ordinal` for module name comparison. `"BillingModule"` and `"billingModule"` are different names.

4. **Is the topology manually constructed?** If you build `HostTopologyInfo` manually instead of using `FromEntryAssembly()`, verify that the `ModuleName` values match the actual class names.

---

## Startup Crashes with InvalidOperationException

The host fails to start with a message like: "Discovery validation failed for host 'MyHost': [DISC001] ..."

### Checklist

1. **Is `ThrowOnValidationFailure = true`?** This is the expected behavior when Error-severity issues are detected. The exception message lists all errors.

2. **Read the error codes:**
   - **DISC001**: Module deployed in multiple hosts. Fix by using `[RemoteBoundary<T>]` on the host that should invoke remotely.
   - **DISC002**: Provider mismatch on the same database. Fix by aligning providers across hosts.

3. **Is this a development environment?** Consider disabling `ThrowOnValidationFailure` in development:

   ```csharp
   opts.ThrowOnValidationFailure = !env.IsDevelopment();
   ```

---

## FindHostsForModuleAsync Returns Empty

You query for a module but get an empty list.

### Checklist

1. **Is the module name correct?** The match is case-sensitive. Use the exact class name (e.g., `"BillingModule"`, not `"Billing"`).

2. **Has the host registered?** Registration is asynchronous. If you query before the other host has started and registered, the backend has no entries.

3. **Are you using the right backend?** InMemory only has data from the current process. A shared backend is needed to see other hosts.

4. **Did auto-registration succeed?** Check the logs for the registration confirmation:

   ```
   info: Registering host topology: MyHost with 3 module(s)
   ```

   If you see the "No HostTopology metadata found" warning instead, registration was skipped.

---

## Custom Backend Not Being Used

You registered a custom backend but InMemory is still being used.

### Checklist

1. **Is `UseDiscoveryBackend<T>()` called AFTER `AddDiscovery()`?** The order matters:

   ```csharp
   services.AddDiscovery();                              // Registers InMemory
   services.UseDiscoveryBackend<RedisDiscoveryBackend>(); // Replaces InMemory
   ```

2. **Does your backend implement `IDiscoveryBackend`?** The generic constraint `where TBackend : class, IDiscoveryBackend` requires this.

3. **Are the backend's dependencies registered?** If your backend requires other services (e.g., `IConnectionMultiplexer` for Redis), they must be registered in the DI container before the backend is resolved.

---

## DISC002 Warning on Same Module

You get a warning about provider mismatch but believe the configuration is correct.

### Cause

DISC002 fires when the same module name + the same database name appear in two hosts with different `Provider` values:

```
Host A: BookingModule → BookingDb (provider: "PostgreSql")
Host B: BookingModule → BookingDb (provider: "InMemory")
```

### Fix

Align the providers. This typically happens when one host is a development host using InMemory while the other uses a real database. In distributed deployments, all hosts sharing a database should use the same provider.

If this is intentional (e.g., test host vs. production host in the same registry), consider using separate discovery registries per environment.

---

## FAQ

### Does Discovery work without Pragmatic.Composition?

Partially. You can register topology manually by constructing `HostTopologyInfo` objects and calling `RegisterAsync()`. But automatic metadata reading (`FromEntryAssembly()`) requires the Composition SG to have emitted the `[assembly: PragmaticMetadata]` attribute.

### Can I register the same host multiple times?

Yes. `IDiscoveryBackend.StoreAsync` uses the `HostName` as the key. Re-registering overwrites the previous entry. This is by design -- hosts can update their topology on restart.

### Does validation run continuously or only at startup?

Only at startup, when `DiscoveryHostedService.StartAsync()` is called. There is no periodic re-validation. If a new host registers after your host has started, your host does not re-validate. The newly registering host validates against all existing hosts, including yours.

### How do I test Discovery in integration tests?

Disable auto-registration and build topology manually:

```csharp
services.AddDiscovery(opts =>
{
    opts.AutoRegisterOnStartup = false;
    opts.ValidateOnStartup = false;
});
```

Then call `RegisterAsync` and `ValidateAsync` directly in your test methods.

### What happens if the backend is unavailable at startup?

`DiscoveryHostedService` calls `RegisterAsync` and `ValidateAsync` without a try-catch -- exceptions propagate to the host startup pipeline. If the backend throws (e.g., Redis connection refused), the host fails to start. This is intentional: if you cannot register, you cannot validate, and the deployment state is unknown.

---

## Getting Help

- **GitHub Issues**: [github.com/pragmatic-design/Pragmatic.Design/issues](https://github.com/pragmatic-design/Pragmatic.Design/issues)
- **Showcase Examples**: See the `Showcase` project for working Discovery integration in monolith and distributed hosts.
- **Topology Validation**: See [topology-validation.md](topology-validation.md) for detailed validation rules and custom backend examples.
