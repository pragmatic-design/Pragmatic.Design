# Common Mistakes

These are the most common issues developers encounter when using Pragmatic.Composition. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Missing [Module] Attribute on the Host Module Class

**Wrong:**

```csharp
// MyApp.Host/AppModule.cs
[Include<CatalogModule, AppDatabase>]
[Include<BookingModule, AppDatabase>]
public sealed class AppModule;
```

**Compile result:** The `[Include]` attributes are ignored. The SG does not detect a host module, so no `PragmaticApp.RunAsync` implementation is generated. At runtime, the stub `PragmaticApp.RunAsync` runs -- which does nothing meaningful -- or the build fails because the generated host code is missing.

**Right:**

```csharp
[Module]
[Include<CatalogModule, AppDatabase>]
[Include<BookingModule, AppDatabase>]
public sealed class AppModule;
```

**Why:** The `[Module]` attribute is the trigger that tells the SG this class defines a module. Without it, `[Include]`, `[NeedsStep]`, and `[RemoteBoundary]` are invisible to the generator. The SG uses `[Module]` to determine whether the project is a library (class library) or a host (executable), which controls the entire code generation pipeline.

---

## 2. Registering Infrastructure Services in IStartupStep Instead of IPragmaticBuilder

**Wrong:**

```csharp
[StartupStep]
public class AppStartupStep : IStartupStep
{
    public int Order => 60;

    public void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Infrastructure choices buried in a startup step
        services.AddAuthentication("Bearer")
            .AddJwtBearer(options =>
            {
                options.Authority = configuration["Jwt:Authority"];
            });

        services.AddSingleton<IClock, SystemClock>();

        services.AddDbContext<AppDbContext>(o =>
            o.UseSqlServer(configuration.GetConnectionString("App")));
    }
}
```

**Runtime result:** Works, but the infrastructure choices are hidden inside a startup step with an arbitrary `Order`. Other steps that run before `Order 60` cannot depend on the authentication scheme being registered. If another step at Order 50 needs `IAuthenticationService`, it fails. The SG-generated defaults may also conflict because they run before steps.

**Right:**

```csharp
// Program.cs -- infrastructure choices
await PragmaticApp.RunAsync(args, app =>
{
    app.UseJwtAuthentication(jwt =>
    {
        jwt.Authority = app.Configuration["Jwt:Authority"]!;
    });
}).ConfigureAwait(false);

// Startup step -- business wiring only
[StartupStep]
public class AppStartupStep : IStartupStep
{
    public int Order => 60;

    public void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOpenApi();
        services.AddScoped<IQueryFilter, OwnOrdersFilter>();
    }
}
```

**Why:** `IPragmaticBuilder` runs before all startup steps and after SG defaults, making it the correct place for infrastructure decisions (auth strategy, database, logging, storage). `IStartupStep` is for business-level wiring that depends on those infrastructure choices being already in place. Mixing the two makes the startup order fragile and hides important decisions inside steps that may not run in the expected sequence.

---

## 3. Wrong IStartupStep Ordering

**Wrong:**

```csharp
[StartupStep]
public class AuthStep : IStartupStep
{
    public int Order => 200;  // Runs AFTER business step

    public void ConfigurePipeline(IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }
}

[StartupStep]
public class BusinessStep : IStartupStep
{
    public int Order => 100;  // Runs BEFORE auth

    public void ConfigurePipeline(IApplicationBuilder app)
    {
        // Authorization middleware not yet registered!
        app.UseMiddleware<TenantResolutionMiddleware>();
    }
}
```

**Runtime result:** The middleware pipeline is `TenantResolution -> Authentication -> Authorization`. If `TenantResolutionMiddleware` reads claims from `HttpContext.User`, they are not populated yet because authentication has not run. All tenant resolution logic returns null/empty.

**Right:**

```csharp
[StartupStep]
public class AuthStep : IStartupStep
{
    public int Order => 100;  // Auth first

    public void ConfigurePipeline(IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }
}

[StartupStep]
public class BusinessStep : IStartupStep
{
    public int Order => 200;  // Business after auth

    public void ConfigurePipeline(IApplicationBuilder app)
    {
        app.UseMiddleware<TenantResolutionMiddleware>();
    }
}
```

**Why:** `IStartupStep.ConfigurePipeline` calls are executed in ascending `Order`. ASP.NET Core middleware runs in the order it is registered. Authentication must come before any middleware that reads claims, and authorization must come before any middleware that checks permissions. Follow the order range conventions: infrastructure (0-99), module steps (100-499), application steps (500+).

---

## 4. Using [Service] on an Abstract Class

**Wrong:**

```csharp
[Service]
public abstract class BaseNotificationService : INotificationService
{
    public abstract Task SendAsync(Notification notification);
}
```

