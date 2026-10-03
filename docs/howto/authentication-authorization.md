# Authentication & Authorization: How To

Practical entry point for common questions.

Use this document when you need to answer "how do I do X?" quickly.

## Read This First

If you are completely new:

1. [`../../Pragmatic.Authorization/README.md`](../../Pragmatic.Authorization/README.md)
2. This How To
3. The module documentation: [`../../Pragmatic.Authorization/docs/`](../../Pragmatic.Authorization/docs/)

---

## Setting Up Authentication

### How do I set up authentication for development?

Use `NoOpAuthenticationHandler` and `HeaderUserMiddleware`:

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    app.UseAuthentication<NoOpAuthenticationHandler>("PragmaticDefault");
});
```

In your `IStartupStep.ConfigurePipeline`, add:

```csharp
app.UseMiddleware<HeaderUserMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
```

Then send identity via HTTP headers:

```
X-User-Id: user-123
X-User-Name: John Doe
X-User-Roles: booking-manager,catalog-viewer
X-User-Permissions: billing.invoice.refund
X-User-Groups: customer-care
X-Tenant-Id: acme
```

### How do I set up JWT authentication for production?

Add the `Pragmatic.Identity.Local.Jwt` package and configure:

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    app.UseJwtAuthentication(jwt =>
    {
        jwt.SigningKey = config["Jwt:SigningKey"]!;
        jwt.Issuer = "my-app";
        jwt.Audience = "my-app";
        jwt.TokenExpiration = TimeSpan.FromHours(8);
    });
});
```

The `JwtAuthenticationHandler` validates the token and populates `ClaimsPrincipal`.
`LoginUser` action returns a JWT token containing roles, permissions, and tenant claims.

### How do I switch between dev and prod auth?

Check configuration and pick the handler:

```csharp
var jwtKey = config["Jwt:SigningKey"];
if (!string.IsNullOrEmpty(jwtKey))
    app.UseJwtAuthentication(jwt => { jwt.SigningKey = jwtKey; /* ... */ });
else
    app.UseAuthentication<NoOpAuthenticationHandler>("PragmaticDefault");
```

This is exactly what the Showcase does in `Program.cs`.

---

## Permission Basics

### How do I require authentication only (no permissions)?

Set the default policy at host level:

```csharp
app.UseAuthorization(authz =>
{
    // Endpoints require authentication by default (PragmaticEndpointsOptions.RequireAuthorizationByDefault);
    // opt a single endpoint out with [AllowAnonymous].
});
```

Use this when you want authenticated users but not fine-grained permissions yet.

### How do I require one permission?

Annotate the operation:

```csharp
[RequirePermission("booking.reservation.read")]
public partial class GetReservationEndpoint : Endpoint<ReservationDto>;
```

Also valid on domain actions, mutations, and queries.

Use the SG-generated strongly-typed constants:

```csharp
[RequirePermission(BookingPermissions.Reservation.Read)]
```

### How do I require ALL of multiple permissions?

```csharp
[RequirePermission("catalog.property.read", "catalog.amenity.read")]
```

`RequirePermission` means the user must have **all** listed permissions.

### How do I require ANY of multiple permissions?

```csharp
[RequireAnyPermission("catalog.property.read", "catalog.property.search")]
```

`RequireAnyPermission` means the user must have **at least one**.

### How are CRUD permissions generated?

The SG auto-generates permission constants from entities with `[Repository]` or mutations:

```
{boundary}.{entity}.read     ← from [Repository] or [Query]
{boundary}.{entity}.create   ← from Create mutation
{boundary}.{entity}.update   ← from Update mutation
{boundary}.{entity}.delete   ← from SoftDelete or Delete mutation
```

Example: `booking.reservation.read`, `catalog.amenity.create`.

Access them via generated constants: `BookingPermissions.Reservation.Read`.

---

## Roles, Groups, and Composition

### How do I define a role?

Implement `IRole` in your host project:

```csharp
public sealed class BookingManagerRole : IRole
{
    public static string Name => "booking-manager";
    public static string? Description => "Full booking operations with catalog context";
    public static IReadOnlyList<string> DefaultPermissions => [];
}
```

### How do I compose roles from module definitions?

Modules define reusable permission templates with `IRoleDefinition`.
The host builds the actual application roles:

```csharp
app.UseAuthorization(authz =>
{
    authz.MapRole<BookingManagerRole>(r => r
        .IncludeDefinition<BookingOperator>()     // reservation.*, guest.*, guestprefs.*
        .IncludeDefinition<CatalogReader>());     // amenity.read, property.read, ...
});
```

### How do I use wildcards?

