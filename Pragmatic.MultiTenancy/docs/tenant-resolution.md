# Tenant Resolution

Tenant resolution is the process of determining which tenant a request belongs to. Pragmatic.MultiTenancy provides a pluggable strategy system with four built-in HTTP resolvers and support for custom implementations.

## How Resolution Works

### HTTP Pipeline

In HTTP contexts, tenant resolution follows this flow:

```
HTTP Request
    |
    v
TenantResolutionMiddleware
    |--- resolves ITenantResolver from DI (scoped)
    |--- calls resolver.ResolveAsync()
    |--- populates MutableTenantContext.TenantId
    |
    v
Routing / Endpoint Execution
    |--- ITenantContext is available to all downstream services
    |--- IQueryFilter<T> (TenantFilter) reads ITenantContext.TenantId
    |
    v
Response
```

The middleware runs early in the pipeline (before routing) so that the tenant context is available everywhere. It resolves `ITenantResolver` and `MutableTenantContext` from the scoped DI container and populates the context with the resolved tenant ID.

### Claim Guard (Cross-Tenant Protection)

> **Security.** The `Header`, `Route`, and `Subdomain` strategies read the tenant from **client-controlled** request input. They are intended for **pre-auth / anonymous** resolution only.

After resolution, the middleware enforces a **fail-closed claim guard** (`MultiTenancyOptions.EnforceTenantClaim`, default `true`):

- If the request is **authenticated** AND the user carries a tenant claim (`MultiTenancyOptions.TenantClaimType`, default `tenant_id`), the tenant resolved by **any** strategy MUST equal the claim value.
- On **mismatch**, the request is rejected with **HTTP 403**: neither the client-supplied value nor the claim is silently used (no fail-open).
- When **no tenant claim is present** (anonymous / pre-auth), the resolved value is kept as-is.

This makes the authenticated user's tenant claim **authoritative**: an authenticated user from tenant A cannot send `X-Tenant-Id: B` (or hit `/b/...`, or `b.app.com`) to read or write tenant B's data. The claim resolver trivially satisfies the guard. Set `EnforceTenantClaim = false` only if your identity tokens deliberately omit a tenant claim and you accept client-supplied tenants for authenticated requests.

### State Guard

A resolved tenant is only served if it is `Active` (`MultiTenancyOptions.EnforceTenantState`, default
`true`):

- The check runs only when an `ITenantStore` is registered **and** it knows the tenant. The store is
  the authority on state; where there is no store there is no state to read.
- A tenant in `Suspended`, `Deactivated`, `Provisioning` or `Migrating` is rejected with **HTTP 403**.
- A tenant the store does not know is passed through: this guard is about state, and absent is not a
  state.

Without it `TenantState` is written and never read. Suspending a tenant for non-payment, deactivating
it under a GDPR request, or catching one still provisioning recorded a value and served the requests
anyway, including the case `TenantMigrationOrchestrator` writes `Suspended` for, which is a tenant
whose schema is in an indeterminate state.

Set `EnforceTenantState = false` if administration endpoints have to stay reachable while addressed
as the suspended tenant itself. Reactivating from a management context (a different tenant, or none)
works either way.

### Unknown Tenant Guard

`MultiTenancyOptions.RequireKnownTenant` rejects a resolved tenant the store does not contain, with
**HTTP 404**: an unknown tenant is not a forbidden one, and answering `403` would confirm that the
id names something.

**It is off by default, unlike every other guard here, and the reason is a fact about the framework
rather than a preference.** When multi-tenancy is detected the generated host registers an *empty*
`InMemoryTenantStore`. Defaulting this to `true` would answer 404 to every request of every
multi-tenant application until someone populated that store: "unknown" cannot mean anything while the
default list is empty.

Turn it on where the store really is the list of tenants that exist, and it is worth turning on.
`Header`, `Route` and `Subdomain` read the tenant straight out of the request, so without it an
invented id becomes the request's tenant. Reads then filter on a tenant with no rows, which is not a
leak; writes create rows under a tenant that does not exist, and nothing says so.

It is a separate switch from `EnforceTenantState` because it asks a stronger question. That one
trusts the store about the tenants it knows; this one trusts it about which tenants exist at all.

### Non-HTTP Contexts

For background jobs, seed scripts, message handlers, or tests, use `TenantScope`:

```csharp
using var scope = TenantScope.BeginScope("tenant-123", "Acme Corp");
// All code in this async flow sees TenantId = "tenant-123"
await ProcessBackgroundJobAsync();
```

