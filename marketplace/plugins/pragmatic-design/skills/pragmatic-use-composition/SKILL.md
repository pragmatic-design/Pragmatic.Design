---
name: pragmatic-use-composition
description: Use when wiring the host, DI, modules, startup steps or the module-to-database topology — Pragmatic.Composition, PragmaticApp, IPragmaticBuilder, [Service], [Decorator], [Module], [Include], [StartupStep].
---

# Pragmatic Use Composition

**Covers:** Bootstrap host, DI, modules, and startup pipeline with Pragmatic.Composition consumed from NuGet. [Service], [Decorator], [Module], [Include], [StartupStep], IPragmaticBuilder, PragmaticApp.

`Pragmatic.Composition` is the hosting and DI layer. It works by **composition by presence**: whatever you reference (NuGet package or boundary library) is discovered by the source generator and activated. You do not write manual `services.Add*()` calls for Pragmatic features.

## When to use

- You are creating the host (`Program.cs`) of a Pragmatic app.
- You are registering custom services, decorators, modules, or startup steps.
- You are choosing the infrastructure strategy (auth, storage, jobs) via `Use*()`.

For structuring into modules/boundaries and architectural decisions: `pragmatic-architecture`.

## Packages

```xml
<PackageReference Include="Pragmatic.Abstractions" Version="1.0.0-alpha.*" />   <!-- attributes -->
<PackageReference Include="Pragmatic.Composition" Version="1.0.0-alpha.*" />    <!-- host runtime -->
<PackageReference Include="Pragmatic.SourceGenerator" Version="1.0.0-alpha.*">
  <IncludeAssets>analyzers; build; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

Dense API surface: **`../pragmatic-ecosystem/references/api-surface/composition.md`**.

Public namespaces: `Pragmatic.Composition.Attributes`, `Pragmatic.Composition.Enums`, `Pragmatic.Composition.Database`, `Pragmatic.Composition` (`IPragmaticBuilder`), `Pragmatic.Composition.Abstractions` (`IStartupStep`), `Pragmatic.Composition.Hosting` (`PragmaticApp`). There is **no** `Pragmatic.DependencyInjection` package.

## 3-tier config — the mental model

```
Topology (compile-time)  →  Strategy (Program.cs)       →  Business wiring (IStartupStep)
[Module] [Include]          IPragmaticBuilder.Use*()       ConfigureServices / ConfigurePipeline
SG auto-detects             Dev chooses infrastructure      Dev does domain wiring
```

| | `IPragmaticBuilder` (`Use*()`) | `IStartupStep` |
|---|---|---|
| Purpose | Infrastructure choices (auth scheme, transport, storage) | Business wiring (services, filters, OpenAPI) |
| How many | One, callback in `PragmaticApp.RunAsync` | Many, ordered by `Order` |
| HTTP pipeline | No | Yes (`ConfigurePipeline`) |

## Host bootstrap

```csharp
using Pragmatic.Composition.Hosting;

await PragmaticApp.RunAsync(args, app =>
{
    // Strategy tier: infrastructure choices
    app.UsePragmaticMigrations();
    app.UseAuthorization(authz => { /* ... */ });
}).ConfigureAwait(false);
```

`PragmaticApp` has two entry points: `RunAsync` (web) and `RunWorkerAsync` (background worker, no HTTP). `RunAsync` applies auto-discovery: registers generated DI, mounts endpoints, activates interceptors.

## Core patterns

### 1. Service registration

```csharp
using Pragmatic.Composition.Attributes;

[Service]                                           // Scoped lifetime (default), target = first interface
public sealed class OrderPricingService : IOrderPricingService;

[Service<IOrderPricingService>(Lifetime = Lifetime.Singleton)]
public sealed class CachedPricingService : IOrderPricingService;

