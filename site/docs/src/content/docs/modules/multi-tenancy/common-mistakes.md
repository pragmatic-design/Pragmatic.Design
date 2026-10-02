---
title: "Common Mistakes"
description: "These are the most common issues developers encounter when using Pragmatic.MultiTenancy. Each section shows the wrong approach, the correct approach, and explai"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.MultiTenancy/docs/common-mistakes.md
sidebar:
  order: 4
---
These are the most common issues developers encounter when using Pragmatic.MultiTenancy. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Forgetting ITenantEntity on Entities That Need Isolation

**Wrong:**

```csharp
[Entity]
[BelongsTo<CatalogBoundary>]
public partial class Invoice : IEntity
{
    public string Number { get; private set; } = "";
    public decimal Amount { get; private set; }
    public string TenantId { get; set; } = "";  // Property exists but no interface
}
```

**Right:**

```csharp
[Entity]
[BelongsTo<CatalogBoundary>]
public partial class Invoice : IEntity, ITenantEntity
{
    public string Number { get; private set; } = "";
    public decimal Amount { get; private set; }
    public string TenantId { get; set; } = "";
}
```

**Why:** The Source Generator only generates `TenantFilter` for entities that implement the `ITenantEntity` interface. Having a `TenantId` property without the interface means no automatic filtering -- queries will return data from all tenants. The interface is the trigger, not the property name.

---

## 2. Manually Filtering by TenantId in Queries

**Wrong:**

```csharp
public class InvoiceService(
    IReadRepository<Invoice> invoices,
    ITenantContext tenantContext)
{
    public async Task<List<Invoice>> GetInvoicesAsync(CancellationToken ct)
    {
        return await invoices
            .Where(i => i.TenantId == tenantContext.TenantId)  // redundant!
            .ToListAsync(ct);
    }
}
```

**Right:**

```csharp
public class InvoiceService(IReadRepository<Invoice> invoices)
{
    public async Task<List<Invoice>> GetInvoicesAsync(CancellationToken ct)
    {
        // TenantFilter is applied automatically by the query pipeline
        return await invoices.QueryAsync(ct);
    }
}
```

**Why:** When `Invoice` implements `ITenantEntity`, the SG generates a `TenantFilter` that is applied by the query pipeline to every query. Adding a manual filter results in a redundant `WHERE` clause. Worse, if you only sometimes remember to add the manual filter, you get inconsistent behavior. Trust the framework -- the filter is always applied.

---

## 3. Using the Wrong Resolution Strategy for the Context

**Wrong:**

```csharp
// Using header-based resolution for a user-facing SaaS app
app.UseMultiTenancy(mt => mt.UseHeader());

// Problem: end users don't control HTTP headers in the browser.
// A malicious user could set X-Tenant-Id to another tenant's ID.
```

**Right:**

```csharp
// Use claim-based resolution for user-facing apps -- tenant is in the JWT
app.UseMultiTenancy(mt => mt.UseClaim());

// Or subdomain-based for vanity URLs
app.UseMultiTenancy(mt => mt.UseSubdomain());
```

**Why:** Header-based resolution is appropriate for B2B APIs and service-to-service communication where the caller is trusted. For user-facing applications, the tenant should come from a cryptographically verified source (JWT claim) or from the URL (subdomain/route). The choice of strategy is a security decision.

| Scenario | Recommended Strategy |
|----------|---------------------|
| B2B API, trusted callers | `UseHeader()` |
| User-facing SaaS with JWT | `UseClaim()` |
| Vanity subdomains | `UseSubdomain()` |
| Tenant in URL path | `UseRoute()` |
| Development/testing | `UseHeader()` or `UseSingleTenant()` |

---

## 4. Not Setting Up TenantScope for Background Jobs

**Wrong:**

```csharp
public class MonthlyBillingJob(IServiceProvider services)
{
    public async Task RunAsync(List<string> tenantIds)
    {
        foreach (var tenantId in tenantIds)
        {
            // No tenant context! ITenantContext.IsResolved is false.
            // TenantFilter returns nothing (or worse, all data).
            using var scope = services.CreateScope();
            var invoices = scope.ServiceProvider.GetRequiredService<IInvoiceService>();
            await invoices.GenerateMonthlyBillsAsync();
        }
    }
}
```

**Right:**

```csharp
public class MonthlyBillingJob(IServiceProvider services)
{
    public async Task RunAsync(List<string> tenantIds)
    {
        foreach (var tenantId in tenantIds)
        {
            using var tenantScope = TenantScope.BeginScope(tenantId);
            using var diScope = services.CreateScope();
            var invoices = diScope.ServiceProvider.GetRequiredService<IInvoiceService>();
            await invoices.GenerateMonthlyBillsAsync();
        }
    }
}
```