`TenantScope` uses `AsyncLocal<T>` internally, so it:
- Flows through `async`/`await` continuations.
- Supports nesting with correct restore semantics.
- Is independent of DI (static API, no service resolution needed).

## Built-In Resolvers

### HeaderTenantResolver

Reads the tenant ID from an HTTP request header.

```csharp
app.UseMultiTenancy(mt => mt.UseHeader());            // default: X-Tenant-Id
app.UseMultiTenancy(mt => mt.UseHeader("Tenant-Key")); // custom header name
```

**Configuration**: `MultiTenancyOptions.TenantHeaderName` (default: `"X-Tenant-Id"`).

**When to use**: API-to-API communication, development/testing, B2B APIs where the caller controls the header. For authenticated requests the resolved header value is validated against the user's `tenant_id` claim (see [Claim Guard](#claim-guard-cross-tenant-protection)).

**Example request**:
```http
GET /api/invoices HTTP/1.1
X-Tenant-Id: acme-corp
```

### ClaimTenantResolver

Reads the tenant ID from a JWT claim on the authenticated user.

```csharp
app.UseMultiTenancy(mt => mt.UseClaim());              // default: tenant_id
app.UseMultiTenancy(mt => mt.UseClaim("org_id"));       // custom claim type
```

**Configuration**: `MultiTenancyOptions.TenantClaimType` (default: `"tenant_id"`).

**When to use**: Applications where the identity provider embeds the tenant ID in the JWT token. The most secure strategy since the tenant is cryptographically bound to the token.

**Requirement**: Authentication middleware must run before tenant resolution for claims to be available.

### SubdomainTenantResolver

Extracts the tenant ID from the first segment of the request's host.

```csharp
app.UseMultiTenancy(mt => mt.UseSubdomain());
```

**Resolution logic**: splits the host by `.` and uses the first segment if there are at least 3 parts (subdomain.domain.tld).

| Host | Resolved Tenant |
|------|----------------|
| `acme.app.com` | `acme` |
| `beta.staging.app.com` | `beta` |
| `app.com` | `null` (only 2 segments) |
| `localhost` | `null` (only 1 segment) |

**When to use**: SaaS applications with vanity subdomains per tenant.

### RouteTenantResolver

Reads the tenant ID from a route parameter.

```csharp
app.UseMultiTenancy(mt => mt.UseRoute());              // default: {tenantId}
app.UseMultiTenancy(mt => mt.UseRoute("orgSlug"));      // custom parameter name
```

**Configuration**: `MultiTenancyOptions.TenantRouteParameter` (default: `"tenantId"`).

**When to use**: APIs structured as `/{tenantId}/invoices`, `/{tenantId}/orders`, etc.

**Example route**: `GET /acme-corp/invoices` with route template `/{tenantId}/invoices`.

### SingleTenantResolver

Returns a fixed tenant ID. This is the default when no strategy is configured.

```csharp
app.UseMultiTenancy(mt => mt.UseSingleTenant());           // default: "default"
app.UseMultiTenancy(mt => mt.UseSingleTenant("my-app"));   // custom fixed ID
```

**When to use**: Single-tenant deployments. Registered as `Singleton` for zero per-request overhead.

## Composite Resolution (Chain of Responsibility)

When several strategies are configured, they are wrapped in a `CompositeTenantResolver` that tries each one in the order the `Use*` calls were made (one strategy alone is registered as itself):

```csharp
app.UseMultiTenancy(mt => mt
    .UseHeader()              // 1st: try X-Tenant-Id header
    .UseClaim()               // 2nd: try JWT claim
    .UseSingleTenant("demo")  // 3rd: fallback to "demo"
);
```

**Behavior**:
- Resolvers are tried sequentially in call order.
- The first resolver to return a non-null, non-empty string wins.
- If a resolver throws an exception (not `OperationCanceledException`), the error is logged at `Warning` level and the chain continues to the next resolver.
- If all resolvers return null, the composite returns null and logs a warning.

This enables resilient tenant resolution with graceful fallback chains.

