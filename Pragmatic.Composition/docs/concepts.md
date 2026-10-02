# Architecture and Core Concepts

This guide explains **why** Pragmatic.Composition exists, how its pieces fit together, and how to choose the right abstraction for each situation. Read this before diving into the individual feature guides.

---

## The Problem

As a .NET application grows, dependency injection and application startup accumulate boilerplate that scatters across `Program.cs`, extension methods, and configuration helpers. The developer becomes responsible for keeping all of it consistent, ordered, and discoverable.

### Manual DI: verbose and error-prone

```csharp
var builder = WebApplication.CreateBuilder(args);

// Service registration -- one line per service, repeated for every module
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IShippingService, ShippingService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IAuditService, AuditService>();
// ... 50 more lines, growing with every new class

// Decorators -- manual ordering, easy to get wrong
builder.Services.Decorate<IOrderService, LoggingOrderService>();
builder.Services.Decorate<IOrderService, CachingOrderService>();

// Infra modules -- scattered, no standard ordering
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(config.GetConnectionString("App")));
builder.Services.AddDbContext<BillingDbContext>(o => o.UseSqlServer(config.GetConnectionString("Billing")));
builder.Services.AddAuthentication("Bearer").AddJwtBearer(o => { /* ... */ });
builder.Services.AddAuthorization();
builder.Services.AddCors();
builder.Services.AddResponseCompression();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Middleware -- order matters, but nothing enforces it
app.UseResponseCompression();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
```

A `Program.cs` with 20 services: 80+ lines. Most of it is mechanical wiring. Add a new module and you must remember to register its services, its DbContext, its middleware, and its health checks -- all in the right order, in the right place. Forget one line and the application silently misbehaves.

### Assembly scanning: magical and opaque

```csharp
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Startup>());
builder.Services.AddAutoMapper(typeof(Startup));
builder.Services.AddValidatorsFromAssemblyContaining<OrderValidator>();
```

Solves the verbosity problem but introduces runtime reflection, hidden lifetime defaults, and no compile-time validation. When a service fails to resolve, the stack trace points to `ActivatorUtilities.CreateInstance` deep inside the DI container -- not to the missing registration.

### The fundamental issues

1. **No module boundaries.** All services register into one flat container. Nothing prevents the Billing module from depending on Booking internals.
2. **No standard startup ordering.** Middleware must be ordered correctly, but the ordering is implicit and fragile. Move one line and authentication breaks.
3. **No compile-time validation.** Missing registrations, lifetime mismatches, and circular dependencies surface only at runtime.
4. **No topology awareness.** The application does not know which modules it contains, which databases they use, or how they relate to each other.

---

## The Solution

Pragmatic.Composition inverts the model. You declare the application's **structure** -- modules, boundaries, databases, services -- and the source generator produces all registration code at compile time.

The same application from above:

```csharp
// Program.cs -- the ENTIRE file
await PragmaticApp.RunAsync(args, app =>
{
    if (app.Environment.IsDevelopment())
        app.UseDatabaseEnsureCreated();

    app.UseAuthentication<NoOpAuthenticationHandler>("PragmaticDefault");
}).ConfigureAwait(false);
```

```csharp
// Module topology (one class per module)
[Module]
[Include<OrdersModule, AppDatabase>]
[Include<BillingModule, FinancialDatabase>]
[Include<ShippingModule, AppDatabase>]
[NeedsStep<RoutingStep>]
public sealed class MyAppModule;
```

```csharp
// Service registration (on the class itself)
[Service]
public class OrderService(IOrderRepository repository) : IOrderService { }
```

The source generator reads all `[Module]`, `[Service]`, `[Decorator]`, `[StartupStep]`, and `[Include]` declarations at compile time and produces:

- **`PragmaticApp.RunAsync`** -- the complete host startup implementation
- **`PragmaticHost.RegisterAllPragmaticServices`** -- infrastructure module auto-registration
- **`PragmaticHost.CallConfigureServices`** -- ordered invocation of all `IStartupStep` implementations
- **`PragmaticHost.ConfigurePipeline`** -- ordered middleware pipeline setup
- **`PragmaticHost.MapAllEndpoints`** -- endpoint route mapping
- **Service registration code** per assembly -- `AddMyAppServices()` extension methods
- **Database registration** -- DbContext setup per boundary
- **Topology validation** -- circular dependency detection, missing boundary checks, lifetime mismatch warnings

All at compile time. Zero reflection at runtime. Every generated file is visible under `obj/` and fully debuggable.

---

## How It Works: The 3-Tier Model

Pragmatic.Composition organizes startup into three tiers, each with a distinct responsibility and a fixed execution order.