**Why:** Outside HTTP requests, there is no middleware to resolve the tenant. `TenantScope.BeginScope` sets the tenant for the current async flow via `AsyncLocal<T>`. Without it, `ITenantContext.IsResolved` is `false` and `TenantId` is `null`, which means the `TenantFilter` will not match any rows (or all rows, depending on the null-handling semantics).

---

## 5. Hardcoding Tenant IDs in Service Logic

**Wrong:**

```csharp
public class ReportService(IDbContext db)
{
    public async Task<Report> GetReportAsync(string tenantId, CancellationToken ct)
    {
        // Hardcoded tenant filtering -- bypasses the framework
        var data = await db.Invoices
            .Where(i => i.TenantId == tenantId)
            .GroupBy(i => i.Month)
            .ToListAsync(ct);

        return new Report(data);
    }
}
```

**Right:**

```csharp
public class ReportService(IReadRepository<Invoice> invoices)
{
    public async Task<Report> GetReportAsync(CancellationToken ct)
    {
        // Tenant filtering is automatic -- no tenantId parameter needed
        var data = await invoices.QueryAsync(ct);
        // data is already filtered to the current tenant

        return new Report(GroupByMonth(data));
    }
}
```

**Why:** Passing `tenantId` as a parameter means every caller must know the current tenant and pass it correctly. This is error-prone and defeats the purpose of ambient tenant context. Use `ITenantContext` (injected automatically) and let the query pipeline handle filtering. If you need the tenant ID for business logic (not filtering), inject `ITenantContext` directly.

---

## 6. Confusing ITenantContext with ICurrentUser.TenantId

**Wrong:**

```csharp
public class TenantAwareService(ICurrentUser currentUser)
{
    public void DoWork()
    {
        // Using ICurrentUser.TenantId for tenant filtering context
        var tenantId = currentUser.TenantId;
        // Problem: ICurrentUser.TenantId comes from JWT claims
        // ITenantContext.TenantId comes from the resolution strategy
        // They may be different (e.g., admin viewing another tenant's data)
    }
}
```

**Right:**

```csharp
public class TenantAwareService(ITenantContext tenantContext)
{
    public void DoWork()
    {
        // ITenantContext is the canonical source for "which tenant's data to access"
        var tenantId = tenantContext.TenantId;
    }
}
```

**Why:** `ITenantContext` and `ICurrentUser.TenantId` serve different purposes. `ITenantContext` answers "which tenant's data should this request access" -- it comes from the resolution strategy (header, claim, subdomain, etc.). `ICurrentUser.TenantId` answers "which tenant does this user belong to" -- it always comes from the JWT. In most cases they match, but they can differ (e.g., admin impersonation, cross-tenant operations).

| Context | Source | Use For |
|---------|--------|---------|
| `ITenantContext.TenantId` | Resolution strategy | Data filtering, scoping |
| `ICurrentUser.TenantId` | JWT claim | Authorization, audit trail |

---

## 7. Assuming FeatureFlagContext.TenantId Is Auto-Populated

**Wrong:**

```csharp
public class FeatureCheckService(IFeatureFlagStore flags)
{
    public async Task<bool> IsNewCheckoutEnabled(CancellationToken ct)
    {
        // Assuming TenantId is automatically populated
        var context = new FeatureFlagContext();
        return await flags.IsEnabledAsync("new-checkout", context, ct);
        // context.TenantId is null -- tenant rules won't match
    }
}
```

**Right:**

```csharp
public class FeatureCheckService(IFeatureFlagStore flags, ITenantContext tenantContext)
{
    public async Task<bool> IsNewCheckoutEnabled(CancellationToken ct)
    {
        var context = new FeatureFlagContext
        {
            TenantId = tenantContext.TenantId  // explicit bridging
        };
        return await flags.IsEnabledAsync("new-checkout", context, ct);
    }
}
```

**Or better -- implement `IFeatureFlagContextProvider`:**

```csharp
public class HttpFeatureFlagContextProvider(
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IHostEnvironment hostEnv) : IFeatureFlagContextProvider
{
    public Task<FeatureFlagContext> GetContextAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new FeatureFlagContext
        {
            TenantId = tenantContext.TenantId,
            UserId = currentUser.UserId,
            Environment = hostEnv.EnvironmentName
        });
    }
}
```

**Why:** There is no automatic wiring between `ITenantContext` and `FeatureFlagContext.TenantId`. These are separate systems. You must explicitly copy the tenant ID into the feature flag context. The `IFeatureFlagContextProvider` pattern centralizes this bridging so you don't repeat it in every feature flag check.

---

## 8. Not Using TryAdd Awareness When Registering Resolvers

**Wrong:**

```csharp
// In Program.cs
services.AddSingleton<ITenantResolver>(new SingleTenantResolver("manual-default"));

// Later in the same file or a startup step
app.UseMultiTenancy(mt => mt.UseHeader());

// The UseHeader() registration replaces SingleTenantResolver because
// AddScoped<ITenantResolver, HeaderTenantResolver>() overwrites the singleton.
```