## Custom Resolvers

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
            return null;

        return await tenantStore.GetTenantIdByApiKeyAsync(apiKey, cancellationToken);
    }
}
```

Register it via the builder:

```csharp
app.UseMultiTenancy(mt => mt.UseResolver<ApiKeyTenantResolver>());
```

Custom resolvers participate in the composite chain like built-in ones.

## Configuration Reference

All options are centralized in `MultiTenancyOptions`:

```csharp
services.Configure<MultiTenancyOptions>(options =>
{
    options.DefaultTenantId = "primary";
    options.TenantHeaderName = "X-Organization-Id";
    options.TenantClaimType = "org_id";
    options.TenantRouteParameter = "orgSlug";
    options.RequireTenant = true;
    options.EnforceTenantClaim = true; // validate resolved tenant against the user's tenant claim
    options.EnforceTenantState = true;  // serve only tenants the store reports as Active
    options.RequireKnownTenant = true;  // ...and only tenants the store contains (off by default)
});
```

The builder methods (`UseHeader`, `UseClaim`, `UseRoute`) accept optional parameters that configure these options automatically. Direct `Configure<MultiTenancyOptions>` is also supported for advanced scenarios.

## Middleware Ordering

`TenantResolutionMiddleware` runs after route matching and authentication, and before the endpoint:

```
Routing            (matches the route; the endpoint and its metadata are now known)
      |
Authentication     (sets ClaimsPrincipal)
      |
TenantResolution   (reads claims/headers, sets ITenantContext, may refuse)
      |
The endpoint       (ITenantContext available to it, to filters, and to services)
```

The step orders are `RoutingStep` 50, `AuthenticationStep` 91, `TenantResolutionStep` 92. Routing
runs *before* tenant resolution, and that order is what makes `TenantAgnosticEndpoint` below
possible: an endpoint's metadata cannot be read before the route it belongs to has been matched.

In Pragmatic.Design applications using `PragmaticApp.RunAsync`, the middleware is added automatically in the correct position. The SG-generated host wiring handles this.

## Routes That Belong to No Tenant

With `RequireTenant` (the default), a request that resolves no tenant is refused with `400` before
any endpoint runs. That is right for the domain surface and wrong for what sits underneath it: a
liveness probe sends no tenant header and has nowhere to get one, and the published OpenAPI document
is what a caller fetches *before* being anyone.

Mark such an endpoint:

```csharp
endpoints.MapGet("/health", Handler)
    .WithMetadata(TenantAgnosticEndpoint.Instance);
```

The framework marks its own: the aggregated health endpoint (`HealthEndpointMapper`) and the
compile-time contract (`MapPragmaticOpenApi`). An application marks anything of its own that answers
about the process rather than about a tenant.

⚠️ **It relaxes the requirement, not the resolution.** A tenant supplied on one of these routes is
still resolved, still checked against the caller's claim, and still published downstream; only its
absence stops being an error. The tenant *state* check is also skipped, because the state of one
tenant is not a fact about a route that belongs to none: suspending a tenant must not take the
liveness probe with it.

⚠️ **A marker, not a list of paths.** Both framework routes are configurable (the health path through
`HostHealthOptions`, the document's through `MapPragmaticOpenApi(pattern)`), so a default list of
strings would be right only for applications that changed nothing, and silently wrong for the rest.

**Measured, not assumed:** before this existed, a host that had simply enabled the health endpoint
answered `/health` with **400** and, with `X-Tenant-Id`, with **200**. A Kubernetes probe would have
marked the application dead and restarted it for ever, by a check that never ran.

## Logging

Both `TenantResolutionMiddleware` and `CompositeTenantResolver` use `[LoggerMessage]` for zero-allocation structured logging:

| Level | Message | When |
|-------|---------|------|
| `Debug` | `Tenant '{TenantId}' resolved for {RequestPath}` | Tenant successfully resolved |
| `Trace` | `No tenant resolved for {RequestPath}` | No resolver returned a value |
| `Warning` | `Tenant claim mismatch ... Rejecting request (403) ...` | Resolved tenant differs from the authenticated user's tenant claim |
| `Warning` | `Tenant resolver {ResolverName} failed, continuing to next resolver` | A resolver threw an exception |
| `Warning` | `No tenant resolver succeeded out of {ResolverCount} registered resolver(s). Returning null tenant ID` | All resolvers returned null |

## DI Registrations

`AddPragmaticMultiTenancy()` registers:

| Service | Lifetime | Implementation |
|---------|----------|----------------|
| `MutableTenantContext` | Scoped | Concrete class |
| `ITenantContext` | Scoped | Delegates to `MutableTenantContext` |
| `ITenantResolver` | Depends on strategy | `SingleTenantResolver` (Singleton) or HTTP resolvers (Scoped) |

All core registrations use `TryAdd` to avoid duplicate registrations when both SG auto-registration and explicit `UseMultiTenancy()` are present. The resolver registration does **not** use `TryAdd`, so `UseMultiTenancy()` correctly overrides the SG default.
