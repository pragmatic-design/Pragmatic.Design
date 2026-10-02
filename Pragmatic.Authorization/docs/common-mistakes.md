# Common Mistakes

These are the most common issues developers encounter when using Pragmatic.Authorization. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Hardcoding Permission Strings Instead of Using Constants

**Wrong:**

```csharp
[RequirePermission("booking.reservation.read")]
public sealed class SearchReservationsQuery : IQuery<PagedResult<ReservationDto>>
{
    // ...
}

// Elsewhere in the codebase:
[RequirePermission("booking.reservations.read")]  // Typo: "reservations" vs "reservation"
public sealed class GetReservationEndpoint : Endpoint<ReservationDto>
{
    // ...
}
```

**Runtime result:** The query works, but the endpoint never authorizes correctly because `"booking.reservations.read"` (plural) is a different permission from `"booking.reservation.read"` (singular). No compile-time error. No runtime warning. The user gets 403 Forbidden and nobody knows why.

**Right:**

```csharp
[RequirePermission(BookingPermissions.Reservation.Read)]
public sealed class SearchReservationsQuery : IQuery<PagedResult<ReservationDto>>
{
    // ...
}

[RequirePermission(BookingPermissions.Reservation.Read)]
public sealed class GetReservationEndpoint : Endpoint<ReservationDto>
{
    // ...
}
```

**Why:** The SG generates `BookingPermissions.Reservation.Read` as a `const string`. A typo in the constant name is a compile error. A typo in a string literal is a silent authorization failure.

---

## 2. Forgetting to Configure UseAuthorization in Program.cs

**Wrong:**

```csharp
// Program.cs
await PragmaticApp.RunAsync(args, app =>
{
    // No UseAuthorization call!
    app.UseAuthentication<JwtBearerHandler>("Bearer");
});
```

```csharp
// In a module:
[RequirePermission(BookingPermissions.Reservation.Read)]
public sealed class SearchReservationsQuery : IQuery<PagedResult<ReservationDto>>
{
    // ...
}
```

**Runtime result:** When `Pragmatic.Authorization` is referenced, the SG auto-registers `AddPragmaticAuthorization()` with default settings. However, no roles are mapped and `DefaultPolicy` is `AllowAnonymous`. The `[RequirePermission]` attribute on the action is checked by the pipeline, but the user has no resolved permissions because no role store is configured. Authenticated users without direct `permission` claims get 403 on everything.