**Also wrong:**

```csharp
// Expecting TryAdd to keep the first registration
services.TryAddSingleton<ITenantResolver>(new SingleTenantResolver("my-default"));
app.UseMultiTenancy(mt => mt.UseClaim());
// UseClaim() uses AddScoped (not TryAdd) -- it always replaces.
```

**Right:**

```csharp
// Use the builder fluently -- one place, one strategy
app.UseMultiTenancy(mt => mt.UseClaim());

// Or chain for fallback
app.UseMultiTenancy(mt => mt
    .UseClaim()
    .UseSingleTenant("fallback")
);
```

**Why:** The builder methods use `AddScoped` (not `TryAdd`) for resolver registration, so they always override previous registrations. The `MutableTenantContext` and `ITenantContext` use `TryAdd` (registered once). The design intention is: the SG registers a default, and `UseMultiTenancy()` overrides it. Use the builder as the single source of truth for your resolution strategy.

---

## 9. Writing a Pre-Processor to Enforce What the Middleware Already Enforces

**Wrong:**

```csharp
// A pre-processor that refuses a request with no resolved tenant.
public class TenantValidationPreProcessor(ITenantContext tenantContext) : IEndpointPreProcessor
{
    public ValueTask<PreProcessorResult> ProcessAsync(IEndpointContext context, CancellationToken ct = default)
        => tenantContext.IsResolved
            ? ValueTask.FromResult(PreProcessorResult.Continue())
            : ValueTask.FromResult(PreProcessorResult.Fail(UnauthorizedError.Create("TenantNotResolved")));
}
```

**Right:**

```csharp
// Nothing. RequireTenant is true by default, and TenantResolutionMiddleware refuses the request
// with 400 before any endpoint runs. The pre-processor's condition can never be true.
```

**Why:** `RequireTenant` is enforced at runtime: `TenantResolutionMiddleware` reads the flag, and
short-circuits an unresolved tenant with `400 Bad Request`. The default is `true`, not `false`.

The reference application carries a `TenantValidationPreProcessor` whose `!IsResolved` branch is
unreachable, because the middleware answers first. It reads as a safety net and protects nothing.

If you want a different **status code** or a typed error body for the unresolved case, that is a
reason to reach for a pre-processor — but set `RequireTenant = false` first, or the middleware will
answer before your processor is asked.

**And the same applies where there is no request.** A message handler that opens with

```csharp
if (string.IsNullOrEmpty(context.TenantId))
    throw new InvalidOperationException("no tenant: refusing to write");
```

is the pre-processor's mistake in a consumer. `RequireTenant` covers the write path too since
`TenantInterceptor` throws `TenantNotResolvedException` when an `ITenantEntity` would be
inserted or updated with no tenant resolved, so the message is nacked and dead-lettered instead of
landing in the wrong database. The hand-written guard was written when that was *not* true — the read
filter was fail-closed and the write path was not, so a row belonging to nobody was written onto the
**shared** database without a complaint and was then invisible to the service's own reads. It is a
monument to the same kind of sentence.

What the framework still cannot do for you is **choose** the tenant when nobody sent one: a job or a
CLI opens its own with `TenantScope.BeginScope(tenantId)`, and the scope has to be around the
`SaveChangesAsync`, not only around the in-memory change — the connection is picked when the context
opens it, so one context cannot write for two organisations however the scopes are nested.

---

## 10. Not Testing Tenant Isolation

**Wrong:**

```csharp
[Fact]
public async Task CreateInvoice_SavesSuccessfully()
{
    // Testing without tenant context -- TenantFilter behavior unknown
    var invoice = Invoice.Create("INV-001", 1500.00m);
    await repository.CreateAsync(invoice);

    var result = await repository.GetByIdAsync(invoice.Id);
    result.Should().NotBeNull();
}
```

**Right:**

```csharp
[Fact]
public async Task CreateInvoice_IsolatedByTenant()
{
    // Set tenant A, create invoice
    using (TenantScope.BeginScope("tenant-a"))
    {
        var invoice = Invoice.Create("INV-001", 1500.00m);
        await repository.CreateAsync(invoice);
    }

    // Verify tenant B cannot see it
    using (TenantScope.BeginScope("tenant-b"))
    {
        var results = await repository.QueryAsync();
        results.Should().BeEmpty();
    }

    // Verify tenant A can see it
    using (TenantScope.BeginScope("tenant-a"))
    {
        var results = await repository.QueryAsync();
        results.Should().ContainSingle(i => i.Number == "INV-001");
    }
}
```

**Why:** Multi-tenancy is a security boundary. If you don't test that tenants are isolated from each other, you won't catch cross-tenant data leaks. Always test that data created by one tenant is invisible to another tenant. `TenantScope` makes this straightforward -- no HTTP infrastructure needed.
