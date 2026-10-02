# Architecture and Core Concepts

This guide explains **why** Pragmatic.MultiTenancy exists, how its pieces fit together, and how to choose the right resolution strategy for your application. Read this before diving into the individual feature guides.

---

## The Problem

Multi-tenant SaaS applications need to isolate data between tenants. Without framework support, this isolation is manual, error-prone, and scattered across the codebase.

### Scattered tenant checks everywhere

```csharp
public class InvoiceService(IDbContext db, IHttpContextAccessor http)
{
    public async Task<List<Invoice>> GetInvoicesAsync(CancellationToken ct)
    {
        // Every query must manually filter by tenant
        var tenantId = http.HttpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        if (string.IsNullOrEmpty(tenantId))
            throw new UnauthorizedException("No tenant");

        return await db.Invoices
            .Where(i => i.TenantId == tenantId)  // easy to forget
            .ToListAsync(ct);
    }

    public async Task CreateInvoiceAsync(Invoice invoice, CancellationToken ct)
    {
        // Every write must manually set the tenant
        var tenantId = http.HttpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        invoice.TenantId = tenantId ?? throw new UnauthorizedException("No tenant");

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);
    }
}
```

Every service, every query, every write must remember to filter by tenant. Miss one `WHERE` clause and you have a cross-tenant data leak. Miss one tenant assignment on insert and the data is orphaned.

### Hardcoded resolution logic

```csharp
// Tenant resolution is coupled to the service layer
var tenantId = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();

// What happens when you need to switch to JWT claims?
// Or subdomains? Or route parameters?
// Every service that reads the tenant must change.
```

The resolution strategy is spread across the codebase. Changing from header-based to claim-based resolution means touching dozens of files.

### No isolation in background jobs

```csharp
public class MonthlyBillingJob
{
    public async Task RunAsync()
    {
        // No HTTP context here -- how do you set the tenant?
        // Every background job needs custom tenant-setting code
        foreach (var tenant in tenants)
        {
            // Manually pass tenantId through every method call?
            await ProcessBillingAsync(tenant.Id);
        }
    }
}
```

Background jobs, seed scripts, and tests have no HTTP request. Without a framework-level ambient context, you end up passing `tenantId` as a parameter through every method call.

### The fundamental issues

1. **Tenant filtering is manual** -- every query must include `WHERE TenantId = @current`, and forgetting one is a data leak.
2. **Resolution logic is scattered** -- the strategy for determining the tenant is duplicated across services.
3. **No ambient context** -- non-HTTP scenarios (background jobs, tests, message handlers) have no clean way to set the tenant.
4. **Changing strategies is expensive** -- switching from headers to claims means a codebase-wide refactor.

---

## The Solution

Pragmatic.MultiTenancy separates the three concerns -- resolution, context, and filtering -- into distinct layers that compose automatically.

```csharp
// 1. Configure ONCE in Program.cs
await PragmaticApp.RunAsync(args, app =>
{
    app.UseMultiTenancy(mt => mt.UseHeader());
});

// 2. Mark entities as tenant-scoped
[Entity]
[BelongsTo<CatalogBoundary>]
public partial class Invoice : IEntity, ITenantEntity
{
    public string Number { get; private set; } = "";
    public decimal Amount { get; private set; }
    public string TenantId { get; set; } = "";  // auto-set, auto-filtered
}

// 3. Business logic is tenant-unaware
public class InvoiceService(IReadRepository<Invoice> invoices)
{
    public async Task<List<Invoice>> GetInvoicesAsync(CancellationToken ct)
    {
        // No tenant filtering -- the SG-generated TenantFilter handles it
        return await invoices.QueryAsync(ct);
    }
}
```

The source generator produces a `Invoice.TenantFilter` class -- an `IQueryFilter<Invoice>` that automatically adds `WHERE TenantId = @currentTenant` to every query. The middleware resolves the tenant from the HTTP request and populates `ITenantContext`. Business logic never touches tenant isolation directly.

---

## How It Works

### The Three Layers

Pragmatic.MultiTenancy is built on three layers, each with a single responsibility:

```
HTTP Request
    |
    v
+----------------------------+
| 1. Resolution              |   ITenantResolver
|    "Who is the tenant?"    |   HeaderTenantResolver, ClaimTenantResolver, etc.
+----------------------------+
    |
    v
+----------------------------+
| 2. Context                 |   ITenantContext
|    "What is the current    |   MutableTenantContext (scoped per request)
|     tenant?"               |   TenantScope (AsyncLocal for non-HTTP)
+----------------------------+
    |
    v
+----------------------------+
| 3. Filtering               |   IQueryFilter<T>
|    "Show only this         |   SG-generated TenantFilter (per entity)
|     tenant's data"         |   Applied globally by query pipeline
+----------------------------+
```

