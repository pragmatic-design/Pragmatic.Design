---
title: "Data Ownership & Scoped Visibility"
description: "Three levels of data access control that compose naturally with entity traits."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Persistence/docs/18-data-ownership.md
sidebar:
  order: 19
---
Three levels of data access control that compose naturally with entity traits.

## Overview

| Level | Attribute | What it does | Use case |
|-------|-----------|-------------|----------|
| L1 | `[HasOwner]` | "I see only my records" | Personal data, user-created resources |
| L2 | `[HasAccessScopes]` | "My team sees our records" | Team/department/region visibility |
| L3 | `DataScopeRule<T>` | "Records matching X belong to scope Y" | Rule-based assignment, dynamic groups |

All three levels integrate with `IPermissionBasedFilter<T>`: admin users with the bypass permission see everything.

---

## L1: Creator Ownership

```csharp
[Entity]
[Auditable]
[HasOwner]
public partial class Reservation { ... }
```

### What gets generated

| File | Content |
|------|---------|
| `Reservation.Ownership.g.cs` | `OwnerId` property, `SetOwnerId()` method, `IOwnedEntity` interface |
| `Reservation.OwnershipFilter.g.cs` | Nested `OwnershipFilter` : `IPermissionBasedFilter<Reservation>` |

### How it works

1. **On create**: `MutationInvoker.CreateEntity()` calls `entity.SetOwnerId(_currentUser?.Id ?? "")` automatically
2. **On query**: `OwnershipFilter` applies `e => e.OwnerId == currentUser.Id`
3. **Admin bypass**: users with `booking.reservation.view-all` permission skip the filter
4. **No user context**: background jobs and seed data skip the filter (no `ICurrentUser`)

### Bypass permission format

```
{boundary}.{entity-kebab-case}.view-all
```

Examples: `booking.reservation.view-all`, `catalog.room-type.view-all`

### Manual override

If you declare `OwnerId` yourself, the SG skips property generation but still generates the filter:

```csharp
[Entity]
[HasOwner]
public partial class Document
{
    public string OwnerId { get; private set; } = ""; // Manual: SG skips this
}
```

---

## L2: Scope-Based Access

```csharp
[Entity]
[HasAccessScopes]
public partial class Invoice { ... }
```

### What gets generated

| File | Content |
|------|---------|
| `Invoice.Scoping.g.cs` | `AccessScopes` property, `GrantScope()`/`RevokeScope()` methods, `IScopedEntity` |
| `Invoice.ScopedDataFilter.g.cs` | Nested `ScopedDataFilter` : `IPermissionBasedFilter<Invoice>` |

### How scopes work

`AccessScopes` is a `List<string>` stored as a JSON column. Each entry is a scope identifier:

```
["user:alice", "role:billing-agent", "scope:billing-eu"]
```

At query time, `IUserScopeResolver` expands the current user's identity into scope identifiers. The filter checks for overlap:

```csharp
entity => entity.AccessScopes.Any(s => userScopes.Contains(s))
```

### Default scope expansion

`DefaultUserScopeResolver` produces:
- `user:{userId}`: always present
- `role:{roleName}`: for each assigned role
- `scope:{value}`: for each `data-scope` claim

### Who fills `AccessScopes`

`ScopeInterceptor`, at `SaveChanges`, on every write path, which is the same place `OwnershipInterceptor`
stamps the owner and `AuditingInterceptor` stamps attribution, and for the same reason: an entity
created by an action through a repository has to be attributed too, not only one created by a
mutation.

| | |
|---|---|
| An inserted row with an **empty** `AccessScopes` | gets the caller's own scope, `user:{id}` |
| An inserted row that **already carries scopes** | keeps them: an import attributing rows to the department that owned them is not re-attributed to whoever ran the import |
| An **updated** row | is never re-stamped: the last person to touch a row does not acquire it |
| No current user (a job, a message off a bus, a seed) | nothing is stamped, and the row is the system's |

⚠️ The filter reads this column and nothing else, so a scoped entity whose rows carry no scope is
invisible to everyone but a caller holding the `view-all` bypass. The stamp is what keeps the ordinary
case ("I created it, I can see it") from needing that bypass.

⚠️ The stamped string has to be the one `IUserScopeResolver` produces for the same principal. Both
sides read it from `ScopeIdentifiers` for that reason: a divergence is not an error anywhere, it is a
row nobody can see.

### Granting/revoking access

```csharp
invoice.GrantScope("scope:billing-eu");   // EU team can now see this invoice
invoice.GrantScope("user:bob");           // Bob can now see this invoice
invoice.RevokeScope("scope:billing-eu");  // EU team loses access
```