```
+---------------------------+     +---------------------------+     +---------------------------+
|   Tier 1: Topology        |     |   Tier 2: Module Strategy |     |   Tier 3: Business Wiring |
|                           |     |                           |     |                           |
|   [Module]                | --> |   IPragmaticBuilder       | --> |   IStartupStep            |
|   [Include<T, TDb>]       |     |   Use*() extensions       |     |   ConfigureServices()     |
|   [BelongsTo<T>]          |     |   Program.cs callback     |     |   ConfigurePipeline()     |
|   [IncludeModule<T>]      |     |                           |     |                           |
|                           |     |   Developer configures    |     |   Developer configures    |
|   SG auto-detects         |     |   infrastructure choices  |     |   business logic          |
+---------------------------+     +---------------------------+     +---------------------------+
       Compile-time                     Before steps                     After defaults + builder
```

### Execution order within RunAsync

```
1. WebApplicationBuilder created
2. SG-generated infrastructure defaults (Tier 1 detection -> Tier 2 auto-registration)
3. IPragmaticBuilder callback (Tier 2 overrides via DI last-registration-wins)
4. IStartupStep.ConfigureServices (Tier 3, sorted by Order ascending)
5. WebApplication built
6. IStartupStep.ConfigurePipeline (Tier 3, sorted by Order ascending)
7. Endpoint mapping
8. Application runs
```

This ensures a deterministic, predictable startup sequence. Infrastructure defaults are established first, the developer overrides what they need, and business logic wires last.

---

## Module System

Modules are the building blocks of a Pragmatic application. They define boundaries, declare dependencies, and organize code into cohesive units.

### Declaring a Module

A module is an empty sealed class decorated with `[Module]`:

```csharp
[Module(Name = "MyApp.Catalog", Version = "1.0.0", Description = "Product catalog management")]
public sealed class CatalogModule;
```

The `Name`, `Version`, and `Description` properties are optional metadata used in topology reports and diagnostics. The class itself has no methods or properties -- it is a marker type.

### Module Types

There are two categories of modules:

| Category | Where | Purpose |
|----------|-------|---------|
| **Domain module** | Class library project | Contains entities, actions, services for a bounded context |
| **Host module** | Executable project | Declares deployment topology: which modules are included and how |

The source generator automatically distinguishes them based on the output type of the project. An executable project (`.csproj` with `<OutputType>Exe</OutputType>`) triggers Host mode; a class library triggers Library mode.

### Library-Level Dependencies

When a domain module depends on types from another module (e.g., Booking needs Catalog for property lookups), declare it with `[IncludeModule<T>]`:

```csharp
[Module(Name = "MyApp.Booking", Description = "Reservation management")]
[IncludeModule<CatalogModule>]
public sealed class BookingModule;
```

This is a compile-time dependency declaration. It does not register services -- it tells the SG (and other developers) that Booking expects Catalog to be present. The host does not bring Catalog in on Booking's behalf: a host that hosts Booking must also `[Include<CatalogModule>]`, or declare it `[RemoteBoundary<CatalogModule>]` if it runs elsewhere — otherwise the build fails with **PRAG1603**.

### Host-Level Topology

The host module declares the deployment topology -- which domain modules are included, which databases they use, and which infrastructure steps are needed:

```csharp
[Module]
[Include<AccountsModule, AppDatabase>]
[Include<BillingModule, FinancialDatabase>]
[Include<BookingModule, AppDatabase>]
[Include<CatalogModule, AppDatabase>]
[NeedsStep<InternationalizationStep>]
[NeedsStep<RoutingStep>]
public sealed class ShowcaseHostModule;
```

### Include Variants

The `[Include]` attribute has three overloads:

| Attribute | Purpose |
|-----------|---------|
| `[Include<TModule>]` | Include module without a database (no persistence) |
| `[Include<TModule, TDatabase>]` | Include module wired to a database (DbContext name auto-derived from module) |
| `[Include<TModule, TDatabase, TDbContext>]` | Include module with explicit DbContext class name |

### Databases

Physical databases are declared by deriving from `PragmaticDatabase`:

```csharp
[PragmaticDatabase(Provider = DatabaseProvider.SqlServer, ConfigKey = "ConnectionStrings:App")]
public sealed class AppDatabase : PragmaticDatabase { }
```

Supported providers: `SqlServer`, `PostgreSql`, `SQLite`, `MySql`, `InMemory`.

The SG generates a DbContext per boundary-database pair. Multiple modules sharing the same database class get the same DbContext, ensuring entities are co-located in one migration set.

### A Host Without Authentication

Endpoints require authorization by default. A host that deliberately has no authentication (a household app on a LAN, a kiosk, an internal tool behind a reverse proxy that authenticates) says so on its host module:

```csharp
[Module]
[AnonymousHost]
[Include<CatalogModule, AppDatabase>]
public sealed class HostModule;
```

