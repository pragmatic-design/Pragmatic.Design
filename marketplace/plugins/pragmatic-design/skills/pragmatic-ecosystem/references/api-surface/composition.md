# API Surface — Pragmatic.Composition

Reference for host bootstrap, DI, modules and startup. Diagnostics: `../diagnostics.md` (PRAG1600-1699).

## Namespaces

| Namespace | Assembly | Content |
|---|---|---|
| `Pragmatic.Composition.Attributes` | `Pragmatic.Abstractions` | `[Service]`, `[Decorator]`, `[Inject]`, `[Module]`, `[IncludeModule]`, `[Include]`, `[StartupStep]`, `[RequiresConfig]`, `[UsePackage]`, `[RemoteBoundary]`, `[PragmaticDatabase]`, `[ServiceFactory]`, `[Factory]`, `Lifetime` |
| `Pragmatic.Composition.Attributes` | `Pragmatic.Composition.Host` | `[NeedsStep]`, `[AnonymousHost]` |
| `Pragmatic.Composition.Enums` | `Pragmatic.Abstractions` | `DatabaseProvider` |
| `Pragmatic.Composition.Database` | `Pragmatic.Abstractions` | `PragmaticDatabase` (base) |
| `Pragmatic.Composition` | `Pragmatic.Abstractions` | `IPragmaticBuilder`, `IPackageDefinition` |
| `Pragmatic.Composition.Abstractions` | `Pragmatic.Composition.Host` | `IStartupStep` |
| `Pragmatic.Composition.Hosting` | `Pragmatic.Composition.Host` | `PragmaticApp` |

There is no `Pragmatic.DependencyInjection` package.

---

# DI

## `[Service]` / `[Service<TInterface>]`

```csharp
[Service]                                              // Scoped lifetime, target = 1st interface
[Service(Lifetime = Lifetime.Singleton)]
[Service(AsSelf = true)]                               // register as concrete type
[Service<IMyService>]                                  // generic form — preferred
[Service<IMyService>(Key = "stripe")]                  // keyed service (.NET 8+)
```

- **Target**: concrete `class` (PRAG1640/1645). Default lifetime `Scoped`.
- The SG generates the centralized registration in `_Infra.*.Registration.g.cs`.

## `[Decorator]`

```csharp
[Decorator(Order = 1)]                                 // ascending Order = closer to the original
public sealed class LoggingService(IMyService inner) : IMyService;
```

Must implement the same interface and accept the inner service in the constructor (PRAG1660/1661).

## `[Inject]`

```csharp
[Inject] [Inject(Required = true)] [Inject(Key = "stripe")]
```

- **Target**: property or method, inside a `[Service]` class. (Actions/Mutations use uninitialized private fields instead.)

## `[ServiceFactory]` / `[Factory]`

`[ServiceFactory]` on a factory class (singleton); `[Factory]` on its methods (configurable `Lifetime`).

---

# Modules

## `[Module]`

```csharp
[Module(Name = "MyApp.Sales", Version = "1.0.0", Description = "...")]
public sealed class SalesModule;
```

## `[IncludeModule<TModule>]`

Module-to-module dependency. A dependency that names no known module → PRAG1601; cycle → PRAG1602; a hosted module's dependency the host neither includes nor declares remote → PRAG1603.

## `[NeedsStep<TStep>]`

On `[Module]` — declares that the module requires an `IStartupStep` provided by a package. Type not found → PRAG1632.

## `[Include<...>]` (host)

Attaches a module to the host and a database. Three overloads:

```csharp
[Include<TModule>]                          // module without a database
[Include<TModule, TDatabase>]               // TDatabase : PragmaticDatabase
[Include<TModule, TDatabase, TDbContext>]   // explicit DbContext (name collision)
```

## `[AnonymousHost]` (host)

On the host `[Module]` — the host deliberately has no authentication. The generated endpoint root
carries no `RequireAuthorization()` and PRAG1695 is not reported. Without it, a host that references
Authorization and not Identity fails with PRAG1695 (Error). Authorization an endpoint or a group
declares for itself is unaffected.

---

# Startup

## `IStartupStep`

```csharp
public interface IStartupStep
{
    int Order => 0;                          // 0-99 infra, 100-499 modules, 500+ consumer
    void ConfigureServices(IServiceCollection services, IConfiguration config, IHostEnvironment env);
    void ConfigurePipeline(IApplicationBuilder app);
}
```

`[StartupStep]` on the class; `[RequiresConfig("key", Description?)]` (`AllowMultiple`) for fail-fast on missing configuration. Step without `IStartupStep` → PRAG1630.

---

# Hosting

## `PragmaticApp`

```csharp
await PragmaticApp.RunAsync(args, app => { /* Use*() */ });        // web
await PragmaticApp.RunWorkerAsync(args, app => { ... });           // background worker, no HTTP
```

There is no console entry point.

`RunAsync` applies auto-discovery: registers generated DI, mounts endpoints, activates interceptors.

## `IPragmaticBuilder`

```csharp
public interface IPragmaticBuilder
{
    IServiceCollection Services { get; }
    IConfiguration Configuration { get; }
    IHostEnvironment Environment { get; }
}
```

## `Use*()` catalog

Available when the package is referenced:

| Method | Package |
|---|---|
| `UsePragmaticMigrations([Action])` | Migrations |
| `UseDatabaseEnsureCreated()` / `UseDatabaseMigrate()` | Composition.Host |
| `UseAuthorization(Action)` | Authorization |
| `UseJwtAuthentication()` (the `Jwt` section) / `UseJwtAuthentication(Action)` / `UseAuthentication[<T>](...)` | Identity |
| `UseMultiTenancy(Action)` | MultiTenancy.AspNetCore |
| `UseJobs(Action)` | Jobs |
| `UseMessaging(Action)` | Messaging |
| `UseI18N(Action)` / `UseTemporal(Action)` / `UseLogging(Action)` | I18n / Temporal / Logging |
| `UseStorage(...)` / `UseNotifications(Action)` | Storage / Notifications |
| `UseMaintenanceMode([Action])` | Composition.Host |

---

# Database

## `[PragmaticDatabase]`

```csharp
[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
public sealed class AppDatabase : PragmaticDatabase;
```

- `Provider` (`DatabaseProvider`): `SqlServer | PostgreSql | SQLite | MySql | InMemory`.
- `ConfigKey` — `IConfiguration` key for the connection string. Optional `MigrationConfigKey` to use a separate DDL connection.
- Boundary without an assigned database → PRAG1651; DbContext name collision → PRAG1652.

---

# UsePackage / RemoteBoundary

## `[UsePackage<TPackage>]`

```csharp
[UsePackage<LocalIdentityPackage>(RoutePrefix = "auth")]
```

- On `[Module]`. `TPackage : IPackageDefinition`. Merges the package's actions/entities/services into the module. Duplicate → PRAG1050.
- `[ExposeEndpoint<TAction>(HttpVerb, route)]` (on `[Module]`) exposes an action over HTTP; `AllowAnonymous`, `AdditionalPermissions`. Usually a package action — those carry no `[Endpoint]`, because what they publish is the consumer's to decide — but ⚠️ nothing restricts it: the module's own action works too, mapped on the host root instead of the package prefix.

## `[RemoteBoundary<TModule>]`

```csharp
[RemoteBoundary<BillingModule>]                        // URL from config
[RemoteBoundary<BillingModule>(BaseUrl = "http://...")]
```

- On host `[Module]`. Mutually exclusive with `[Include<TModule>]` (PRAG1685).
- **Generates**: HTTP invoker + dispatcher. Named HttpClient `Pragmatic.Remote.{Module}`. Config key `Pragmatic:RemoteBoundaries:{Module}:BaseUrl`.
- **MVP does not support**: cross-boundary mutations, distributed events, service discovery, gRPC, retry/circuit-breaker.

---

# Multi-tenancy

`UseMultiTenancy(Action<MultiTenancyBuilder>)` — tenant resolution strategies:

| Method | Source |
|---|---|
| `UseSingleTenant(id)` | Fixed tenant |
| `UseHeader(name?)` | Header (default `X-Tenant-Id`) |
| `UseClaim(type?)` | Claim (default `tenant_id`) |
| `UseSubdomain()` | Host subdomain |
| `UseRoute(param?)` | Route parameter (default `tenantId`) |
| `UseResolver<T>()` | Custom `ITenantResolver` |

Several `Use*` chain in call order — `UseHeader().UseClaim()` tries the header, then the claim — and the
first non-empty answer wins; one strategy is registered as itself, several as a `CompositeTenantResolver`.

---

# Key diagnostics

| ID | Sev | Cause |
|---|---|---|
| PRAG1601 | Error | `[IncludeModule<T>]` names no known module |
| PRAG1602 | Error | Circular dependency between modules |
| PRAG1603 | Error | A hosted module's dependency is not hosted or remote |
| PRAG1630 | Error | `[StartupStep]` without `IStartupStep` |
| PRAG1632 | Error | `[NeedsStep<T>]` type not found |
| PRAG1640 | Error | `[Service]` on a non-class |
| PRAG1641 | Warning | Unregistered DI dependency |
| PRAG1642 | Warning | Singleton depends on Scoped (captive dependency) |
| PRAG1645 | Error | `[Service]` on an abstract class |
| PRAG1651 | Warning | Boundary without a database |
| PRAG1652 | Error | DbContext name collision |
| PRAG1661 | Error | `[Decorator]` without inner service parameter |
| PRAG1685 | Error | `[RemoteBoundary]` + `[Include]` on the same module |
| PRAG1695 | Error | Authorization without Identity, and the host does not declare `[AnonymousHost]` |

---

# Showcase examples

> ⚠️ Paths into the **upstream Pragmatic.Design repository**, not into your project. If you installed these skills from the marketplace you do not have these files, and you do not need them: everything above is self-contained. Reach for them only with that repository open.

| What | File |
|---|---|
| `Program.cs` with `Use*()` | `examples/showcase/src/Showcase.Host/Program.cs` |
| Host module `[Include<M,DB>]` + `[NeedsStep]` | `examples/showcase/src/Showcase.Host/ShowcaseHostModule.cs` |
| `IStartupStep` + `[RequiresConfig]` | `examples/showcase/src/Showcase.Host/ShowcaseStartupStep.cs` |
| `[PragmaticDatabase]` | `examples/showcase/src/Showcase.Host/ShowcaseAppDatabase.cs` |
| Distributed app `[RemoteBoundary<T>]` | `examples/showcase/src/Showcase.Host.Distributed/ShowcaseDistributedModule.cs` |
| `[UsePackage]` + `[ExposeEndpoint]` | `examples/showcase/src/Showcase.Accounts/AccountsModule.cs` |
