# Pragmatic.Authorization.Management

Optional RBAC management package for Pragmatic.Design.

Use this package when static roles and permissions are not enough and you need runtime-managed RBAC:
- create permissions and roles dynamically
- assign roles to users
- manage tenant-aware assignments
- expose admin endpoints for authorization management

## Start Here

Read in this order:

1. [`../../../Pragmatic.Authorization/README.md`](../../../Pragmatic.Authorization/README.md)
2. [`../../../docs/howto/authentication-authorization.md`](../../../docs/howto/authentication-authorization.md)

## When To Use It

Use `Pragmatic.Authorization.Management` when:
- roles must be changed without redeploying
- tenants need different RBAC data
- you want admin UIs or admin APIs for authorization

Do not use it when:
- static role composition in code or JSON is enough
- you are still bootstrapping a project and do not need runtime RBAC yet

## Current Surface In Code

Today the public action surface covers:
- create and list permissions
- create and list roles
- assign permissions to roles
- assign and revoke roles for users
- inspect effective permissions through the management action

Important current limits:
- the package contains a `UserGroupAssignment` entity, but public group management actions are not currently present here
- the `GetEffectivePermissions` management action is the code-backed admin entry point; it is not the same thing as the default `IPermissionCatalog.GetEffectivePermissionsAsync(...)`

## Quick Start

Import the package into the boundary whose database holds its tables:

```csharp
[Module]
[UsePackage<AuthorizationManagementPackage, MyBoundary>]
public sealed class MyModule;
```

Reference the package of that boundary's engine beside this one:

| Engine | Package |
|--------|---------|
| EF Core | `Pragmatic.Authorization.Management.EFCore` |
| Sql, documents | `Pragmatic.Authorization.Management.Engines` |

The boundary's generated registration adds the store of its engine. Without that package the import is
`PRAG0469`, which names it.

Optionally expose only the endpoints you want:

```csharp
[ExposeEndpoint<CreateRole>]
[ExposeEndpoint<AssignRoleToUser>]
[ExposeEndpoint<ListPermissions>]
```

Recommended host policy:

- expose only the actions you need
- secure them with management permissions such as `authorization.view` and `authorization.roles.manage`

## How To

How do I list all known permissions?

- Use `ListPermissions`.
- This merges static permission registry data with dynamic permission store data.

How do I create a runtime role?

- Use `CreateRole`.
- Then use `AssignPermissionsToRole`.

How do I assign a role to a user?

- Use `AssignRoleToUser`.
- Use `RevokeRoleFromUser` to remove it later.

How do I inspect effective permissions for a user today?

- Use the management action `GetEffectivePermissions`.
- Prefer this workflow over calling low-level catalog APIs directly from application code.
- Today this action resolves persisted role assignments through the role store; treat it as an admin-side lookup, not as a full replacement for the request-time resolver.

## Main Actions

| Action | Permission Required |
|--------|---------------------|
| `CreatePermission` | `authorization.permissions.manage` |
| `ListPermissions` | `authorization.view` |
| `CreateRole` | `authorization.roles.manage` |
| `AssignPermissionsToRole` | `authorization.roles.manage` |
| `ListRoles` | `authorization.view` |
| `AssignRoleToUser` | `authorization.assignments.manage` |
| `RevokeRoleFromUser` | `authorization.assignments.manage` |
| `GetEffectivePermissions` | `authorization.view` |

A caller with a tenant gives only what they hold: `AssignPermissionsToRole` refuses a permission the caller does
not hold, and `AssignRoleToUser` refuses a role carrying one (every grant of the role not yet expired). Both answer
`403`. A caller with no tenant is the platform, and is not bound by what they hold.

`CreatePermission` refuses (`409`) a name the permission catalog already holds, compared ignoring case: a
generated permission, or a dynamic one of the caller's tenant or global. A dynamic permission is a new name.

## Persisted Model

| Entity | Purpose | Notes |
|--------|---------|-------|
| `DynamicPermission` | Runtime-created permissions | Audited and soft-deletable |
| `DynamicRole` | Runtime-created roles | Audited and soft-deletable |
| `DynamicRolePermission` | Role to permission mapping | Temporal |
| `UserRoleAssignment` | User to role assignment | Temporal |
| `UserGroupAssignment` | User to group assignment | Temporal |

All entities are tenant-aware through `TenantId`. A row with no tenant holds in every tenant, so only a
caller with no tenant may write one.

The indexes are declared on the entities, so every engine's tables have them:
- a unique index on `(Name, TenantId)` for roles and for permissions;
- one on `(RoleName, PermissionName, TenantId)` for grants;
- one on `(UserId, RoleName, TenantId)` for role assignments;
- one on `(UserId, GroupName, TenantId)` for group assignments.

A second role or permission of the same name in a tenant is refused by the database, not only by the
operation's check before the insert. ⚠️ A global row's `TenantId` is NULL, and PostgreSQL, SQLite and
MySQL hold NULLs distinct in a unique index: there, two global roles of one name are refused by the check
alone. The grants and the assignments are not unique, because an ended one is kept.

## Enforcement

`ManagedRolePermissionProvider` is an `IPermissionProvider` the package registers: on every permission
resolution it reads the caller's active `UserRoleAssignment`s (in the request's tenant or global) and
the active `DynamicRolePermission`s of those roles. An assigned role grants on the next request; a revoked
one stops granting, because the assign, revoke and assign-permissions actions evict the affected users
from the permission cache when one is configured.

`ManagedRoleProvider` is the package's `IRoleProvider`: it puts the names of the same assignments among the
caller's roles, beside the identity's own `role` claims, so `ICurrentUser.Authorization.Roles` and
`IsInRole` agree with what the roles grant.

A caller bound to a tenant can put into a role only permissions they hold themselves (a wildcard they
hold covers what it names), and is refused with 403 otherwise, so the role and assignment permissions
together do not let a tenant admin grant themselves more than they have. A caller with no tenant, the
platform operator, is not bound by this.

## Stores

| Store | Implements |
|-------|------------|
| `ManagedDynamicPermissionStore` | `IDynamicPermissionStore` |
| `ManagedDynamicRoleStore` | `IDynamicRoleStore` |

The package registers both, and `DefaultPermissionCatalog` merges them with the compiled permissions and
roles: `ListRoles` and `ListPermissions` show what was created at runtime, in the caller's tenant and
globally.

## Engines

The package references no engine. Its actions, its permission provider and the stores above read and
write through `IAuthorizationManagementStore`, which the importing boundary's registration answers with
the store of its engine:

| Engine | Store | Where the rows are |
|--------|-------|--------------------|
| EF Core | `EfAuthorizationManagementStore` | the entities' tables, mapped by the importing boundary's context |
| Sql | `SqlAuthorizationManagementStore` | the same tables, column for column, in the schema of the boundary's database |
| Documents | `DocAuthorizationManagementStore` | one collection per entity, in the boundary's `{Boundary}DocSchema` |

Which assignment is in force at an instant, and the tenant rule (a tenant's rows and the global ones), are
decided by the package, the same on every engine.

## Recommended Learning Path

Newcomer:

1. Start with static roles in `Pragmatic.Authorization`.
2. Add JSON seeding if you need a simple shared baseline.
3. Move to `Authorization.Management` only when you need runtime edits.

Contributor:

1. Trace the stores and management actions.
2. Compare the static registry flow with the dynamic store flow before changing contracts.

## Go Deeper

- [`../../../docs/howto/authentication-authorization.md`](../../../docs/howto/authentication-authorization.md)