```csharp
authz.MapRole<AdminRole>(r => r.WithAllPermissions());                // → "*"
authz.MapRole<CatalogEditor>(r => r.WithAllPermissions<CatalogBoundary>()); // → "catalog.*"
authz.MapRole("auditor", r => r
    .WithOperation<BookingBoundary>(CrudOperation.Read)               // → "booking.*.read"
    .WithOperation<BillingBoundary>(CrudOperation.Read));             // → "billing.*.read"
```

Wildcards are expanded at runtime by `WildcardMatcher`:
- `*` matches everything
- `booking.*` matches all booking permissions
- `booking.*.read` matches read on all booking entities

### How do I remove permissions from a role?

Use `WithoutPermissions` with **explicit** permissions only:

```csharp
authz.MapRole("receptionist", r => r
    .WithPermissions(
        BookingPermissions.Reservation.Read,
        BookingPermissions.Reservation.Create,
        BookingPermissions.Reservation.Update)
    .WithoutPermissions(BookingPermissions.Reservation.Update));
```

**Do not** combine `WithoutPermissions` with wildcard grants.
The builder fails fast at startup when an exclusion conflicts with a wildcard.

### How do I define a group?

```csharp
public sealed class CustomerCareGroup : IGroup
{
    public static string Name => "customer-care";
    public static string? Description => "All customer-facing staff";
    public static IReadOnlyList<string> DefaultRoles => ["booking-manager", "catalog-viewer"];
}
```

Register: `authz.MapGroup<CustomerCareGroup>();`

Groups expand to roles, which expand to permissions. The chain is:
`Group → Roles → Permissions → WildcardMatcher → resolved set`.

### How do I seed roles and groups from JSON?

Add `roles.pragmatic.json` to your project and call `SeedFromJson()`:

```csharp
authz.SeedFromJson();
```

```json
{
  "roles": {
    "front-desk": {
      "description": "Front desk staff",
      "permissions": ["booking.reservation.read", "booking.reservation.create"],
      "inherits": ["catalog-reader"]
    },
    "catalog-reader": {
      "permissions": ["catalog.amenity.read", "catalog.property.read"]
    }
  },
  "groups": {
    "operations": {
      "roles": ["front-desk"]
    }
  }
}
```

Notes:
- `inherits` is expanded recursively at compile time (cycle-safe)
- `description` appears as comments in the generated code
- JSON is a static baseline, not a replacement for runtime management

---

## Advanced Authorization

### How do I add a resource-level authorization check?

Use `IResourceAuthorizer<TResource>` when the decision depends on the specific instance:

```csharp
public sealed class InvoiceAuthorizer : IResourceAuthorizer<RefundInvoiceAction>
{
    public ValueTask<bool> CanAccessAsync(
        ICurrentUser user,
        RefundInvoiceAction action,
        string operation,
        CancellationToken ct)
        => ValueTask.FromResult(
            user.Authorization.HasPermission("billing.invoice.refund"));
}
```

Register: `authz.AddResourceAuthorizer<InvoiceAuthorizer>();`

Runs at Order 250 in the pipeline (after permission check at 200 and policy at 210).

### How do I add a composable policy?

Use `ResourcePolicy` for complex rules combining multiple conditions:

```csharp
public sealed class ReservationManagementPolicy : ResourcePolicy
{
    public override bool Evaluate(ICurrentUser user)
    {
        var isService = HasPrincipalKind(PrincipalKind.Service);
        var hasPermission = IsAuthenticated() & RequirePermission("booking.reservation.create");
        return (isService | hasPermission).Evaluate(user);
    }
}
```

Apply: `[RequirePolicy<ReservationManagementPolicy>]` on the action.

Operators: `&` (AND), `|` (OR), `!` (NOT). Like Specification but for authorization.

### How do I add row-level security?

Use `IPermissionBasedFilter<T>`:

```csharp
public sealed class OwnReservationsFilter(ICurrentUser user)
    : IPermissionBasedFilter<Reservation>
{
    public string BypassPermission => "booking.reservations.view-all";
    public int Priority => 300;

    public Expression<Func<Reservation, bool>> GetFilter()
        => r => r.CreatedByUserId == user.Id;
}
```

Register: `services.AddScoped<IQueryFilter<Reservation>, OwnReservationsFilter>();`

Important behavior:
- Permission-based filters are skipped for unauthenticated users (no user context = no scope to evaluate)
- Skipped in admin/background filter modes
- Users with `BypassPermission` see everything

### How do internal calls skip authorization?

The SG-generated `I{Boundary}InternalActions` facade (the one a module injects to call its own operations) wraps invoker calls in `EnterInternalCall()`:

```csharp
// Generated in BoundaryInterfaceTemplate, for I{Boundary}InternalActions
public async Task<Result<...>> PlaceOrder(PlaceOrderAction action, CancellationToken ct = default)
{
    using var __scope = _callContext?.EnterInternalCall();
    return await _placeOrder.InvokeAsync(action, ct).ConfigureAwait(false);
}
```

