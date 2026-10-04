---
title: "Policies"
description: "Pragmatic.Authorization provides a composable policy system for authorization rules that go beyond simple permission checks. Policies are analogous to `Specific"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Authorization/docs/policies.md
sidebar:
  order: 4
---
Pragmatic.Authorization provides a composable policy system for authorization rules that go beyond simple permission checks. Policies are analogous to `Specification<T>` but operate on `ICurrentUser` instead of entity predicates.

## ResourcePolicy

`ResourcePolicy` is an abstract class with a single abstract method:

```csharp
public abstract bool Evaluate(ICurrentUser user);
```

Policies evaluate synchronously against the current user and return true (allow) or false (deny).

## Factory Methods

All factory methods are static members of `ResourcePolicy`:

### Allow and Deny

Identity elements for composition:

```csharp
ResourcePolicy.Allow   // Always returns true
ResourcePolicy.Deny    // Always returns false
```

### RequirePermission

Checks a single permission via `IUserAuthorization.HasPermission`:

```csharp
ResourcePolicy.RequirePermission("booking.reservation.create")
```

### RequireAnyPermission

Checks if the user has at least one of the specified permissions (OR logic):

```csharp
ResourcePolicy.RequireAnyPermission("booking.reservation.create", "booking.reservation.update")
```

### RequireAllPermissions

Checks if the user has all of the specified permissions (AND logic):

```csharp
ResourcePolicy.RequireAllPermissions("booking.reservation.create", "billing.invoice.read")
```

### InRole

Checks role membership via `IUserAuthorization.IsInRole`:

```csharp
ResourcePolicy.InRole("admin")
```

### InGroup

Checks group membership via `IUserAuthorization.IsInGroup`:

```csharp
ResourcePolicy.InGroup("customer-care")
```

### HasClaim

Checks if the user has a claim with the specified type. Optionally checks the value:

```csharp
ResourcePolicy.HasClaim("department")                    // claim exists
ResourcePolicy.HasClaim("department", "engineering")     // claim exists with specific value
```

The implementation reads from `ICurrentUser.Claims`:
- If `claimValue` is null, any value matches (presence check).
- If `claimValue` is specified, it must be in the claim's value list.

### IsAuthenticated

Checks `ICurrentUser.IsAuthenticated`. Singleton instance:

```csharp
ResourcePolicy.IsAuthenticated()
```

### RequiresMfa

Checks `ICurrentUser.Authentication.IsMfaAuthenticated`: for the ASP.NET Core context, an `amr`
claim equal to `mfa`. Singleton instance:

```csharp
ResourcePolicy.RequiresMfa()
```

Authentication is not MFA, so this is the only thing that makes MFA a requirement rather than an
observation. Before it existed the flag was populated and read by no decision point.

### HasPrincipalKind

Checks `ICurrentUser.Kind` against the specified `PrincipalKind`:

```csharp
ResourcePolicy.HasPrincipalKind(PrincipalKind.Service)
ResourcePolicy.HasPrincipalKind(PrincipalKind.User)
```

### Custom

Creates a policy from a delegate. Not serializable:

```csharp
ResourcePolicy.Custom(user => user.Claims.ContainsKey("premium"))
```

## Composition Operators

Policies compose with C# operators `&` (AND), `|` (OR), and `!` (NOT):

```csharp
// User must be authenticated AND have the permission
var policy = ResourcePolicy.IsAuthenticated()
    & ResourcePolicy.RequirePermission("booking.reservation.create");

// ...OR be a service principal
var fullPolicy = policy | ResourcePolicy.HasPrincipalKind(PrincipalKind.Service);

// Negation
var notAdmin = !ResourcePolicy.InRole("admin");

// Step-up: the permission is not enough on its own for a destructive operation
var stepUp = ResourcePolicy.RequiresMfa()
    & ResourcePolicy.RequirePermission("booking.reservation.cancel");
```

Equivalent fluent methods are available:

```csharp
var policy = ResourcePolicy.IsAuthenticated()
    .And(ResourcePolicy.RequirePermission("booking.reservation.create"))
    .Or(ResourcePolicy.HasPrincipalKind(PrincipalKind.Service));
```

### Operator Precedence

C# operator precedence applies. `&` binds tighter than `|`, so:

```csharp
A & B | C    // means: (A & B) | C
A | B & C    // means: A | (B & C)
```

Use parentheses for clarity.

### Evaluation

AND short-circuits on false (left-to-right). OR short-circuits on true (sync evaluation does not short-circuit at the `ResourcePolicy` level since both `Evaluate` calls are synchronous; async does).

## Defining Custom Policies

Create a class that extends `ResourcePolicy`:

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

This pattern composes built-in policies within the `Evaluate` method, keeping the logic declarative.

## Applying Policies to Actions

Use the `[RequirePolicy<T>]` attribute on actions, mutations, or queries:

```csharp
[RequirePolicy<ReservationManagementPolicy>]
public sealed class CreateReservationMutation : Mutation<Reservation>
{
    // ...
}
```

Requirements:
- The policy type must have a parameterless constructor.
- The instance is created once and cached by `PolicyEvaluationFilter`.
- Evaluated at Order 210 in the action pipeline.
- When the policy evaluates to false, the filter returns a 403 Forbidden.

## AsyncResourcePolicy

For policies that require I/O (external service checks, database lookups), use `AsyncResourcePolicy`:

```csharp
public abstract class AsyncResourcePolicy
{
    public abstract ValueTask<bool> EvaluateAsync(ICurrentUser user, CancellationToken ct = default);
}
```

### Implicit Conversion

Sync policies convert implicitly to async via `SyncToAsyncPolicy`:

```csharp
ResourcePolicy sync = ResourcePolicy.IsAuthenticated();
AsyncResourcePolicy asyncPolicy = sync;  // implicit operator
```