The generator reads the declaration at compile time. The generated endpoint root carries no `RequireAuthorization()`, and PRAG1695 is not reported. Only the root default is dropped: authorization that an endpoint or a group declares for itself is generated as before.

Without the declaration, a host that references `Pragmatic.Authorization` (`Pragmatic.Actions` depends on it) and not `Pragmatic.Identity` fails the build with **PRAG1695**, because its endpoints would require an authorization that nothing in the host can grant. Setting `RequireAuthorizationByDefault = false` at runtime does not replace the declaration, since the build cannot see it.

### BelongsTo

Entities and actions declare which boundary they belong to using `[BelongsTo<T>]`:

```csharp
[Entity]
[BelongsTo<BookingBoundary>]
public partial class Reservation { /* ... */ }
```

This enables the SG to route entity configurations, repositories, and actions to the correct DbContext and registration group.

### What Gets Generated for Modules

| Generated Artifact | Content | When |
|-------------------|---------|------|
| `[PragmaticMetadata]` assembly attributes | Module info, service registrations, startup steps as JSON metadata | Library mode (every class library) |
| `_Infra.DI.ServiceRegistration.g.cs` | `Add{Prefix}Services()` extension method with all `[Service]` and `[Decorator]` registrations | Library mode, when services exist |
| `_Infra.DI.PipelineStepRegistration.g.cs` | Step registration code | Library mode, when startup steps exist |
| `Host.Entry.g.cs` | `PragmaticApp.RunAsync()` implementation | Host mode |
| `Host.Services.g.cs` | `PragmaticHost` static class with all aggregated registrations | Host mode |
| `Host.Topology.g.cs` | Topology report string (Debug builds only) | Host mode |

---

## IPragmaticBuilder

`IPragmaticBuilder` is the Tier 2 configuration surface. It runs in `Program.cs` as a callback to `PragmaticApp.RunAsync` and is your single point for infrastructure decisions.

### Interface Definition

Defined in `Pragmatic.Abstractions` (namespace `Pragmatic.Composition`):

```csharp
public interface IPragmaticBuilder
{
    IServiceCollection Services { get; }
    IConfiguration Configuration { get; }
    IHostEnvironment Environment { get; }
}
```

The concrete implementation is `PragmaticBuilder` in `Pragmatic.Composition.Host`, which adds:

```csharp
public PragmaticOptions Options { get; }
```

`PragmaticOptions` controls cross-cutting hosting concerns: telemetry, maintenance mode, database initialization, and JSON serialization defaults.

### Use\*() Extension Methods

Each Pragmatic infrastructure module contributes extension methods on `IPragmaticBuilder`. IntelliSense shows only the methods for packages you have referenced:

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    // Database initialization (from Pragmatic.Persistence.EFCore)
    app.UseDatabaseEnsureCreated();

    // Multi-tenancy strategy (from Pragmatic.MultiTenancy)
    app.UseMultiTenancy(mt => mt.UseHeader());

    // Authentication handler (from Pragmatic.Identity)
    app.UseAuthentication<NoOpAuthenticationHandler>("PragmaticDefault");

    // JWT authentication (from Pragmatic.Identity.Jwt)
    app.UseJwtAuthentication(jwt =>
    {
        jwt.SigningKey = app.Configuration["Jwt:Key"]!;
        jwt.Issuer = "my-app";
    });

    // Authorization configuration (from Pragmatic.Authorization)
    app.UseAuthorization(authz =>
    {
        // Endpoints require authentication by default (PragmaticEndpointsOptions.RequireAuthorizationByDefault);
        // opt a single endpoint out with [AllowAnonymous].
        authz.MapRole<AdminRole>();
    });

    // Logging sinks (from Pragmatic.Logging)
    app.UseLogging(log =>
    {
        log.AddConsole(PragmaticConsoleConfiguration.ForDevelopment());
        log.AddFile("logs/app-{Date}.log");
    });

    // File storage strategy (from Pragmatic.Storage)
    app.UseStorage(sp => new LocalDiskFileStorage(basePath, sp.GetRequiredService<ILogger<LocalDiskFileStorage>>()));
});
```

### How Overrides Work: Last-Registration-Wins

The SG auto-registers infrastructure defaults based on detected packages (see Auto-Registration below). The `IPragmaticBuilder` callback runs **after** these defaults. Because .NET DI uses last-registration-wins semantics, your `Use*()` calls replace the defaults:

```
Step 1: SG auto-registers SystemClock as IClock (Singleton)
Step 2: You call app.UseClock<FakeClock>() in the callback
Result: FakeClock wins (last registration)
```

This pattern means you never need to "remove" a default -- you simply override it.

### PragmaticOptions

The builder's `Options` property controls host-level behavior:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `EnsureDatabaseCreated` | `bool` | `false` | Call `EnsureCreatedAsync` on all DbContexts at startup |
| `AutoMigrations` | `bool` | `false` | Apply pending EF Core migrations at startup |
| `Telemetry` | `TelemetryOptions` | (enabled) | OpenTelemetry configuration |
| `MaintenanceMode` | `MaintenanceModeOptions` | (enabled) | Maintenance mode on startup failure |

---

## IStartupStep

`IStartupStep` is the Tier 3 configuration surface. It handles business-level wiring: registering application services, configuring middleware, and setting up infrastructure that depends on business decisions.

### Interface Definition

```csharp
public interface IStartupStep
{
    int Order => 0;

