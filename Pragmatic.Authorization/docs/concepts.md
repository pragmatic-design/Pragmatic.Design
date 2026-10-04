# Architecture and Core Concepts

This guide explains **why** Pragmatic.Authorization exists, how its pieces fit together, and how to choose the right abstraction for each situation. Read this before diving into the individual feature guides.

---

## The Problem

Authorization in most .NET applications starts simple and deteriorates into a fragile web of scattered checks that nobody trusts.

### Scattered permission checks, no single source of truth

```csharp
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class ReservationsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(CancellationToken ct)
    {
        // Check 1: manual permission string, duplicated across controllers
        if (!User.HasClaim("permission", "booking.reservation.read"))
            return Forbid();

        var result = await _reservations.SearchAsync(ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRequest request, CancellationToken ct)
    {
        // Check 2: different pattern, same intent
        var isAdmin = User.IsInRole("admin");
        var hasPermission = User.HasClaim("permission", "booking.reservation.create");
        if (!isAdmin && !hasPermission)
            return Forbid();

        // Check 3: instance-level check buried in business logic
        if (request.CustomerId != GetCurrentUserId() && !isAdmin)
            return Forbid();

        var reservation = await _reservations.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = reservation.Id }, reservation);
    }

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        // Check 4: yet another pattern for the same system
        var permissions = User.Claims
            .Where(c => c.Type == "permission")
            .Select(c => c.Value)
            .ToHashSet();

        if (!permissions.Contains("booking.reservation.cancel")
            && !permissions.Any(p => p == "booking.reservation.*")
            && !permissions.Contains("*"))
            return Forbid();

        await _reservations.CancelAsync(id, ct);
        return NoContent();
    }
}
```

Four endpoints, four different authorization patterns. The same permission string `"booking.reservation.read"` is hardcoded in multiple files. Role-to-permission mapping lives in if-statements. Wildcard handling is hand-rolled and incomplete. When someone asks "what permissions does the booking-manager role have?", the answer requires reading every controller in the project.

### Real consequences

| Symptom | Root cause |
|---------|-----------|
| Permission strings drift across files | No single source of truth for permission names |
| Admin bypass logic is inconsistent | Role-to-permission mapping is ad-hoc, not declarative |
| "Can user X do Y on resource Z?" requires reading code | Instance-level authorization is mixed into business logic |
| Wildcard matching has edge-case bugs | Each developer implements `*` support differently |
| Token contains permissions that expand over time | No group/role expansion -- permissions are flat in the JWT |
| Permission changes require code deployment | Roles and permissions are hardcoded, not configurable |
| No audit trail for permission evaluations | Authorization checks are invisible to observability |

**The fundamental issue**: authorization decisions are made in business code, scattered across the codebase, with no enforced structure, no compile-time safety, and no runtime observability.

---

## The Solution

Pragmatic.Authorization inverts the model. You declare **what** is protected and **how** -- the framework handles resolution, matching, caching, and enforcement.

The same reservation authorization, the Pragmatic way:

```csharp
// 1. Permissions are generated constants (compile-time safe)
[RequirePermission(BookingPermissions.Reservation.Read)]
public sealed class SearchReservationsQuery : IQuery<PagedResult<ReservationDto>>
{
    // query properties...
}

[RequirePermission(BookingPermissions.Reservation.Create)]
public sealed class CreateReservationMutation : Mutation<Reservation>
{
    // mutation properties...
}

[RequirePermission(BookingPermissions.Reservation.Cancel)]
[RequirePolicy<ReservationCancellationPolicy>]
public sealed class CancelReservationAction : VoidDomainAction
{
    // action properties...
}
```

```csharp
// 2. Roles compose from module definitions (host-level, declarative)
await PragmaticApp.RunAsync(args, app =>
{
    app.UseAuthorization(authz =>
    {
        // Endpoints require authentication by default (PragmaticEndpointsOptions.RequireAuthorizationByDefault);
        // opt a single endpoint out with [AllowAnonymous].

        authz.MapRole<AdminRole>();
        authz.MapRole<BookingManagerRole>(r => r
            .IncludeDefinition<BookingOperator>()
            .IncludeDefinition<CatalogReader>());

        authz.MapGroup<CustomerCareGroup>();
        authz.UsePermissionCache(TimeSpan.FromMinutes(5));
    });
});
```

The source generator produces:
- `BookingPermissions.Reservation.Read`, `.Create`, `.Update`, `.Delete`, `.All` -- compile-time constants
- `PermissionRegistry` -- a complete list of all known permissions for admin tooling
- `SeedFromJson()` if `roles.pragmatic.json` exists -- role definitions from config, not code

At runtime:
- The provider chain resolves user claims to a flat permission set (claims, roles, groups)
- `WildcardMatcher` handles `*` patterns consistently
- `CachedPermissionResolver` caches the resolved set per request (and optionally cross-request)
- `PolicyEvaluationFilter` evaluates composable policies before the action runs (Order 210)
- `ResourceAuthorizationFilter` handles instance-level checks (Order 250)
- `AuthorizationDiagnostics` emits OTel counters for every check

No reflection at check time. No manual wildcard code. No permission strings scattered across business logic.

---

## How It Works: The Runtime Pipeline

Every request flows through four layers of authorization, each progressively more specific. The framework runs only the layers that are configured.

