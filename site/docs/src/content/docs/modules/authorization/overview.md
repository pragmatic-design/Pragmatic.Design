---
title: "Pragmatic.Authorization"
description: "The authorization engine for Pragmatic.Design: permissions, roles, groups, wildcard matching,"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Authorization/README.md
sidebar:
  order: 0
  label: Overview
---
The authorization engine for Pragmatic.Design: permissions, roles, groups, wildcard matching,
resource (instance-level) policies, and a permission cache, declarative and observable.

## The Problem

Authorization in most .NET apps starts simple and rots. Permission strings are hardcoded and scattered
across controllers; role-to-permission mapping lives in `if`-statements; wildcard handling is
hand-rolled and inconsistent; instance-level checks ("can this user act on *this* invoice?") are buried
in business logic. Ask "what can the booking-manager role do?" and the answer means reading every
controller.

```csharp
// Typical: scattered, fragile, invisible
var permissions = User.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToHashSet();
if (!permissions.Contains("booking.reservation.cancel")
    && !permissions.Any(p => p == "booking.reservation.*")
    && !permissions.Contains("*"))
    return Forbid();
```

## The Solution

Declare **what** is protected and **how**; the framework handles resolution, wildcard matching,
caching, and enforcement.

```csharp
[DomainAction]
[RequirePermission(BookingPermissions.Reservation.Cancel)]
[RequirePolicy<ReservationCancellationPolicy>]
public sealed partial class CancelReservationAction : VoidDomainAction
{
    // business logic only: no authorization code here
}
```

```csharp
// Roles compose from module definitions, declared once in Program.cs
app.UseAuthorization(authz =>
{
    authz.MapRole<BookingManagerRole>(r => r
        .IncludeDefinition<BookingOperator>()
        .IncludeDefinition<CatalogReader>());
    authz.AddResourceAuthorizer<InvoiceAuthorizer>();   // instance-level (ABAC)
    authz.UsePermissionCache(TimeSpan.FromMinutes(5));
});
```

## What it covers

- **Permissions**: wildcard-aware matching (`booking.*`, `*`), one generated `{Boundary}Permissions` class
  of `const`s: the entities' CRUD and the custom ones declared with `[assembly: Permission]`.
- **Roles & groups**: compose roles from reusable definitions; map groups to roles.
- **Resource policies (`ResourcePolicy`)**: instance-level "can this user act on this object?" checks.
- **Stores & caching**: pluggable role/permission/group stores; cross-request `HybridCache`.
- **Pipeline enforcement**: `[RequirePermission]` / `[RequirePolicy]` enforced in the Actions/Endpoints pipeline.

## Installation

```bash
dotnet add package Pragmatic.Authorization
dotnet add package Pragmatic.SourceGenerator   # generates permission constants/registry
```

## Status

**Functional** within 1.0.0-alpha: roles, groups, wildcard permissions, resource authorizers, and the
permission cache. See the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

| [Concepts](/modules/authorization/concepts/) | Mental model, permission strings, the resolution chain, where authorization runs |
| [Getting Started](/modules/authorization/getting-started/) | Protect an action, define permissions, map a role |
| [Permission Resolution](/modules/authorization/permission-resolution/) | The full resolution chain, wildcard matching, caching |
| [Roles & Groups](/modules/authorization/roles-and-groups/) | `IRole`/`IRoleDefinition`, composition, groups |
| [Policies](/modules/authorization/policies/) | `ResourcePolicy`, `[RequirePolicy<T>]`, instance-level (ABAC) checks |
| [Stores](/modules/authorization/stores/) | Role/permission/group stores, customization, temporal/tenant variants |
| [Common Mistakes](/modules/authorization/common-mistakes/) | The most frequent authorization pitfalls |
| [Troubleshooting](/modules/authorization/troubleshooting/) | Problem/solution guide with diagnostics |

## Cross-module integration

Enforced by [Actions](/modules/actions/overview/) and [Endpoints](/modules/endpoints/overview/);
identity comes from [Identity](/modules/identity/overview/); data-level filters live in
[Persistence](/modules/persistence/overview/). Interfaces live in `Pragmatic.Abstractions`.

## Requirements

- .NET 10.0+
- `Pragmatic.SourceGenerator` analyzer

## License

Part of the [Pragmatic.Design](/modules/authorization/overview/) ecosystem. See [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.Authorization is licensed under the **PolyForm Small Business 1.0.0** license (free for small
businesses; commercial license above the threshold).