    void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    { }

    void ConfigurePipeline(IApplicationBuilder app)
    { }
}
```

Both methods have default implementations (no-op), so you implement only what you need. Mark implementations with `[StartupStep]` for auto-discovery:

```csharp
[StartupStep]
[RequiresConfig("ConnectionStrings:App")]
public class MyAppStartupStep : IStartupStep
{
    public int Order => 60;

    public void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOpenApi(options => options.AddResultTypeSupport());
        services.AddScoped<IQueryFilter, OwnReservationsFilter>();
    }

    public void ConfigurePipeline(IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }
}
```

### Two-Phase Execution

The generated host executes startup steps in two passes, both sorted by `Order` ascending:

1. **Phase 1 -- ConfigureServices**: Runs before `builder.Build()`. Registers DI services.
2. **Phase 2 -- ConfigurePipeline**: Runs after `builder.Build()`. Configures HTTP middleware.

Steps that only implement `ConfigureServices` (no `ConfigurePipeline`) participate in Phase 1 but produce no middleware.

### Order Ranges

Follow these conventions to avoid ordering conflicts:

| Range | Purpose | Examples |
|-------|---------|----------|
| **0-24** | Early infrastructure | Custom exception handlers |
| **25** | Response compression | `ResponseCompressionStep` (built-in) |
| **26-49** | Pre-routing middleware | Static files, HTTPS redirect |
| **50** | Routing | `RoutingStep` (built-in) |
| **51-74** | Post-routing, pre-auth | Tenant resolution |
| **75** | CORS | `CorsStep` (built-in) |
| **76-99** | Pre-auth infrastructure | Rate limiting, request logging |
| **100-499** | Module steps | Authentication (100), Authorization (110), I18n (200) |
| **500+** | Application steps | Business services, OpenAPI, custom middleware |

### Built-In Steps

`Pragmatic.Composition.Host` provides three infrastructure steps that are automatically included when you use `[NeedsStep<T>]`:

| Step | Order | Pipeline Action |
|------|-------|-----------------|
| `ResponseCompressionStep` | 25 | `app.UseResponseCompression()` |
| `RoutingStep` | 50 | `app.UseRouting()` |
| `CorsStep` | 75 | `app.UseCors()` |

### NeedsStep Declaration

Modules declare which steps they need via `[NeedsStep<T>]` on the host module:

```csharp
[Module]
[Include<BookingModule, AppDatabase>]
[NeedsStep<RoutingStep>]
[NeedsStep<InternationalizationStep>]
public sealed class ShowcaseHostModule;
```

The SG aggregates all `[NeedsStep<T>]` declarations, deduplicates by type, and orders by `Order`. You do not instantiate steps manually.

### Configuration Validation

`[RequiresConfig("path")]` declares that a configuration key must exist at startup:

```csharp
[StartupStep]
[RequiresConfig("ConnectionStrings:App")]
[RequiresConfig("Jwt:Key", Description = "JWT signing key for token validation")]
public class SecureStartupStep : IStartupStep { /* ... */ }
```

The SG generates a `ValidateConfiguration()` call that runs before any step. If a required key is missing, the application fails fast with a clear error listing all missing keys.

### IPragmaticBuilder vs IStartupStep -- When to Use Which

| Aspect | `IPragmaticBuilder` | `IStartupStep` |
|--------|---------------------|-----------------|
| **When** | Before all steps | After SG defaults + builder |
| **Scope** | Infrastructure choices | Business wiring |
| **What** | Which auth handler, which storage, which logging | App services, query filters, middleware |
| **Who** | Developer configuring the host | Developer configuring business logic |
| **Count** | One (callback in `RunAsync`) | Multiple, ordered by `Order` |
| **HTTP Pipeline** | No | Yes (`ConfigurePipeline`) |
| **Example** | `app.UseAuthentication<T>(...)` | `services.AddScoped<IQueryFilter, ...>()` |

The rule of thumb: if it is an **infrastructure decision** (auth strategy, logging sink, database mode), use `IPragmaticBuilder`. If it is **business logic** (services, filters, middleware), use `IStartupStep`.

---


## The Health Endpoint

The host publishes the aggregated health of the process — `IHostStatus` plus every registered
`IHostHealthContributor` — at `/health`, **by default**.

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    app.UseHealthEndpoint("/internal/alive");  // somewhere else
    app.DisableHealthEndpoint();               // nowhere
});
```

