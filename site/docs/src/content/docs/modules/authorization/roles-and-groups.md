---
title: "Roles and Groups"
description: "Pragmatic.Authorization uses a strongly-typed, compile-safe role and group system built on three interfaces: `IRole`, `IRoleDefinition`, and `IGroup`. This docu"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Authorization/docs/roles-and-groups.md
sidebar:
  order: 5
---
Pragmatic.Authorization uses a strongly-typed, compile-safe role and group system built on three interfaces: `IRole`, `IRoleDefinition`, and `IGroup`. This document explains the composition model and the APIs for building application roles from module permission templates.

## The Three Interfaces

### IRole -- Application Roles

`IRole` defines an application-level role with a name, description, and default permissions. Roles are defined in the **host** project.

```csharp
public interface IRole
{
    static abstract string Name { get; }
    static abstract string? Description { get; }
    static abstract IReadOnlyList<string> DefaultPermissions { get; }
}
```

Declare it with attributes on a `partial` class, and the generator writes the three members:

```csharp
[Role("manager", "Decides the leave requests of the teams they manage")]
[IncludesRole<EmployeeRole>]                        // repeatable: its permissions are included
[Grants(LeavePermissions.LeaveRequest.Decide)]      // repeatable, params: constants or values
public sealed partial class ManagerRole;
```

`DefaultPermissions` is the role's grants plus every included role's — flattened, de-duplicated and ordered —
and the role registry lists the same set. A granted constant is resolved through the permission catalogue,
so the generator's own `LeavePermissions.*` work; one no generator writes is `PRAG1008`. A class that is not
a top-level `partial` is `PRAG1006`, roles that include each other are `PRAG1007`. An included role can be a
`[Role]` class or a hand-written `IRole` of the same assembly, or a `[Role]` class of a referenced one — its
attributes are in the metadata; a hand-written role of another assembly cannot be read (`PRAG1009`).

A list held in **another assembly** has no initializer for the build to follow — a referenced assembly is
metadata, and a `static readonly string[]` has no constant value. The assembly that declares it publishes it:

```csharp
public static class Shared
{
    [PermissionSet]
    public static readonly string[] Granted = ["kb.read", "kb.write"];
}
```

The generator then writes `[assembly: PermissionSetValues("Company.Grants.Shared.Granted", "kb.read", "kb.write")]`
into that assembly, and the compilation whose role reads the list takes the values from there. A list nobody
marked is `PRAG1015` on the role that reads it: the registry says it cannot tell, instead of listing an empty
grant while the runtime grants every entry.

A hand-written `IRole` keeps working, and so does inheriting by spreading another role's list —
`DefaultPermissions => [.. EmployeeRole.DefaultPermissions, "extra"]` — which the role registry lists as the
union. A spread the generator cannot follow (a method call) is `PRAG1013`:

```csharp
public sealed class ShowcaseAdmin : IRole
{
    public static string Name => "admin";
    public static string? Description => "Full system access";
    public static IReadOnlyList<string> DefaultPermissions => ["*"];
}
```

Roles are consumed at runtime when the user has a `role` claim matching the role's `Name`. The `RoleExpansionProvider` queries the `IRolePermissionStore` to resolve the role's permissions.

### IRoleDefinition -- Module Permission Templates

`IRoleDefinition` defines a reusable permission bundle. Definitions are defined in **modules** and compose into application roles at the host.

```csharp
public interface IRoleDefinition
{
    static abstract string Name { get; }
    static abstract string? Description { get; }
    static abstract IReadOnlyList<string> Permissions { get; }
}
```

Example:

```csharp
// In Showcase.Booking module
public sealed class BookingOperator : IRoleDefinition
{
    public static string Name => "booking-operator";
    public static string? Description => "Full CRUD on reservations and guests";
    public static IReadOnlyList<string> Permissions =>
    [
        BookingPermissions.Reservation.All,      // "booking.reservation.*"
        BookingPermissions.Guest.All,            // "booking.guest.*"
        BookingPermissions.GuestPreferences.All  // "booking.guestpreferences.*"
    ];
}

public sealed class BookingReader : IRoleDefinition
{
    public static string Name => "booking-reader";
    public static string? Description => "Read-only access to booking data";
    public static IReadOnlyList<string> Permissions =>
    [
        BookingPermissions.Reservation.Read,
        BookingPermissions.Guest.Read,
        BookingPermissions.RoomAssignment.Read,
        BookingPermissions.StaffAssignment.Read
    ];
}
```