**Layer 1 -- Resolution**: Determines the tenant from the incoming request (header, claim, subdomain, route, or custom logic). Runs once per request in the middleware.

**Layer 2 -- Context**: Holds the resolved tenant ID for the duration of the request scope. Injected everywhere via `ITenantContext`. For non-HTTP scenarios, `TenantScope` provides the same context via `AsyncLocal<T>`.

**Layer 3 -- Filtering**: The source generator produces `IQueryFilter<T>` implementations for every entity that implements `ITenantEntity`. These filters are applied automatically by the persistence pipeline -- no manual `WHERE` clauses.

### Request Flow (HTTP)

```
HTTP Request
    |
    v
Authentication Middleware          Sets ClaimsPrincipal
    |
    v
TenantResolutionMiddleware         Resolves ITenantResolver from scoped DI
    |--- calls resolver.ResolveAsync()
    |--- sets MutableTenantContext.TenantId
    |
    v
Routing / Endpoint Execution
    |--- ITenantContext available to all services
    |--- IQueryFilter<T> reads ITenantContext.TenantId
    |--- Queries automatically filtered to current tenant
    |
    v
HTTP Response
```

The middleware runs early in the pipeline -- after authentication (so claims are available) but before routing (so the tenant context is available to all downstream components).

### Non-HTTP Flow

For background jobs, message handlers, seed scripts, or tests:

```csharp
using var scope = TenantScope.BeginScope("tenant-123");
// All code in this async flow sees TenantId = "tenant-123"
// IQueryFilter<T> reads from TenantScope automatically
await ProcessBackgroundJobAsync();
```

`TenantScope` uses `AsyncLocal<T>` and flows through async continuations. It supports nesting with correct restore semantics:

```csharp
using (TenantScope.BeginScope("tenant-a"))
{
    // TenantId = "tenant-a"
    using (TenantScope.BeginScope("tenant-b"))
    {
        // TenantId = "tenant-b"
    }
    // TenantId = "tenant-a" (restored)
}
// TenantId = null (restored)
```

---

## Tenant Resolution Strategies

Resolution is pluggable. Choose the strategy that matches your architecture, or chain multiple strategies for fallback behavior.

### HeaderTenantResolver

Reads the tenant ID from an HTTP request header.

```csharp
app.UseMultiTenancy(mt => mt.UseHeader());             // default: X-Tenant-Id
app.UseMultiTenancy(mt => mt.UseHeader("Tenant-Key")); // custom header name
```

| Aspect | Details |
|--------|---------|
| **Configuration** | `MultiTenancyOptions.TenantHeaderName` (default: `"X-Tenant-Id"`) |
| **Best for** | API-to-API communication, development, B2B APIs |
| **Security** | Low -- the caller controls the header. Validate against allowed tenants if needed. |

Example request:

```http
GET /api/invoices HTTP/1.1
X-Tenant-Id: acme-corp
```

### ClaimTenantResolver

Reads the tenant ID from a JWT claim on the authenticated user.

```csharp
app.UseMultiTenancy(mt => mt.UseClaim());             // default: tenant_id
app.UseMultiTenancy(mt => mt.UseClaim("org_id"));     // custom claim type
```

| Aspect | Details |
|--------|---------|
| **Configuration** | `MultiTenancyOptions.TenantClaimType` (default: `"tenant_id"`) |
| **Best for** | Applications where the identity provider embeds the tenant in the JWT |
| **Security** | High -- the tenant is cryptographically bound to the token |
| **Requirement** | Authentication middleware must run before tenant resolution |

### SubdomainTenantResolver

Extracts the tenant ID from the first segment of the request's host name.

```csharp
app.UseMultiTenancy(mt => mt.UseSubdomain());
```

| Host | Resolved Tenant |
|------|----------------|
| `acme.app.com` | `acme` |
| `beta.staging.app.com` | `beta` |
| `app.com` | `null` (only 2 segments) |
| `localhost` | `null` (only 1 segment) |

| Aspect | Details |
|--------|---------|
| **Best for** | SaaS applications with vanity subdomains per tenant |
| **Requirement** | DNS wildcard entry (`*.app.com`) and at least 3 domain segments |

### RouteTenantResolver