The public `I{Boundary}Actions` facade does not: another module calling through it is held to the permission of the operation it invokes, unless the caller is already inside an internal call, such as an event handler or an operation that declares `[AbsorbsChildPermissions]`.

When `IsInternalCall == true`:
- `PermissionAuthorizationFilter` (Order 200) skips
- `PolicyEvaluationFilter` (Order 210) skips
- `ResourceAuthorizationFilter` (Order 250) still runs (instance checks apply)
- Data-level filters still apply

This is the intended path for composite flows, event handlers, and intra-boundary orchestration.

---

## Permission Caching

### How does permission caching work?

Two levels:

1. **Request-scoped** (always active): `CachedPermissionResolver` resolves permissions lazily on first access per request and caches the result for the remainder of the request.

2. **Cross-request** (opt-in): `HybridCache` caches the resolved permission set across requests.

```csharp
authz.UsePermissionCache(TimeSpan.FromMinutes(5));
// or
authz.UsePermissionCache(options =>
{
    options.Expiration = TimeSpan.FromMinutes(5);
    options.KeyPrefix = "perms";  // multi-tenant safe
});
```

### How does the permission resolution chain work?

```
1. ClaimsPermissionProvider (Order 0)    ← reads "permission" claims from JWT
2. RoleExpansionProvider (Order 100)     ← expands roles → permissions via IRolePermissionStore
3. GroupExpansionProvider (Order 200)    ← expands groups → roles → permissions via IGroupRoleStore

All results merge into a single HashSet<string>.
WildcardMatcher resolves patterns (booking.*, *) against the required permission.
```

---

## Pipeline Order Reference

```
Request
 └─ Authentication (L1)         ← middleware: JWT or NoOp handler
 └─ Endpoint Authorization      ← ASP.NET Core: PragmaticPermissionRequirement
 └─ Action Pipeline
     ├─ Validation               (Order 100)
     ├─ PermissionAuthorizationFilter  (Order 200) ← [RequirePermission]
     ├─ PolicyEvaluationFilter   (Order 210) ← [RequirePolicy<T>]
     ├─ ResourceAuthorizationFilter (Order 250) ← IResourceAuthorizer<T>
     ├─ Transaction              (Order 300)
     └─ Action Execution
 └─ Data-Level Filtering (L4)   ← IQueryFilter<T> applied at query time
```

---

## Troubleshooting

### I get 401 but I'm sending credentials

Check:
1. Are you using `HeaderUserMiddleware`? It requires `X-User-Id` header.
2. Is `UseAuthentication()` called before `UseAuthorization()` in the pipeline?
3. For JWT: is the token expired? Is the signing key correct?

### I get 403 but I think I have the right permission

Check:
1. Is the permission string exactly right? Use `BookingPermissions.Reservation.Read` instead of typing the string.
2. Does your role actually include that permission? Check the role mapping in `Program.cs`.
3. Is the permission check happening at endpoint level or action level? Both are independent.
4. For wildcards: `booking.*` covers `booking.reservation.read` but `booking.reservation.*` does not cover `booking.guest.read`.

### Internal calls still get 403

Check:
1. Are you calling through `I{Boundary}InternalActions`? The public `I{Boundary}Actions` and direct invoker calls don't enter internal mode: they enforce the callee's permission, which is what a 403 here says.
2. Is `ICallContext` registered in DI? It's registered automatically when using `Pragmatic.Actions`.

### Permission-based filters don't seem to work on public endpoints

By design: permission-based filters are skipped when no authenticated user is present.
If your endpoint is `[AllowAnonymous]` and also needs data filtering, use a regular `IQueryFilter<T>` (not `IPermissionBasedFilter<T>`).

---

## What is stable and what is evolving?

| Area | Status | Notes |
|------|--------|-------|
| Permission gate (L2) | Stable | `[RequirePermission]`, role/group expansion, wildcards |
| ResourcePolicy (L2b) | Stable | Composable policies, `[RequirePolicy<T>]` |
| Resource authorization (L3) | Stable | `IResourceAuthorizer<T>` |
| Data-level filters (L4) | Stable | `IPermissionBasedFilter<T>`, `IQueryFilter<T>` |
| JWT authentication | Stable | `Identity.Local.Jwt` package |
| JSON seeding | Stable | `roles.pragmatic.json` with inherits |
| Permission caching | Stable | HybridCache cross-request |
| Internal call bypass | Stable | `EnterInternalCall()` in the `I{Boundary}InternalActions` facade |
| Dynamic permissions (DB-backed policies) | Future | Designed, not yet implemented |
| RBAC admin UI | Future | Package exists, endpoints not yet exposed |
| Multi-tenant RBAC | Future | Forward-compat interfaces exist |
