---
title: "Identity Persistence"
description: "Database-backed authorization with EF Core: temporal role/permission stores and user-group-role entities."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Identity/docs/persistence.md
sidebar:
  order: 6
---
Database-backed authorization with EF Core: temporal role/permission stores and user-group-role entities.

> ## The user entity
>
> There is no user base class or identity `DbContext` to inherit. The host module declares
> `[UsePackage<LocalIdentityPackage>]`, and the user is a plain entity, with the generator owning the
> identity members and the EF configuration:
>
> ```csharp
> [PragmaticUser]
> public partial class AppUser
> {
>     public Guid Id { get; set; }
>     public string? DisplayName { get; set; }
> }
> ```
>
> The EF stores (`EfRolePermissionStore`, `EfGroupRoleStore`) and the temporal entities documented
> below do not need to know the user type. **There is no JIT provisioning**: creating a local
> record on first external login is not in `LocalIdentityPackage`. If it is added, it will be
> package-shaped, never an inheritance base.

## Overview

`Pragmatic.Identity.Persistence` replaces the in-memory authorization stores from `Pragmatic.Authorization` with EF Core implementations that support:

- **Temporal validity** -- permissions and role assignments have `ValidFrom`/`ValidTo` timestamps
- **Runtime management** -- role/permission mappings can be changed without redeployment
- **Audit trail** -- revocation sets `ValidTo` instead of deleting, preserving history

## Entity Model

All join entities implement `ITemporalRelation` with `ValidFrom`/`ValidTo` for time-bounded validity.

```
                          +-----------------+
                          |  ExternalIdent  |
                          |  Record<TKey>   |
                          |  Provider       |
                          |  Issuer|Subject |
                          +--------+--------+
                                   |
                                   | UserId (FK)
                                   v
+------------------+    +-----------------+    +------------------+
|  UserRole<TKey>  |<-->| IdentityUser    |<-->| UserGroup<TKey>  |
|  RoleName        |    | Base<TKey>      |    | GroupName        |
|  ValidFrom/To    |    | DisplayName     |    | ValidFrom/To     |
|  AssignedBy      |    | Email           |    | AssignedBy       |
+--------+---------+    | ExternalIdKey   |    +--------+---------+
         |              | ProvisionSource |             |
         |              +-----------------+             |
         v                                              v
+------------------+                           +------------------+
| RolePermission   |                           |    GroupRole     |
| RoleName         |                           |  GroupName       |
| PermissionName   |                           |  RoleName        |
| ValidFrom/To     |                           |  ValidFrom/To    |
+------------------+                           +------------------+
```

### Entity Details

#### RolePermission

Maps a role to a permission with temporal validity.

| Column | Type | Indexed |
|--------|------|:---:|
| `Id` | `Guid` (PK) | Yes |
| `RoleName` | `string(256)` | Yes (composite) |
| `PermissionName` | `string(512)` | Yes (composite) |
| `ValidFrom` | `DateTimeOffset` | Yes (composite) |
| `ValidTo` | `DateTimeOffset?` | Yes (composite) |

Table: `RolePermissions`
Indices:
- `IX_RolePermissions_Role_Temporal` on (`RoleName`, `ValidFrom`, `ValidTo`)
- `IX_RolePermissions_Role_Permission_ValidFrom` unique on (`RoleName`, `PermissionName`, `ValidFrom`)

#### GroupRole

Maps a group to a role with temporal validity.

| Column | Type |
|--------|------|
| `Id` | `Guid` (PK) |
| `GroupName` | `string` |
| `RoleName` | `string` |
| `ValidFrom` | `DateTimeOffset` |
| `ValidTo` | `DateTimeOffset?` |

Table: `GroupRoles`

#### UserRole\<TKey\>

Assigns a role to a user with temporal validity.

| Column | Type |
|--------|------|
| `Id` | `Guid` (PK) |
| `UserId` | `TKey` (FK) |
| `RoleName` | `string` |
| `AssignedBy` | `string?` |
| `ValidFrom` | `DateTimeOffset` |
| `ValidTo` | `DateTimeOffset?` |

#### UserGroup\<TKey\>

Assigns a user to a group with temporal validity.

| Column | Type |
|--------|------|
| `Id` | `Guid` (PK) |
| `UserId` | `TKey` (FK) |
| `GroupName` | `string` |
| `AssignedBy` | `string?` |
| `ValidFrom` | `DateTimeOffset` |
| `ValidTo` | `DateTimeOffset?` |

#### ExternalIdentityRecord\<TKey\>