### Composition

Async policies support the same operators (`&`, `|`, `!`):

```csharp
AsyncResourcePolicy asyncPolicy =
    ResourcePolicy.IsAuthenticated()        // sync, auto-converted
    & AsyncResourcePolicy.RequireExternalPermission(
        async (user, ct) => await externalService.CheckAsync(user.Id, ct));
```

### Short-Circuit Evaluation

Async AND and OR short-circuit:

- `AsyncAndPolicy`: If left evaluates to false, skips right entirely.
- `AsyncOrPolicy`: If left evaluates to true, skips right entirely.

This avoids unnecessary I/O calls.

### Factory Methods

`AsyncResourcePolicy` has one factory method:

```csharp
AsyncResourcePolicy.RequireExternalPermission(
    Func<ICurrentUser, CancellationToken, ValueTask<bool>> check)
```

Creates an `AsyncDelegatePolicy`. Not serializable.

## Policy Serialization

`PolicySerializer` converts between `ResourcePolicy` and `PolicyExpression` for JSON-safe storage.

### Serialize

```csharp
var policy = ResourcePolicy.RequirePermission("orders.read")
    & ResourcePolicy.IsAuthenticated();

PolicyExpression expr = PolicySerializer.Serialize(policy);
```

The resulting `PolicyExpression` is a record tree:

```json
{
  "type": "And",
  "children": [
    { "type": "Permission", "value": "orders.read" },
    { "type": "Authenticated" }
  ]
}
```

### Deserialize

```csharp
ResourcePolicy restored = PolicySerializer.Deserialize(expr);
bool result = restored.Evaluate(currentUser);
```

### PolicyExpression Structure

```csharp
public sealed record PolicyExpression
{
    public required PolicyExpressionType Type { get; init; }
    public string? Value { get; init; }          // Permission, Role, Group, PrincipalKind, Claim value
    public string[]? Values { get; init; }       // AnyPermission, AllPermissions
    public string? ClaimType { get; init; }      // Claim
    public PolicyExpression[]? Children { get; init; }  // And, Or, Not
}
```

### PolicyExpressionType

| Type | Value | Values | ClaimType | Children |
|------|-------|--------|-----------|----------|
| `Allow` | - | - | - | - |
| `Deny` | - | - | - | - |
| `Permission` | permission name | - | - | - |
| `AnyPermission` | - | permission names | - | - |
| `AllPermissions` | - | permission names | - | - |
| `Role` | role name | - | - | - |
| `Group` | group name | - | - | - |
| `Claim` | claim value (optional) | - | claim type | - |
| `Authenticated` | - | - | - | - |
| `PrincipalKind` | kind name | - | - | - |
| `And` | - | - | - | 2+ children |
| `Or` | - | - | - | 2+ children |
| `Not` | - | - | - | 1 child |

### Limitations

- `CustomPolicy` (delegate-based) throws `NotSupportedException` on serialize.
- `AsyncDelegatePolicy` is not serializable.
- `And` and `Or` require at least 2 children.
- `Not` requires exactly 1 child.

## Internal Policy Classes

All concrete policy implementations are `internal sealed`. They are created through factory methods and composition operators.

| Class | Created by | Behavior |
|-------|-----------|----------|
| `AllowPolicy` | `ResourcePolicy.Allow` | Returns true (singleton) |
| `DenyPolicy` | `ResourcePolicy.Deny` | Returns false (singleton) |
| `PermissionPolicy` | `RequirePermission(string)` | `user.Authorization.HasPermission(p)` |
| `AnyPermissionPolicy` | `RequireAnyPermission(string[])` | `user.Authorization.HasAnyPermission(ps)` |
| `AllPermissionsPolicy` | `RequireAllPermissions(string[])` | `user.Authorization.HasAllPermissions(ps)` |
| `RolePolicy` | `InRole(string)` | `user.Authorization.IsInRole(r)` |
| `GroupPolicy` | `InGroup(string)` | `user.Authorization.IsInGroup(g)` |
| `ClaimPolicy` | `HasClaim(string, string?)` | Checks `user.Claims` |
| `AuthenticatedPolicy` | `IsAuthenticated()` | `user.IsAuthenticated` (singleton) |
| `PrincipalKindPolicy` | `HasPrincipalKind(PrincipalKind)` | `user.Kind == kind` |
| `CustomPolicy` | `Custom(Func<...>)` | Delegate (not serializable) |
| `AndPolicy` | `&` operator | `left.Evaluate(user) && right.Evaluate(user)` |
| `OrPolicy` | `|` operator | `left.Evaluate(user) \|\| right.Evaluate(user)` |
| `NotPolicy` | `!` operator | `!inner.Evaluate(user)` |
| `SyncToAsyncPolicy` | Implicit conversion | Wraps sync in `ValueTask.FromResult` |
| `AsyncAndPolicy` | Async `&` | Short-circuit AND |
| `AsyncOrPolicy` | Async `|` | Short-circuit OR |
| `AsyncNotPolicy` | Async `!` | Negation |
| `AsyncDelegatePolicy` | `RequireExternalPermission` | External async check |

## Best Practices

1. **Keep policies declarative**: Compose built-in factory methods inside `Evaluate` rather than writing imperative logic.
2. **Use `[RequirePermission]` for simple cases**: Policies are for complex rules. If a single permission check suffices, use the attribute.
3. **Prefer serializable policies**: Avoid `Custom()` and `RequireExternalPermission()` when the policy needs to be stored in a database.
4. **Group related checks**: Create named policy classes (e.g., `ReservationManagementPolicy`) rather than inline compositions.
5. **Test policies independently**: Policies are pure functions on `ICurrentUser` -- they are easy to unit test.
