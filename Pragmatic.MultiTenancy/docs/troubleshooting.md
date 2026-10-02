# Troubleshooting

Practical problem/solution guide for Pragmatic.MultiTenancy. Each section covers a common issue, the likely causes, and the fix.

---

## TenantId Is Always Null

The tenant is never resolved -- `ITenantContext.IsResolved` is `false` and `TenantId` is `null`.

### Checklist

1. **Did you configure a resolution strategy?** Without `app.UseMultiTenancy(mt => ...)`, the SG registers `SingleTenantResolver` which returns `"default"` (not null). If you see null, it means either no resolver is registered or the resolver is failing silently.

2. **Is the middleware in the pipeline?** In `PragmaticApp.RunAsync`, the middleware is added automatically. If you are manually composing the pipeline, ensure `app.UseMiddleware<TenantResolutionMiddleware>()` is called.

3. **For header-based resolution**: Is the client sending the `X-Tenant-Id` header? Check the header name in `MultiTenancyOptions.TenantHeaderName`.

4. **For claim-based resolution**: Is authentication middleware running before tenant resolution? The `ClaimTenantResolver` reads `HttpContext.User`, which is only populated after `UseAuthentication()`. Also verify the claim type matches (`MultiTenancyOptions.TenantClaimType`, default: `"tenant_id"`).

5. **For subdomain-based resolution**: Does the host have at least 3 segments? `localhost` and `app.com` return null. Only `subdomain.domain.tld` (3+ segments) resolves a tenant.

6. **For route-based resolution**: Is the route parameter name correct? Default is `"tenantId"`. The route template must include `{tenantId}` (or your custom parameter name).

7. **Check the logs.** Enable `Debug` level for `Pragmatic.MultiTenancy`. The middleware logs `"Tenant '{TenantId}' resolved for {RequestPath}"` on success and `"No tenant resolved for {RequestPath}"` when null.

---

## Queries Return Data From All Tenants

Tenant filtering is not applied -- queries return rows belonging to other tenants.

### Checklist

1. **Does the entity implement `ITenantEntity`?** The SG only generates `TenantFilter` for entities with the `ITenantEntity` interface. Having a `TenantId` property without the interface is not enough.

2. **Is the SG running?** Check that the SG reference is in the `.csproj` with `OutputItemType="Analyzer"`. Look for the generated `{Entity}.TenantFilter.g.cs` file in `obj/`.

3. **Is `ITenantContext` resolved correctly?** If `ITenantContext.TenantId` is null at the time the query executes, the filter might not match any rows (or might return all rows, depending on the null-comparison semantics of your database).

4. **Are you using raw SQL or `IQueryable` outside the repository?** `TenantFilter` is applied by the Pragmatic query pipeline. If you bypass the pipeline with raw SQL or direct `DbContext` access, the filter is not applied. Always use the repository abstraction.

5. **Is TenantScope set for non-HTTP contexts?** In background jobs and tests, use `TenantScope.BeginScope("tenant-id")` before executing queries.

---

## Wrong Tenant Resolved

The tenant is resolved, but it is the wrong value.

### Possible Causes

1. **Header name mismatch.** The client sends `X-Organization-Id` but the resolver reads `X-Tenant-Id` (default). Fix: `mt.UseHeader("X-Organization-Id")`.

2. **Claim type mismatch.** The JWT has `org_id` but the resolver reads `tenant_id` (default). Fix: `mt.UseClaim("org_id")`.

3. **Composite resolver order.** With chained resolvers, the first non-null result wins. If header-based is first and the header has a stale value, it takes precedence over the JWT claim. Reorder the chain or remove the header strategy.

4. **Subdomain parsing.** The subdomain resolver takes the first segment. For `beta.staging.app.com`, the tenant is `beta`, not `staging`. If your tenant is the second segment, implement a custom resolver.

---

## TenantScope Not Flowing in Async Code

`TenantScope.BeginScope` is called but the tenant is not visible in async continuations.

### Checklist

1. **Is the scope disposed too early?** The `using` block must encompass all async operations:

   ```csharp
   // Wrong: scope is disposed before async completes
   using (TenantScope.BeginScope("tenant-a"))
   {
       _ = Task.Run(async () => await DoWorkAsync()); // fire-and-forget
   }
   // DoWorkAsync runs after scope is disposed

   // Right: await within the scope
   using (TenantScope.BeginScope("tenant-a"))
   {
       await DoWorkAsync();
   }
   ```

2. **Are you using `Task.Run` or `new Thread`?** `AsyncLocal<T>` flows through `async`/`await` continuations and `Task.Run`, but does NOT flow into raw `Thread` instances. Use `Task.Run` or the `await` pattern.

3. **Nested scopes conflicting?** If you nest `TenantScope.BeginScope` calls, the inner scope overrides the outer one. On dispose, the outer scope is restored. Verify the nesting order is correct.

---

## DI Registration Conflicts

Multiple tenant context or resolver registrations causing unexpected behavior.

### Checklist

1. **SG default vs explicit registration.** The SG registers `AddPragmaticMultiTenancy()` (single-tenant default). `app.UseMultiTenancy(...)` calls the same method again, overriding the resolver. This is by design (DI last-registration-wins).

2. **`ITenantContext` registered as singleton.** `ITenantContext` must be scoped (per-request). If it is registered as singleton, all requests share the same context -- a cross-tenant data leak. The default registration is scoped. Check for manual singleton registrations.

3. **Multiple `UseMultiTenancy` calls.** Each call overrides the previous resolver registration. Only the last call wins. Use a single `UseMultiTenancy` call with a chained builder for fallback:

   ```csharp
   // Wrong: two separate calls
   app.UseMultiTenancy(mt => mt.UseHeader());
   app.UseMultiTenancy(mt => mt.UseClaim());  // this replaces header

   // Right: one call with chain
   app.UseMultiTenancy(mt => mt
       .UseHeader()
       .UseClaim()
   );
   ```