**Compile result:** `PRAG1645` error -- "[Service] cannot be applied to abstract class 'BaseNotificationService'". The SG cannot generate `services.AddScoped<INotificationService, BaseNotificationService>()` because DI cannot instantiate abstract classes.

**Right:**

```csharp
public abstract class BaseNotificationService : INotificationService
{
    public abstract Task SendAsync(Notification notification);
}

[Service]
public class EmailNotificationService(IEmailSender sender) : BaseNotificationService
{
    public override Task SendAsync(Notification notification)
        => sender.SendAsync(notification.To, notification.Subject, notification.Body);
}
```

**Why:** The DI container needs to create instances. Abstract classes cannot be instantiated. Put `[Service]` on the concrete implementation, not the base class. If you have multiple implementations of the same interface, use `[Service<INotificationService>(Key = "email")]` for keyed registration.

---

## 5. Missing [BelongsTo] on Entities and Actions

**Wrong:**

```csharp
// No boundary declaration
[Entity]
public partial class Invoice { /* ... */ }

[DomainAction]
public partial class CreateInvoiceAction : DomainAction<InvoiceDto> { /* ... */ }
```

**Runtime result:** The SG generates the entity and action code, but without `[BelongsTo]`, they are not assigned to any boundary. The host module's `[Include<BillingModule, FinancialDatabase>]` declaration does not include them in the Billing boundary. Repositories are not generated, the entity is not included in the BillingDbContext, and the action's invoker is not registered in the Billing module's service collection.

**Right:**

```csharp
[Entity]
[BelongsTo<BillingBoundary>]
public partial class Invoice { /* ... */ }

[DomainAction]
[BelongsTo<BillingBoundary>]
public partial class CreateInvoiceAction : DomainAction<InvoiceDto> { /* ... */ }
```

**Why:** `[BelongsTo<T>]` tells the SG which boundary owns this type. Without it, the type has no boundary context and cannot be wired into the module topology. The SG uses boundary information to generate DbContext configurations, repository registrations, and action invoker registrations per module.

---

## 6. Declaring Both [Include] and [RemoteBoundary] for the Same Module

**Wrong:**

```csharp
[Module]
[Include<BillingModule, FinancialDatabase>]
[RemoteBoundary<BillingModule>]
public sealed class AppHostModule;
```

**Compile result:** `PRAG1685` error -- "Module 'BillingModule' is declared both as [RemoteBoundary] and [Include]. Remove one -- a boundary cannot be both local and remote."

**Right (local):**

```csharp
[Module]
[Include<BillingModule, FinancialDatabase>]
public sealed class AppHostModule;
```

**Right (remote):**

```csharp
[Module]
[RemoteBoundary<BillingModule>]
public sealed class AppHostModule;
```

**Why:** A module is either local (included with `[Include]`, running in-process) or remote (accessed via HTTP through `[RemoteBoundary]`). It cannot be both. `[Include]` generates local DI registrations, DbContext setup, and repository wiring. `[RemoteBoundary]` generates HTTP invokers that call a remote host. Having both creates contradictory registration code.

---

## 7. Forgetting [StartupStep] Attribute on IStartupStep Implementation

**Wrong:**

```csharp
public class MyAppStartupStep : IStartupStep
{
    public int Order => 60;

    public void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOpenApi();
    }
}
```

**Runtime result:** The step is never discovered. The SG only processes classes decorated with `[StartupStep]`. Without the attribute, `ConfigureServices` and `ConfigurePipeline` are never called. Services you expected to be registered are missing, and middleware you expected to be in the pipeline is absent.

**Right:**

```csharp
[StartupStep]
public class MyAppStartupStep : IStartupStep
{
    public int Order => 60;

    public void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOpenApi();
    }
}
```

**Why:** The `[StartupStep]` attribute is the marker that triggers SG processing. The SG uses `ForAttributeWithMetadataName` to find all startup step classes. Without the attribute, the class is invisible to the generator. Implementing `IStartupStep` alone is not enough -- the interface provides the shape, the attribute provides discoverability.

---

## 8. Singleton Depending on a Scoped Service (Captive Dependency)

**Wrong:**

```csharp
[Service(Lifetime = Lifetime.Singleton)]
public class CacheService(IOrderRepository repository) : ICacheService
{
    // IOrderRepository is Scoped -- it will be captured by the Singleton
}
```

**Compile result:** `PRAG1642` warning -- "Singleton 'CacheService' depends on Scoped service 'IOrderRepository'. This may cause captive dependency issues."

**Runtime result:** The scoped `IOrderRepository` is captured by the singleton. Every request gets the same repository instance, which may hold stale data, disposed DbContexts, or cause concurrency issues.

**Right:**

```csharp
[Service(Lifetime = Lifetime.Singleton)]
public class CacheService(IServiceScopeFactory scopeFactory) : ICacheService
{
    public async Task<Order?> GetOrderAsync(Guid id)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        return await repository.GetByIdAsync(id);
    }
}
```