Links a local user to an external identity provider.

| Column | Type |
|--------|------|
| `Id` | `Guid` (PK) |
| `UserId` | `TKey` (FK) |
| `Provider` | `string` (e.g., "Auth0", "EntraID", "Keycloak") |
| `Issuer` | `string` (issuer URI) |
| `Subject` | `string` (provider-unique user ID) |
| `ExternalIdentityKey` | `string` (computed: `{Issuer}\|{Subject}`) |
| `LastUsedAt` | `DateTimeOffset?` |
| + `IAuditable` fields | |

## EF Stores

### EfRolePermissionStore

Implements `ITemporalRolePermissionStore` (extends `IRolePermissionStore`).

Queries the `RolePermission` table and filters by temporal validity using `IClock.UtcNow`:

```csharp
// Only returns permissions where: ValidFrom <= now AND (ValidTo is null OR ValidTo > now)
var permissions = await store.GetPermissionsForRoleAsync("booking-manager");

// Point-in-time query (e.g., for audit)
var pastPermissions = await store.GetPermissionsForRoleAsync("booking-manager", asOf: someDate);
```

### EfGroupRoleStore

Implements `ITemporalGroupRoleStore` (extends `IGroupRoleStore`).

Same temporal filtering for the group-to-role mapping:

```csharp
var roles = await store.GetRolesForGroupAsync("customer-care");
```

### How They Integrate

When registered, these stores replace the in-memory stores from `Pragmatic.Authorization`. The permission resolution chain becomes:

```
User claims (roles, groups)
    |
    v
CachedPermissionResolver
    |
    +-- ClaimsPermissionProvider      (reads "permission" claims directly)
    +-- RoleExpansionProvider          (calls EfRolePermissionStore for each role)
    +-- GroupExpansionProvider          (calls EfGroupRoleStore for each group, then EfRolePermissionStore)
    |
    v
Merged permission set (cached per request or cross-request via ICacheStack)
```

## The User Entity and its Resolver

`[PragmaticUser]` marks the application's user entity. With `Pragmatic.Identity.Persistence`
referenced, the generator writes a resolver beside it, `{User}Resolver`, which loads the entity of the
signed-in caller:

```csharp
[Entity]
[PragmaticUser(MatchClaim = "sub")]
public partial class Employee : IEntity { /* … */ }

// Generated: TimeOff.Leave.Entities.EmployeeResolver
public sealed class EmployeeResolver(IReadRepository<Employee> repository, ICurrentUser currentUser)
{
    public Task<Employee?> ResolveAsync(CancellationToken ct = default);  // null: not signed in, or no row
    public Employee? Resolve();                                          // for synchronous contracts
    public Task<Employee?> FindByIdentityKeyAsync(string identityKey, CancellationToken ct = default);
    public Task<IUserProfile?> GetProfileAsync(CancellationToken ct = default);
}
```

- **The match.** `MatchProperty` names the entity member compared with the caller: by default
  `ExternalIdentityKey`, on the entity or on its `IdentityRecord` navigation. With `MatchClaim = "sub"`
  and the default key, the caller's side is `ICurrentUser.Authentication.ExternalIdentityKey`; any other
  claim is compared as it is.
- **It reads through `IReadRepository<TUser>`**, not a `DbContext`: the resolver lives in the module, and
  the boundary's context is generated in the host.
- **By a given key.** `FindByIdentityKeyAsync(key)` is the same match for a key the caller holds rather
  than the current user's: what an `IUserClaimsContributor` does for the account being signed in, when
  nobody is authenticated yet.
- **Registration.** It is registered like any `[Service]` of the module (scoped, published to the host
  through the module's DI metadata) when the entity is public (the host names it from its own
  assembly). So an action, a mutation or a service takes it as a dependency, a field included:

  ```csharp
  private EmployeeResolver _currentEmployee = null!;
  // …
  var employee = await _currentEmployee.ResolveAsync(ct).ConfigureAwait(false);
  ```

  That is the lookup; do not write a second one against `ExternalIdentityKey`. `UseUserCulture()` also
  registers it, with `TryAdd`, for a container built without the module's registration.