It is on by default, and the trade is deliberate: mapping a route is an opinion about the host's route
table, so an application that already owns `/health` has to move one of the two. That is a visible,
one-line problem. The alternative — off by default — produces a health endpoint nobody turns on:
contributors collected on every host and their verdict published on none, which is the failure you
cannot see.

The collision is handled rather than avoided. `HealthEndpointMapper` reads the route table before
mapping, and when the path is already taken it steps aside and says so, leaving the application's own
endpoint alone. Two endpoints on one route would otherwise be an `AmbiguousMatchException` thrown on
the first request — for a health route, at the moment a probe asks, and the answer it gets is a `500`
that reads like the application is broken.

`DisableHealthEndpoint()` exists so that "no health endpoint" can be a decision in the source rather
than an omission: an application that simply never called `UseHealthEndpoint()` — which was every
application here — left no way to tell the two apart. The contributors are still collected either
way; what stops is the route.

⚠️ The endpoint is marked `TenantAgnosticEndpoint`, so a multi-tenant host does not refuse a probe for
want of a tenant header. Without it, a host that enabled this endpoint answered `/health` with **400**
and, with `X-Tenant-Id`, with **200** — measured, and enough to have a liveness probe restart the
application for ever.

## PragmaticApp.RunAsync

`PragmaticApp` is a static class with stub methods in `Pragmatic.Composition.Host`. The source generator replaces the implementation with fully wired startup code based on discovered modules.

### Entry Points

| Method | Description | Use Case |
|--------|-------------|----------|
| `RunAsync(args, configure?)` | Web application host with full HTTP pipeline | APIs, web apps |
| `RunWorkerAsync(args, configure?)` | Background worker host without HTTP | Background jobs, message consumers |

### What the Generated RunAsync Does

The source generator produces a `PragmaticApp.RunAsync` method that follows this sequence:

```
1. Create WebApplicationBuilder
2. Validate required configuration (fail-fast)
3. RegisterAllPragmaticServices (SG defaults for detected infra modules)
4. Create PragmaticBuilder and invoke configure callback
5. Configure telemetry (OpenTelemetry auto-setup)
6. Configure JSON defaults (camelCase, enum-as-string, null handling)
7. Register databases (DbContext per boundary-database pair)
8. Call ConfigureServices on all startup steps (sorted by Order)
9. Build the WebApplication
10. Database initialization (EnsureCreated or Migrate if enabled)
11. Configure pipeline (all startup steps, sorted by Order)
12. Map all endpoints
13. Run the application
14. On failure: enter maintenance mode (if enabled)
```

### Maintenance Mode

When the application fails to start and `MaintenanceModeOptions.EnableOnStartupFailure` is true (default), the application enters maintenance mode instead of crashing:

| Endpoint | Response |
|----------|----------|
| `/health` | 503 with status, error type, message |
| `/maintenance` | 503 with full error details, stack trace (dev only), suggestions |
| Any other path | 503 with pointer to `/health` and `/maintenance` |

This ensures monitoring systems can detect the failure and operators can diagnose the issue without SSH access to the container.

---

## Service Registration

Services are the runtime types that compose your application. Pragmatic.Composition provides multiple registration mechanisms, all of which produce compile-time code (except assembly scanning, which is runtime-based).

### [Service] -- Attribute-Based

The simplest and recommended approach:

```csharp
// Scoped (default), registered as IOrderService (first interface)
[Service]
public class OrderService : IOrderService { }

// Explicit interface via generic attribute
[Service<IPaymentProvider>(Key = "stripe")]
public class StripePaymentProvider : IPaymentProvider { }

// Register as concrete type
[Service(AsSelf = true, Lifetime = Lifetime.Singleton)]
public class CacheWarmupService { }
```

The SG generates `services.AddScoped<IOrderService, OrderService>()` (and equivalent for other lifetimes) at compile time. Cross-assembly discovery works through `[PragmaticMetadata]` assembly attributes -- each library declares what it registers, the host aggregates automatically.

### [Decorator] -- Service Wrapping

Decorators wrap existing services with cross-cutting behavior:

```csharp
[Decorator(Order = 1)]
public class LoggingPricingService(
    IReservationPricingService inner,
    ILogger<LoggingPricingService> logger) : IReservationPricingService
{
    public decimal CalculatePrice(Reservation reservation)
    {
        logger.LogInformation("Calculating price...");
        return inner.CalculatePrice(reservation);
    }
}
```

Order semantics: Lower order = closer to the real service. Higher order = outermost wrapper. Call flow: `Outer (Order=2) -> Inner (Order=1) -> RealService`.

### [ServiceFactory] -- Custom Construction

For services that need custom construction logic:

```csharp
[ServiceFactory]
public class InfraFactories
{
    [Factory(Lifetime = Lifetime.Singleton)]
    public IDbConnection CreateConnection(IConfiguration config)
        => new SqlConnection(config.GetConnectionString("Default"));
}
```

### [Inject] -- Property Injection

For optional dependencies or post-construction initialization:

```csharp
[Service]
public class NotificationService : INotificationService
{
    [Inject]
    public IEmailSender? EmailSender { get; set; }  // Optional

    [Inject(Required = true)]
    public IMessageQueue Queue { get; set; } = null!;  // Required
}
```

### Assembly Scanning

For convention-based registration without attributes (runtime reflection):

```csharp
services.Scan(scan => scan
    .FromAssemblyOf<OrderService>()
    .AddClasses(filter => filter.AssignableTo<ICommandHandler>())
    .AsImplementedInterfaces()
    .WithScopedLifetime());
```

Use `[Service]` for all known services. Reserve scanning for bulk convention-based scenarios where attribute decoration is impractical.

---

## Auto-Registration of Infrastructure Modules

When a Pragmatic infrastructure package is referenced in the host's `.csproj`, the source generator auto-detects it via `FeatureDetector` and registers sensible defaults. This is Tier 1 -> Tier 2 automation.

### Detected Modules

| Module | Detection Marker | Default Registration |
|--------|-----------------|---------------------|
| Temporal | `Pragmatic.Temporal.IClock` | `SystemClock` as `IClock` (Singleton) |
| Resilience | `Pragmatic.Resilience` assembly | `AddPragmaticResilience()` + bind `ResilienceOptions` |
| Caching | `Pragmatic.Caching` assembly | `AddHybridCache()` + `AddPragmaticCaching()` |
| I18n | `Pragmatic.Internationalization` assembly | `AddPragmaticI18n()` |
| Identity | `Pragmatic.Identity.AspNetCore` assembly | `AddPragmaticAuthorization()` |
| MultiTenancy | `Pragmatic.MultiTenancy` assembly | `AddPragmaticMultiTenancy()` (SingleTenant default) |
| FeatureFlags | `Pragmatic.FeatureFlags` assembly | `AddPragmaticFeatureFlags()` |
| Discovery | `Pragmatic.Discovery` assembly | `AddPragmaticDiscovery()` |
| Authorization | `Pragmatic.Authorization` assembly | `AddPragmaticAuthorization()` |

### How Detection Works

`FeatureDetector` runs at compile time inside the SG. It checks for well-known type names using `compilation.GetTypeByMetadataName()`. When a type is found, the corresponding `DetectedFeatures` flag is set to `true`. The `PragmaticHostTemplate` uses these flags to generate `RegisterAllPragmaticServices()`.

```
Referenced NuGet: Pragmatic.MultiTenancy
  -> FeatureDetector finds Pragmatic.MultiTenancy types
  -> DetectedFeatures.HasMultiTenancy = true
  -> PragmaticHostTemplate generates: services.AddPragmaticMultiTenancy()
  -> You can override in IPragmaticBuilder: app.UseMultiTenancy(mt => mt.UseHeader())
```

### Composition-by-Presence

This model means adding a capability is as simple as adding a NuGet reference. Remove the reference and the registration disappears. No manual wiring needed.

---

## Cross-Assembly Discovery

Each domain module (class library) emits `[PragmaticMetadata]` assembly-level attributes with category-specific JSON data. The host generator reads these from referenced assemblies and generates aggregated registration code.

### How It Works

1. **Library mode**: The SG generates `[assembly: PragmaticMetadata(category, json)]` attributes for every `[Service]`, `[Decorator]`, `[StartupStep]`, `[Module]`, and `[EventHandler]` in the assembly.
2. **Host mode**: The SG reads `[PragmaticMetadata]` from all referenced assemblies (via `MetadataReader`), deserializes the JSON, and generates a unified `PragmaticHost` class that aggregates all registrations.

### Metadata Categories

`MetadataCategory` enum values control how metadata is grouped:

| Category | Content |
|----------|---------|
| `DI` | Service and decorator registrations |
| `Startup` | Pipeline step declarations |
| `Module` | Module topology (name, version, dependencies) |
| `Actions` | Domain action declarations |
| `Endpoints` | Endpoint route registrations |
| `Validation` | Validator registrations |
| `Mapping` | Mapping configuration |
| `Persistence` | Entity and repository metadata |
| `EventHandlers` | Domain event handler registrations |
| `HealthChecks` | Health check registrations |
| `Identifiers` | Custom ID type converters |
| `Translations` | Translation key registrations |
| `Caching` | Cache configuration metadata |
| `Configuration` | Configuration section bindings |
| `HostTopology` | Host-level includes and databases |

