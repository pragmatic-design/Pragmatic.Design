---
title: "Troubleshooting"
description: "Practical problem/solution guide for Pragmatic.Composition. Each section covers a common issue, the likely causes, and the fix."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Composition/docs/troubleshooting.md
sidebar:
  order: 7
---
Practical problem/solution guide for Pragmatic.Composition. Each section covers a common issue, the likely causes, and the fix.

---

## Services Not Resolving (DI Errors at Runtime)

Your application starts but a service resolution fails with `InvalidOperationException: Unable to resolve service for type 'IMyService'`.

### Checklist

1. **Is the class decorated with `[Service]`?** Without the attribute, the SG does not generate a registration. Add `[Service]` to the class.

2. **Does the class implement an interface?** The default behavior registers the service as its first interface. If the class has no interfaces, `PRAG1643` fires. Use `[Service(AsSelf = true)]` for concrete registration, or implement an interface.

3. **Is `Pragmatic.Composition` referenced in the library project?** The SG only generates metadata when the package is referenced. Without it, `[Service]` does not resolve to the Pragmatic attribute at all and nothing is generated — silently.

4. **Is the library project referenced by the host?** The host SG reads `[PragmaticMetadata]` from referenced assemblies. If the library is not referenced (directly or transitively), its services are not discovered.

5. **Check the generated code.** Open `obj/Debug/net10.0/generated/` and look for `_Infra.DI.ServiceRegistration.g.cs` in the library project. Verify the service appears in the `Add{Prefix}Services()` method. In the host project, check `Host.Services.g.cs` for the aggregated registration.

6. **Is the service lifetime correct?** A `Transient` service resolved via `IServiceProvider` (not a scope) will work, but a `Scoped` service resolved outside a scope throws. Verify the resolution context matches the lifetime.

---

## Module Not Detected by the Source Generator

The host builds but the SG does not include a module's services, repositories, or actions.

### Checklist

1. **Does the module class have `[Module]`?** The attribute is required for SG detection. Without it, the module is invisible.

2. **Does the host module have `[Include<TModule>]` or `[Include<TModule, TDatabase>]`?** Library-level `[Module]` declares the module, but the host must explicitly include it.

3. **Is the module assembly referenced by the host project?** Check the `.csproj` for a `<ProjectReference>` or `<PackageReference>` to the module's assembly.

4. **Check `Host.Topology.g.cs` (Debug builds only).** This file contains a topology report listing all discovered modules, their boundaries, and registrations. If your module is not listed, it was not discovered.

5. **Diagnostic `PRAG1690`** reports the count of discovered modules. If it says "Discovered 0 Pragmatic modules", no `[PragmaticMetadata]` attributes were found in any referenced assembly.

### Possible Causes

- The module project does not reference `Pragmatic.Composition` or `Pragmatic.Abstractions`, so no metadata is generated.
- The module project references the SG but the SG is not configured correctly (`OutputItemType="Analyzer"` missing).
- The host references an older build of the module that was compiled before `[PragmaticMetadata]` attributes were generated. Clean and rebuild.

---

## StartupStep Not Executing

Your `IStartupStep` implementation exists, but `ConfigureServices` or `ConfigurePipeline` is never called.

### Checklist

1. **Does the class have `[StartupStep]`?** The attribute is required for discovery. Implementing `IStartupStep` alone is not enough.

2. **Does the class implement `IStartupStep`?** The attribute without the interface triggers `PRAG1630`. Both are required.

3. **Is the step in a library or host project?**
   - **Library project**: The step is registered via `[PragmaticMetadata]`. Verify the library is referenced by the host.
   - **Host project**: The step is processed directly. Verify it builds without errors.

4. **Check the `Order` value.** Steps with the same `Order` execute in an undefined order relative to each other. If your step depends on another step's services, ensure your `Order` is higher.

5. **Diagnostic `PRAG1694`** fires when no `[StartupStep]` registrations are found. If this appears, no steps are being discovered from any referenced assembly.

---

## Pipeline Middleware in Wrong Order

Authentication fails, CORS headers are missing, or middleware behaves unexpectedly.

### Checklist

1. **Check the step `Order` values.** `ConfigurePipeline` calls execute in ascending `Order`. Authentication (typically Order 100) must come before authorization (110). Routing (50) must come before endpoint-dependent middleware.

2. **Review the effective pipeline.** For Debug builds, check `Host.Topology.g.cs` for the ordered list of steps. The generated `ConfigurePipeline` method in `Host.Services.g.cs` shows the exact call sequence.

3. **Built-in steps have fixed orders:** ResponseCompression (25), Routing (50), CORS (75). Your steps must work around these.

4. **Multiple steps with the same Order.** This is valid but the relative order between them is non-deterministic. If order matters, assign different `Order` values.

### Common Pipeline Order Issues