**Right:**

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    app.UseAuthentication<JwtBearerHandler>("Bearer");
    app.UseAuthorization(authz =>
    {
        // Endpoints require authentication by default (PragmaticEndpointsOptions.RequireAuthorizationByDefault);
        // opt a single endpoint out with [AllowAnonymous].
        authz.MapRole<AdminRole>();
        authz.MapRole<BookingManagerRole>(r => r
            .IncludeDefinition<BookingOperator>());
    });
});
```

**Why:** Without `MapRole`, the `RoleExpansionProvider` is not registered. Users with `role` claims have no mechanism to resolve those roles into permissions. Always configure `UseAuthorization` with at least one role mapping or a custom `IRolePermissionStore`.

---

## 3. Wrong Permission Naming Convention

**Wrong:**

```csharp
[assembly: Permission("ApproveReservation", "Approve a reservation")]
```

**Compile result:** `PRAG1004` -- the first segment, `ApproveReservation`, is none of the assembly's
boundaries. A declared permission is checked; a literal written straight into `[RequirePermission("ApproveReservation")]`
is not, and compiles cleanly -- the cost shows up later, when wildcard grants stop matching.

**Right:**

```csharp
[assembly: Permission("booking.reservation.approve", "Approve a reservation")]
// → BookingPermissions.Reservation.Approve, beside the CRUD constants
```

**Why:** The three-segment convention `{boundary}.{entity}.{operation}` is what the SG uses to generate `BookingPermissions.Reservation.Read` and `BookingPermissions.Reservation.Approve` constants. It is also what `RoleBuilder.WithAllPermissions<BookingBoundary>()` uses to derive `"booking.*"` and `RoleBuilder.WithOperation<BookingBoundary>(CrudOperation.Read)` uses to derive `"booking.*.read"`. Non-standard names break wildcard matching and role composition. Wildcard `"booking.*"` will not match `"ReadReservation"`.

---

## 4. Not Configuring the Provider Chain for Groups

**Wrong:**

```csharp
// Program.cs
app.UseAuthorization(authz =>
{
    authz.MapRole<AdminRole>();
    authz.MapRole<BookingManagerRole>(r => r
        .IncludeDefinition<BookingOperator>());
    // No MapGroup! But users have "group" claims...
});
```

**Runtime result:** Users with `group` claims (e.g., `"group": ["customer-care"]`) get no permissions from their groups. The `GroupExpansionProvider` is not registered because no groups were mapped and no custom `IGroupRoleStore` was configured. The `group` claim is silently ignored.

**Right:**

```csharp
app.UseAuthorization(authz =>
{
    authz.MapRole<AdminRole>();
    authz.MapRole<BookingManagerRole>(r => r
        .IncludeDefinition<BookingOperator>());

    // Map the group so GroupExpansionProvider is registered
    authz.MapGroup<CustomerCareGroup>();
});
```

**Why:** The `GroupExpansionProvider` is only registered when `MapGroup` is called or a custom `IGroupRoleStore` is registered. Without it, `group` claims are not expanded to roles and permissions. If your identity provider includes `group` claims in the token, you must map those groups in `UseAuthorization`.

---

## 5. Resource Authorizer Without Filter Registration

**Wrong:**

```csharp
public sealed class InvoiceAuthorizer : IResourceAuthorizer<RefundInvoiceAction>
{
    public ValueTask<bool> CanAccessAsync(
        ICurrentUser user, RefundInvoiceAction resource, string action,
        CancellationToken ct = default)
    {
        return ValueTask.FromResult(
            user.Authorization.HasPermission("billing.invoice.refund"));
    }
}

// In Program.cs:
app.UseAuthorization(authz =>
{
    authz.MapRole<AdminRole>();
    // Forgot: authz.AddResourceAuthorizer<InvoiceAuthorizer>();
});
```

**Runtime result:** The `InvoiceAuthorizer` is never called. `ResourceAuthorizationFilter` (Order 250) resolves `IResourceAuthorizer<RefundInvoiceAction>` from DI. Since the authorizer is not registered, the filter resolves `null` and is a no-op. No error, no warning -- the resource-level check is simply skipped.

**Right:**

```csharp
app.UseAuthorization(authz =>
{
    authz.MapRole<AdminRole>();
    authz.AddResourceAuthorizer<InvoiceAuthorizer>();
});
```

**Why:** `AddResourceAuthorizer<T>()` discovers all `IResourceAuthorizer<T>` interfaces on the type and registers each in DI. Without this registration, the filter cannot find the authorizer. Always register resource authorizers explicitly.

---

## 6. Checking Permissions Manually Instead of Via Attributes

**Wrong:**

```csharp
[Endpoint(HttpVerb.Post, "/reservations")]
public partial class CreateReservationEndpoint : Endpoint<ReservationDto>
{
    private ICurrentUser _currentUser = null!;

