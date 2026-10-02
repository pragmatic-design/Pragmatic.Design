# Getting Started with Pragmatic.Authorization

This guide walks you through adding authorization to a Pragmatic.Design application from scratch.

## Prerequisites

- A Pragmatic.Design host application using `PragmaticApp.RunAsync`
- At least one module with entities and actions (e.g., `Showcase.Booking`)

## Step 1: Add the Package Reference

The host project typically gets `Pragmatic.Authorization` transitively through modules or `Pragmatic.Abstractions`. The SG auto-detects it via `FeatureDetector` when any referenced assembly includes authorization types.

If you need a direct reference:

```xml
<PackageReference Include="Pragmatic.Authorization" />
```

## Step 2: Configure Authorization in Program.cs

The `UseAuthorization` method on `IPragmaticBuilder` is the entry point:

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    app.UseAuthorization(authz =>
    {
        // Endpoints require authentication by default (PragmaticEndpointsOptions.RequireAuthorizationByDefault);
        // opt a single endpoint out with [AllowAnonymous].
    });
});
```

An endpoint with no `[RequirePermission]` or `[RequirePolicy]` still requires a valid identity:
`PragmaticEndpointsOptions.RequireAuthorizationByDefault` is `true`, and the generated root group
applies it. Use `[AllowAnonymous]` on the endpoints that must be reachable without one.

Requiring a *permission* by default is a separate switch, off unless you turn it on: auto-derivation
gives every operation that declares none a permission derived from its name.

## Step 3: Define Your First Role

Application roles live in the host project. Each role is a class implementing `IRole`:

```csharp
using Pragmatic.Authorization;

namespace MyApp.Host.Authorization;

public sealed class AdminRole : IRole
{
    public static string Name => "admin";
    public static string? Description => "Full system access";
    public static IReadOnlyList<string> DefaultPermissions => ["*"];
}
```

Then map it in `Program.cs`:

```csharp
app.UseAuthorization(authz =>
{
    // Endpoints require authentication by default (PragmaticEndpointsOptions.RequireAuthorizationByDefault);
    // opt a single endpoint out with [AllowAnonymous].
    authz.MapRole<AdminRole>();
});
```

When a user has the `role` claim set to `"admin"`, the `RoleExpansionProvider` resolves it to `["*"]`, which `WildcardMatcher` matches against any required permission.

## Step 4: Add Permission Enforcement

Annotate actions, mutations, or queries with `[RequirePermission]`:

```csharp
using Pragmatic.Authorization;

[RequirePermission("booking.reservation.read")]
public sealed class SearchReservationsQuery : IQuery<PagedResult<ReservationDto>>
{
    // query properties...
}
```

The SG generates permission constants per entity. After a build, you can use them:

```csharp
[RequirePermission(BookingPermissions.Reservation.Read)]
public sealed class SearchReservationsQuery : IQuery<PagedResult<ReservationDto>>
{
    // ...
}
```

## Step 5: Define Module Permission Templates

Modules define `IRoleDefinition` to expose reusable permission bundles. These are NOT application roles -- they are building blocks that the host composes into roles.

```csharp
using Pragmatic.Authorization;

namespace MyModule.Authorization;

public sealed class BookingOperator : IRoleDefinition
{
    public static string Name => "booking-operator";
    public static string? Description => "Full CRUD on reservations and guests";
    public static IReadOnlyList<string> Permissions =>
    [
        BookingPermissions.Reservation.All,      // "booking.reservation.*"
        BookingPermissions.Guest.All             // "booking.guest.*"
    ];
}
```

## Step 6: Compose Roles from Definitions

In the host's `Program.cs`, compose application roles by pulling in module definitions:

```csharp
app.UseAuthorization(authz =>
{
    // Endpoints require authentication by default (PragmaticEndpointsOptions.RequireAuthorizationByDefault);
    // opt a single endpoint out with [AllowAnonymous].

    authz.MapRole<AdminRole>();

    authz.MapRole<ManagerRole>(r => r
        .IncludeDefinition<BookingOperator>()
        .IncludeDefinition<CatalogReader>());

    authz.MapRole<ReceptionistRole>(r => r
        .WithPermissions(
            BookingPermissions.Reservation.Read,
            BookingPermissions.Reservation.Create,
            BookingPermissions.Reservation.Update)
        .IncludeDefinition<CatalogReader>());
});
```

The `ManagerRole` class itself can have empty `DefaultPermissions` -- all permissions come from the composition in `Program.cs`.

## Step 7: Add Groups (Optional)

Groups aggregate roles. A user with a `group` claim gets all the roles (and their permissions) assigned to that group.

```csharp
using Pragmatic.Authorization;

namespace MyApp.Host.Authorization;

public sealed class CustomerCareGroup : IGroup
{
    public static string Name => "customer-care";
    public static string? Description => "Customer care team";
    public static IReadOnlyList<string> DefaultRoles =>
    [
        BookingManagerRole.Name,
        CatalogViewerRole.Name
    ];
}
```

Then map it:

```csharp
authz.MapGroup<CustomerCareGroup>();
```

## Step 8: Add Custom Permissions (Optional)

For operations beyond standard CRUD, declare the permission on the assembly — one line each:

```csharp
using Pragmatic.Authorization;