| Symptom | Cause | Fix |
|---------|-------|-----|
| 401 on all requests | Auth middleware after endpoint mapping | Set auth step Order < endpoint mapping |
| CORS headers missing | CORS step after routing | Use built-in `CorsStep` (Order 75) |
| Tenant ID always null | Tenant resolution before authentication | Set tenant step Order > auth step |
| Response not compressed | Compression after response started | Use built-in `ResponseCompressionStep` (Order 25) |

---

## Auto-Registration Not Working for Infrastructure Modules

You added a Pragmatic infrastructure package (e.g., `Pragmatic.MultiTenancy`) but the default registration is not happening.

### Checklist

1. **Is the package referenced in the host project's `.csproj`?** The SG only runs in the host project. Infrastructure packages must be referenced (directly or transitively) from the host.

2. **Is the SG analyzer configured?** The host project must have:

   ```xml
   <ProjectReference Include="..\Pragmatic.SourceGenerator\...\Pragmatic.SourceGenerator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
   ```

3. **Check `Host.Services.g.cs`.** Look for `RegisterAllPragmaticServices`. It should contain calls like `services.AddPragmaticMultiTenancy()` for each detected module.

4. **Verify `FeatureDetector` detection.** The SG detects packages by checking for well-known types via `GetTypeByMetadataName()`. If the type is not found (e.g., the package API changed), detection fails silently.

5. **Clean and rebuild.** SG output is cached aggressively. After adding a new package reference, a clean rebuild ensures the SG re-evaluates all features.

---

## Host Fails to Start

The application crashes or enters maintenance mode on startup.

### Checklist

1. **Check `/maintenance` endpoint.** If maintenance mode is enabled (default), the application serves diagnostic info at `/maintenance` instead of crashing. Navigate to `http://localhost:{port}/maintenance` for error details.

2. **Missing configuration.** Steps with `[RequiresConfig("path")]` cause fail-fast validation. The error message lists all missing keys:

   ```
   Missing required configuration keys: ConnectionStrings:App, Jwt:Key.
   ```

   Add the missing keys to `appsettings.json` or environment variables.

3. **Database connection failure.** If `UseDatabaseEnsureCreated()` or `UseDatabaseMigrate()` is enabled, a database connection error at startup will trigger maintenance mode. Verify the connection string and database availability.

4. **Circular dependency in DI.** If a service's constructor creates a circular chain (`A -> B -> C -> A`), the DI container throws at resolution time. Circular *module* dependencies are caught at compile time by `PRAG1602`; circular *service* dependencies are only caught at runtime.

5. **Check the exception.** If maintenance mode is disabled, the application crashes with a stack trace. Read the exception message. Common causes:
   - `InvalidOperationException: Unable to resolve service` -- missing DI registration
   - `SqlException: Cannot open database` -- wrong connection string
   - `ArgumentException: An item with the same key has already been added` -- duplicate keyed service

---

## Boundary Services Not Isolated

Services from one module are accidentally resolving services from another module's boundary.

### Possible Causes

1. **Missing `[BelongsTo]`.** Without boundary annotation, types float freely and may be registered in the wrong context.

2. **Direct reference instead of interface.** If Module A directly references a concrete class from Module B (instead of depending on an interface), the boundary isolation is broken.

3. **Shared database.** When two modules use `[Include<TModule, SameDatabase>]`, they share a DbContext. This is by design -- their entities are co-located. If isolation is needed, use separate databases.

### Best Practice

Modules should communicate through action invokers (`IDomainActionInvoker<T>`), not direct service references. This ensures boundary isolation and enables future distribution via `[RemoteBoundary]`.

---

## Feature Detection Missing Modules

A Pragmatic package is referenced but the SG does not detect it.

### Possible Causes

1. **Transitive reference too deep.** The SG checks `compilation.GetTypeByMetadataName()` for specific marker types. If the package is referenced transitively but its types are not visible in the compilation, detection fails.

2. **Version mismatch.** The SG looks for specific FQN strings (e.g., `Pragmatic.MultiTenancy.ITenantResolver`). If the package renamed or moved the type, detection breaks. Ensure all Pragmatic packages are on the same version.

3. **SG not updated.** If you updated the runtime package but not the SG, the SG may not know about new feature markers. Keep `Pragmatic.SourceGenerator` version aligned with runtime packages.

---

## Diagnostics Reference

All Composition diagnostics use the `PRAG16xx` range:

| ID | Severity | Category | Description |
|----|----------|----------|-------------|
| PRAG1050 | Error | Package | Duplicate `[UsePackage<T>]` on module |
| PRAG0449 | Error | Package | An imported operation needs a boundary-keyed service and the import named no boundary — use `[UsePackage<TPackage, TBoundary>]` |
| PRAG0450 | Error | Package | Two imports on one module name different boundaries |
| PRAG1601 | Error | Topology | `[IncludeModule<T>]` names no known module |
| PRAG1602 | Error | Topology | Circular dependency detected |
| PRAG1603 | Error | Topology | A hosted module's dependency is neither included nor declared remote |
| PRAG1607 | Warning | Database | Two database-bound modules resolve to the same name, so the 2-arity `[Include]` binding is ambiguous |
| PRAG1608 | Warning | Database | Included module not discovered — its DbContext is **not** registered and will fail at runtime |
| PRAG1609 | Warning | Database | Relational database without `ConfigKey` — the generated DbContext has no connection string |
| PRAG1610 | Error | Schema | Incompatible metadata schema version |
| PRAG1611 | Warning | Schema | Newer metadata schema version than supported |
| PRAG1612 | Info | Schema | Legacy metadata schema version (backward compat) |
| PRAG1630 | Error | Startup | `[StartupStep]` must implement `IStartupStep` |
| PRAG1631 | Error | Startup | `[StartupStep]` must be on a class |
| PRAG1632 | Error | Startup | `[NeedsStep<T>]` references unavailable step type |
| PRAG1640 | Error | Service | `[Service]` requires a class type |
| PRAG1641 | Warning | Service | Dependency not registered |
| PRAG1642 | Warning | Service | Singleton depends on scoped service (captive dependency) |
| PRAG1643 | Warning | Service | No interface found for service |
| PRAG1645 | Error | Service | Abstract class cannot be a service |
| PRAG1646 | Warning | Service | Keyed services require .NET 8+ |
| PRAG1647 | Warning | Service | `[Inject]` members on an open-generic service are ignored — use constructor parameters |
| PRAG1651 | Warning | Database | Boundary has no database configured |
| PRAG1652 | Error | Database | DbContext name collision across different databases |
| PRAG1660 | Error | Decorator | Decorator must implement at least one interface |
| PRAG1661 | Error | Decorator | Decorator missing inner service constructor parameter |
| PRAG1670 | Error | Events | `[EventHandler]` on class not implementing `IDomainEventHandler<T>` |
| PRAG1680 | Warning | Package | `[ExposeEndpoint<T>]` references non-package action |
| PRAG1685 | Error | Remote | `[RemoteBoundary<T>]` and `[Include<T>]` on same module |
| PRAG1686 | Warning | Remote | `[RemoteBoundary<T>]` module has no actions |
| PRAG1687 | Info | Remote | `[RemoteBoundary<T>]` base URL not configured |
| PRAG1690 | Info | Discovery | Discovered Pragmatic modules count |
| PRAG1691 | Info | Discovery | Module composition info |
| PRAG1688 | Warning | Configuration | A configuration key required at startup is missing from `appsettings.json` |
| PRAG1693 | Info | Discovery | No `[Service]` or `[Decorator]` registrations found |
| PRAG1694 | Info | Discovery | No `[StartupStep]` registrations found |
| PRAG1695 | Error | Reference | `Pragmatic.Authorization` referenced without `Pragmatic.Identity`, and the host does not declare `[AnonymousHost]` (see [A Host Without Authentication](/modules/composition/concepts/#a-host-without-authentication)) |
| PRAG1696 | Warning | Reference | `Pragmatic.Identity.Persistence` referenced without `Pragmatic.Authorization` |

Captive dependencies and `BuildServiceProvider()` calls in your own code are reported separately by
`Pragmatic.Abstractions.Analyzers` as `PRAG1450` and `PRAG1451`.

---

## Generated Code Not Compiling

The build fails with errors in SG-generated files (`Host.Entry.g.cs` or `Host.Services.g.cs`).

### Checklist

1. **Are all Pragmatic packages on the same version?** Mismatched versions between runtime packages and the SG can produce incompatible generated code. Ensure `Pragmatic.SourceGenerator`, `Pragmatic.Abstractions`, `Pragmatic.Composition`, and all runtime packages share the same version.

2. **Is the SG analyzer reference correct?** The `.csproj` must have:

   ```xml
   <ProjectReference Include="..\Pragmatic.SourceGenerator\...\Pragmatic.SourceGenerator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
   ```

   Missing `OutputItemType="Analyzer"` means the SG runs as a normal reference, not as a code generator.

3. **Check for namespace conflicts.** If your project defines a type named `PragmaticApp` or `PragmaticHost`, it collides with the generated types. The SG generates into the `Pragmatic.Composition.Hosting` namespace for entry points and into the host's root namespace for `PragmaticHost`.

4. **Clean and rebuild.** SG caching can produce stale output. Run `dotnet clean` followed by `dotnet build` to force a full regeneration.

---

## Remote Boundary Actions Returning Errors

Actions invoked via `[RemoteBoundary]` return `RemoteError` instead of the expected business error.

### Checklist

1. **Is the remote host running?** Verify the remote service is accessible at the configured URL. Check `appsettings.json`:

   ```json
   {
     "Pragmatic": {
       "RemoteBoundaries": {
         "Billing": { "BaseUrl": "https://billing-service:5001" }
       }
     }
   }
   ```

2. **Is the action registered on the remote host?** The remote host must include the module locally (`[Include<BillingModule, BillingDatabase>]`). Without it, the `/_pragmatic/invoke` endpoint cannot dispatch the action.

3. **Check the `RemoteError.Code`.** The error code indicates the failure point:

   | Code | Meaning | Fix |
   |------|---------|-----|
   | `REMOTE_INVOKE_FAILED` | HTTP call failed | Check network, URL, firewall |
   | `REMOTE_DESERIALIZE_FAILED` | Response could not be deserialized | Check JSON serialization compatibility |
   | `REMOTE_UNKNOWN_ERROR` | Error with no ProblemDetails | Check remote host logs |
   | `REMOTE_ERROR` | Business error from remote | Error propagated correctly; inspect the Title/Description |

4. **Configure the HttpClient.** Add timeout and retry policies to the named HttpClient:

   ```csharp
   services.AddHttpClient("Pragmatic.Remote.Billing", client =>
   {
       client.Timeout = TimeSpan.FromSeconds(30);
   });
   ```

---

## FAQ

### Why does my Program.cs only have two lines?

`PragmaticApp.RunAsync(args, configure)` is a stub method. The source generator replaces it with a complete host startup implementation based on discovered modules, services, and configuration. All the registration code lives in the generated `Host.Entry.g.cs` and `Host.Services.g.cs` files.

### How do I see what the SG generated?

In Visual Studio, expand **Dependencies > Analyzers > Pragmatic.SourceGenerator** in Solution Explorer. All generated files are listed there. Alternatively, look in `obj/Debug/net10.0/generated/Pragmatic.SourceGenerator/` on disk.

### Can I mix PragmaticApp.RunAsync with manual service registration?

Yes, via `IStartupStep.ConfigureServices`. The step runs after SG defaults and `IPragmaticBuilder`, so you can register additional services or override existing ones. You cannot directly modify the `WebApplicationBuilder` before `Build()` -- use `IPragmaticBuilder.Services` for that.

### What happens if two modules register the same interface?

DI last-registration-wins applies. The SG processes assemblies in a deterministic order, but the exact order depends on the dependency graph. If you need both implementations, use keyed services. If you need one shared implementation, put it in a shared project.

### Can I use [StartupStep] without [Module]?

Yes. Startup steps are independent of modules. A host project can have `[StartupStep]` classes without declaring a `[Module]`. However, without a module, there is no topology and the SG cannot auto-register infrastructure or map boundaries.

### How do I disable a SG-generated default?

Override it in the `IPragmaticBuilder` callback. DI last-registration-wins means your registration replaces the default:

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    // Replace the default IClock registration
    app.Services.AddSingleton<IClock, FakeClock>();
});
```

### Why does my application enter maintenance mode?

Maintenance mode activates when startup fails and `MaintenanceModeOptions.EnableOnStartupFailure` is true (default). Check the `/maintenance` endpoint for error details. Common causes: missing configuration keys, database connection failures, DI resolution errors.

### How do I add a custom middleware in the right place?

Create an `IStartupStep` with the appropriate `Order` and add middleware in `ConfigurePipeline`. Refer to the order ranges: infrastructure (0-99), module (100-499), application (500+). For example, a request logging middleware should be at Order 80 (after routing but before authentication).

### Can I run multiple IStartupSteps with the same Order?

Yes. Steps with the same `Order` are both executed, but their relative order to each other is non-deterministic. If the order between two steps matters, assign them different `Order` values.

### How do I test without PragmaticApp.RunAsync?

For integration tests, use `WebApplicationFactory<T>` as usual. The SG-generated `PragmaticApp.RunAsync` is the production entry point. In tests, you can override services via `WebApplicationFactory.WithWebHostBuilder`. The `IStartupStep` pattern works with test hosts because the generated code is just standard ASP.NET Core service registration.

---

## Getting Help

- **GitHub Issues**: [github.com/pragmatic-design/Pragmatic.Design/issues](https://github.com/pragmatic-design/Pragmatic.Design/issues)
- **Showcase Examples**: See the `Showcase` project for working composition across all modules.
- **Architecture Concepts**: See [concepts.md](/modules/composition/concepts/) for the 3-Tier model and ecosystem integration.
- **Service Registration**: See [service-registration.md](/modules/composition/service-registration/) for all registration patterns.
- **Startup Pipeline**: See [startup-pipeline.md](/modules/composition/startup-pipeline/) for step ordering and two-phase lifecycle.
- **Remote Boundaries**: See [remote-boundaries.md](/modules/composition/remote-boundaries/) for distributed deployment.