    public override async Task<Result<ReservationDto>> HandleAsync(CancellationToken ct)
    {
        // Manual permission check in business logic
        if (!_currentUser.Authorization.HasPermission("booking.reservation.create"))
            return Result<ReservationDto>.Failure(ForbiddenError.ActionDenied("CreateReservation"));

        // ... business logic
    }
}
```

**Runtime result:** Works, but the permission check is invisible to the framework. The OpenAPI spec does not show that the endpoint requires `booking.reservation.create`. The `PermissionRegistry` does not know this endpoint is protected. Admin tooling that reads `IResourceCatalog` cannot discover the permission requirement. The check is duplicated if multiple endpoints need the same permission.

**Right:**

```csharp
[Endpoint(HttpVerb.Post, "/reservations")]
[RequirePermission(BookingPermissions.Reservation.Create)]
public partial class CreateReservationEndpoint : Endpoint<ReservationDto>
{
    public override async Task<Result<ReservationDto>> HandleAsync(CancellationToken ct)
    {
        // No manual check needed -- the pipeline enforces it before HandleAsync runs
        // ... business logic
    }
}
```

**Why:** `[RequirePermission]` is declarative. The SG uses it to generate OpenAPI metadata, the action pipeline enforces it before your handler runs, and the `PermissionRegistry` makes it discoverable. Manual checks bypass all of these benefits.

**Exception:** Manual checks are appropriate for fine-grained, context-dependent decisions inside business logic (e.g., "if the user has `billing.admin`, show all invoices; otherwise, show only their own"). Use `[RequirePermission]` for the gate, and `IUserAuthorization.HasPermission` for branching logic.

---

## 7. Using WithoutPermissions with Wildcard Grants

**Wrong:**

```csharp
authz.MapRole<LimitedBookingRole>(r => r
    .WithPermissions("booking.*")
    .WithoutPermissions("booking.reservation.delete"));
```

**Startup result:** `InvalidOperationException` at startup: "Cannot exclude 'booking.reservation.delete' -- covered by wildcard 'booking.*'. Use explicit permissions instead of wildcards when using WithoutPermissions."

**Right:**

```csharp
// Option A: Use explicit permissions instead of wildcards
authz.MapRole<LimitedBookingRole>(r => r
    .WithPermissions(
        BookingPermissions.Reservation.Read,
        BookingPermissions.Reservation.Create,
        BookingPermissions.Reservation.Update,
        // Deliberately omitting .Delete
        BookingPermissions.Guest.All));

// Option B: Use IncludeDefinition and exclude from the explicit set
authz.MapRole<LimitedBookingRole>(r => r
    .IncludeDefinition<BookingOperator>()
    .WithoutPermissions("booking.reservation.delete"));
// Works IF BookingOperator.Permissions uses explicit permissions,
// not "booking.*" wildcards
```

**Why:** `WithoutPermissions` performs exact-match removal from the permission list. A wildcard like `"booking.*"` is a single string in the set -- removing `"booking.reservation.delete"` from a set that contains `"booking.*"` does not narrow the wildcard. At runtime, `WildcardMatcher` would still match `"booking.reservation.delete"` against `"booking.*"`. The `RoleBuilder` detects this conflict and fails fast at startup to prevent silent authorization bypasses.

---

## 8. Not Invalidating Cache After Permission Changes

**Wrong:**

```csharp
// Admin endpoint: update user's role
public async Task<VoidResult<IError>> Execute(CancellationToken ct)
{
    await _roleStore.UpdateUserRoleAsync(UserId, NewRole, ct);
    // Done! But... the user's cached permissions are stale
    return VoidResult<IError>.Success();
}
```

**Runtime result:** With `UsePermissionCache(TimeSpan.FromMinutes(5))` configured, the user continues operating with their old permissions for up to 5 minutes after the role change. They might access resources they should no longer have access to, or be denied access to newly granted resources.

**Right:**

```csharp
public async Task<VoidResult<IError>> Execute(CancellationToken ct)
{
    await _roleStore.UpdateUserRoleAsync(UserId, NewRole, ct);

    // Invalidate the user's cached permission set (registered by UsePermissionCache)
    await _invalidator.InvalidateUserAsync(UserId, ct);

    return VoidResult<IError>.Success();
}
```

**Why:** Cross-request permission caching stores the resolved permission set tagged by `user:{userId}`,
`tenant:{tenantId}`, and each contributing `role:{name}` / `group:{name}`. When permissions change you must
evict the affected entries. Inject `IPermissionCacheInvalidator` (registered by `UsePermissionCache`) and
call `InvalidateUserAsync` for a single user, or `InvalidateRoleAsync` / `InvalidateGroupAsync` to reach
every user affected by a role or group change without enumerating them. This is the
`PermissionCacheStrategy.ManualInvalidation` workflow; under the default `TimeToLive` strategy entries
expire on their own and the call is optional.

If you are not using cross-request caching (`UsePermissionCache` is not configured), this is not an issue -- request-scoped caching resolves fresh on every request.

---

## 9. Wildcard Pattern Mistakes

### 9a. Mid-segment wildcards

**Wrong:**

```csharp
authz.MapRole<CustomRole>(r => r
    .WithPermissions("booking.reserv*.read"));