This mechanism is invisible to the developer. You add `[Service]` to a class in a library, and the host automatically picks it up.

---

## Packages

Packages are pre-built collections of entities, actions, and services that fuse into a host module. They are the distribution mechanism for reusable bounded contexts.

### Defining a Package

```csharp
public class LocalIdentityPackage : IPackageDefinition
{
    public static string PackageName => "Identity.Local";
    public static string? RoutePrefix => "identity/local";
    public static string? Description => "Local username/password identity provider";
}
```

### Importing a Package

```csharp
[Module(Name = "MyApp.Accounts")]
[UsePackage<LocalIdentityPackage>]
public sealed class AccountsModule;
```

When a module uses a package, the SG:
1. Includes the package's services, actions, and entities in the module's registrations
2. Routes package endpoints under the package's `RoutePrefix`
3. Validates that required dependencies are available

### A package that needs a database

A package declares no `[Boundary]` — that is what makes it a package — and `DbContext` and
`IUnitOfWork` are registered **keyed by boundary**. So an operation in a package that declares one of
those gets an invoker asking for it **unkeyed**, and that constructor is compiled inside the package:
whoever imports it cannot add the key afterwards.

The key therefore comes from the composition, which is where the import is:

```csharp
[Module(Name = "MyApp.Accounts")]
[UsePackage<AuthorizationManagementPackage, AccountsBoundary>]
public sealed class AccountsModule;
```

The package writes into its own metadata which services it asks for without a key; the importing
module's generated registration answers that request from the named boundary's keyed instance. Neither
side guesses: the package knows what it needs, the importer knows which boundary it has.

- A package that touches no persistence — both of the packages this framework ships — is imported with
  `[UsePackage<TPackage>]` and names nothing.
- A package that needs one and is imported without a boundary is **PRAG0449**, an error. The
  alternative is an application that starts and throws from the first request that reaches the
  operation.
- The bridge is one unkeyed registration per container, so two imports naming **different** boundaries
  is **PRAG0450**: the second would shadow the first, and one package would silently read the other's
  database. The boundary is named per import, but every import on a module has to name the same one.

### IPackageDefinition Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `PackageName` | `string` | Yes | Unique package identifier |
| `RoutePrefix` | `string?` | No | URL prefix for package endpoints |
| `Description` | `string?` | No | Human-readable description |

---

## Remote Boundaries

For distributed deployments, `[RemoteBoundary<TModule>]` replaces a local module with HTTP-based action invokers. The calling code does not change -- it still depends on `IDomainActionInvoker<TAction, TReturn>` -- but the implementation sends HTTP requests instead of executing in-process.

### Monolith to Distributed

```csharp
// Monolith: all modules local
[Module]
[Include<BookingModule, AppDatabase>]
[Include<BillingModule, FinancialDatabase>]
public sealed class MonolithHostModule;

// Distributed: Billing runs on a separate service
[Module]
[Include<BookingModule, AppDatabase>]
[RemoteBoundary<BillingModule>]
public sealed class DistributedHostModule;
```

The SG generates `HttpActionInvoker<TAction, TReturn>` for each action in the remote module. Actions are serialized as JSON, POSTed to `/_pragmatic/invoke`, and deserialized back. The calling code is identical in both topologies.

### Configuration

```json
{
  "Pragmatic": {
    "RemoteBoundaries": {
      "Billing": {
        "BaseUrl": "https://billing-service:5001"
      }
    }
  }
}
```

For full details, see [Remote Boundaries](remote-boundaries.md).

---

## What Gets Generated

The source generator operates in two modes based on the project type:

### Library Mode (Class Libraries)

Generated for every class library that references `Pragmatic.Composition`:

| Generated File | Content | Condition |
|----------------|---------|-----------|
| `_Infra.DI.ServiceRegistration.g.cs` | `Add{Prefix}Services()` extension method | Has `[Service]` or `[Decorator]` types |
| `_Infra.DI.PipelineStepRegistration.g.cs` | Step registration code | Has `[StartupStep]` types |
| `_Infra.DI.ServiceMetadata.g.cs` | `[PragmaticMetadata]` for DI registrations | Has services |
| `_Infra.DI.StartupMetadata.g.cs` | `[PragmaticMetadata]` for startup steps | Has startup steps |
| `_Infra.DI.ModuleMetadata.g.cs` | `[PragmaticMetadata]` for module topology | Has `[Module]` class |
| `_Infra.DI.EventHandlerMetadata.g.cs` | `[PragmaticMetadata]` for event handlers | Has `[EventHandler]` types |

### Host Mode (Executable Projects)

Generated for the host project (the `.csproj` with `<OutputType>Exe</OutputType>`):

