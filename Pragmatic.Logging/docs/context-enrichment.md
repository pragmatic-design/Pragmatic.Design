# Context Enrichment

## The Problem

A bare log entry like `"Order placed"` tells you what happened but not who triggered it, which HTTP request it belongs to, or what machine produced it. When you are debugging a production incident across dozens of service replicas, you need every log entry to carry ambient context -- correlation IDs, user identifiers, request paths, machine names, and thread IDs -- without requiring the developer to pass these values explicitly at every call site.

Context enrichment solves this by automatically attaching ambient information to every log entry. Providers are registered once at startup, and every subsequent log call picks up the current context without any changes to business code.

---

## 1. IContextProvider

The `IContextProvider` interface is the extension point for injecting ambient data into the logging pipeline. Each provider has a name, a priority that controls evaluation order, and two methods: one to supply properties and one to indicate whether the provider is available in the current environment.

```csharp
public interface IContextProvider
{
    /// Unique name for identification and configuration filtering.
    string Name { get; }

    /// Priority order -- lower values are evaluated first.
    int Priority { get; }

    /// True (the default) when the properties never change while the process runs.
    bool IsStatic => true;

    /// Returns the context properties this provider can supply.
    IReadOnlyDictionary<string, object?> GetContextProperties();

    /// Returns false when the provider cannot supply context
    /// (e.g., HttpContextProvider outside an HTTP request).
    bool IsAvailable();
}
```

### Priority Ordering

Providers are sorted by `Priority` in ascending order. When two providers supply a property with the same key, the provider with the lower priority value wins. This lets you override defaults with higher-priority providers.

| Priority Range | Convention |
|---------------|------------|
| 1-99 | Application-specific overrides |
| 100-499 | Request-scoped providers (HTTP, correlation) |
| 500-899 | Thread and runtime providers |
| 900-999 | Process-level providers |
| 1000+ | Machine-level (static) providers |

### Static and Per-Call Providers

`IsStatic` decides when a provider is asked for its properties.

- **Static** (the default): the context manager asks the provider once, keeps the aggregate, and asks it again only
  after a provider is registered or removed. Machine and process information belong here.
- **Per call** (`IsStatic => false`): the provider is asked, `IsAvailable()` included, every time an entry is
  enriched, on the thread that writes it. Anything that describes the current thread, request, user or tenant
  belongs here.

A provider that reads the current request and leaves `IsStatic` at its default is asked once. Every entry then
carries the request that happened to come first, or none at all if the first entry was written outside a
request. When a per-call provider and a static one supply the same key, the per-call value is the one written.

### ContextProviderBase

The `ContextProviderBase` abstract class provides a constructor that accepts `name` and `priority`, and a helper method `CreatePropertiesDictionary` for building read-only dictionaries from tuples. Most providers extend this base class rather than implementing the interface directly.

```csharp
public abstract class ContextProviderBase : IContextProvider
{
    protected ContextProviderBase(string name, int priority = 100) { ... }

    public string Name { get; }
    public int Priority { get; }
    public virtual bool IsStatic => true;

    public abstract IReadOnlyDictionary<string, object?> GetContextProperties();
    public virtual bool IsAvailable() => true;

    protected static IReadOnlyDictionary<string, object?> CreatePropertiesDictionary(
        params (string name, object? value)[] properties);
}
```

---

## 2. IContextManager

The `IContextManager` interface is the central registry that holds all providers, collects their properties, and exposes cache invalidation and change notifications. It supports both synchronous and asynchronous property collection, targeted queries against specific providers, and bulk registration.

### Key Methods

| Method | Description |
|--------|-------------|
| `RegisterProvider(provider)` | Add or replace a provider (auto-sorts by priority) |
| `RegisterProviders(providers)` | Bulk add (sorts once after all registrations) |
| `UnregisterProvider(name)` | Remove a provider by name |
| `GetProviders()` | Get all registered providers sorted by priority |
| `GetContextProperties()` | Synchronous: the cached static properties, plus the per-call providers read now |
| `GetAggregateContextAsync(ct)` | Async: collect from all providers including async-only ones |
| `GetProviderContextAsync(name, ct)` | Get properties from a specific provider |
| `GetContextPropertyAsync(provider, property, ct)` | Get a single property from a specific provider |
| `InvalidateCache()` | Force fresh retrieval on next access |
| `HasProvider(name)` | Check if a provider is registered |
| `ProviderCount` | Number of registered providers |