```

**Runtime result:** The pattern `"booking.reserv*.read"` is treated as a literal string (not expanded). `WildcardMatcher` only recognizes `*` as a whole segment or trailing wildcard. The pattern `"reserv*"` does not match `"reservation"` -- it matches literally `"reserv*"`.

**Right:**

```csharp
authz.MapRole<CustomRole>(r => r
    .WithPermissions("booking.reservation.read"));
// Or use segment wildcard:
authz.MapRole<CustomRole>(r => r
    .WithPermissions("booking.*.read"));
```

### 9b. Double wildcards

**Wrong:**

```csharp
authz.MapRole<SuperRole>(r => r
    .WithPermissions("booking.*.*"));
```

**Runtime result:** This works but may not do what you expect. `"booking.*.*"` matches only 3-segment permissions where the first segment is `"booking"`. It does NOT match `"booking.reservation.read.history"` (4 segments). For "everything in booking", use `"booking.*"` (trailing wildcard matches all remaining segments).

**Right:**

```csharp
authz.MapRole<SuperRole>(r => r
    .WithPermissions("booking.*"));
// "booking.*" matches "booking.reservation.read",
// "booking.guest.create", etc. (any depth)
```

### 9c. Case sensitivity assumptions

**Wrong:**

```csharp
// Assuming case-sensitive matching
if (user.Authorization.HasPermission("Booking.Reservation.Read"))
    // ...
```

**Runtime result:** This works correctly. All permission matching in Pragmatic.Authorization is case-insensitive (`StringComparer.OrdinalIgnoreCase`). However, the convention is lowercase with dots, and the SG generates lowercase constants. Use the generated constants to avoid any ambiguity.

---

## 10. Policy Without Parameterless Constructor

**Wrong:**

```csharp
public sealed class TenantPolicy : ResourcePolicy
{
    private readonly string _tenantId;

    public TenantPolicy(string tenantId) => _tenantId = tenantId;

    public override bool Evaluate(ICurrentUser user)
        => user.TenantId == _tenantId;
}

[RequirePolicy<TenantPolicy>]
public sealed class SomeAction : DomainAction<SomeResult>
{
    // ...
}
```

**Compile result:** Compile error on `[RequirePolicy<TenantPolicy>]` -- the `where TPolicy : ResourcePolicy, new()` constraint requires a parameterless constructor. `TenantPolicy` has only a constructor with a `string tenantId` parameter.

**Right:**

```csharp
// Option A: Use composition with factory methods (no constructor parameters)
public sealed class TenantIsolationPolicy : ResourcePolicy
{
    public override bool Evaluate(ICurrentUser user)
        => user.IsAuthenticated && user.TenantId is not null;
}

// Option B: Use ResourcePolicy.Custom for parameterized checks
// (but this is not usable with [RequirePolicy<T>])
var policy = ResourcePolicy.Custom(user => user.TenantId == expectedTenantId);
```

**Why:** `PolicyEvaluationFilter` creates policy instances via `Activator.CreateInstance<TPolicy>()` and caches them. The `new()` constraint on `RequirePolicyAttribute<TPolicy>` enforces this at compile time. Policies that need runtime state should compose built-in factory methods inside `Evaluate` (which access `ICurrentUser` directly) rather than taking constructor parameters.

---

## 11. Mixing IRole and IRoleDefinition

**Wrong:**

```csharp
// In a module: using IRole instead of IRoleDefinition
public sealed class BookingOperator : IRole  // Should be IRoleDefinition!
{
    public static string Name => "booking-operator";
    public static string? Description => "Full CRUD on reservations and guests";
    public static IReadOnlyList<string> DefaultPermissions =>
    [
        BookingPermissions.Reservation.All,
        BookingPermissions.Guest.All
    ];
}