| Generated File | Content | Condition |
|----------------|---------|-----------|
| `Host.Entry.g.cs` | `PragmaticApp.RunAsync()`, `RunWorkerAsync()` | Always |
| `Host.Services.g.cs` | `PragmaticHost` class with all registration methods | Always |
| `Host.Topology.g.cs` | Topology report string (Debug only) | Debug builds |
| `Host.EventHandlers.g.cs` | Event handler registrations | Has event handlers |
| `Host.HttpInvoker.{Module}.g.cs` | HTTP action invokers per remote boundary | Has `[RemoteBoundary]` |
| `Host.InvokeEndpoint.g.cs` | `/_pragmatic/invoke` dispatcher | Has local actions |

### PragmaticHost Methods (Host.Services.g.cs)

The `PragmaticHost` static class contains categorized registration methods:

| Method | Purpose |
|--------|---------|
| `RegisterAllPragmaticServices` | Infrastructure defaults (Tier 1 auto-detection) |
| `RegisterAllRepositories` | All repository + read-repository registrations per boundary |
| `RegisterAllDomainActions` | All domain action invoker registrations |
| `RegisterAllPipelineSteps` | All `IStartupStep` instantiation and registration |
| `RegisterAllDatabases` | DbContext registrations per database-boundary pair |
| `RegisterAllServices` | Service + decorator registrations from all assemblies |
| `CallConfigureServices` | Ordered invocation of step ConfigureServices methods |
| `ConfigurePipeline` | Ordered invocation of step ConfigurePipeline methods |
| `MapAllEndpoints` | Endpoint route mapping via `MapPragmaticEndpoints()` |
| `ValidateConfiguration` | Fail-fast validation of `[RequiresConfig]` keys |

---

## Ecosystem Integration

Pragmatic.Composition is the orchestration layer. Every other Pragmatic module plugs into it to register services, expose endpoints, and participate in the startup pipeline.

### How Modules Integrate

| Module | Integration Point | What Gets Registered |
|--------|-------------------|---------------------|
| **Actions** | `[PragmaticMetadata(Actions)]` | Action invokers via `RegisterAllDomainActions()` |
| **Endpoints** | `[PragmaticMetadata(Endpoints)]` | Routes via `MapAllEndpoints()` |
| **Persistence** | `[PragmaticMetadata(Persistence)]` | Repositories, query filters, DbContexts |
| **Validation** | `[PragmaticMetadata(Validation)]` | Validators via generated extension methods |
| **Mapping** | `[PragmaticMetadata(Mapping)]` | Mapping configurations (static, no DI needed) |
| **Events** | `[PragmaticMetadata(EventHandlers)]` | Domain event handlers |
| **Caching** | `[PragmaticMetadata(Caching)]` | Cache profiles and invalidation rules |
| **Identity** | Auto-detected | Authentication and authorization pipeline |
| **MultiTenancy** | Auto-detected | Tenant resolution middleware and services |
| **Resilience** | Auto-detected | Resilience policies and circuit breakers |

### The Key Insight

You never manually wire these integrations. Reference a Pragmatic package in your `.csproj`, add the attributes to your types, and the SG generates all registration code. This is what "composition by presence" means:

1. Add `Pragmatic.Validation` to your `.csproj`
2. Add `[Validator]` to your validator class
3. The SG generates `AddGeneratedValidators()` in the library
4. The host SG discovers the `[PragmaticMetadata]` attribute and includes the call in `RegisterAllPragmaticServices()`

No manual `services.AddXxx()` needed.

---

## Telemetry

The generated `PragmaticApp.RunAsync` automatically configures OpenTelemetry with sensible defaults:

- **Tracing**: ASP.NET Core, HttpClient, EF Core instrumentation. All Pragmatic ActivitySources registered.
- **Metrics**: ASP.NET Core metrics. All Pragmatic Meters registered.
- **Logging**: OTel logging bridge with formatted messages and scopes.
- **Sampling**: AlwaysOn in Development, ratio-based (default 10%) in production.
- **Exporters**: Console in Development, OTLP when `TelemetryOptions.UseOtlpExporter = true`.

Configure via `PragmaticOptions.Telemetry` or `appsettings.json`:

```json
{
  "Telemetry": {
    "Enabled": true,
    "UseOtlpExporter": true,
    "SamplingRatio": 0.1
  }
}
```

---

## See Also

- [Getting Started](getting-started.md) -- Minimal host setup, adding modules step by step
- [Startup Pipeline](startup-pipeline.md) -- IStartupStep ordering, ConfigureServices vs ConfigurePipeline
- [Service Registration](service-registration.md) -- [Service], [Decorator], [ServiceFactory], keyed services
- [Remote Boundaries](remote-boundaries.md) -- [RemoteBoundary], HttpActionInvoker, distributed deployment
- [Common Mistakes](common-mistakes.md) -- Patterns to avoid and how to fix them
- [Troubleshooting](troubleshooting.md) -- Problem/solution guide for common issues