---

## Middleware Ordering Issues

Tenant resolution fails or produces wrong results due to middleware ordering.

### Checklist

1. **Authentication before tenant resolution.** `ClaimTenantResolver` reads `HttpContext.User`. If `UseAuthentication()` runs after `TenantResolutionMiddleware`, claims are empty. Order:

   ```
   UseAuthentication()
   TenantResolutionMiddleware   <-- must be after auth
   UseRouting()
   ```

2. **With `PragmaticApp.RunAsync`.** The SG-generated host wiring places the middleware automatically in the correct position. You should not need to add it manually.

---

## TenantId Not Set on New Entities

Entities are created but `TenantId` is empty or null.

### Checklist

1. **Are you using the Pragmatic persistence layer?** The auto-set behavior is part of the persistence pipeline. If you are using raw `DbContext.Add()` or `db.Set<T>().Add()`, the tenant is not set automatically.

2. **Is `ITenantContext` resolved?** The persistence layer reads `ITenantContext.TenantId` to set the entity's `TenantId`. If the context is not resolved (null), the entity gets an empty `TenantId`.

3. **Is the entity created in a non-HTTP context without TenantScope?** Use `TenantScope.BeginScope("tenant-id")` before creating entities in background jobs, seed scripts, or tests.

---

## Subdomain Resolution Returns Null on localhost

During development, `SubdomainTenantResolver` always returns null.

### Why

The resolver requires at least 3 domain segments (`subdomain.domain.tld`). `localhost` has 1 segment and `localhost:5001` still has 1 segment. This is by design -- `localhost` has no subdomain.

### Fix

Use a different strategy for local development:

```csharp
app.UseMultiTenancy(mt =>
{
    if (builder.Environment.IsDevelopment())
        mt.UseHeader();       // Use X-Tenant-Id header in dev
    else
        mt.UseSubdomain();    // Use subdomain in production
});
```

Or configure a local domain in your `hosts` file:

```
127.0.0.1  acme.myapp.local
127.0.0.1  contoso.myapp.local
```

---

## Cross-Tenant Data Leak in Raw Queries

Data from other tenants is returned when using raw SQL or direct `DbContext` queries.

### Why

`TenantFilter` is applied by the Pragmatic query pipeline (repositories, query methods). If you bypass the pipeline with raw SQL (`db.Database.SqlQueryRaw(...)`) or direct `IQueryable` access (`db.Set<Invoice>()`), the filter is not applied.

### Fix

Always use the Pragmatic repository abstraction for tenant-scoped entities:

```csharp
// Safe: TenantFilter applied
var invoices = await repository.QueryAsync(ct);

// NOT safe: bypasses TenantFilter
var invoices = await dbContext.Invoices.ToListAsync(ct);
```

If raw SQL is unavoidable, add the tenant filter manually:

```csharp
var tenantId = tenantContext.TenantId;
var invoices = await db.Database.SqlQueryRaw<Invoice>(
    "SELECT * FROM Invoices WHERE TenantId = {0}", tenantId).ToListAsync(ct);
```

---

## CompositeTenantResolver Logging Too Many Warnings

The logs show frequent warnings from `CompositeTenantResolver`.

### Why

When multiple resolvers are chained and the first resolvers return null (expected behavior for fallback chains), the composite resolver still tries all of them. If the last resolver also returns null, it logs `"No tenant resolver succeeded out of N registered resolver(s)"`.

### Fix

1. **Add a `SingleTenantResolver` as the last fallback.** This ensures at least one resolver always succeeds:

   ```csharp
   app.UseMultiTenancy(mt => mt
       .UseHeader()
       .UseClaim()
       .UseSingleTenant("default")  // always succeeds
   );
   ```

2. **Adjust log levels.** The warning is expected when no resolver matches (e.g., anonymous/unauthenticated requests). If this is expected behavior, adjust the log level for `Pragmatic.MultiTenancy.CompositeTenantResolver` to `Error` or `None` in your logging configuration.

---

## Frequently Asked Questions

### Can I use different resolution strategies per endpoint?

Not directly. The resolution strategy is global (registered in DI). However, you can use a custom `ITenantResolver` that inspects the request path and delegates to different strategies based on the endpoint.

### Can I disable tenant filtering for admin queries?

Use `IQueryFilterToggle` to temporarily disable specific filters. This is part of `Pragmatic.Persistence`, not the multi-tenancy module.

### Does TenantFilter apply to GetByIdAsync?

Yes. `TenantFilter` is applied to all queries executed through the Pragmatic repository, including `GetByIdAsync`. A tenant cannot access another tenant's entity even with the correct ID.

### What happens when TenantId is null on an ITenantEntity?

The entity would not match any tenant filter. It is effectively orphaned. Always ensure `TenantId` is set -- the persistence layer does this automatically on insert.

### Can I change the TenantFilter priority?

Not directly. The SG generates it with priority 200. If you need custom ordering, you can disable the SG filter (do not implement `ITenantEntity`) and write your own `IQueryFilter<T>` with a custom priority.

---

## Getting Help

1. **Check the logs.** Enable `Debug` level for `Pragmatic.MultiTenancy` to see resolution results.
2. **Inspect generated code.** Look at `{Entity}.TenantFilter.g.cs` in the `obj/` directory to verify the filter is generated correctly.
3. **Verify DI registrations.** Use `services.BuildServiceProvider().GetService<ITenantContext>()` in a test to confirm the registration chain.
4. **Read the guides.** See [Concepts](concepts.md), [Getting Started](getting-started.md), and [Common Mistakes](common-mistakes.md).
