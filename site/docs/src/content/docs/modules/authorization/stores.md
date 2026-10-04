---
title: "Stores"
description: "Pragmatic.Authorization uses pluggable stores for role-permission and group-role mappings. This document covers the in-memory implementations, the store interfa"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Authorization/docs/stores.md
sidebar:
  order: 6
---
Pragmatic.Authorization uses pluggable stores for role-permission and group-role mappings. This document covers the in-memory implementations, the store interfaces, and the forward-compatible interfaces for temporal and multi-tenant scenarios.

## Store Interfaces

### IRolePermissionStore

Resolves permissions assigned to a role:

```csharp
public interface IRolePermissionStore
{
    ValueTask<IReadOnlySet<string>> GetPermissionsForRoleAsync(
        string roleName, CancellationToken ct = default);

    ValueTask<IReadOnlyList<string>> GetAllRolesAsync(CancellationToken ct = default);
}
```

Used by `RoleExpansionProvider` (Order 100) and `GroupExpansionProvider` (Order 200, indirectly).

### IGroupRoleStore

Resolves roles assigned to a group:

```csharp
public interface IGroupRoleStore
{
    ValueTask<IReadOnlySet<string>> GetRolesForGroupAsync(
        string groupName, CancellationToken ct = default);

    ValueTask<IReadOnlyList<string>> GetAllGroupsAsync(CancellationToken ct = default);
}
```

Used by `GroupExpansionProvider` (Order 200).

## In-Memory Stores

### InMemoryRolePermissionStore

Default store for development, testing, and configuration-driven setups. Populated by `AuthorizationBuilder.MapRole` and `SeedFromJson()`.

**Thread safety**: Uses `ConcurrentDictionary<string, HashSet<string>>` for the role map. Per-set locking on `AddRole` and `GetPermissionsForRoleAsync` (snapshot under lock to avoid concurrent modification during enumeration).

**Lifetime**: Registered as `Singleton` when populated via `MapRole`. The in-memory store is created during `UseAuthorization` configuration and lives for the application lifetime.

```csharp
public sealed class InMemoryRolePermissionStore : IRolePermissionStore
{
    public void AddRole(string roleName, IEnumerable<string> permissions);
    public ValueTask<IReadOnlySet<string>> GetPermissionsForRoleAsync(string roleName, CancellationToken ct = default);
    public ValueTask<IReadOnlyList<string>> GetAllRolesAsync(CancellationToken ct = default);
}
```

Case-insensitive role name lookup (uses `StringComparer.OrdinalIgnoreCase`).

### InMemoryGroupRoleStore

Default store for group-role mappings. Populated by `AuthorizationBuilder.MapGroup`.

```csharp
public sealed class InMemoryGroupRoleStore : IGroupRoleStore
{
    public void AddGroup(string groupName, IEnumerable<string> roles);
    public ValueTask<IReadOnlySet<string>> GetRolesForGroupAsync(string groupName, CancellationToken ct = default);
    public ValueTask<IReadOnlyList<string>> GetAllGroupsAsync(CancellationToken ct = default);
}
```

Case-insensitive group name lookup.

## Custom Stores

Override the in-memory stores with database-backed implementations:

```csharp
authz.UseRolePermissionStore<EfRolePermissionStore>();
authz.UseGroupRoleStore<EfGroupRoleStore>();
```

When a custom store is registered:
- The in-memory store (if any roles were mapped) is **not** registered.
- The `RoleExpansionProvider` or `GroupExpansionProvider` is still registered to use the custom store.

Custom stores are registered as **Scoped** (one per request) via `Services.AddScoped<IRolePermissionStore, T>()`.

### Implementing a Custom Store

```csharp
public sealed class EfRolePermissionStore(MyDbContext db) : IRolePermissionStore
{
    public async ValueTask<IReadOnlySet<string>> GetPermissionsForRoleAsync(
        string roleName, CancellationToken ct = default)
    {
        var permissions = await db.RolePermissions
            .Where(rp => rp.RoleName == roleName)
            .Select(rp => rp.PermissionName)
            .ToListAsync(ct);

        return new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
    }

    public async ValueTask<IReadOnlyList<string>> GetAllRolesAsync(CancellationToken ct = default)
    {
        return await db.RolePermissions
            .Select(rp => rp.RoleName)
            .Distinct()
            .ToListAsync(ct);
    }
}
```

Register:

```csharp
authz.UseRolePermissionStore<EfRolePermissionStore>();
```

## Store Selection Logic

`PragmaticBuilderAuthorizationExtensions.AddPragmaticAuthorization` determines which stores and providers to register:

### Role Store

| Condition | Store | Provider |
|-----------|-------|----------|
| `MapRole` called, no custom store | `InMemoryRolePermissionStore` (Singleton) | `RoleExpansionProvider` (Scoped) |
| `UseRolePermissionStore<T>` called | Custom `T` (Scoped) | `RoleExpansionProvider` (Scoped) |
| Neither called | None | None |

### Group Store

| Condition | Store | Provider |
|-----------|-------|----------|
| `MapGroup` called, no custom store | `InMemoryGroupRoleStore` (Singleton) | `GroupExpansionProvider` (Scoped) |
| `UseGroupRoleStore<T>` called | Custom `T` (Scoped) | `GroupExpansionProvider` (Scoped) |
| Neither called | None | None |

### Group Dependencies

`GroupExpansionProvider` depends on **both** `IGroupRoleStore` and `IRolePermissionStore` (group → role →
permission). If groups are mapped (or a custom group store is registered) but no role store exists at all,
`AddPragmaticAuthorization` **throws `InvalidOperationException` at startup** with an actionable message:
without a role store every group lookup would silently resolve to zero permissions, which reads like an
authorization bug. Map at least one role, or register a custom `IRolePermissionStore`, before enabling
groups.