### ProvidersChanged Event

The `ProvidersChanged` event fires whenever providers are added, removed, or cleared. Components that cache context data can subscribe to this event and refresh their caches.

```csharp
contextManager.ProvidersChanged += (sender, args) =>
{
    switch (args.ChangeType)
    {
        case ContextProviderChangeType.Added:
            logger.LogInformation("Context provider added: {Name}", args.ProviderName);
            break;
        case ContextProviderChangeType.Removed:
            logger.LogInformation("Context provider removed: {Name}", args.ProviderName);
            break;
    }
};
```

### Dependency Injection

The `ContextManager` concrete class implements `IContextManager` and is registered as a singleton. You can inject it and register custom providers at startup or from hosted services.

```csharp
services.AddContextManagerWithFactory(sp =>
{
    var contextManager = new ContextManager(registerDefaultProviders: true);
    contextManager.RegisterProvider(new TenantContextProvider(sp.GetRequiredService<IHttpContextAccessor>()));
    return contextManager;
});
```

A provider registered here lives as long as the manager: the whole process. What it reads per request it
reads per call (see [Creating a Custom Provider](#4-creating-a-custom-provider)), never from a scoped service
captured at registration.

### ContextManagerOptions

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `EnableDefaultProviders` | `bool` | `true` | Register Machine, Process, Thread providers |
| `CacheTimeout` | `TimeSpan` | `1 min` | Duration before cached context expires |
| `EnableMetrics` | `bool` | `true` | Collect provider performance metrics |
| `MaxAsyncCacheSize` | `int` | `1000` | Max async context cache entries |

---

## 3. Built-in Providers

Pragmatic.Logging ships with five built-in context providers that cover the most common ambient data sources.

### Provider Summary

| Provider | Name | Priority | Read | Key Properties |
|----------|------|----------|-------|---------------|
| `HttpContextProvider` | `HttpContext` | 100 | Per call | RequestPath, RequestMethod, UserId, RemoteIpAddress |
| `CorrelationIdProvider` | `CorrelationId` | 50 | Per call | CorrelationId, TraceId, SpanId |
| `ThreadContextProvider` | `Thread` | 500 | Per call | ThreadId, ThreadName, IsBackground, Culture |
| `ProcessContextProvider` | `Process` | 900 | Once (static) | ProcessId, ProcessName, AppVersion, StartTime |
| `MachineContextProvider` | `Machine` | 1000 | Once (static) | MachineName, OSVersion, CLRVersion, RuntimeIdentifier |

### HttpContextProvider

Extracts request, response, connection, and user information from `IHttpContextAccessor`. Only available during HTTP request processing.

**Properties provided:**

| Property | Example Value | Description |
|----------|--------------|-------------|
| `RequestId` | `0HN3V...` | ASP.NET Core trace identifier |
| `RequestPath` | `/api/orders` | Request URL path |
| `RequestMethod` | `GET` | HTTP method |
| `RequestScheme` | `https` | URL scheme |
| `RequestHost` | `api.example.com` | Host header |
| `RemoteIpAddress` | `192.168.1.100` | Client IP |
| `IsAuthenticated` | `true` | Whether user is authenticated |
| `UserId` | `user-42` | From NameIdentifier claim |
| `UserEmail` | `user@example.com` | From Email claim |
| `UserRole` | `Admin` | From Role claim |
| `Header_User-Agent` | `Mozilla/5.0...` | Selected request headers |
| `Header_X-Forwarded-For` | `10.0.0.1` | Proxy chain |

The provider also extracts selected headers (`User-Agent`, `X-Forwarded-For`, `X-Real-IP`, `X-Correlation-ID`, `X-Request-ID`, `Accept-Language`, `Referer`) and adds them with a `Header_` prefix.

### CorrelationIdProvider

Manages correlation IDs for distributed request tracing. Supports three sources in priority order: existing `HttpContext.Items`, the `X-Correlation-ID` request header, and the W3C `traceparent` header via `Activity.Current`. If none are found, generates a new GUID.

**Properties provided:**

| Property | Source | Description |
|----------|--------|-------------|
| `CorrelationId` | Header / Activity / Generated | Request correlation identifier |
| `TraceId` | `Activity.Current.TraceId` | W3C trace ID (when Activity exists) |
| `SpanId` | `Activity.Current.SpanId` | W3C span ID (when Activity exists) |

The correlation ID is automatically propagated to the response via the `X-Correlation-ID` header, enabling clients to include it in bug reports.

### MachineContextProvider

Provides static machine-level information that is computed once (via `Lazy<T>`) and cached for the lifetime of the process. Useful for identifying which replica produced a log entry in multi-instance deployments.

**Properties provided:** `MachineName`, `UserName`, `OSVersion`, `ProcessorCount`, `Is64BitOperatingSystem`, `Is64BitProcess`, `CLRVersion`, `RuntimeIdentifier`, `OSDescription`, `FrameworkDescription`.

### ProcessContextProvider

Provides static process-level information, also computed once via `Lazy<T>`. Identifies the running application and its version.

**Properties provided:** `ProcessId`, `ProcessName`, `ApplicationName`, `ApplicationVersion`, `StartTime`, `WorkingDirectory`.

Not the command line: it is where secrets travel (a connection string or a token passed as an argument), and declared redaction cannot reach a string read from the environment. If you need it, add it through a context provider of your own, deliberately.

### ThreadContextProvider

Provides per-call thread information. Unlike Machine and Process providers, it is not static (`IsStatic` is false), so it is read on every call: the logging thread changes from one request to the next.

**Properties provided:** `ThreadId`, `ThreadName`, `IsBackground`, `IsThreadPoolThread`, `CurrentCulture`, `CurrentUICulture`.

---

## 4. Creating a Custom Provider

When your application needs domain-specific context -- tenant ID, feature flags, or business-specific metadata -- you create a custom provider by extending `ContextProviderBase`.

### Example: TenantContextProvider

In a multi-tenant application, every log entry should carry the current tenant identifier so operators can filter logs by tenant during troubleshooting.

The context manager is a singleton, and the tenant belongs to the request: a provider that took an
`ITenantContext` in its constructor would hold the first tenant it saw for the life of the process (and,
with scope validation on, fail to resolve at all). It reads the tenant of the current request on each call
instead, through `IHttpContextAccessor`, the way the built-in `HttpContextProvider` reads the request, and
says so with `IsStatic => false`: without it the manager would ask once and keep the first answer.

```csharp
public sealed class TenantContextProvider(IHttpContextAccessor httpContextAccessor)
    : ContextProviderBase("Tenant", priority: 80) // High priority: tenant is critical context
{
    // The tenant changes with the request: read it on every call, not once.
    public override bool IsStatic => false;

    public override bool IsAvailable() => CurrentTenant() is { IsResolved: true };

    public override IReadOnlyDictionary<string, object?> GetContextProperties()
    {
        var tenant = CurrentTenant();
        return CreatePropertiesDictionary(
            ("TenantId", tenant?.TenantId),
            ("TenantName", tenant?.TenantName));
    }

    // The request's own ITenantContext, resolved when the log entry is written.
    private ITenantContext? CurrentTenant()
        => httpContextAccessor.HttpContext?.RequestServices.GetService<ITenantContext>();
}
```

### Registration

Declare it on the logging builder: the container constructs it, with the accessor it needs, and the
context manager carries it beside the system providers.

```csharp
services.AddHttpContextAccessor();
services.AddPragmaticLogging(logging =>
{
    logging.ConfigureContext(ctx => ctx.AddProvider<TenantContextProvider>());
});
```

`AddProvider<T>()` registers `T` as a singleton unless the application already registered it. When the
provider needs something the container cannot build on its own, declare how it is built:

```csharp
logging.ConfigureContext(ctx => ctx.AddProvider(sp =>
    new TenantContextProvider(sp.GetRequiredService<IHttpContextAccessor>())));
```

Registering it on the manager directly still works, and is the form to reach for when the whole manager
is yours to build:

```csharp
services.AddContextManagerWithFactory(sp =>
{
    var manager = new ContextManager(registerDefaultProviders: true);
    manager.RegisterProvider(new TenantContextProvider(sp.GetRequiredService<IHttpContextAccessor>()));
    return manager;
});
```

---

## 5. ASP.NET Core Middleware

Two middleware components wire context enrichment into the HTTP request pipeline.

### LoggingEnrichmentMiddleware

This middleware runs early in the pipeline and creates a logging scope that includes correlation ID, request path, request method, and user identity. It optionally logs request start and completion events with timing information and maps HTTP status codes to log levels (5xx = Error, 4xx = Warning, 2xx/3xx = Information).

```csharp
app.UseMiddleware<LoggingEnrichmentMiddleware>(new LoggingEnrichmentOptions
{
    LogRequestStart = true,
    LogRequestCompletion = true,
    LogUnhandledExceptions = true,
    ContextEnricher = httpContext => new[]
    {
        new KeyValuePair<string, object?>("Environment", "Production"),
        new KeyValuePair<string, object?>("Region", "eu-west-1")
    }
});
```

**LoggingEnrichmentOptions:**

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `LogRequestStart` | `bool` | `true` | Log when request begins |
| `LogRequestCompletion` | `bool` | `true` | Log when request completes (with status code and elapsed time) |
| `LogUnhandledExceptions` | `bool` | `true` | Log unhandled exceptions with correlation context |
| `ContextEnricher` | `Func<HttpContext, IEnumerable<KVP>>?` | `null` | Custom enricher delegate |

### BaggagePropagationMiddleware

Distributed systems need context to flow across service boundaries. This middleware propagates selected context values into `Activity.Baggage`, which is automatically forwarded to downstream services via W3C Baggage headers.

```csharp
app.UseMiddleware<BaggagePropagationMiddleware>(new BaggagePropagationOptions
{
    PropagateUserId = true,
    PropagateTenantId = true,
    UserIdClaimType = "sub",
    TenantIdHeaderName = "X-Tenant-ID",
    CustomBaggageHeaders = new[]
    {
        new BaggageHeaderMapping("X-Region", "region"),
        new BaggageHeaderMapping("X-Feature-Flags", "feature.flags")
    }
});
```

When `PropagateUserId` is enabled, the middleware extracts the user ID from the authenticated principal and sets it as `user.id` in Activity baggage. When `PropagateTenantId` is enabled, it reads the tenant ID from the configured header or `HttpContext.Items` and sets it as `tenant.id`.

### Middleware Order

Place enrichment middleware early in the pipeline so that all downstream middleware and handlers benefit from the enriched context:

```csharp
app.UseMiddleware<BaggagePropagationMiddleware>();
app.UseMiddleware<LoggingEnrichmentMiddleware>();
// ... other middleware
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
```

---

## 6. Configuration

Context enrichment is configured through `ContextOptions` in `PragmaticLoggingOptions`, which can be set via code or `appsettings.json`.

### Code Configuration

```csharp
services.AddPragmaticLogging(logging =>
{
    logging.ConfigureContext(ctx =>
    {
        ctx.EnableEnrichment = true;        // false: the manager carries no provider at all
        ctx.IncludeMachineContext = true;   // the Machine provider
        ctx.IncludeProcessContext = true;   // the Process provider
        ctx.IncludeThreadContext = true;    // the Thread provider

        // These three describe what a log provider writes, and reach the provider configuration:
        ctx.EnableCorrelationId = true;
        ctx.IncludeUserContext = true;
        ctx.IncludeRequestContext = true;

        ctx.AddProvider<TenantContextProvider>();   // your own, constructed by the container
    });

    logging.AddConsole();
});
```

The three `Include*Context` switches decide which system providers the `IContextManager` carries.

⚠️ There is **one** context manager per process, and the container answers with it. A log provider is
constructed with a name and a configuration (never from the container), so what it enriches an entry with
is the ambient manager, `ContextManager.Instance`. `ConfigureContext` therefore configures that one, and the
ASP.NET integration registers `HttpContext` and `CorrelationId` on the same one, so neither half of the
context goes to a manager of its own.

### appsettings.json Configuration

```json
{
  "PragmaticLogging": {
    "Context": {
      "IncludeCorrelationId": true,
      "IncludeUserContext": true,
      "IncludeRequestContext": true,
      "IncludeMachineContext": true,
      "CustomProperties": {
        "ServiceName": "OrderService",
        "Environment": "Production",
        "Region": "eu-west-1"
      }
    }
  }
}
```

### ContextOptions Reference

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `IncludeCorrelationId` | `bool` | `true` | Add CorrelationId, TraceId, SpanId |
| `IncludeUserContext` | `bool` | `true` | Add UserId, UserEmail, UserRole |
| `IncludeRequestContext` | `bool` | `true` | Add RequestPath, RequestMethod, StatusCode |
| `IncludeMachineContext` | `bool` | `true` | Add MachineName, OSVersion, CLRVersion |
| `CustomProperties` | `Dictionary<string, string>` | Empty | Static key-value pairs added to every entry |

### Per-Provider Context Filtering

Each provider can independently control which context properties it includes via `ContextFilterConfiguration`. This lets you add full context to file logs while keeping console output compact.

```csharp
logging.AddConsole(config =>
{
    config.IncludeContextEnrichment = true;
    config.ContextFilter = new ContextFilterConfiguration
    {
        Mode = ContextFilterMode.Include,
        PropertyNames = new HashSet<string> { "CorrelationId", "UserId" }
    };
});

logging.AddFile("logs/app.log", config =>
{
    config.IncludeContextEnrichment = true;
    config.ContextFilter = new ContextFilterConfiguration
    {
        Mode = ContextFilterMode.Exclude,
        PropertyNames = new HashSet<string> { "UserName", "WorkingDirectory" }
    };
});
```

**ContextFilterMode values:**

| Mode | Behavior |
|------|----------|
| `All` | Include all context properties (default) |
| `Include` | Include only properties in `PropertyNames` or matching `PropertyPatterns` |
| `Exclude` | Include all except properties in `PropertyNames` or matching `PropertyPatterns` |
| `None` | Include no context properties |

---

## How Context Flows Through the Pipeline

When a log entry is produced, `PragmaticLoggerProviderBase.WriteLog()` enriches it with context from two sources before passing it to `WriteLogCore`:

1. **LogContextScope** -- Ambient properties pushed by middleware via `LogContextScope.PushContext()`. These are scoped to the current async flow and automatically pop when the scope is disposed.

2. **ContextManager.Instance** -- The static providers' properties come from the manager's cache, merged in priority order when a provider was last registered or removed. The per-call providers are queried now, respecting `IsAvailable()`.

Both sources are filtered through the provider's `ContextFilterConfiguration` before being attached to the `LogEntry.Properties` dictionary. This means each provider can see a different subset of context properties, matching its output requirements.

---

## Log Output Example

With enrichment enabled, a structured JSON log entry includes both business properties and ambient context:

```json
{
  "@timestamp": "2026-03-22T14:30:00.123Z",
  "@level": "INFO",
  "@logger": "Booking.Orders.PlaceOrderHandler",
  "@message": "Order placed successfully",
  "@properties": {
    "OrderId": "a1b2c3",
    "Total": 150.00,
    "CorrelationId": "7f3d2a1b-4e5f-6789-abcd-ef0123456789",
    "TraceId": "0af7651916cd43dd8448eb211c80319c",
    "SpanId": "b7ad6b7169203331",
    "RequestPath": "/api/orders",
    "RequestMethod": "POST",
    "UserId": "user-42",
    "TenantId": "acme-corp",
    "MachineName": "web-server-03",
    "ProcessId": 12345,
    "ThreadId": 7
  }
}
```

Without enrichment, only `OrderId` and `Total` would appear. The rest is injected automatically by context providers.