// In the host:
authz.MapRole<BookingManagerRole>(r => r
    .IncludeDefinition<BookingOperator>());  // Compile error: BookingOperator is IRole, not IRoleDefinition
```

**Compile result:** `IncludeDefinition<T>()` has a `where T : IRoleDefinition` constraint. Using `IRole` does not satisfy this constraint.

**Right:**

```csharp
// In a module: IRoleDefinition for permission templates
public sealed class BookingOperator : IRoleDefinition
{
    public static string Name => "booking-operator";
    public static string? Description => "Full CRUD on reservations and guests";
    public static IReadOnlyList<string> Permissions =>
    [
        BookingPermissions.Reservation.All,
        BookingPermissions.Guest.All
    ];
}

// In the host: IRole for application roles
public sealed class BookingManagerRole : IRole
{
    public static string Name => "booking-manager";
    public static string? Description => "Full booking operations";
    public static IReadOnlyList<string> DefaultPermissions => [];
}
```

**Why:** `IRole` is for application roles (defined in the host, mapped to user claims). `IRoleDefinition` is for module permission templates (building blocks that compose into roles). The interfaces have different members: `IRole.DefaultPermissions` vs `IRoleDefinition.Permissions`. Modules should never define `IRole` -- that is the host's responsibility.

---

## 12. Duplicate Permission Names

**Wrong:**

```csharp
[assembly: Permission("billing.invoice.refund", "Refund a paid invoice")]
[assembly: Permission("billing.invoice.read", "View invoices")]   // the CRUD read, already generated

// ... and on an action:
[RequirePermission("billing.invoice.refund", Description = "Refund an invoice")]   // declared again
```

**Compile result:** `PRAG1001` error, once for each -- "Permission name 'billing.invoice.read' is defined by both 'the CRUD permissions of Invoice' and '[assembly: Permission]'."

**Right:**

Declare each value once, and name the constant everywhere else:

```csharp
[assembly: Permission("billing.invoice.refund", "Refund a paid invoice")]

[RequirePermission(BillingPermissions.Invoice.Refund)]
// and the CRUD read is BillingPermissions.Invoice.Read -- nothing to declare
```

**Why:** A declared permission is one `const` in the boundary's permissions class and one registry entry. Two declarations of one value would be two constants of one name -- a compile error in a file you cannot open -- and two registry entries with different descriptions. The SG reports `PRAG1001` instead. A value whose constant would take a name the class already uses, such as `billing.invoice` beside the entity `Invoice`'s class, is `PRAG1005` for the same reason.

---

## Quick Reference

| Mistake | Diagnostic / Symptom |
|---------|---------------------|
| Hardcoded permission strings | Typos cause silent 403 |
| Missing `UseAuthorization` | Users with `role` claims get no permissions |
| Wrong naming convention | `PRAG1004` when declared; silent in a `[RequirePermission]` literal: wildcard matching fails at runtime |
| Missing `MapGroup` | `group` claims silently ignored |
| Resource authorizer not registered | `IResourceAuthorizer<T>` never called |
| Manual permission checks | Invisible to OpenAPI, catalog, pipeline |
| `WithoutPermissions` + wildcard | `InvalidOperationException` at startup |
| Cache not invalidated | Stale permissions for up to TTL |
| Mid-segment wildcards | Treated as literal, no matching |
| Policy without `new()` constructor | Compile error on `[RequirePolicy<T>]` |
| Mixing IRole and IRoleDefinition | Compile error on `IncludeDefinition<T>()` |
| Duplicate permission names | `PRAG1001` compile error |
