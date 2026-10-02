---
title: "Startup Pipeline"
description: "This guide explains how `IStartupStep` orchestrates application startup, the two-phase lifecycle, ordering rules, and how built-in steps compose with applicatio"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Composition/docs/startup-pipeline.md
sidebar:
  order: 5
---
This guide explains how `IStartupStep` orchestrates application startup, the two-phase lifecycle, ordering rules, and how built-in steps compose with application steps.

## IStartupStep Interface

`IStartupStep` defines two phases, both optional:

```csharp
// Pragmatic.Composition.Abstractions namespace
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

- **`ConfigureServices`** runs during the DI container build phase (before the app is built).
- **`ConfigurePipeline`** runs after the app is built, configuring the HTTP middleware pipeline.
- **`Order`** determines execution sequence. Lower values execute first. Default is 0.

Both methods have default implementations (no-op), so you can implement only what you need.

## Two-Phase Execution

The generated `PragmaticApp.RunAsync` executes startup in this order:

1. **Create `WebApplicationBuilder`** with standard configuration (JSON, environment variables)
2. **SG-generated infrastructure defaults** (Clock, Resilience, Caching, etc. -- from `FeatureDetector`)
3. **`IPragmaticBuilder` callback** (your `Program.cs` `Use*()` calls -- overrides defaults via DI last-registration-wins)
4. **Phase 1 -- `ConfigureServices`**: All `IStartupStep` implementations, sorted by `Order` ascending
5. **Build the `WebApplication`**
6. **Phase 2 -- `ConfigurePipeline`**: All `IStartupStep` implementations, sorted by `Order` ascending
7. **Map endpoints** (generated endpoint mapping)
8. **Run the application**

If startup fails and `MaintenanceModeOptions.EnableOnStartupFailure` is true (default), the application enters maintenance mode instead of crashing.

## Order Ranges

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

## Built-In Steps

`Pragmatic.Composition.Host` provides three infrastructure steps:

### ResponseCompressionStep (Order 25)

```csharp
public class ResponseCompressionStep : IStartupStep
{
    public int Order => 25;
    public void ConfigurePipeline(IApplicationBuilder app) => app.UseResponseCompression();
}
```

### RoutingStep (Order 50)

```csharp
public class RoutingStep : IStartupStep
{
    public int Order => 50;
    public void ConfigurePipeline(IApplicationBuilder app) => app.UseRouting();
}
```

### CorsStep (Order 75)

```csharp
public class CorsStep : IStartupStep
{
    public int Order => 75;
    public void ConfigurePipeline(IApplicationBuilder app) => app.UseCors();
}
```

## Declaring Step Dependencies

Modules declare which steps they need via `[NeedsStep<T>]`:

```csharp
[Module]
[Include<BookingModule, AppDatabase>]
[NeedsStep<InternationalizationStep>]
[NeedsStep<RoutingStep>]
public sealed class ShowcaseHostModule;
```

The SG aggregates all `[NeedsStep<T>]` declarations across all modules, deduplicates by type, and orders by `IStartupStep.Order`. You do not manually instantiate steps -- the SG handles it.

## Writing a Startup Step

### Minimal Example (Services Only)

```csharp
using Pragmatic.Composition.Abstractions;
using Pragmatic.Composition.Attributes;

namespace MyApp.Host;

[StartupStep]
public class OpenApiStartupStep : IStartupStep
{
    public int Order => 600;

    public void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOpenApi();
    }
}
```

### Full Example (Services + Pipeline)

```csharp
[StartupStep]
[RequiresConfig("ConnectionStrings:App")]
public class ShowcaseStartupStep : IStartupStep
{
    public int Order => 60;

    public void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Generated configuration binding
        services.AddShowcaseConfiguration(configuration);

        // Query filter infrastructure
        services.AddShowcaseQueryFilters();

        // Lookup caches
        services.AddShowcaseLookupCaches();

        // Custom query filter
        services.AddScoped<IQueryFilter, OwnReservationsFilter>();

        // Pre/post processors
        services.AddScoped<IEndpointPreProcessor, TenantValidationPreProcessor>();
        services.AddScoped<IEndpointPostProcessor, AuditLogPostProcessor>();

        // OpenAPI
        services.AddOpenApi(options => options.AddResultTypeSupport());
    }

    public void ConfigurePipeline(IApplicationBuilder app)
    {
        // Tenant resolution (before auth)
        app.UseMiddleware<TenantResolutionMiddleware>();

        // Identity from headers (dev/test)
        app.UseMiddleware<HeaderUserMiddleware>();

        // Auth pipeline
        app.UseAuthentication();
        app.UseAuthorization();

        // Dev-only: ASP.NET's runtime document. The compile-time one, and Scalar over it, are
        // published by the generated host (Pragmatic.Endpoints.OpenApi, Scalar.AspNetCore referenced).
        if (app is WebApplication webApp && webApp.Environment.IsDevelopment())
            webApp.MapOpenApi("/openapi/runtime.json");
    }
}
```

## Configuration Validation

`[RequiresConfig("path")]` declares that a configuration key must exist at startup. Multiple attributes are allowed:

```csharp
[StartupStep]
[RequiresConfig("ConnectionStrings:App")]
[RequiresConfig("Jwt:Key", Description = "JWT signing key for token validation")]
public class SecureStartupStep : IStartupStep { /* ... */ }
```

The host generator reads all `[RequiresConfig]` declarations and emits a `ValidateConfiguration()` call that runs before any step's `ConfigureServices`. If a required key is missing, the application fails fast with a message listing all missing keys:

```
Missing required configuration keys: ConnectionStrings:App, Jwt:Key.
Ensure the connection strings are configured in appsettings.json or environment variables.
```

## IPragmaticBuilder vs IStartupStep

These two mechanisms serve different purposes and run at different times:

| Aspect | `IPragmaticBuilder` (Program.cs) | `IStartupStep` |
|--------|----------------------------------|-----------------|
| **When** | Before all steps | After SG defaults + builder |
| **Scope** | Infrastructure choices | Business wiring |
| **What** | Which auth handler, which storage, which logging | App services, query filters, middleware |
| **Who** | Developer configuring the host | Developer configuring business logic |
| **Count** | One (callback in `RunAsync`) | Multiple, ordered by `Order` |
| **HTTP Pipeline** | No | Yes (`ConfigurePipeline`) |
| **Example** | `app.UseAuthentication<T>(...)` | `services.AddScoped<IQueryFilter, ...>()` |

The rule of thumb: if it is an **infrastructure decision** (auth strategy, logging sink, database mode), use `IPragmaticBuilder`. If it is **business logic** (services, filters, middleware), use `IStartupStep`.

## Pipeline Order Visualization

For the Showcase application, the effective pipeline order is:

```
Order 25  : ResponseCompressionStep     -- UseResponseCompression()
Order 50  : RoutingStep                 -- UseRouting()
Order 60  : ShowcaseStartupStep         -- ConfigureServices() + ConfigurePipeline()
              UseMiddleware<TenantResolutionMiddleware>()
              UseMiddleware<HeaderUserMiddleware>()
              UseAuthentication()
              UseAuthorization()
Order 75  : CorsStep                    -- UseCors()
Order 200 : InternationalizationStep    -- UseRequestLocalization()
            [Endpoint mapping]          -- MapPragmaticEndpoints()
```

Steps that only implement `ConfigureServices` (no `ConfigurePipeline`) participate in Phase 1 but produce no middleware.