[Service(Key = "stripe")]                           // keyed service (.NET 8+)
public sealed class StripeProvider : IPaymentProvider;
```

- Prefer the generic form `[Service<TInterface>]` (type-safe). `AsSelf = true` registers as the concrete type.
- The SG generates centralised registration; do not call `services.AddScoped<...>()` manually.
- Diagnostics: PRAG1640 (non-class), PRAG1642 (singleton→scoped captive dependency), PRAG1645 (abstract class).

When construction needs logic — a connection string read, a client built from options — write a
factory method instead of a `[Service]`:

```csharp
[ServiceFactory]                                    // the class is registered as a singleton
public sealed class InfrastructureFactories
{
    [Factory(Lifetime = Lifetime.Singleton)]         // default Scoped; the return type is the service
    public IPaymentGateway CreateGateway(IConfiguration config) => new PaymentGateway(config["Payments:ApiKey"]!);
}
```

Parameters are resolved from DI. Factories in the host and in referenced boundary libraries are all
registered by the generated `AddPragmaticServiceFactories()`.

### 2. Decorator

```csharp
[Decorator(Order = 1)]                              // ascending Order = closer to the original
public sealed class LoggingPricingService(IOrderPricingService inner) : IOrderPricingService;
```

Must implement the same interface as the decorated service and accept it as a constructor parameter (**PRAG1661** if missing).

### 3. Inject into a service

Inside a `[Service]` class you can use `[Inject]` on a property/method (`Required`, `Key`). **Action/Mutation** classes use uninitialised private fields instead — see `pragmatic-use-actions-endpoints`.

### 4. Module

```csharp
[Module(Name = "MyApp.Sales")]
public sealed class SalesModule;
```

A boundary library declares one `[Module]`. To depend on another module: `[IncludeModule<CatalogModule>]` — a declaration, not a registration: the host that hosts this module must `[Include<>]` Catalog too, or declare it `[RemoteBoundary<>]` (PRAG1603). To declare that the module requires a startup step from a package: `[NeedsStep<RoutingStep>]`.

A host that deliberately has no authentication (LAN app, kiosk, tool behind an authenticating proxy) puts `[AnonymousHost]` on its host `[Module]`. The generated endpoint root then does not require authorization, and PRAG1695 is not reported. There is no runtime option to set and no `NoWarn`.

### 5. StartupStep — business wiring

The interface and the attribute live in **different namespaces** — you need both usings.

```csharp
using Pragmatic.Composition.Abstractions;   // IStartupStep
using Pragmatic.Composition.Attributes;     // [StartupStep], [RequiresConfig]

[StartupStep]
[RequiresConfig("ConnectionStrings:App")]                    // fail-fast if the key is missing
public sealed class AppStartupStep : IStartupStep
{
    public int Order => 500;                                 // 0-99 infra, 100-499 modules, 500+ consumer

    public void ConfigureServices(IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        services.AddDataScopeRule<EurInvoiceScopeRule, Invoice>();
    }

    public void ConfigurePipeline(IApplicationBuilder app) { /* custom middleware */ }
}
```

Use this for domain wiring. Infrastructure choices belong in `Program.cs` via `Use*()`.

### 6. Database marker

```csharp
using Pragmatic.Composition.Database;
using Pragmatic.Composition.Enums;