### The Distinction

| Aspect | IRole | IRoleDefinition |
|--------|-------|-----------------|
| **Defined in** | Host | Module |
| **Purpose** | Application role (mapped to user claim) | Permission template (building block) |
| **Used in** | `authz.MapRole<T>()` | `roleBuilder.IncludeDefinition<T>()` |
| **At runtime** | Resolved by `RoleExpansionProvider` via `role` claim | Not resolved directly |
| **Naming** | Application-specific (`"admin"`, `"booking-manager"`) | Module-specific (`"booking-operator"`, `"catalog-reader"`) |

Modules publish permission templates. The host composes application roles from those templates. This separation means modules do not dictate application role names.

### IGroup -- Role Aggregation

`IGroup` aggregates roles into a group. Users with a `group` claim get all the roles (and permissions) of that group.

```csharp
public interface IGroup
{
    static abstract string Name { get; }
    static abstract string? Description { get; }
    static abstract IReadOnlyList<string> DefaultRoles { get; }
}
```

Example:

```csharp
public sealed class CustomerCareGroup : IGroup
{
    public static string Name => "customer-care";
    public static string? Description => "Customer care team";
    public static IReadOnlyList<string> DefaultRoles =>
    [
        BookingManagerRole.Name,    // "booking-manager"
        CatalogViewerRole.Name     // "catalog-viewer"
    ];
}
```

## RoleBuilder API

`RoleBuilder` provides fluent configuration when mapping roles. It supports additive, subtractive, and replacement operations.

### WithPermissions

Adds explicit permissions:

```csharp
authz.MapRole<ReceptionistRole>(r => r
    .WithPermissions(
        BookingPermissions.Reservation.Read,
        BookingPermissions.Reservation.Create,
        BookingPermissions.Reservation.Update));
```

### IncludeDefinition

Includes all permissions from a module `IRoleDefinition`:

```csharp
authz.MapRole<BookingManagerRole>(r => r
    .IncludeDefinition<BookingOperator>()        // booking.reservation.*, booking.guest.*, booking.guestpreferences.*
    .IncludeDefinition<CatalogReader>());        // catalog.amenity.read, catalog.property.read, ...
```

Multiple definitions can be combined in a single role. All permissions are additive.

### WithAllPermissions

Grants the global wildcard `*`:

```csharp
authz.MapRole<AdminRole>(r => r.WithAllPermissions());
```

### WithAllPermissions\<TBoundary>

Grants all permissions for a specific boundary. The boundary slug is derived by stripping the `"Boundary"` suffix and lowercasing:

```csharp
authz.MapRole<BookingAdmin>(r => r.WithAllPermissions<BookingBoundary>());
// Adds: "booking.*"
```

### WithOperation\<TBoundary>(CrudOperation)

Grants a specific CRUD operation across all entities in a boundary:

```csharp
authz.MapRole("auditor", r => r
    .WithOperation<BookingBoundary>(CrudOperation.Read)     // "booking.*.read"
    .WithOperation<BillingBoundary>(CrudOperation.Read)     // "billing.*.read"
    .WithOperation<CatalogBoundary>(CrudOperation.Read));   // "catalog.*.read"
```

`CrudOperation` values: `Read`, `Create`, `Update`, `Delete`.

### WithoutPermissions

Removes specific permissions from the set (including defaults from `IRole.DefaultPermissions`):

```csharp
authz.MapRole<LimitedRole>(r => r
    .IncludeDefinition<BookingOperator>()
    .WithoutPermissions("booking.reservation.delete"));
```