Reads the tenant ID from a route parameter.

```csharp
app.UseMultiTenancy(mt => mt.UseRoute());               // default: {tenantId}
app.UseMultiTenancy(mt => mt.UseRoute("orgSlug"));       // custom parameter name
```

| Aspect | Details |
|--------|---------|
| **Configuration** | `MultiTenancyOptions.TenantRouteParameter` (default: `"tenantId"`) |
| **Best for** | APIs structured as `/{tenantId}/invoices`, `/{tenantId}/orders` |

Example route: `GET /acme-corp/invoices` with route template `/{tenantId}/invoices`.

### SingleTenantResolver

Returns a fixed tenant ID. This is the default when no strategy is configured.

```csharp
app.UseMultiTenancy(mt => mt.UseSingleTenant());           // default: "default"
app.UseMultiTenancy(mt => mt.UseSingleTenant("my-app"));   // custom fixed ID
```

| Aspect | Details |
|--------|---------|
| **Best for** | Single-tenant deployments |
| **Lifetime** | Singleton -- zero per-request overhead |

A single-tenant app is simply a multi-tenant app with one tenant. This means you can upgrade to multi-tenancy later by just changing the resolution strategy.

### Composite Resolution (Chain of Responsibility)

Register multiple strategies. `CompositeTenantResolver` tries each in order and returns the first non-null result:

```csharp
app.UseMultiTenancy(mt => mt
    .UseHeader()              // 1st: try X-Tenant-Id header
    .UseClaim()               // 2nd: try JWT claim
    .UseSingleTenant("demo")  // 3rd: fallback to "demo"
);
```

**Behavior:**

- Resolvers are tried sequentially in call order.
- The first resolver to return a non-null, non-empty string wins.
- If a resolver throws an exception (not `OperationCanceledException`), the error is logged at `Warning` level and the chain continues.
- If all resolvers return null, the composite returns null and logs a warning.
- `OperationCanceledException` is re-thrown (respects cancellation).

### Custom Resolvers

Implement `ITenantResolver` for application-specific resolution logic:

```csharp
public sealed class ApiKeyTenantResolver(
    IHttpContextAccessor accessor,
    ITenantStore tenantStore) : ITenantResolver
{
    public async ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = accessor.HttpContext?.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(apiKey))
            return null;  // Return null to pass to next resolver in chain

        return await tenantStore.GetTenantIdByApiKeyAsync(apiKey, cancellationToken);
    }
}
```

Register via the builder:

```csharp
app.UseMultiTenancy(mt => mt.UseResolver<ApiKeyTenantResolver>());
```

Custom resolvers participate in the composite chain like built-in resolvers.

### Choosing the Right Strategy

| Scenario | Strategy | Why |
|----------|----------|-----|
| Single-tenant deployment | `UseSingleTenant()` | Zero overhead, upgrade path to multi-tenant |
| B2B API, caller controls tenant | `UseHeader()` | Simple, explicit |
| User-facing SaaS with JWT | `UseClaim()` | Most secure -- tenant bound to token |
| Vanity subdomain SaaS | `UseSubdomain()` | User-friendly URLs (`acme.app.com`) |
| Tenant in URL path | `UseRoute()` | Explicit, bookmarkable |
| API key identifies tenant | `UseResolver<T>()` | Custom lookup from any source |
| Multiple sources with fallback | Chain (e.g., Header + Claim + SingleTenant) | Resilient, handles edge cases |

---

## Isolation Model: Shared Schema, Row-Level Filtering

Pragmatic.MultiTenancy uses **shared-schema, row-level isolation**:

- All tenants share the same database and tables.
- Entities that implement `ITenantEntity` get a `TenantId` column.
- The Source Generator produces a `TenantFilter` (priority 200) for each `ITenantEntity`.
- `TenantId` is set automatically on insert by the persistence layer.

### Why Shared Schema?

| Model | Isolation | Cost | Complexity | Best For |
|-------|-----------|------|------------|----------|
| **Shared schema** (this module) | Row-level | Low | Low | Most SaaS apps, small-to-medium tenants |
| Database per tenant | Database-level | High | High | Compliance, large tenants, per-tenant backup |

Shared schema is appropriate for most applications. Database-per-tenant isolation is a separate infrastructure concern and is outside the scope of this module.

### ITenantEntity

Mark entities that need tenant isolation:

```csharp
[Entity]
[BelongsTo<CatalogBoundary>]
public partial class Property : IEntity, ITenantEntity
{
    public string Name { get; private set; } = "";
    public string TenantId { get; set; } = "";  // required by ITenantEntity
}
```