[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
public sealed class AppDatabase : PragmaticDatabase;
```

`DatabaseProvider`: `SqlServer | PostgreSql | SQLite | MySql | InMemory`. Optional `MigrationConfigKey` to separate the DDL connection. Details on boundary↔database mapping and `[Include<TModule, TDatabase>]`: `pragmatic-use-persistence` and `pragmatic-architecture`.

### 7. `Use*()` catalogue (Strategy tier)

Available on `IPragmaticBuilder` when the corresponding package is referenced:

| Method | Purpose |
|---|---|
| `UsePragmaticMigrations()` | Declarative schema diff |
| `UseDatabaseEnsureCreated()` / `UseDatabaseMigrate()` | Dev / prod schema |
| `UseAuthorization(...)` / `UseJwtAuthentication(...)` / `UseAuthentication<T>(...)` | Permissions and auth |
| `UseMultiTenancy(...)` | Tenant resolution (header/claim/subdomain/route) |
| `UseJobs(...)` | Background jobs |
| `UseMessaging(...)` | Bus, outbox, saga |
| `UseI18N(...)` / `UseTemporal(...)` / `UseLogging(...)` / `UseStorage(...)` / `UseNotifications(...)` | Cross-cutting |
| `UseMaintenanceMode(...)` | Maintenance mode |

Infrastructure topology choices (provider, transport, scheme) go here; domain wiring goes in `IStartupStep`.

**A host that fails to start stays up, answering `503`.** That is `MaintenanceModeOptions.EnableOnStartupFailure`
(default `true`), and it is on without calling `UseMaintenanceMode()`. Turn it off with
`UseMaintenanceMode(m => m.EnableOnStartupFailure = false)`, or from configuration with
`Pragmatic:MaintenanceMode:EnableOnStartupFailure = false` — the configuration key can only switch it
off, and `"false"` is the one value it reads. Tests should set it: otherwise a startup failure surfaces
as a `503` on the first request instead of as the exception that caused it.

### 8. UsePackage — integrating a full-stack package

```csharp
[Module(Name = "MyApp.Accounts")]
[UsePackage<LocalIdentityPackage>(RoutePrefix = "auth")]
[ExposeEndpoint<SignInUser>(HttpVerb.Post, "sign-in", AllowAnonymous = true)]   // POST auth/sign-in → AccessToken
public sealed class AccountsModule;
```

`[UsePackage<T>]` folds the actions/entities/services of a self-contained Pragmatic package into the module; `[ExposeEndpoint<T>]` (on the `[Module]`) exposes an action over HTTP. ⚠️ It is not restricted to package actions — the module's own work too, mapped on the host root rather than under the package prefix — but that is the case it exists for, since a package's actions carry no `[Endpoint]` of their own.

### 9. A `[Service]` may depend on what somebody else registers

A module's compilation sees its own `[Service]` classes and nothing else, so the check behind
**PRAG1641** needs each of the other two cases to say so. Both are declarations, and neither is a list
you maintain:

| Who registers it | What says so | Example |
|---|---|---|
| The host, through a `Use*` call | `[ProvidedByHost(Lifetime.X)]` on the contract, in the package that registers it | `IFileStorage`, `IEmailSender`, `IStringLocalizer`, `IClock`, `ITenantContext`, every repository |
| Another module of the same host | that module's DI metadata, read from the reference | a sibling's `[Service]`, and the `I{Module}Reads` a `[Published]` query generates |

So a class like this compiles and registers itself, with no line in `Program.cs`:

```csharp
[Service<IOverdueReminderSweep>]
public sealed class OverdueReminderSweep(
    IRegistryReads registry,     // the sibling module registers it
    IFileStorage files,          // app.UseStorage(...) does
    IEmailSender email,          // app.UseEmail(...) does
    IRepository<Invoice> invoices) : IOverdueReminderSweep;
```

⚠️ **Name the lifetime when you write `[ProvidedByHost]` on a contract of your own.** It is what lets
PRAG1642 still refuse a singleton that captures a per-request contract; a bare `[ProvidedByHost]` says
"the host registers it, and I am not saying with what", which is right only when the host really
decides (Validation takes the lifetime as a parameter).

⚠️ And it is a statement, not a check: if the host never calls the extension that registers the
contract, the dependency fails when it is first resolved, not at build time.

## Most frequent diagnostics

| ID | Sev | Trigger | Fix |
|---|---|---|---|
| **PRAG1601** | Error | `[IncludeModule<T>]` names no known module | Fix the reference |
| **PRAG1602** | Error | Circular dependency between modules | Break the cycle |
| **PRAG1603** | Error | The host hosts a module whose dependency it does not host | `[Include<T>]` the dependency, or `[RemoteBoundary<T>]` |
| **PRAG1632** | Error | `[NeedsStep<T>]` type not found | Reference the package that provides the step |
| **PRAG1641** | Warning | DI dependency not registered | Add `[Service]`, or a manual registration. Not reported for a contract that says who registers it — see below |
| **PRAG1642** | Warning | Singleton depends on Scoped | Align lifetimes |
| **PRAG1645** | Error | `[Service]` on an abstract class | Make it concrete or use an interface |
| **PRAG1651** | Warning | Boundary with no database assigned | Assign via `[Include<M,DB>]` |
| **PRAG1685** | Error | `[RemoteBoundary]` + `[Include]` on the same module | Choose one |
| **PRAG1695** | Error | Host references Authorization and not Identity, with no `[AnonymousHost]` | Add `Pragmatic.Identity.AspNetCore`, or declare `[AnonymousHost]` on the host module |

## Troubleshooting

**`PragmaticApp` not resolved** — Reference `Pragmatic.Composition` and use `using Pragmatic.Composition.Hosting`.

**Service not injected** — Does the class have `[Service]`? Is the SG referenced as an analyzer? Check `PRAG1641`.

**`Use*()` not available** — The target module's package is not referenced by the host (e.g. `UseJobs` requires `Pragmatic.Jobs`).

**Generated DI registrations to inspect** — `obj/Debug/net10.0/generated/Pragmatic.SourceGenerator/`, files `_Infra.*.Registration.g.cs` and `PragmaticHost.*.g.cs`.

## Build verification

```powershell
dotnet build                                                # PRAG16xx = DI/module issues
dotnet test path\to\App.Tests --no-restore -v minimal
```

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Invoicing example application — code
that compiles and that `Invoicing.IntegrationTests` exercises — and kept identical to it by the gate: the
host's `Program.cs` and topology, the database marker, a module that includes another, a boundary, and
two `[Service<T>]` classes — one replacing a framework default, one depending on what other packages
register.