[assembly: Permission("billing.invoice.refund", "Refund a paid invoice", Category = "Billing")]
```

The SG writes its constant into the boundary's permissions class, beside the CRUD ones, and the operation
names it:

```csharp
[DomainAction]
[RequirePermission(BillingPermissions.Invoice.Refund)]
public partial class RefundInvoiceAction : VoidDomainAction<ConflictError> { /* ... */ }
```

A permission only one operation requires can be declared where it is required —
`[RequirePermission("billing.invoice.refund", Description = "Refund a paid invoice")]` — with the same
effect: the full value, as written, nothing derived.

## Step 9: Add a Resource Authorizer (Optional)

For instance-level access control (e.g., "can this user access this specific invoice?"):

```csharp
using Pragmatic.Authorization;
using Pragmatic.Identity;

public sealed class InvoiceAuthorizer : IResourceAuthorizer<RefundInvoiceAction>
{
    public ValueTask<bool> CanAccessAsync(
        ICurrentUser user, RefundInvoiceAction resource, string action,
        CancellationToken ct = default)
    {
        if (user.Authorization.HasPermission("billing.admin"))
            return ValueTask.FromResult(true);

        if (!user.IsAuthenticated)
            return ValueTask.FromResult(false);

        return ValueTask.FromResult(
            user.Authorization.HasPermission("billing.invoice.refund"));
    }
}
```

Register it — prefer the two-type-parameter, reflection-free overload:

```csharp
authz.AddResourceAuthorizer<InvoiceAuthorizer, RefundInvoiceAction>();
// or, to auto-discover every IResourceAuthorizer<T> the type implements (uses reflection):
// authz.AddResourceAuthorizer<InvoiceAuthorizer>();
```

## Step 10: Enable Caching (Optional)

For cross-request permission caching via `ICacheStack` (using `CacheCategories.Permissions`):

```csharp
authz.UsePermissionCache(TimeSpan.FromMinutes(5));

// or configure the consistency strategy explicitly:
authz.UsePermissionCache(o =>
{
    o.Expiration = TimeSpan.FromMinutes(30);
    o.Strategy = PermissionCacheStrategy.ManualInvalidation;
});
```

Cache keys include the user ID (and tenant ID if multi-tenant); entries are tagged by `user:{userId}`,
`tenant:{tenantId}`, and each contributing role/group. `UsePermissionCache` also registers
`IPermissionCacheInvalidator` — inject it to evict entries after a permission change
(`InvalidateUserAsync` / `InvalidateRoleAsync` / `InvalidateGroupAsync` / `InvalidateTenantAsync`).

## Step 11: Seed Roles from JSON (Optional)

Add `roles.pragmatic.json` to your host project:

```json
{
  "roles": {
    "front-desk": {
      "description": "Front desk staff",
      "permissions": ["booking.reservation.read", "booking.reservation.create"]
    }
  },
  "groups": {
    "operations": {
      "description": "All operational staff",
      "roles": ["front-desk", "catalog-viewer"]
    }
  }
}
```

The SG generates `SeedFromJson()`:

```csharp
authz.SeedFromJson();
```

## Complete Example

Putting it all together (from the Showcase application):

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    app.UseAuthorization(authz =>
    {
        // Endpoints require authentication by default (PragmaticEndpointsOptions.RequireAuthorizationByDefault);
        // opt a single endpoint out with [AllowAnonymous].

        // Roles
        authz.MapRole<ShowcaseAdmin>();
        authz.MapRole<BookingManagerRole>(r => r
            .IncludeDefinition<BookingOperator>()
            .IncludeDefinition<CatalogReader>());
        authz.MapRole<CatalogEditorRole>(r => r
            .IncludeDefinition<CatalogEditor>());
        authz.MapRole("auditor", r => r
            .WithOperation<BookingBoundary>(CrudOperation.Read)
            .WithOperation<BillingBoundary>(CrudOperation.Read)
            .WithOperation<CatalogBoundary>(CrudOperation.Read));

        // Groups
        authz.MapGroup<CustomerCareGroup>();

        // ABAC
        authz.AddResourceAuthorizer<InvoiceAuthorizer>();

        // JSON seeding
        authz.SeedFromJson();

        // Caching
        authz.UsePermissionCache(TimeSpan.FromMinutes(5));
    });
});
```

## How Users Get Claims

The authorization system reads claims from `ICurrentUser.Claims`:

| Claim type | Used by |
|------------|---------|
| `permission` | `ClaimsPermissionProvider` (direct permissions) |
| `role` | `RoleExpansionProvider` (expanded to permissions) |
| `group` | `GroupExpansionProvider` (expanded to roles, then permissions) |
| `scope` | `IUserAuthorization.Scopes` |

How these claims get populated depends on your authentication setup:

- **JWT**: Claims from the token (standard JWT claims)
- **NoOp (development)**: Claims from HTTP headers (`X-User-Id`, `X-User-Roles`, `X-User-Permissions`, `X-User-Groups`)
- **External IdP**: Claims from the identity provider's token or userinfo endpoint

## Next Steps

- [Permission Resolution](permission-resolution.md) -- internals of the provider chain
- [Policies](policies.md) -- composable authorization rules
- [Roles and Groups](roles-and-groups.md) -- the composition model in depth
- [Stores](stores.md) -- in-memory vs database-backed stores