The `ITenantEntity` interface requires a single property:

```csharp
public interface ITenantEntity
{
    string TenantId { get; set; }
}
```

### SG-Generated TenantFilter

For each entity implementing `ITenantEntity`, the Source Generator produces a nested `TenantFilter` class:

```csharp
// Generated: Property.TenantFilter.g.cs
public partial class Property
{
    public sealed class TenantFilter : IQueryFilter<Property>
    {
        private readonly ITenantContext _tenantContext;

        public TenantFilter(ITenantContext tenantContext)
            => _tenantContext = tenantContext;

        public int Priority => 200;  // after SoftDelete (100)

        public IQueryable<Property> Apply(IQueryable<Property> query)
            => query.Where(e => e.TenantId == _tenantContext.TenantId);
    }
}
```

**Key characteristics:**

| Aspect | Details |
|--------|---------|
| **Priority** | 200 (runs after SoftDelete filters at 100) |
| **Lifetime** | Scoped (reads `ITenantContext` per request) |
| **Registration** | Auto-registered by SG as `IQueryFilter<T>` |
| **Application** | Applied by the query pipeline to every query for the entity |

### Not All Entities Need Tenant Isolation

Only implement `ITenantEntity` on entities that contain tenant-specific data:

```csharp
// Tenant-scoped: different data per tenant
public partial class Invoice : IEntity, ITenantEntity { ... }
public partial class Customer : IEntity, ITenantEntity { ... }

// Shared across tenants: same data for everyone
public partial class Country : IEntity { ... }   // No ITenantEntity; "IT" is a [LogicKey] Code, not the key
public partial class Currency : IEntity { ... }  // No ITenantEntity
```

---

## Data Filtering: How Queries Stay Isolated

The SG-generated `TenantFilter` is applied by the Pragmatic.Persistence query pipeline. This means every query method -- `QueryAsync`, `GetByIdAsync`, `GridFilterAsync` -- automatically scopes to the current tenant.

### Automatic on Reads

```csharp
// The query pipeline applies TenantFilter before executing
var invoices = await repository.QueryAsync(ct);
// SQL: SELECT * FROM Invoices WHERE TenantId = 'acme'
//      ^^^^^^^^^^^^^^^^^^^^^^^^^ added by TenantFilter
```

### Automatic on Writes

The persistence layer sets `TenantId` on entity creation:

```csharp
// TenantId is set automatically when the entity is created
var invoice = Invoice.Create("INV-001", 1500.00m);
// invoice.TenantId is set to ITenantContext.TenantId by the persistence layer
```

### Filter Priority Order

When multiple query filters apply, they execute in priority order:

| Priority | Filter | Description |
|----------|--------|-------------|
| 100 | `SoftDeleteFilter` | Excludes logically deleted rows |
| 200 | `TenantFilter` | Scopes to current tenant |
| 250 | `ResourceAuthorizationFilter` | Row-level authorization |

---

## Key Types

### Abstractions (in `Pragmatic.Abstractions`)

These types live in the Abstractions package so any module can depend on them without pulling in the full runtime.

| Type | Description |
|------|-------------|
| `ITenantContext` | Read-only access to current tenant (`TenantId`, `TenantName`, `IsResolved`) |
| `ITenantResolver` | Strategy interface: `ValueTask<string?> ResolveAsync(CancellationToken)` |
| `ITenantEntity` | Marker for tenant-scoped entities: `string TenantId { get; set; }` |
| `UnresolvedTenantContext` | Singleton fallback -- all null, `IsResolved = false` |

### Core Package (`Pragmatic.MultiTenancy`)

| Type | Description |
|------|-------------|
| `MutableTenantContext` | Scoped, writable context. Set by middleware, read via `ITenantContext` |
| `SingleTenantResolver` | Fixed tenant ID (zero overhead). Default when no strategy configured |
| `CompositeTenantResolver` | Chain-of-responsibility over multiple `ITenantResolver` instances |
| `TenantScope` | AsyncLocal-based scope for non-HTTP contexts. Implements `ITenantContext` |
| `MultiTenancyOptions` | Configuration: header name, claim type, route parameter, defaults |

### ASP.NET Core Package (`Pragmatic.MultiTenancy.AspNetCore`)

