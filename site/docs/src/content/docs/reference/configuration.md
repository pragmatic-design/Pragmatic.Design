---
title: Configuration
description: "Three-tier configuration model: topology, module strategy, business wiring."
---

Pragmatic Design uses a **3-tier configuration model**. Each tier answers a different question and lives in a different place.

```
Topology (compile-time)  →  Module Strategy (Program.cs)  →  Business Wiring (IStartupStep)
       SG auto-detect               IPragmaticBuilder              IStartupStep
```

| Tier | Where | What | Who writes it |
|------|-------|------|---------------|
| **Topology** | `[Module]`, `[Boundary]`, `[BelongsTo<T>]`, `[UsePackage<T>]` | Compile-time structure and module dependencies | Source Generator (automatic) |
| **Module Strategy** | `Program.cs` via `IPragmaticBuilder.Use*()` | Infrastructure choices: auth handler, storage backend, cache provider | Developer (explicit) |
| **Business Wiring** | `IStartupStep` | Services, query filters, OpenAPI, feature-specific DI | Developer (explicit) |

## Tier 1: Topology (Source Generator)

You don't configure topology; you **declare** it with attributes, and the unified Source Generator detects it:

```csharp
[Module] public sealed class BookingModule;

[Boundary] public sealed class ReservationsBoundary;

[BelongsTo<ReservationsBoundary>]
public partial class CreateReservation : DomainAction<ReservationResult> { ... }

[Entity]
[HasComments]          // a trait: the Comments package generates its entity, actions and endpoints
public partial class Reservation { ... }
```

At compile time the generator emits:
- module discovery (`PragmaticHost`): who is registered, in what order
- package loading (`[UsePackage<T>]` metadata fusion) and entity traits (`[HasComments]`, `[HasTags]`, …)
- boundary/sub-boundary grouping for endpoints and actions
- feature flags (`DetectedFeatures`) based on referenced assemblies

Nothing to tweak in `Program.cs`. If the Source Generator got it wrong, the fix is in the attribute declaration or the referenced NuGet.

## Tier 2: Module Strategy (`IPragmaticBuilder`)

Modules that have a **strategy to pick** (auth scheme, storage backend, transport) expose a `Use*()` extension on the `IPragmaticBuilder`. You call them as statements inside the `PragmaticApp.RunAsync` callback (the parameter is the builder; it also exposes `Configuration` and `Environment`):

```csharp
using Pragmatic.Composition.Hosting;

await PragmaticApp.RunAsync(args, app =>
{
    // Authentication: config-driven (JWT in production)
    app.UseJwtAuthentication(jwt =>
    {
        jwt.SigningKey = app.Configuration["Jwt:Key"]!;
        jwt.Issuer = app.Configuration["Jwt:Issuer"];
    });

    // Authorization: compose roles from module definitions
    app.UseAuthorization(authz => authz.MapRole<BookingManager>());

    // Multi-tenancy: resolve the tenant from an HTTP header
    app.UseMultiTenancy(mt => mt.UseHeader());

    // Messaging: in-process Channels transport + auditing
    app.UseMessaging(msg =>
    {
        msg.UseChannels();
        msg.EnableAuditing();
    });

    // Background jobs
    app.UseJobs(jobs => jobs.WithWorkerCount(2));

    // Declarative schema diff (replaces EF Core migrations)
    app.UsePragmaticMigrations();

    // File storage: LocalDisk for dev; swap for Azure/S3 in prod
    app.UseStorage(sp => new LocalDiskFileStorage(
        Path.Combine(app.Environment.ContentRootPath, "wwwroot"),
        sp.GetRequiredService<ILogger<LocalDiskFileStorage>>()));
});
```

### Available `Use*()` extensions (1.0.0-alpha)

These are the strategy extensions that ship today on `IPragmaticBuilder`:

| Extension | Purpose | Module |
|-----------|---------|--------|
| `UseAuthorization` | Role/group/permission stores, resource authorizers, permission cache | Authorization |
| `UseDevelopmentIdentity` | A signed-in development user, in Development only | Identity.AspNetCore |
| `UseAuthentication<THandler>` | Plug a custom authentication handler | Identity.AspNetCore |
| `UseJwtAuthentication` | JWT bearer authentication | Identity.Local.Jwt |
| `UseOidcAuthentication` | An external OpenID Connect provider | Identity.Oidc |
| `UseKeycloakAuthentication` | Keycloak, with its role mapping | Identity.Keycloak |
| `UseMultiTenancy` | Tenant resolution: `.UseClaim()`, `.UseHeader()`, `.UseSubdomain()`, `.UseRoute()`, `.UseSingleTenant()` | MultiTenancy.AspNetCore |
| `UseMessaging` | Transport (`.UseInMemory()` / `.UseChannels()` / `.UseRabbitMq()` / `.UseKafka()` / `.UseAzureServiceBus()` / `.UseSqlTransport()`), outbox, sagas | Messaging |
| `UseJobs` | Worker count, polling; `.UseEfCore()` with `.UseEfCorePersistence()` for durable jobs | Jobs |
| `UsePragmaticMigrations` | Declarative schema diff, applied on startup | Migrations |
| `UseDatabaseEnsureCreated` / `UseDatabaseMigrate` | How the host brings its databases up | Composition.Host |
| `UseStorage` | File storage backend (provider factory) | Storage |
| `UseI18N` | Cultures, JSON translations, ProblemDetails localization | Internationalization.AspNetCore |
| `UseTemporal` | Default time zone, holiday provider | Temporal |
| `UseJson` | Register generated JSON contexts; switch off the reflection fallback for AOT | Abstractions |
| `UseNotifications` | Delivery channels, audience | Notifications |
| `UseEmail` | Templated email rendering and transport | Email |
| `UseLogging` | Console / rolling-file sinks | Logging |
| `UseApiDocumentation` | Publish the OpenAPI document in every environment, not only Development | Endpoints.OpenApi |
| `UseMcp` | Endpoints exposed as MCP tools | Endpoints.Mcp |
| `UseHealthEndpoint` | The host health endpoint (on by default) | Composition.Host |
| `UseMaintenanceMode` | Runtime maintenance toggle + admin panel | Composition.Host |
| `UseAgent` | Local coordination daemon connection; backs `IControlPlane`, `IClusterLeadership`, config/flag/tenant stores | Agent.Client |