**Wildcard safety**: If you try to exclude a permission that is covered by a wildcard grant, the builder throws `InvalidOperationException` at startup. This prevents silent failures where the wildcard would override the exclusion.

```csharp
// This THROWS at startup:
authz.MapRole<BadRole>(r => r
    .WithPermissions("booking.*")
    .WithoutPermissions("booking.reservation.delete"));
// InvalidOperationException: Cannot exclude 'booking.reservation.delete' -- covered by wildcard 'booking.*'.
// Use explicit permissions instead of wildcards when using WithoutPermissions.
```

Use explicit permissions instead of wildcards when you need exclusions.

### ClearDefaults

Clears all default permissions from `IRole.DefaultPermissions`. Only permissions added after this call apply:

```csharp
authz.MapRole<OverriddenRole>(r => r
    .ClearDefaults()
    .WithPermissions("only.these.permissions"));
```

### Resolution Order

The `RoleBuilder.Resolve(defaults)` method produces the final permission set:

1. Start with `IRole.DefaultPermissions` (unless `ClearDefaults()` was called).
2. Add all permissions from `WithPermissions()` and `IncludeDefinition<T>()`.
3. Remove permissions from `WithoutPermissions()` (with wildcard safety check).

## GroupBuilder API

`GroupBuilder` provides fluent configuration for groups.

### WithRoles

Adds roles by name:

```csharp
authz.MapGroup("operations", g => g.WithRoles("front-desk", "catalog-viewer"));
```

### WithRole\<T>

Adds a strongly-typed role:

```csharp
authz.MapGroup<ExtendedGroup>(g => g.WithRole<ExtraRole>());
```

## An Access Level That Signs In as a Role

A user entity that keeps what the user may do in an enum declares, on each member, the role it signs in as:

```csharp
public enum AccessRole
{
    [SignsInAs<EmployeeRole>] Employee,
    [SignsInAs<ManagerRole>] Manager,
    [SignsInAs<HrAdministratorRole>] HrAdministrator
}

// at sign-in
claims.Roles.Add(employee.Role.RoleNameOf());
```

The generator writes `AccessRoleNames.RoleNameOf(this AccessRole)` — a switch with one arm per member, each
answering the role's own `Name` (a `[Role]` class or a hand-written `IRole`). Once any member of an enum declares
it, every member must: a member without one is `PRAG1014`, an error, and the method is not generated — so a new
access level cannot reach a sign-in with no role, as it could with a hand-written switch and its
`ArgumentOutOfRangeException`.

## Mapping Roles by Name

For roles that do not need a class (e.g., defined in JSON or external systems), use the string overload:

```csharp
authz.MapRole("auditor", r => r
    .WithOperation<BookingBoundary>(CrudOperation.Read)
    .WithOperation<BillingBoundary>(CrudOperation.Read)
    .WithOperation<CatalogBoundary>(CrudOperation.Read));
```

Similarly for groups:

```csharp
authz.MapGroup("operations", g => g.WithRoles("front-desk", "catalog-viewer"));
```

## JSON Seeding

The `roles.pragmatic.json` file provides a declarative way to define additional roles and groups without code. The SG reads the file at compile-time and generates a `SeedFromJson()` extension method.

### File Format

```json
{
  "roles": {
    "role-name": {
      "description": "Human-readable description",
      "permissions": ["permission.one", "permission.two"],
      "inherits": ["other-role-name"]
    }
  },
  "groups": {
    "group-name": {
      "description": "Human-readable description",
      "roles": ["role-one", "role-two"]
    }
  }
}
```

### Inheritance

Roles can inherit from other roles **of the same file** via the `inherits` array. The SG expands inheritance at compile-time, producing flattened permission sets in the generated code. A role the file does not declare — a code-defined `IRole` included — is not inherited, and is reported; so are roles that inherit each other.

### What Is Reported

The generator reads one file and names it in every diagnostic:

| Diagnostic | When | What is seeded |
|---|---|---|
| `PRAG1010` | The file is not valid JSON (the diagnostic points at the line) | Nothing |
| `PRAG1011` | A property the format does not have (`"permission"` for `"permissions"`), a value of the wrong kind (`"permissions": "x"`, a number in a list), an `inherits` naming a role the file does not declare, roles that inherit each other | Everything that could be read |
| `PRAG1012` | A second `roles.pragmatic.json` in the same project | The first file only |

Without these diagnostics each case would be silent — the roles would simply not exist at runtime — or,
for a value of the wrong kind, the whole generator's output would be lost.

### Usage

```csharp
authz.SeedFromJson();
```

This calls the SG-generated `RoleSeedingExtensions.SeedFromJson()`, which calls `MapRole(name, r => r.WithPermissions(...))` for each role and `MapGroup(name, g => g.WithRoles(...))` for each group.

## Custom Permissions -- `[assembly: Permission]`

For operations beyond standard CRUD, declare the permission on the assembly — one line, with what holding it
allows and, optionally, the group a role screen lists it under:

```csharp
[assembly: Permission("billing.invoice.refund", "Refund a paid invoice", Category = "Billing")]
```

The SG adds its constant to the boundary's permissions class (below) — `BillingPermissions.Invoice.Refund`, a
`const` — and its entry to the generated `PermissionRegistry`, so `IPermissionCatalog.GetAllPermissionsAsync()`
lists it with its description and category. A role names the constant like any other:

```csharp
public static IReadOnlyList<string> DefaultPermissions => [BillingPermissions.All, BillingPermissions.Invoice.Refund];
```

The first segment must be one of the assembly's boundaries (`PRAG1004`); a value declared twice, or equal to a
CRUD permission, is `PRAG1001`; one whose constant would take a name the class already uses is `PRAG1005`.
It is the one way: there is no type per permission.

## SG-Generated Permission Constants

The source generator generates one static class per boundary, the only one, with the CRUD constants of each
entity and the declared permissions beside them. The naming follows the convention
`{boundary}.{entity}.{operation}`:

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
        // ...
    }
}
```

Use these constants in `[RequirePermission]` attributes and `IRoleDefinition.Permissions` for compile-time safety.

## Runtime Resolution Flow

```
User arrives with claims:
  { "role": ["booking-manager"], "group": ["customer-care"] }

1. RoleExpansionProvider (Order 100):
   "booking-manager" -> IRolePermissionStore -> permissions from MapRole configuration

2. GroupExpansionProvider (Order 200):
   "customer-care" -> IGroupRoleStore -> ["booking-manager", "catalog-viewer"]
   For each role -> IRolePermissionStore -> permissions

3. CachedPermissionResolver merges all provider results (union)

4. HasPermission("catalog.property.read") -> check resolved set
```

## Showcase Example

The Showcase application demonstrates the full composition model:

**Modules define templates:**
- `BookingOperator` (booking module) -- full CRUD on reservations and guests
- `BookingReader` (booking module) -- read-only booking access
- `CatalogReader` (catalog module) -- read-only catalog access
- `CatalogEditor` (catalog module) -- full catalog CRUD

**Host defines roles:**
- `ShowcaseAdmin` -- `["*"]`
- `BookingManagerRole` -- `BookingOperator + CatalogReader`
- `CatalogViewerRole` -- `CatalogReader`
- `CatalogEditorRole` -- `CatalogEditor`
- `ReceptionistRole` -- explicit booking permissions (read, create, update) + `CatalogReader`
- `BillingClerkRole` -- `BillingPermissions.All + BookingReader`
- `FinanceManagerRole` -- `BillingPermissions.All + BillingPermissions.Invoice.Refund + BookingReader`
- `"auditor"` (string) -- `booking.*.read + billing.*.read + catalog.*.read`

**Host defines groups:**
- `CustomerCareGroup` -- `BookingManagerRole + CatalogViewerRole`

**JSON-seeded roles:**
- `front-desk` -- `booking.reservation.read`, `booking.reservation.create`, `booking.guest.read`
- `revenue-manager` -- `catalog.*`, `booking.reservation.read`

**JSON-seeded groups:**
- `operations` -- `front-desk + catalog-viewer`