| Type | Description |
|------|-------------|
| `MultiTenancyBuilder` | Fluent builder for resolution strategies (`UseHeader`, `UseClaim`, etc.) |
| `TenantResolutionMiddleware` | HTTP middleware that resolves tenant early in pipeline |
| `HeaderTenantResolver` | Resolves from HTTP header |
| `ClaimTenantResolver` | Resolves from JWT claim |
| `SubdomainTenantResolver` | Resolves from subdomain |
| `RouteTenantResolver` | Resolves from route parameter |
| `PragmaticBuilderMultiTenancyExtensions` | `IPragmaticBuilder.UseMultiTenancy()` extension |

---

## Ecosystem Integration

### Persistence

The primary integration point. The Source Generator detects `ITenantEntity` and produces:

- **`{Entity}.TenantFilter`** -- `IQueryFilter<T>` with priority 200.
- **Auto-set on insert** -- the persistence layer sets `TenantId` from `ITenantContext`.

See [Pragmatic.Persistence](../../Pragmatic.Persistence/README.md) for details on the query pipeline.

### Identity (ICurrentUser)

`ICurrentUser` exposes a `TenantId` property from the identity layer (JWT claims). This value is available alongside `ITenantContext` for authorization and auditing. The two contexts are independent -- `ITenantContext` comes from the resolution strategy, `ICurrentUser.TenantId` comes from the JWT.

See [Pragmatic.Identity](../../Pragmatic.Identity/README.md) for the identity model.

### Caching

`ITenantContext` is consumed by `Pragmatic.Caching` for tenant-scoped cache keys. This ensures that cached data is isolated per tenant -- a cache entry for tenant A is never served to tenant B.

See [Pragmatic.Caching](../../Pragmatic.Caching/README.md) for caching integration.

### Feature Flags

`FeatureFlagContext` has a `TenantId` property, but bridging with `ITenantContext` is **manual**. Your `IFeatureFlagContextProvider` must explicitly copy `tenantContext.TenantId` into `FeatureFlagContext.TenantId`. There is no automatic wiring between the two contexts.

See [Pragmatic.FeatureFlags](../../Pragmatic.FeatureFlags/README.md) for feature flag integration.

### Composition (SG Auto-Registration)

When the SG detects `Pragmatic.MultiTenancy` in the project references (`HasMultiTenancy` feature flag in `DetectedFeatures`), it auto-registers `services.AddPragmaticMultiTenancy()` with single-tenant defaults. Calling `app.UseMultiTenancy(...)` in `Program.cs` overrides the default via DI last-registration-wins.

See [Pragmatic.Composition](../../Pragmatic.Composition/README.md) for the host wiring model.

---

## Configuration Reference