## Forward-Compatible Store Interfaces

These interfaces extend the base stores with additional parameters. They are defined today for forward compatibility but are not consumed by the current runtime. Implement them in your custom store to prepare for future features.

### ITemporalRolePermissionStore

Point-in-time permission resolution for temporal authorization:

```csharp
public interface ITemporalRolePermissionStore : IRolePermissionStore
{
    ValueTask<IReadOnlySet<string>> GetPermissionsForRoleAsync(
        string roleName, DateTimeOffset asOf, CancellationToken ct = default);
}
```

Use case: "What permissions did the admin role have on January 15?" Useful for audit trails and compliance.

### ITenantRolePermissionStore

Tenant-specific permission resolution for multi-tenant authorization:

```csharp
public interface ITenantRolePermissionStore : IRolePermissionStore
{
    ValueTask<IReadOnlySet<string>> GetPermissionsForRoleAsync(
        string roleName, string tenantId, CancellationToken ct = default);
}
```

Use case: "What permissions does the manager role have in Tenant A?" Different tenants may have different permission sets for the same role.

### ITemporalGroupRoleStore

Point-in-time group resolution:

```csharp
public interface ITemporalGroupRoleStore : IGroupRoleStore
{
    ValueTask<IReadOnlySet<string>> GetRolesForGroupAsync(
        string groupName, DateTimeOffset asOf, CancellationToken ct = default);
}
```

### ITenantGroupRoleStore

Tenant-specific group resolution:

```csharp
public interface ITenantGroupRoleStore : IGroupRoleStore
{
    ValueTask<IReadOnlySet<string>> GetRolesForGroupAsync(
        string groupName, string tenantId, CancellationToken ct = default);
}
```

## Dynamic Stores

These interfaces support runtime RBAC management (creating roles and permissions at runtime via admin UIs). They are consumed by the catalog system and the optional `Pragmatic.Authorization.Management` package.

> ⚠️ They feed the **catalog**: what exists, what an admin screen lists. They are not what a request
> is authorized against. What a request is authorized against, besides the identity's roles and
> `IRolePermissionStore`, is the management package's own assignments: its `ManagedRolePermissionProvider`
> reads `UserRoleAssignment` and `DynamicRolePermission` on every resolution, and its assign and revoke
> actions evict the users they change from the permission cache.
>
> The management package registers `EfDynamicRoleStore` and `EfDynamicPermissionStore`, so a role or
> permission created with `CreateRole`/`CreatePermission` is listed by `ListRoles`/`ListPermissions` in its
> tenant (and, when global, in every tenant). They read the tenant the request resolved first and the
> tenant claim second. `IPermissionCatalog` is scoped for the same reason: resolve it from a scope, not
> from the root provider.

### IDynamicPermissionStore

```csharp
public interface IDynamicPermissionStore
{
    ValueTask<IReadOnlyList<PermissionInfo>> GetAllAsync(CancellationToken ct = default);
    ValueTask<bool> ExistsAsync(string permissionName, CancellationToken ct = default);
}
```

Used by `DefaultPermissionCatalog` to merge runtime-created permissions with SG-generated static permissions.

### IDynamicRoleStore

```csharp
public interface IDynamicRoleStore
{
    ValueTask<IReadOnlyList<RoleInfo>> GetAllAsync(CancellationToken ct = default);
    ValueTask<bool> ExistsAsync(string roleName, CancellationToken ct = default);
}
```

Used by `DefaultPermissionCatalog` to merge runtime-created roles.

### IResourcePolicyStore

```csharp
public interface IResourcePolicyStore
{
    ValueTask<IReadOnlyList<ProtectedResource>> GetAllResourcesAsync(CancellationToken ct = default);
    ValueTask<PolicyExpression?> GetPolicyAsync(string resourceIdentifier, CancellationToken ct = default);
}
```

Used by `DefaultResourceCatalog` for dynamic policy-to-resource assignments.

## Testing with In-Memory Stores

For integration tests, use the in-memory stores directly:

```csharp
var store = new InMemoryRolePermissionStore();
store.AddRole("test-role", ["permission.one", "permission.two"]);

services.AddSingleton<IRolePermissionStore>(store);
services.AddScoped<IPermissionProvider, RoleExpansionProvider>();
```

Or use the full `AuthorizationBuilder`:

```csharp
services.AddPragmaticAuthorization(authz =>
{
    authz.MapRole("test-admin", r => r.WithAllPermissions());
    authz.MapRole("test-viewer", r => r.WithPermissions("entity.read"));
});
```

The Showcase integration tests use `CreateClientWithRoles` and `CreateClientWithGroups` helpers that set HTTP headers (`X-User-Roles`, `X-User-Groups`), which the `NoOpAuthenticationHandler` maps to claims. The `RoleExpansionProvider` and `GroupExpansionProvider` then resolve these claims to permissions via the in-memory stores.

## Store Lifecycle Summary

| Store | Default Implementation | Lifetime | Populated by |
|-------|----------------------|----------|-------------|
| `IRolePermissionStore` | `InMemoryRolePermissionStore` | Singleton | `MapRole`, `SeedFromJson` |
| `IGroupRoleStore` | `InMemoryGroupRoleStore` | Singleton | `MapGroup`, `SeedFromJson` |
| `IDynamicPermissionStore` | None (optional) | Scoped | RBAC Management package |
| `IDynamicRoleStore` | None (optional) | Scoped | RBAC Management package |
| `IResourcePolicyStore` | None (optional) | Scoped | RBAC Management package |

Custom stores registered via `UseRolePermissionStore<T>()` or `UseGroupRoleStore<T>()` are Scoped.