**A query reads it with `[FromCurrentUser(member)]`.** `[FromCurrentUser(nameof(Employee.Id))]` on a
`[Query]` property makes the query's generated invoker construct the resolver (the generator knows
its constructor, so it does not ask DI for it), resolve the caller's entity after validation and the
permission check, and write that member into the property before the read. No entity is 404, no
caller is 401. The member form needs the `[PragmaticUser]` entity in the same compilation as the query,
and this package referenced; otherwise `PRAG0731` says why. The whole form:
[Filtering by the caller](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Persistence/docs/09-query-system.md#filtering-by-the-caller-fromcurrentuser).

## Setup

Put `[UsePackage<LocalIdentityPackage>]` on the host module: the generator registers the EF stores
and composes the identity entities into the boundary DbContext.

For direct DI registration (a test host, or an application not using the package):

```csharp
services.AddPragmaticIdentityPersistence();
```

### Skipping Default Stores

With your own `IRolePermissionStore` or `IGroupRoleStore`, skip the EF defaults:

```csharp
services.AddPragmaticIdentityPersistence(persistence =>
{
    persistence.SkipRolePermissionStore();  // You provide your own
    persistence.SkipGroupRoleStore();       // You provide your own
});
```

## Database Configuration

### Using ApplyIdentityConfigurations

Apply identity configurations to an existing DbContext:

```csharp
public class AppDbContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyIdentityConfigurations<AppUser, Guid>();
    }
}
```

### Recommended: UsePackage Composition

The preferred approach is to use the `[UsePackage<LocalIdentityPackage>]` attribute on your host module. The source generator integrates identity entities into your boundary DbContext via OwnsOne composition.

## Temporal Authorization

### Concept

All authorization entities use `ValidFrom`/`ValidTo` for time-bounded validity. This enables:

- **Scheduled permissions**: Grant access starting from a future date
- **Expiring assignments**: Auto-revoke after a deadline
- **Audit trail**: Revocation sets `ValidTo` instead of deleting rows
- **Point-in-time queries**: Check what permissions were active at any moment

### Example: Temporary Role Assignment

```csharp
// Grant "booking-manager" role for 30 days
var userRole = new UserRole<Guid>
{
    UserId = userId,
    RoleName = "booking-manager",
    AssignedBy = currentUser.Id,
    ValidFrom = DateTimeOffset.UtcNow,
    ValidTo = DateTimeOffset.UtcNow.AddDays(30)
};
dbContext.Set<UserRole<Guid>>().Add(userRole);
await dbContext.SaveChangesAsync();
```

### Example: Revoking a Permission

```csharp
// Revoke by setting ValidTo to now (preserves audit trail)
var rolePermission = await dbContext.Set<RolePermission>()
    .FirstAsync(rp => rp.RoleName == "editor" && rp.PermissionName == "publish.article");

rolePermission.ValidTo = DateTimeOffset.UtcNow;
await dbContext.SaveChangesAsync();
```

### Temporal Queries via IClock

The EF stores use `IClock.UtcNow` for temporal filtering, which means:

- In production, permissions are filtered by the real current time
- In tests, you can inject a `FakeClock` to control the time and test temporal behavior
- Point-in-time queries use the explicit `asOf` parameter overload

## Caching

### Request-Level Cache

`CachedPermissionResolver` always caches resolved permissions for the lifetime of the request scope. No configuration needed.

### Cross-Request Cache

For applications with many users and frequent permission checks, enable cross-request caching:

```csharp
app.UseAuthorization(authz =>
{
    authz.UsePermissionCache(TimeSpan.FromMinutes(5));
});
```

This uses `ICacheStack` (via `CacheCategories.Permissions` category) with:
- **Cache key**: `permissions:t:{tenantId}:u:{userId}` (multi-tenant) or `permissions:g:u:{userId}` (global)
- **Tags**: `user:{userId}`, `tenant:{tenantId}`, `role:{roleName}` (per contributing role), `group:{groupName}` (per contributing group)
- **Expiration**: configurable via the `TimeSpan` parameter

### Cache Invalidation

When temporal role/permission mappings change (e.g., a `ValidTo` is set to revoke early), invalidate the
affected cache entries through `IPermissionCacheInvalidator`: it targets the tags above so you never
enumerate users by hand:

```csharp
public sealed class RevokeRoleHandler(IPermissionCacheInvalidator invalidator)
{
    public async Task RevokeAsync(string userId, CancellationToken ct)
    {
        // ... set ValidTo on the UserRole row ...
        await invalidator.InvalidateUserAsync(userId, ct);   // this user only
    }
}
```

`InvalidateRoleAsync(role)` / `InvalidateGroupAsync(group)` reach every affected user after a role's
permission set or a group's role set changes; `InvalidateTenantAsync(tenantId)` clears a whole tenant.
See [Authorization → Permission Resolution](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Authorization/docs/permission-resolution.md#cache-invalidation).