```
Request
  |
  v
L1 Authentication             Who is the caller?
  |                            ICurrentUser populated from JWT/cookie/headers
  v
L2 Permission Gate             Can the caller perform this operation?
  |                            CachedPermissionResolver + WildcardMatcher
  |                            Enforced by [RequirePermission] on action
  v
L3 Resource Authorization      Can the caller act on THIS resource?
  |                            IResourceAuthorizer<T> + ResourceAuthorizationFilter (Order 250)
  |                            [RequirePolicy<T>] + PolicyEvaluationFilter (Order 210)
  v
L4 Data-Level Filtering        Which rows can the caller see?
  |                            IQueryFilter<T>, IPermissionBasedFilter<T>
  v
Handler
```

L1 is handled by `Pragmatic.Identity` (authentication). L2 through L4 are `Pragmatic.Authorization`. Each layer is independent:

- An endpoint with `[RequirePermission]` but no resource authorizer runs L1 + L2 only.
- An endpoint with `[RequirePolicy<T>]` and an `IResourceAuthorizer<T>` runs L1 + L2 + L3.
- A query with `IQueryFilter<T>` runs L1 + L2 + L4 (data filtering at the IQueryable level).

---

## The Permission Model

Permissions follow a three-segment naming convention: `{boundary}.{entity}.{operation}`.

```
booking.reservation.read       -- Read reservations in the Booking boundary
billing.invoice.refund         -- Custom operation: refund in Billing
catalog.amenity.update         -- Update amenities in Catalog
```

### Permission Hierarchy

```
L0: booking                       -- boundary namespace
L1: booking.reservation.read      -- CRUD (SG-generated from entities)
L2: billing.invoice.refund        -- custom operation ([assembly: Permission])
L3: IQueryFilter<T>               -- data-level scope (row filtering)
```

L0 and L1 permissions are generated automatically by the SG when it detects entities in a module. L2 permissions are declared explicitly, one line each, with `[assembly: Permission]` (below). L3 is enforced at the data layer.

### SG-Generated Permission Constants

For each entity detected in a module, the SG generates nested static classes:

```csharp
// Generated: BookingPermissions
public static class BookingPermissions
{
    public static class Reservation
    {
        public const string Read = "booking.reservation.read";
        public const string Create = "booking.reservation.create";
        public const string Update = "booking.reservation.update";
        public const string Delete = "booking.reservation.delete";
        public const string All = "booking.reservation.*";
    }

    public static class Guest
    {
        public const string Read = "booking.guest.read";
        public const string Create = "booking.guest.create";
        public const string Update = "booking.guest.update";
        public const string Delete = "booking.guest.delete";
        public const string All = "booking.guest.*";
    }
}
```

Use these constants in `[RequirePermission]` attributes and `IRoleDefinition.Permissions` for compile-time safety. Typos become compile errors.

### Custom Permissions: `[assembly: Permission]`

For operations beyond standard CRUD, one line per permission: its value, what holding it allows, and
optionally the group a role screen lists it under:

```csharp
[assembly: Permission("billing.invoice.refund", "Refund a paid invoice", Category = "Billing")]
```

The SG adds its constant to the **same** `{Boundary}Permissions` class the CRUD constants are in (a
resource that is an entity puts it in that entity's class, so this one is `BillingPermissions.Invoice.Refund`,
a `const` usable in `[RequirePermission]`) and its entry (name, description, category) to the generated
`PermissionRegistry`, so `IPermissionCatalog` lists it.

The generator refuses what would not be one constant in that class:

| | Diagnostic |
|---|---|
| The first segment is none of the assembly's boundaries | `PRAG1004` |
| The value is declared twice, or is a CRUD permission (`billing.invoice.read`) | `PRAG1001` |
| Its constant would take a name the class already uses (`billing.invoice` beside the entity `Invoice`'s class) | `PRAG1005` |

In an assembly with **no** boundary (a package such as `Pragmatic.Authorization.Management`) there is no
boundary class and nothing to check the first segment against: the first segment names the class
(`authorization.view` → `AuthorizationPermissions.View`), and when that class is an entity's own CRUD class the
constant joins it.

There is no other way to declare one: there is no type per permission.

### RequirePermission Attribute

Applied to actions, mutations, and queries. Multiple permissions use AND logic (all required):

```csharp
[RequirePermission("booking.reservation.read")]
public sealed class SearchReservationsQuery : IQuery<PagedResult<ReservationDto>>
```

The attribute supports two modes:

| Mode | Usage | Result |
|------|-------|--------|
| Explicit name | `[RequirePermission("booking.reservation.read")]` | Uses the literal string |
| Requirement and declaration | `[RequirePermission("billing.invoice.refund", Description = "Refund a paid invoice")]` | Declares the permission exactly as `[assembly: Permission]` would, and requires it |

Nothing is derived in the second mode: the value is the full string as written, checked by the same rules. A
permission more than one operation requires belongs in `[assembly: Permission]`, and each operation names its
constant.

### What the Caller Is Told

A missing permission is refused with one body, whether the HTTP authorization policy refuses it
(`PragmaticAuthorizationResultHandler`) or the action pipeline does (`PermissionAuthorizationFilter`): a
`ForbiddenError`, written with the host's `IErrorMessageResolver`.

```json
{ "status": 403, "code": "FORBIDDEN", "requiredPermissions": ["booking.reservation.read"], "permissionMatch": "all" }
```

`requiredPermissions` is always an array; `permissionMatch` is `"any"` for `[RequireAnyPermission]`. The
title and the detail are localized like any other error, by the code (`error.forbidden.title`,
`error.forbidden.detail`, with `{permissions}` available to the translation).

---

## The Role System

Pragmatic.Authorization distinguishes between **module permission templates** and **application roles**. This separation lets modules define what permissions exist without dictating how they are assembled into roles.

### IRoleDefinition -- Module Permission Templates

Modules define `IRoleDefinition` to expose reusable permission bundles. These live in the module project:

```csharp
// In Showcase.Booking module
public sealed class BookingOperator : IRoleDefinition
{
    public static string Name => "booking-operator";
    public static string? Description => "Full CRUD on reservations and guests";
    public static IReadOnlyList<string> Permissions =>
    [
        BookingPermissions.Reservation.All,
        BookingPermissions.Guest.All,
        BookingPermissions.GuestPreferences.All
    ];
}
```

`IRoleDefinition` is NOT an application role. It is a building block -- a named set of permissions that the host can compose into application roles.

### IRole -- Application Roles

Application roles live in the host project and represent the actual roles that users are assigned:

```csharp
// In the host project
public sealed class ShowcaseAdmin : IRole
{
    public static string Name => "admin";
    public static string? Description => "Full system access";
    public static IReadOnlyList<string> DefaultPermissions => ["*"];
}

public sealed class BookingManagerRole : IRole
{
    public static string Name => "booking-manager";
    public static string? Description => "Full booking operations with catalog context";
    public static IReadOnlyList<string> DefaultPermissions => [];
    // Permissions come from IncludeDefinition<BookingOperator>() in Program.cs
}
```

Roles are mapped in `Program.cs` via `AuthorizationBuilder`:

```csharp
authz.MapRole<AdminRole>();

authz.MapRole<BookingManagerRole>(r => r
    .IncludeDefinition<BookingOperator>()
    .IncludeDefinition<CatalogReader>());

authz.MapRole<ReceptionistRole>(r => r
    .WithPermissions(
        BookingPermissions.Reservation.Read,
        BookingPermissions.Reservation.Create)
    .IncludeDefinition<CatalogReader>());

authz.MapRole("auditor", r => r
    .WithOperation<BookingBoundary>(CrudOperation.Read)
    .WithOperation<BillingBoundary>(CrudOperation.Read));
```

### Key Distinction

| Aspect | IRole | IRoleDefinition |
|--------|-------|-----------------|
| **Defined in** | Host project | Module project |
| **Purpose** | Application role (mapped to user claim) | Permission template (building block) |
| **Used in** | `authz.MapRole<T>()` | `roleBuilder.IncludeDefinition<T>()` |
| **At runtime** | Resolved by `RoleExpansionProvider` via `role` claim | Not resolved directly -- feeds into roles |
| **Naming** | Application-specific ("admin", "booking-manager") | Module-specific ("booking-operator", "catalog-reader") |

### RoleBuilder API

`RoleBuilder` provides fluent configuration when mapping roles:

| Method | Description |
|--------|-------------|
| `WithPermissions(params string[])` | Adds explicit permissions |
| `IncludeDefinition<T>()` | Includes all permissions from an `IRoleDefinition` |
| `WithAllPermissions()` | Grants `*` (all permissions) |
| `WithAllPermissions<TBoundary>()` | Grants `{boundary}.*` |
| `WithOperation<TBoundary>(CrudOperation)` | Grants `{boundary}.*.{operation}` |
| `WithoutPermissions(params string[])` | Removes specific permissions (fails fast if covered by wildcard) |
| `ClearDefaults()` | Clears `IRole.DefaultPermissions` |

Resolution order:
1. Start with `IRole.DefaultPermissions` (unless `ClearDefaults()` was called)
2. Add all permissions from `WithPermissions()` and `IncludeDefinition<T>()`
3. Remove permissions from `WithoutPermissions()` (with wildcard safety check)

---

## Group Expansion

Groups aggregate roles. A user with a `group` claim gets all the roles (and their permissions) assigned to that group.

### IGroup Interface

```csharp
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

### GroupBuilder API

```csharp
authz.MapGroup<CustomerCareGroup>();

authz.MapGroup<ExtendedGroup>(g => g
    .WithRoles("extra-role")
    .WithRole<AnotherRole>());

authz.MapGroup("operations", g => g.WithRoles("front-desk", "catalog-viewer"));
```

| Method | Description |
|--------|-------------|
| `WithRoles(params string[])` | Adds roles by name |
| `WithRole<TRole>()` | Adds a strongly-typed role |

### Expansion Flow

When a user has `group` claim `"customer-care"`:

```
GroupExpansionProvider (Order 200):
  "customer-care" --> IGroupRoleStore --> ["booking-manager", "catalog-viewer"]

  For each role:
    "booking-manager" --> IRolePermissionStore --> ["booking.reservation.*", "booking.guest.*"]
    "catalog-viewer"  --> IRolePermissionStore --> ["catalog.amenity.read", "catalog.property.read"]

  Merged result: { "booking.reservation.*", "booking.guest.*",
                    "catalog.amenity.read", "catalog.property.read" }
```

---

## The Provider Chain

Permission resolution runs through an ordered chain of `IPermissionProvider` implementations. Each provider contributes permissions from a different source. Results are merged with union semantics -- no provider can remove permissions added by another.

```csharp
public interface IPermissionProvider
{
    int Order { get; }
    ValueTask<IReadOnlySet<string>> ResolvePermissionsAsync(
        ICurrentUser user, CancellationToken ct = default);
}
```

### Built-in Providers

| Provider | Order | Source | Registered when |
|----------|-------|--------|-----------------|
| `ClaimsPermissionProvider` | 0 | `ICurrentUser.Claims["permission"]` | Always |
| `RoleExpansionProvider` | 100 | `IRolePermissionStore` via `role` claims | Roles are mapped or custom store registered |
| `GroupExpansionProvider` | 200 | `IGroupRoleStore` + `IRolePermissionStore` via `group` claims | Groups are mapped or custom store registered |

### ClaimsPermissionProvider (Order 0)

The base provider. Reads the `"permission"` claim from `ICurrentUser.Claims` and returns each value as a permission string:

```
User claims: { "permission": ["booking.reservation.read", "catalog.amenity.read"] }
Result: HashSet { "booking.reservation.read", "catalog.amenity.read" }
```

This provider is always registered. It enables direct permission assignment via JWT claims.

### RoleExpansionProvider (Order 100)

For each `"role"` claim, queries `IRolePermissionStore.GetPermissionsForRoleAsync(roleName)`:

```
User claims: { "role": ["booking-manager"] }

IRolePermissionStore lookup:
  "booking-manager" --> { "booking.reservation.*", "booking.guest.*", "catalog.property.read" }

Result: HashSet { "booking.reservation.*", "booking.guest.*", "catalog.property.read" }
```

### GroupExpansionProvider (Order 200)

For each `"group"` claim, resolves groups to roles, then roles to permissions:

```
User claims: { "group": ["customer-care"] }

IGroupRoleStore:       "customer-care" --> { "booking-manager", "catalog-viewer" }
IRolePermissionStore:  "booking-manager" --> { "booking.reservation.*", "booking.guest.*" }
IRolePermissionStore:  "catalog-viewer"  --> { "catalog.amenity.read" }

Result: HashSet { "booking.reservation.*", "booking.guest.*", "catalog.amenity.read" }
```

### Custom Providers

Register additional providers that resolve permissions from external sources:

```csharp
authz.AddPermissionProvider<ExternalPermissionProvider>();
```

Custom providers participate in the same chain. Set the `Order` property to control execution sequence.

### Resolution Flow Diagram

```
Request arrives
  |
  v
CachedPermissionResolver.ResolvePermissions()
  |
  +-- Already resolved this request? --> Return cached set (cache_hits +1)
  |
  +-- Cross-request cache enabled? --> Check ICacheStack
  |   |
  |   +-- Cache hit? --> Return cached set
  |   |
  |   +-- Cache miss? --> Run provider chain, cache result
  |
  +-- No cross-request cache --> Run provider chain (cache_misses +1)
  |
  v
For each IPermissionProvider (ordered by Order):
  Call ResolvePermissionsAsync(user)
  Merge into HashSet<string> (union)
  |
  v
Store as _resolvedPermissions (request-scoped)
  |
  v
Return IReadOnlySet<string>
```

---

## Resource Authorization

Beyond permission gates ("can the user perform this operation?"), some operations need instance-level authorization ("can the user act on THIS specific resource?").

### IResourceAuthorizer\<T>

```csharp
public interface IResourceAuthorizer<in TResource>
{
    ValueTask<bool> CanAccessAsync(
        ICurrentUser user, TResource resource, string action,
        CancellationToken ct = default);
}
```

The interface is contravariant on `TResource`, so `IResourceAuthorizer<DomainAction<OrderDto>>` can be used for any action that derives from `DomainAction<OrderDto>`.

### Implementation

```csharp
public sealed class InvoiceAuthorizer : IResourceAuthorizer<RefundInvoiceAction>
{
    public ValueTask<bool> CanAccessAsync(
        ICurrentUser user, RefundInvoiceAction resource, string action,
        CancellationToken ct = default)
    {
        // Billing admin can refund anything
        if (user.Authorization.HasPermission("billing.admin"))
            return ValueTask.FromResult(true);

        // Must be authenticated with the refund permission
        if (!user.IsAuthenticated)
            return ValueTask.FromResult(false);

        return ValueTask.FromResult(
            user.Authorization.HasPermission("billing.invoice.refund"));
    }
}
```

### Registration

Two overloads. Prefer the two-type-parameter one: it registers the exact closed interface with **no
reflection** (aligns with the zero-reflection philosophy):

```csharp
// Preferred: reflection-free, names both the authorizer and the resource type
authz.AddResourceAuthorizer<InvoiceAuthorizer, RefundInvoiceAction>();

// Convenience: discovers every IResourceAuthorizer<T> the type implements via GetInterfaces()
authz.AddResourceAuthorizer<InvoiceAuthorizer>();
```

Both register the authorizer in DI as Scoped. The single-parameter overload uses `GetInterfaces()`
reflection at registration time; use it only when one authorizer implements several resource interfaces.

### Enforcement

`ResourceAuthorizationFilter` (Order 250 in the action pipeline) resolves `IResourceAuthorizer<TAction>` from DI and calls `CanAccessAsync`. If the authorizer returns false, the filter returns `ForbiddenError`. If no authorizer is registered for the action type, the filter is a no-op.

---

## Composable Policies (ResourcePolicy)

For authorization rules more complex than a single permission check, `ResourcePolicy` provides composable, declarative rules. Analogous to `Specification<T>` but for authorization decisions.

### Factory Methods

| Method | Description |
|--------|-------------|
| `ResourcePolicy.Allow` | Always allows (identity for OR) |
| `ResourcePolicy.Deny` | Always denies (identity for AND) |
| `ResourcePolicy.RequirePermission(string)` | Single permission check |
| `ResourcePolicy.RequireAnyPermission(params string[])` | Any of the permissions (OR) |
| `ResourcePolicy.RequireAllPermissions(params string[])` | All of the permissions (AND) |
| `ResourcePolicy.InRole(string)` | Role membership check |
| `ResourcePolicy.InGroup(string)` | Group membership check |
| `ResourcePolicy.HasClaim(string, string?)` | Claim existence / value check |
| `ResourcePolicy.IsAuthenticated()` | Authentication check (singleton) |
| `ResourcePolicy.HasPrincipalKind(PrincipalKind)` | Principal kind check |
| `ResourcePolicy.Custom(Func<ICurrentUser, bool>)` | Delegate-based (not serializable) |

### Composition Operators

Policies compose with `&` (AND), `|` (OR), and `!` (NOT):

```csharp
var policy = ResourcePolicy.RequirePermission("booking.reservation.create")
    & ResourcePolicy.IsAuthenticated()
    | ResourcePolicy.HasPrincipalKind(PrincipalKind.Service);

bool allowed = policy.Evaluate(currentUser);
```

C# operator precedence applies: `&` binds tighter than `|`. Use parentheses for clarity.

### Defining Named Policies

```csharp
public sealed class ReservationManagementPolicy : ResourcePolicy
{
    public override bool Evaluate(ICurrentUser user)
    {
        var isService = HasPrincipalKind(PrincipalKind.Service);
        var hasPermission = IsAuthenticated()
            & RequirePermission(BookingPermissions.Reservation.Create);

        return (isService | hasPermission).Evaluate(user);
    }
}
```

### Applying Policies to Actions

Use the `[RequirePolicy<T>]` attribute:

```csharp
[RequirePolicy<ReservationManagementPolicy>]
public sealed class CreateReservationMutation : Mutation<Reservation>
```

Requirements:
- The policy type must have a parameterless constructor.
- The instance is created once and cached by `PolicyEvaluationFilter`.
- Evaluated at Order 210 in the action pipeline.
- Denial returns 403 Forbidden.

### AsyncResourcePolicy

For policies requiring I/O (database lookups, external services):

```csharp
var asyncPolicy = AsyncResourcePolicy.RequireExternalPermission(
    async (user, ct) => await externalService.CheckPermissionAsync(user.Id, ct));
```

Sync policies convert implicitly to async via `SyncToAsyncPolicy`. Async composition supports short-circuit evaluation: AND returns false immediately if the left side is false, OR returns true immediately if the left side is true.

### Policy Serialization

`PolicySerializer` converts between `ResourcePolicy` and `PolicyExpression` for JSON storage:

```csharp
var policy = ResourcePolicy.RequirePermission("orders.read")
    & ResourcePolicy.IsAuthenticated();

PolicyExpression expr = PolicySerializer.Serialize(policy);
// Store expr as JSON in a database

ResourcePolicy restored = PolicySerializer.Deserialize(expr);
bool result = restored.Evaluate(currentUser);
```

`Custom()` and `RequireExternalPermission()` delegate-based policies are not serializable and throw `NotSupportedException`.

---

## Wildcard Matching

`WildcardMatcher` provides consistent wildcard pattern matching for permissions. All matching is case-insensitive.

### Supported Patterns

| Pattern | Matches | Use case |
|---------|---------|----------|
| `*` | All permissions | Super admin |
| `booking.*` | All permissions starting with `booking.` | Boundary admin |
| `booking.reservation.*` | All reservation operations | Entity admin |
| `booking.*.read` | Read on all entities in booking | Boundary reader |
| `*.reservation.read` | Read Reservation across all boundaries | Cross-boundary reader |

### Matching Algorithm

`WildcardMatcher.Matches(pattern, permission)` resolves in this order:

1. **Exact match**: `string.Equals(pattern, permission, OrdinalIgnoreCase)` -- returns true
2. **Global wildcard**: `pattern == "*"` -- returns true for any permission
3. **Suffix wildcard**: Pattern like `"booking.*"` (ends with `.*`, no `*.` inside) -- checks `permission.StartsWith("booking.")`
4. **Segment wildcard**: Pattern like `"booking.*.read"` -- splits on `.` and compares segment by segment, with `*` matching exactly one segment

### Segment Matching Rules

When patterns and permissions have the same number of segments, each `*` matches exactly one segment:

```
Pattern:    booking.*.read
Permission: booking.reservation.read
Match:      booking=booking, *=reservation, read=read --> TRUE
```

When the pattern has a trailing `*` and fewer segments than the permission, it matches all remaining:

```
Pattern:    booking.*
Permission: booking.reservation.read
Trailing * --> prefix check: booking=booking --> TRUE
```

When segment counts differ and there is no trailing `*`:

```
Pattern:    booking.*.read
Permission: booking.reservation
Segments:   3 vs 2, no trailing * --> FALSE
```

### How Matching Integrates with Resolution

`CachedPermissionResolver.MatchesPermission(required)` runs two steps:

1. **Fast path**: Exact match -- `resolved.Contains(required)`. Covers the most common case.
2. **Slow path**: For each resolved permission that contains `*`, calls `WildcardMatcher.Matches(granted, required)`.

In practice, the resolved permission set is small (tens to low hundreds), making the wildcard scan negligible.

---

## Caching

Permission resolution has two caching layers.

### Request-Scoped Caching (Always Active)

`CachedPermissionResolver` is registered as **Scoped** (one per request). Permissions are resolved lazily on first access to `Permissions`, `HasPermission`, `HasAnyPermission`, or `HasAllPermissions`. The resolved set is stored in `_resolvedPermissions` and reused for all subsequent checks within the same request.

This means an endpoint that calls `HasPermission` three times triggers the provider chain only once.

### Cross-Request Caching (Opt-in)

When `UsePermissionCache` is configured and `ICacheStack` is available in DI:

```csharp
authz.UsePermissionCache(TimeSpan.FromMinutes(5));
```

The resolved permission set is cached cross-request via `ICacheStack`.

Cache key format (explicit scope markers prevent cross-tenant collisions):
- Global / single-tenant: `{prefix}:g:u:{userId}`
- Multi-tenant: `{prefix}:t:{tenantId}:u:{userId}`

Cache tags: the entry is tagged by **everything that contributes to it**, so a change to any
contributing role or group can invalidate exactly the affected users:
- `user:{userId}`: direct per-user invalidation / account disable
- `tenant:{tenantId}`: tenant-wide change (present only for multi-tenant requests)
- `role:{roleName}`: one per role claim; invalidate when a role's permission set changes
- `group:{groupName}`: one per group claim; invalidate when a group's role set changes

### Invalidating on demand: IPermissionCacheInvalidator

`UsePermissionCache` also registers `IPermissionCacheInvalidator`: inject it and call the method that
matches the change, rather than composing tag strings by hand:

```csharp
public sealed class AssignRoleHandler(IPermissionCacheInvalidator invalidator)
{
    public async Task Handle(string userId, CancellationToken ct)
    {
        // ... persist the role change ...
        await invalidator.InvalidateUserAsync(userId, ct);      // this user
        // invalidator.InvalidateRoleAsync("booking-manager")   (every holder of the role)
        // invalidator.InvalidateGroupAsync("customer-care")    (every member of the group)
        // invalidator.InvalidateTenantAsync(tenantId)          (a whole tenant)
    }
}
```

This is the `PermissionCacheStrategy.ManualInvalidation` workflow. Under the default
`PermissionCacheStrategy.TimeToLive`, entries simply expire and calling the invalidator is optional.

### PermissionCacheOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Expiration` | `TimeSpan` | 5 minutes | Cache entry TTL (also the safety-net upper bound under `ManualInvalidation`) |
| `KeyPrefix` | `string` | `"permissions"` | Prefix for cache keys |
| `Strategy` | `PermissionCacheStrategy` | `TimeToLive` | `TimeToLive` (expire + re-resolve) or `ManualInvalidation` (keep until `IPermissionCacheInvalidator` evicts) |

Configure the strategy via the options overload of `UsePermissionCache`:

```csharp
authz.UsePermissionCache(o =>
{
    o.Expiration = TimeSpan.FromMinutes(30);
    o.Strategy = PermissionCacheStrategy.ManualInvalidation;
});
```

---

## What Gets Generated

The source generator detects `Pragmatic.Authorization` via `FeatureDetector` and generates the following when entities, `[assembly: Permission]`, `IRole`, or `[RequirePermission]` types are present.

### The `{Boundary}Permissions` class

One static class per boundary, the only one: the CRUD constants of each entity, and the declared
permissions beside them:

```csharp
// Generated: BookingPermissions
public static class BookingPermissions
{
    public static class Reservation
    {
        public const string Read = "booking.reservation.read";
        public const string Create = "booking.reservation.create";
        public const string Update = "booking.reservation.update";
        public const string Delete = "booking.reservation.delete";
        public const string All = "booking.reservation.*";
    }
}
```

A declared permission's path is its value's segments in PascalCase: `billing.invoice.refund` is
`BillingPermissions.Invoice.Refund`, `leave.personal-data.erase` is `LeavePermissions.PersonalData.Erase`,
and it is a `const`, like the CRUD ones.

### PermissionRegistry

A unified registry of all known permissions and roles in the assembly:

```csharp
// Generated: PermissionRegistry
public static class PermissionRegistry
{
    public static IReadOnlyList<PermissionInfo> AllPermissions { get; } = [
        new("booking.reservation.read", "Read Reservation", "Booking"),
        new("booking.reservation.create", "Create Reservation", "Booking"),
        // ...
    ];

    public static IReadOnlyList<RoleInfo> AllRoles { get; } = [
        new("admin", "Full system access", ["*"]),
        // ...
    ];
}
```

The registry feeds `IPermissionCatalog` for admin tooling and inspection.

### Declared Permission Constants

`[assembly: Permission("billing.invoice.refund", "Refund a paid invoice")]` (or the same value on a
`[RequirePermission(..., Description = ...)]`) puts the constant into the boundary's class, beside the CRUD
ones:

```csharp
// Generated: BillingPermissions (one class, CRUD and declared)
public static partial class BillingPermissions
{
    public static partial class Invoice
    {
        public const string Read = "billing.invoice.read";
        // ... the other CRUD constants

        /// <summary>Refund a paid invoice</summary>
        public const string Refund = "billing.invoice.refund";
    }
}
```

### RoleSeedingExtensions

When `roles.pragmatic.json` exists as an AdditionalFile:

```csharp
// Generated: RoleSeedingExtensions
public static class RoleSeedingExtensions
{
    public static AuthorizationBuilder SeedFromJson(this AuthorizationBuilder builder)
    {
        builder.MapRole("front-desk", r => r.WithPermissions(
            "booking.reservation.read", "booking.reservation.create", "booking.guest.read"));
        builder.MapGroup("operations", g => g.WithRoles("front-desk", "catalog-viewer"));
        return builder;
    }
}
```

Role inheritance (`"inherits"` in JSON) is expanded at compile-time. The generated code contains flattened permission sets.

---

## Stores

Stores provide the backing data for role-to-permission and group-to-role mappings.

### In-Memory Stores (Default)

| Store | Populated by | Lifetime |
|-------|-------------|----------|
| `InMemoryRolePermissionStore` | `MapRole`, `SeedFromJson` | Singleton |
| `InMemoryGroupRoleStore` | `MapGroup`, `SeedFromJson` | Singleton |

Thread-safe via `ConcurrentDictionary`. Case-insensitive lookup.

### Custom Stores

Override with database-backed implementations:

```csharp
authz.UseRolePermissionStore<EfRolePermissionStore>();
authz.UseGroupRoleStore<EfGroupRoleStore>();
```

Custom stores are registered as **Scoped**. When registered, they replace the in-memory store (the in-memory store is not registered).

### Store Selection Logic

| Condition | Store | Provider |
|-----------|-------|----------|
| `MapRole` called, no custom store | `InMemoryRolePermissionStore` (Singleton) | `RoleExpansionProvider` (Scoped) |
| `UseRolePermissionStore<T>` called | Custom `T` (Scoped) | `RoleExpansionProvider` (Scoped) |
| Neither called | None | None |

Same logic applies for groups. When groups are mapped but **no role store exists at all**, configuration
throws `InvalidOperationException` at startup: `GroupExpansionProvider` needs an `IRolePermissionStore`
to resolve group → role → permission, and silently returning an empty set would look like an
authorization bug. Map at least one role (or register a custom role store) alongside your groups.

### Forward-Compatible Interfaces

| Interface | Extends | Purpose |
|-----------|---------|---------|
| `ITemporalRolePermissionStore` | `IRolePermissionStore` | Point-in-time permission resolution |
| `ITenantRolePermissionStore` | `IRolePermissionStore` | Tenant-specific permissions |
| `ITemporalGroupRoleStore` | `IGroupRoleStore` | Point-in-time group resolution |
| `ITenantGroupRoleStore` | `IGroupRoleStore` | Tenant-specific groups |

### Dynamic Stores (for RBAC Management)

| Interface | Purpose |
|-----------|---------|
| `IDynamicPermissionStore` | Runtime-created permissions |
| `IDynamicRoleStore` | Runtime-created roles |
| `IResourcePolicyStore` | Dynamic policy-to-resource assignments |

Consumed by the catalog system and the optional `Pragmatic.Authorization.Management` package.

---

## Catalogs

`IPermissionCatalog` and `IResourceCatalog` are discovery APIs for admin tooling and inspection. They merge static (SG-generated) and dynamic (database-backed) sources.

### IPermissionCatalog

```csharp
public interface IPermissionCatalog
{
    ValueTask<IReadOnlyList<PermissionInfo>> GetAllPermissionsAsync(CancellationToken ct = default);
    ValueTask<IReadOnlyList<RoleInfo>> GetAllRolesAsync(CancellationToken ct = default);
    ValueTask<IReadOnlySet<string>> GetEffectivePermissionsAsync(
        string userId, CancellationToken ct = default);
}
```

`DefaultPermissionCatalog` merges the SG-generated `PermissionRegistry` with `IDynamicPermissionStore` (if registered).

### IResourceCatalog

```csharp
public interface IResourceCatalog
{
    ValueTask<IReadOnlyList<ProtectedResource>> GetAllResourcesAsync(CancellationToken ct = default);
    ValueTask<PolicyExpression?> GetResourcePolicyAsync(
        string resourceIdentifier, CancellationToken ct = default);
}
```

`DefaultResourceCatalog` merges static resources with `IResourcePolicyStore` (if registered).

### ProtectedResource

```csharp
public sealed record ProtectedResource(
    string ResourceType,     // "Endpoint", "Action", "Mutation"
    string Identifier,       // route pattern or action FQN
    string DisplayName,
    string? Category)
{
    public IReadOnlyList<string> StaticPermissions { get; init; } = [];
    public PolicyExpression? PolicyExpression { get; init; }
    public string? ResourceGroup { get; init; }
}
```

---

## Observability

`AuthorizationDiagnostics` provides OTel-compatible instrumentation:

| Instrument | Name | Description |
|------------|------|-------------|
| `ActivitySource` | `Pragmatic.Authorization` | Distributed tracing |
| `Counter<long>` | `pragmatic.authorization.permission_checks` | Total permission checks |
| `Counter<long>` | `pragmatic.authorization.permission_denied` | Denied permission checks |
| `Counter<long>` | `pragmatic.authorization.cache_hits` | Cache hits (resolved set already available) |
| `Counter<long>` | `pragmatic.authorization.cache_misses` | Cache misses (full provider chain invoked) |

Use these metrics to monitor:
- **Denial rate**: High `permission_denied / permission_checks` ratio may indicate misconfigured roles.
- **Cache hit rate**: Low `cache_hits / (cache_hits + cache_misses)` suggests caching is not effective (check TTL or whether most requests are unauthenticated).

---

## Ecosystem Integration

### Identity (Pragmatic.Identity)

`ICurrentUser` provides the claims that feed the provider chain:

| Claim type | Used by |
|------------|---------|
| `permission` | `ClaimsPermissionProvider` (direct permissions) |
| `role` | `RoleExpansionProvider` (expanded to permissions) |
| `group` | `GroupExpansionProvider` (expanded to roles, then permissions) |
| `scope` | `IUserAuthorization.Scopes` |

`IUserAuthorization` is accessed via `ICurrentUser.Authorization`, providing `HasPermission`, `IsInRole`, `IsInGroup`, and `HasScope` methods.

### Actions (Pragmatic.Actions)

The action pipeline integrates authorization at two points:

| Filter | Order | Purpose |
|--------|-------|---------|
| `PolicyEvaluationFilter` | 210 | Evaluates `[RequirePolicy<T>]` |
| `ResourceAuthorizationFilter` | 250 | Calls `IResourceAuthorizer<T>.CanAccessAsync` |

Both filters skip evaluation for internal calls (checked via `ActionCallContext.IsInternalCall`).

### Endpoints (Pragmatic.Endpoints)

`[RequirePermission]` and `[RequirePolicy<T>]` on endpoint classes attach authorization metadata to the generated endpoint. The SG generates `.RequireAuthorization()` calls with the appropriate policy.

`[AllowAnonymous]` overrides group-level or default authorization.

What an endpoint without those attributes requires is decided in two places, neither of them here:

| What | Where | Default |
|---|---|---|
| A valid identity | `PragmaticEndpointsOptions.RequireAuthorizationByDefault`, applied by the generated root group | `true` |
| A permission for an operation that declares none | auto-derivation: `[assembly: PragmaticAutoDerivePermissions]` or the `PragmaticAutoDerivePermissions` build property | off |

These two are the whole surface, and deliberately: a second switch deciding the same thing is how two
settings come to disagree, and the loser is silent.

### Persistence (Pragmatic.Persistence)

Data-level authorization integrates via `IQueryFilter<T>` and `IPermissionBasedFilter<T>`. These are separate from Pragmatic.Authorization but consume `IUserAuthorization` for permission decisions at the row level.

**Access scopes for `[HasOwner]` / `[HasAccessScopes]`.** The SG-generated data filters take an
`IUserScopeResolver` from DI to compute the set of access scopes the current user holds.
`AddPragmaticAuthorization` registers `DefaultUserScopeResolver` (via `TryAdd`, so a custom resolver
registered first wins). It expands the user into `user:{id}`, `role:{roleName}` (one per role), and
`scope:{value}` for each `data-scope` claim. Leaving it unregistered would turn every scoped-entity query
into a runtime failure, which is why it is registered by default. Provide your own `IUserScopeResolver`
when access scopes must come from a database or an external system rather than claims.

The **write** side is `Pragmatic.Persistence.EFCore`'s `ScopeInterceptor`, at `SaveChanges`: an
inserted row with no scopes carries its creator's `user:{id}`. The two sides have to agree on the exact
string, and no type enforces it: a divergence is not an error anywhere, it is a row nobody can see,
so both read the spelling from `ScopeIdentifiers` in `Pragmatic.Abstractions`.

### Caching (Pragmatic.Caching)

Cross-request permission caching uses `ICacheStack` from `Pragmatic.Caching`. The cache key includes the user ID (and tenant ID if multi-tenant). Tags enable targeted invalidation.

---

## DI Registration Summary

`UseAuthorization` / `AddPragmaticAuthorization` registers:

| Registration | Lifetime | Notes |
|-------------|----------|-------|
| `IPermissionProvider` (ClaimsPermissionProvider) | Scoped | Always registered |
| `IPermissionProvider` (RoleExpansionProvider) | Scoped | When roles are mapped or custom store used |
| `IPermissionProvider` (GroupExpansionProvider) | Scoped | When groups are mapped or custom store used |
| `IRolePermissionStore` (InMemoryRolePermissionStore) | Singleton | When `MapRole` used without custom store |
| `IGroupRoleStore` (InMemoryGroupRoleStore) | Singleton | When `MapGroup` used without custom store |
| `IUserAuthorization` (CachedPermissionResolver) | Scoped | Always registered |
| `IUserScopeResolver` (DefaultUserScopeResolver) | Scoped | TryAdd: a custom resolver registered first wins |
| `IPermissionCacheInvalidator` (PermissionCacheInvalidator) | Scoped | Only when `UsePermissionCache` is called |
| `IPermissionCatalog` (DefaultPermissionCatalog) | Singleton | TryAdd (SG-generated takes precedence) |
| `IResourceCatalog` (DefaultResourceCatalog) | Singleton | TryAdd (SG-generated takes precedence) |
| `AuthorizationOptions` | Options | Via `IOptions<AuthorizationOptions>` |

---

## JSON Role Seeding

The `roles.pragmatic.json` file provides declarative role and group definitions without code:

```json
{
  "roles": {
    "front-desk": {
      "description": "Front desk staff with basic booking access",
      "permissions": ["booking.reservation.read", "booking.reservation.create", "booking.guest.read"]
    },
    "revenue-manager": {
      "description": "Revenue management with full catalog and rate access",
      "permissions": ["catalog.*"],
      "inherits": ["front-desk"]
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

Role inheritance (`inherits`) is expanded at compile-time by the SG, between roles of the same file; a code-defined role is not inherited. The generated `SeedFromJson()` contains flattened permission sets. A file that is not JSON is `PRAG1010`; a property or value it does not understand, an inherited role it does not declare, or roles that inherit each other is `PRAG1011`; a second file is `PRAG1012` (see [roles-and-groups.md](roles-and-groups.md)).

Usage:

```csharp
authz.SeedFromJson();  // Calls generated RoleSeedingExtensions.SeedFromJson()
```

---

## Performance Characteristics

| Operation | Complexity |
|-----------|-----------|
| First permission check (cold) | O(P * R) where P = providers, R = roles/groups per user |
| Subsequent checks (warm) | O(1) exact match, O(N) wildcard scan where N = resolved permissions |
| Cross-request cache hit | O(1) ICacheStack lookup |
| Wildcard suffix match | O(1) string prefix check |
| Wildcard segment match | O(S) where S = number of segments |

In practice, the resolved permission set is small (tens to low hundreds), making the wildcard scan negligible. The request-scoped cache ensures the provider chain runs at most once per request.

---

## See Also

- [Getting Started](getting-started.md) -- Install and configure authorization from scratch
- [Permission Resolution](permission-resolution.md) -- Provider chain internals and caching details
- [Policies](policies.md) -- Composable ResourcePolicy and AsyncResourcePolicy
- [Roles and Groups](roles-and-groups.md) -- Composition model, RoleBuilder, GroupBuilder
- [Stores](stores.md) -- In-memory vs database-backed stores, forward-compatible interfaces
- [Common Mistakes](common-mistakes.md) -- Mistakes to avoid
- [Troubleshooting](troubleshooting.md) -- Problem/solution guide