⚠️ Both are generated **`internal`**. Only the assembly that owns the entity can grant or revoke a
scope, so these calls belong in that module's own code (a mutation, a domain action, a lifecycle hook)
and not in the host or another boundary.

### Authorization integration

Register named scopes and assign them to groups:

```csharp
// In AuthorizationBuilder
authz.MapDataScope("billing-eu", "EUR invoices");
authz.MapGroup("eu-team", g => g
    .WithRoles("billing-agent")
    .WithDataScopes("billing-eu"));
```

---

## L1 + L2 Combined

When an entity has **both** attributes, a single `DataAccessFilter` replaces separate filters:

```csharp
[Entity]
[HasOwner]
[HasAccessScopes]
public partial class Guest { ... }
```

Generated: `Guest.DataAccessFilter.g.cs` with **OR logic**:

```csharp
entity => entity.OwnerId == userId || entity.AccessScopes.Any(s => userScopes.Contains(s))
```

The user sees the record if they **own it** OR if their scopes **overlap**.

---

## L3: Specification Scopes

Define rules that automatically assign entities to scopes based on expressions.

### Define a rule

```csharp
public sealed class EurInvoiceScopeRule : DataScopeRule<Invoice>
{
    public override string ScopeName => "billing-eu";
    public override ScopeStrategy Strategy => ScopeStrategy.Materialized;

    public override Expression<Func<Invoice, bool>> ToExpression()
        => invoice => invoice.Currency == "EUR";
}
```

### Register

```csharp
services.AddDataScopeRule<EurInvoiceScopeRule, Invoice>();
```

### Strategies

| Strategy | When evaluated | Performance | Accuracy |
|----------|---------------|-------------|----------|
| `Materialized` | On create **and** update, at `SaveChanges` | Fast (indexed) | Stale if the rule changes |
| `Computed` | At query time | Slower | Always current |
| `Hybrid` | Both | Fast + verified | Best |

### Materialize

Nothing to call: `ScopeInterceptor` runs the registered rules over the rows being inserted **and
updated**, and the generated query-filter registration wires one typed step per scoped entity. Saving
an invoice whose `Currency` is `"EUR"` leaves `scope:billing-eu` on it.

Updates matter, and are the difference from the creator's stamp beside it. A materialised scope is a
function of the row's data, so an invoice moving from EUR to USD **loses** `scope:billing-eu` and gains
`scope:billing-usd`, which is why the materializer removes as well as adds.

⚠️ **Order: the stamp first, then the rules**, fixed inside `ScopeInterceptor`. The stamp writes the
creator's scope only when the list is empty, so materialising first would fill the list, "empty" would
never be true again, and no row would ever carry its creator's scope. The two do not otherwise
interfere: the stamp writes `user:`, the rules add and remove only their own `scope:{name}`.

⚠️ Register the rule and nothing else: `AddDataScopeRule<TRule, TEntity>()` is the whole wiring.
Registering `IScopeMaterializer` yourself as well is not an addition: it shadows the registration the
generator makes, and then the rules run or not depending on which registration wins.

### Computed filter

For `Computed` and `Hybrid` strategies, `ComputedScopeFilter<T>` evaluates rule expressions at query time, OR-composing only the rules whose scopes the user has access to. Registered automatically by `AddDataScopeRule`.

---

## Priority Reference

| Filter | Priority | Type |
|--------|----------|------|
| SoftDelete | 100 | Singleton (stateless) |
| Temporal | 200 | Singleton (stateless) |
| Ownership | 200 | Scoped (ICurrentUser) |
| Tenant | 200 | Scoped (ITenantContext) |
| DataAccess (combined) | 200 | Scoped (ICurrentUser + IUserScopeResolver) |
| ScopedData | 250 | Scoped (IUserScopeResolver) |
| ComputedScope | 260 | Scoped (IUserScopeResolver + rules) |
| Custom | 300+ | Your choice: `IQueryFilter.Priority` defaults to `0`, which runs *before* all of these |

Four of them share 200, and nothing orders a tie: they are ANDed, so the order between them does not
change the result.

---

## Common Mistakes

### Using `CreatedBy` instead of `OwnerId`

`CreatedBy` (from `[Auditable]`) tracks *who created the record* for audit purposes. `OwnerId` (from `[HasOwner]`) controls *who can see the record*. They are set to the same value at creation but `OwnerId` can be reassigned; `CreatedBy` cannot.

### Forgetting `[Entity]`

`[HasOwner]` and `[HasAccessScopes]` require `[Entity]` on the class. Without it, the SG won't detect the entity and no filter will be generated.

### Using both attributes when only one is needed

If you only need "see my own records", use `[HasOwner]` alone. Adding `[HasAccessScopes]` without actually using scopes adds unnecessary complexity (JSON column, scope resolver injection).