Agent-backed service discovery is registered on the service collection, not the builder:
`builder.Services.UseAgentDiscovery()` (Agent.Discovery).

Default rule: **a `Use*()` is a choice, and where a module has a sensible default it applies without
one**: in-memory stores, in-process transports, a passthrough cache. Where no default is safe, the
host refuses to start and says what is missing: an application whose endpoints require authorization
needs an authentication method outside Development.

:::note[Configured by presence, not a `Use*()`]
Not every module exposes a strategy builder. **Caching, Resilience, and Feature Flags** activate by package presence and are tuned via `appsettings.json` and/or an `IStartupStep` (defaults apply automatically). A single uniform fluent surface (e.g. `UseIdentity(...)`/`UseCaching(...)` sub-builders chained together) is a **roadmap** direction, not the shipped shape; see the [Roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md). Today, prefer the per-module `Use*()` above or `IStartupStep` for the rest.
:::

## Tier 3: Business Wiring (`IStartupStep`)

Business decisions that don't belong in a module strategy go in one or more `IStartupStep` implementations, classes the host discovers and runs in order.

```csharp
public sealed class BookingStartup : IStartupStep
{
    public int Order => 100;  // lower runs first (default 0)

    public void ConfigureServices(
        IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddScoped<IRoomPricingService, DynamicPricingService>();
        services.AddDataScopeRule<HighSeasonRoomScopeRule, Room>();
    }

    public void ConfigurePipeline(IApplicationBuilder app)
    {
        app.UseMiddleware<RequestTimingMiddleware>();
    }
}
```

`IStartupStep` is discovered automatically at compile time by the Source Generator, with no manual registration. Both methods are optional; steps run in `Order` ascending (default: 0).

### When to use which

| Need | Tier |
|------|------|
| "Which auth scheme?" / "Which cache provider?" | Tier 2 (`IPragmaticBuilder.Use*()`) |
| "Register a service the app needs" | Tier 3 (`IStartupStep.ConfigureServices`) |
| "Customize the HTTP pipeline (custom middleware)" | Tier 3 (`IStartupStep.ConfigurePipeline`) |
| "Add a domain event handler" | Implicit: the SG discovers `IDomainEventHandler<T>` automatically |
| "Change how a module itself behaves" | Tier 2 if supported, else ask / open issue |

## `appsettings.json`: where does it fit?

Configuration values (connection strings, feature flags, tenant URLs) live in `appsettings.json` as usual:

```json
{
  "ConnectionStrings": { "App": "Host=localhost;Database=app" },
  "Pragmatic": {
    "RemoteBoundaries": {
      "Billing": { "BaseUrl": "http://billing.internal" }
    }
  }
}
```

A host that calls a module in another process reads its address from
`Pragmatic:RemoteBoundaries:{Module}:BaseUrl`, and the generator reports a missing one at build time.
Each module documents its own keys on its page.

## Cheat sheet

```csharp
// Program.cs: minimal Pragmatic app
using Pragmatic.Composition.Hosting;

await PragmaticApp.RunAsync(args);
// Every module's default applies automatically (in-memory / in-process / passthrough).
```

```csharp
// Program.cs: production-shaped app
using Pragmatic.Composition.Hosting;

await PragmaticApp.RunAsync(args, app =>
{
    app.UseJwtAuthentication(jwt => jwt.SigningKey = app.Configuration["Jwt:Key"]!);
    app.UseAuthorization(authz => authz
        .MapRole<BookingManager>()
        .UsePermissionCache(TimeSpan.FromMinutes(5)));
    app.UseMessaging(msg =>
    {
        msg.UseRabbitMq();
        msg.EnableAuditing();
    });
    app.UseJobs(jobs =>
    {
        jobs.UseEfCore();
        jobs.UseEfCorePersistence();
    });
    app.UsePragmaticMigrations();
    app.UseStorage(sp => new LocalDiskFileStorage(
        Path.Combine(app.Environment.ContentRootPath, "wwwroot"),
        sp.GetRequiredService<ILogger<LocalDiskFileStorage>>()));
});
```

For the per-module `Use*()` signature and options, see the module's page in the sidebar.
