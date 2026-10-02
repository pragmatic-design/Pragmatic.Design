---
title: "Getting Started with Pragmatic.MultiTenancy"
description: "This guide walks through adding multi-tenancy to a Pragmatic.Design application from scratch."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.MultiTenancy/docs/getting-started.md
sidebar:
  order: 2
---
This guide walks through adding multi-tenancy to a Pragmatic.Design application from scratch.

## Prerequisites

- A Pragmatic.Design host application using `PragmaticApp.RunAsync`
- `Pragmatic.Persistence` (for automatic tenant query filters)

## Step 1: Add the Package

Add the ASP.NET Core bridge package (it pulls in the core package transitively):

```xml
<PackageReference Include="Pragmatic.MultiTenancy.AspNetCore" />
```

Once the package is referenced, the Source Generator detects it via `FeatureDetector` (`HasMultiTenancy` flag) and auto-registers `services.AddPragmaticMultiTenancy()` with single-tenant defaults. **Single-tenant apps need no further configuration.**

## Step 2: Choose a Resolution Strategy

For multi-tenant applications, configure a resolution strategy in `Program.cs`:

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    // Resolve tenant from the X-Tenant-Id HTTP header
    app.UseMultiTenancy(mt => mt.UseHeader());
});
```

This overrides the SG-registered default (single-tenant) via DI last-registration-wins.

Available built-in strategies:

```csharp
// HTTP header (default: X-Tenant-Id)
app.UseMultiTenancy(mt => mt.UseHeader());

// JWT claim (default: tenant_id)
app.UseMultiTenancy(mt => mt.UseClaim());

// Subdomain (acme.app.com -> acme)
app.UseMultiTenancy(mt => mt.UseSubdomain());

// Route parameter (default: {tenantId})
app.UseMultiTenancy(mt => mt.UseRoute());

// Chain: try header, fall back to claim, then fixed default
app.UseMultiTenancy(mt => mt
    .UseHeader()
    .UseClaim()
    .UseSingleTenant("fallback-tenant"));
```

## Step 3: Mark Entities as Tenant-Scoped

Add `ITenantEntity` to entities that need row-level tenant isolation:

```csharp
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;

[Entity]
[BelongsTo<MyBoundary>]
public partial class Invoice : IEntity, ITenantEntity
{
    public string Number { get; private set; } = "";
    public decimal Amount { get; private set; }

    // ITenantEntity implementation
    // Auto-set on insert, auto-filtered on all reads
    public string TenantId { get; set; } = "";
}
```

The Source Generator will produce:

- **`Invoice.TenantFilter`** -- an `IQueryFilter<Invoice>` that adds `WHERE TenantId = @currentTenant` to every query automatically.
- The filter has **priority 200** (runs after SoftDelete filters at priority 100).

No manual `WHERE` clauses or global query filter configuration needed.

## Step 4: Access Tenant Context in Services

Inject `ITenantContext` to read the current tenant anywhere in the pipeline:

```csharp
public class TenantAwareService(ITenantContext tenantContext)
{
    public string GetCurrentTenant()
    {
        if (!tenantContext.IsResolved)
            throw new InvalidOperationException("No tenant resolved");

        return tenantContext.TenantId!;
    }
}
```

`ITenantContext` is scoped per request. In HTTP contexts, the `TenantResolutionMiddleware` populates it before routing. In non-HTTP contexts, use `TenantScope`:

```csharp
// Background job, seed script, or test
using var scope = TenantScope.BeginScope("tenant-42");
await tenantAwareService.DoWorkAsync();
```

## Step 5: Optional -- Enforce Tenant Resolution

If your application requires that every request has a resolved tenant, you can add validation. The Showcase app demonstrates this with an `IEndpointPreProcessor`:

```csharp
public class TenantValidationPreProcessor(
    ITenantContext tenantContext,
    ILogger<TenantValidationPreProcessor> logger) : IEndpointPreProcessor
{
    public ValueTask<PreProcessorResult> ProcessAsync(
        IEndpointContext context, CancellationToken ct = default)
    {
        if (!tenantContext.IsResolved)
        {
            return ValueTask.FromResult(
                PreProcessorResult.Fail(UnauthorizedError.Create("TenantNotResolved")));
        }

        return ValueTask.FromResult(PreProcessorResult.Continue());
    }
}
```

**Note**: you rarely need the pre-processor above. `MultiTenancyOptions.RequireTenant` is on by
default and the framework enforces it in three places -- `TenantResolutionMiddleware` refuses the
request with `400`, the generated query filter returns no rows, and `TenantInterceptor` refuses a write
of an `ITenantEntity` with `TenantNotResolvedException`. The pre-processor is for a *different* answer
(your own error shape, or a rule per endpoint), not for a missing one. See
[Concepts](/modules/multi-tenancy/concepts/#configuration-reference) for what each place does and for the one thing the
framework cannot do for you: a job or a CLI opens its own tenant with `TenantScope.BeginScope`, around
the save.

## What Gets Auto-Registered

When the SG detects `Pragmatic.MultiTenancy` in your project:

| Registration | Lifetime | Description |
|-------------|----------|-------------|
| `MutableTenantContext` | Scoped | Writable context, set by middleware |
| `ITenantContext` | Scoped | Read-only interface, delegates to `MutableTenantContext` |
| `ITenantResolver` | Varies | `SingleTenantResolver` (Singleton) by default, or the configured strategy |
| `{Entity}.TenantFilter` | Scoped | One per `ITenantEntity`, auto-registered as `IQueryFilter<T>` |

When you call `app.UseMultiTenancy(mt => ...)`, the builder replaces the default resolver registration with your chosen strategy. The `MutableTenantContext` and `ITenantContext` registrations use `TryAdd` so they are only registered once.

## Testing

For unit and integration tests, use `TenantScope` to set the tenant without HTTP infrastructure:

```csharp
[Fact]
public async Task Query_FiltersToCurrentTenant()
{
    using var scope = TenantScope.BeginScope("test-tenant");

    var results = await repository.QueryAsync();

    results.Should().AllSatisfy(r => r.TenantId.Should().Be("test-tenant"));
}
```

`TenantScope` supports nesting and correctly restores the previous scope on dispose:

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