All options are in `MultiTenancyOptions`:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DefaultTenantId` | `string` | `"default"` | Tenant ID for single-tenant deployments |
| `TenantHeaderName` | `string` | `"X-Tenant-Id"` | HTTP header name for header-based resolution |
| `TenantClaimType` | `string` | `"tenant_id"` | JWT claim type for claim-based resolution |
| `TenantRouteParameter` | `string` | `"tenantId"` | Route parameter name for route-based resolution |
| `RequireTenant` | `bool` | `true` | An unresolved tenant is refused: with `400` before any endpoint runs, and with `TenantNotResolvedException` when an `ITenantEntity` would be written. Endpoints marked `TenantAgnosticEndpoint` are exempt |

Configuration via options pattern:

```csharp
services.Configure<MultiTenancyOptions>(options =>
{
    options.TenantHeaderName = "X-Organization-Id";
    options.TenantClaimType = "org_id";
    options.RequireTenant = true;
});
```

Or via builder methods (which configure the options automatically):

```csharp
app.UseMultiTenancy(mt => mt.UseHeader("X-Organization-Id"));
```

**What `RequireTenant` enforces.** Three places read it, and this page claimed for a while that nothing
did:

- **the request**: `TenantResolutionMiddleware` refuses a request that resolves no tenant with `400`,
  unless the endpoint is a `TenantAgnosticEndpoint`;
- **reads**: the generated query filter on an `ITenantEntity` is fail-closed — with no tenant resolved
  a tenant query returns no rows rather than everybody's;
- **writes**: `TenantInterceptor` throws `TenantNotResolvedException` when an `ITenantEntity` would be
  inserted or updated with no tenant resolved. This half arrived last: until then such a
  write happened, on the **shared** database and with an empty tenant column, and was invisible
  afterwards to the application's own fail-closed reads — which is how a row belonging to nobody gets
  written without a complaint.

The write half matters most where there is no request to refuse: a message handler, a job, a CLI. A
consumer arrives with the tenant in a transport header and the consume scope restores it, so nothing
in the handler mentions tenants; a job has to open one itself with `TenantScope.BeginScope(tenantId)`,
**around the save** and not only around the in-memory change — the connection is chosen when the
context opens it, so one context cannot write for two organisations.

An application that deliberately writes without a tenant — a seed, a CLI, background work with a
system tenant — sets `RequireTenant = false`, which is what the option is for.

---

## DI Registrations

`AddPragmaticMultiTenancy()` registers:

| Service | Lifetime | Implementation | Notes |
|---------|----------|----------------|-------|
| `MutableTenantContext` | Scoped | Concrete class | `TryAddScoped` -- registered once |
| `ITenantContext` | Scoped | Delegates to `MutableTenantContext` | `TryAddScoped` -- registered once |
| `ITenantResolver` | Depends on strategy | `SingleTenantResolver` (Singleton) or HTTP resolvers (Scoped) | Not `TryAdd` -- can be overridden |

The `MutableTenantContext` and `ITenantContext` registrations use `TryAdd` to avoid duplicates. The resolver registration does **not** use `TryAdd`, so `UseMultiTenancy()` correctly overrides the SG default.

---

## Logging

Both `TenantResolutionMiddleware` and `CompositeTenantResolver` use `[LoggerMessage]` for zero-allocation structured logging:

| Level | Message | When |
|-------|---------|------|
| `Debug` | `Tenant '{TenantId}' resolved for {RequestPath}` | Tenant successfully resolved |
| `Trace` | `No tenant resolved for {RequestPath}` | No resolver returned a value |
| `Warning` | `Tenant resolver {ResolverName} failed, continuing to next resolver` | A resolver threw an exception |
| `Warning` | `No tenant resolver succeeded out of {ResolverCount} registered resolver(s)` | All resolvers returned null |

To debug tenant resolution, enable `Debug` level for the `Pragmatic.MultiTenancy` category in your logging configuration.

---

## Middleware Ordering

`TenantResolutionMiddleware` must run after authentication and before routing:

```
Authentication  (sets ClaimsPrincipal)
      |
TenantResolution  (reads claims/headers, sets ITenantContext)
      |
Routing  (ITenantContext available to endpoints, filters, services)
```

In Pragmatic.Design applications using `PragmaticApp.RunAsync`, the middleware is added automatically in the correct position by the SG-generated host wiring. No manual `app.UseMiddleware<TenantResolutionMiddleware>()` is needed.

---

## Testing

### Unit Tests with TenantScope

`TenantScope` is a static API with no DI dependency:

```csharp
[Fact]
public async Task Query_FiltersToCurrentTenant()
{
    using var scope = TenantScope.BeginScope("test-tenant");

    var results = await repository.QueryAsync();

    results.Should().AllSatisfy(r => r.TenantId.Should().Be("test-tenant"));
}
```

### Testing Multiple Tenants

```csharp
[Fact]
public async Task Tenants_SeeOnlyTheirData()
{
    using (TenantScope.BeginScope("tenant-a"))
    {
        await repository.CreateAsync(new Invoice { Number = "A-001" });
    }

    using (TenantScope.BeginScope("tenant-b"))
    {
        await repository.CreateAsync(new Invoice { Number = "B-001" });
    }

    using (TenantScope.BeginScope("tenant-a"))
    {
        var results = await repository.QueryAsync();
        results.Should().ContainSingle(i => i.Number == "A-001");
        results.Should().NotContain(i => i.Number == "B-001");
    }
}
```

### Integration Tests with Custom Resolver

For tests that need to control the resolver:

```csharp
services.AddSingleton<ITenantResolver>(new SingleTenantResolver("test-tenant"));
```

---

## See Also

- [Getting Started](getting-started.md) -- step-by-step setup from zero
- [Tenant Resolution](tenant-resolution.md) -- strategies, middleware, and configuration details
- [Common Mistakes](common-mistakes.md) -- avoid the most frequent pitfalls
- [Troubleshooting](troubleshooting.md) -- problem/solution guide
- [Pragmatic.Persistence](../../Pragmatic.Persistence/README.md) -- query pipeline and filters
- [Pragmatic.Identity](../../Pragmatic.Identity/README.md) -- ICurrentUser and tenant in identity
- [Pragmatic.FeatureFlags](../../Pragmatic.FeatureFlags/README.md) -- per-tenant feature flags
- [Pragmatic.Caching](../../Pragmatic.Caching/README.md) -- tenant-scoped caching