Or change the service lifetime:

```csharp
[Service(Lifetime = Lifetime.Scoped)]
public class CacheService(IOrderRepository repository) : ICacheService { /* ... */ }
```

**Why:** A singleton lives for the entire application lifetime. A scoped service lives for one HTTP request. When a singleton captures a scoped service, the scoped service outlives its intended scope. This is a well-known DI anti-pattern called "captive dependency." The SG warns about this at compile time via `PRAG1642`. Fix by either matching lifetimes or using `IServiceScopeFactory` to create short-lived scopes.

---

## 9. Not Using PragmaticApp.RunAsync (Manual Host Setup)

**Wrong:**

```csharp
// Program.cs -- bypasses all SG-generated code
var builder = WebApplication.CreateBuilder(args);

// Must manually register everything
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IProductService, ProductService>();
// ... 50 more lines

var app = builder.Build();
app.Run();
```

**Runtime result:** Works as a plain ASP.NET Core application, but none of the SG-generated code runs. No auto-registration of infrastructure modules, no `[PragmaticMetadata]` aggregation, no ordered startup steps, no database initialization, no telemetry setup, no maintenance mode.

**Right:**

```csharp
// Program.cs -- two lines
await PragmaticApp.RunAsync(args, app =>
{
    app.UseAuthentication<NoOpAuthenticationHandler>("PragmaticDefault");
}).ConfigureAwait(false);
```

**Why:** `PragmaticApp.RunAsync` is the entry point that the SG replaces with the fully wired host implementation. It calls `RegisterAllPragmaticServices`, `CallConfigureServices`, `ConfigurePipeline`, `MapAllEndpoints`, and handles maintenance mode. Bypassing it means you lose all composition features and must wire everything manually.

---

## 10. Decorator Missing the Inner Service Constructor Parameter

**Wrong:**

```csharp
[Decorator(Order = 1)]
public class CachingOrderService(
    ICacheStack cache,
    ILogger<CachingOrderService> logger) : IOrderService
{
    // Missing: IOrderService inner parameter!
    public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        // No inner service to delegate to
    }
}
```

**Compile result:** `PRAG1661` error -- "[Decorator] 'CachingOrderService' must have a constructor parameter of the decorated interface type."

**Right:**

```csharp
[Decorator(Order = 1)]
public class CachingOrderService(
    IOrderService inner,           // Required: the inner service being decorated
    ICacheStack cache,
    ILogger<CachingOrderService> logger) : IOrderService
{
    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var cached = await cache.GetOrSetAsync($"order:{id}",
            async _ => await inner.GetByIdAsync(id, ct));
        return cached;
    }
}
```

**Why:** A decorator wraps an existing service to add behavior. The constructor must accept the inner service as a parameter so the DI container can chain decorators correctly. Without the inner service parameter, the decorator cannot delegate to the original implementation and the SG cannot generate the decoration code.

---

## 11. Duplicate Service Registration Across Modules

**Wrong:**

```csharp
// In CatalogModule
[Service]
public class SharedPricingService : IPricingService { }

// In BookingModule (same interface, different implementation)
[Service]
public class SharedPricingService : IPricingService { }
```

**Runtime result:** Both registrations are added to the DI container. The last one wins (DI last-registration-wins), which depends on the order the SG processes assemblies. This is non-deterministic and may change across builds. One module silently loses its implementation.

**Right:**

```csharp
// Shared service in a shared project, registered once
[Service]
public class SharedPricingService : IPricingService { }

// Or use keyed services if both are needed
[Service<IPricingService>(Key = "catalog")]
public class CatalogPricingService : IPricingService { }

[Service<IPricingService>(Key = "booking")]
public class BookingPricingService : IPricingService { }
```

**Why:** When two assemblies register the same interface, the DI container keeps both registrations but resolves the last one by default. If you need multiple implementations, use keyed services (`Key = "..."`) or inject `IEnumerable<IPricingService>`. If you need one shared implementation, place it in a shared project that both modules reference.

---

## Quick Reference

| Mistake | Diagnostic / Symptom |
|---------|---------------------|
| Missing `[Module]` | No generated host code, RunAsync stub runs |
| Infra in IStartupStep | Works but fragile ordering, hidden decisions |
| Wrong step Order | Middleware in wrong sequence, silent failures |
| `[Service]` on abstract class | `PRAG1645` compile error |
| Missing `[BelongsTo]` | Entities/actions not wired to boundary |
| Include + RemoteBoundary | `PRAG1685` compile error |
| Missing `[StartupStep]` | Step never discovered, services missing |
| Singleton captures Scoped | `PRAG1642` warning, stale data at runtime |
| Bypassing PragmaticApp.RunAsync | No SG code runs, manual wiring needed |
| Decorator missing inner param | `PRAG1661` compile error |
| Duplicate service registration | Non-deterministic resolution, last wins |
