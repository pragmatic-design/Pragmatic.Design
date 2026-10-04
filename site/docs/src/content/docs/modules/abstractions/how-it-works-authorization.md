---
title: "Authorization: what the caller is allowed to do"
description: "> **Scope**: `src/Pragmatic.Abstractions/Authorization/`, 22 files; these 17 public types."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Abstractions/docs/how-it-works/authorization.md
sidebar:
  order: 6
---
> **Scope**: `src/Pragmatic.Abstractions/Authorization/`, 22 files; these 17 public types.
> `IUserAuthorization` · `ConstantUserAuthorization` · `NullUserAuthorization` ·
> `FullAccessUserAuthorization` · `IPermissionChecker` · `IPermissionProvider` · `PermissionAttribute` ·
> `IRole` · `IRoleDefinition` · `IGroup` · `IResourceAuthorizer<T>` · `IUserScopeResolver` ·
> `PermissionInfo` · `RoleInfo` · `RequirePermissionAttribute` · `RequireAnyPermissionAttribute` ·
> `ExplicitPermissionAttribute`
>
> **Not covered here**: who the caller *is*; see [identity](/modules/abstractions/how-it-works-identity/). The two are one surface
> cut into two folders: `ICurrentUser.Authorization` is the entry point to everything below, and
> three contracts here take an `ICurrentUser` in their signature. The concrete providers, the
> permission catalogue and the resolver cache live in `Pragmatic.Authorization`; they are named
> where they matter, not opened.

For the member-by-member catalogue, see [interfaces](/modules/abstractions/interfaces/#5-authorization). This
document is about how the pieces fit together, and why the seams fall where they do.

## Two questions, two seams

"Can this user do X" has two answers with very different costs, and the split between them is the
first thing to understand.

`IUserAuthorization` (reached as `ICurrentUser.Authorization`) answers from the request's own
context: roles, groups and scopes come straight off the claims. Permissions do not: the shipped
implementation fans out over the providers and passes through the cache stack, and the synchronous
members block on that work.

`IPermissionChecker` is the other seam, for checks that must go and ask something: a database, a
directory, a remote policy service. It is a service you inject, not a property you read, and the
signature is the warning that a call may cost.

Almost nobody injects `IUserAuthorization` directly. It arrives through `ICurrentUser`, which is why
an authorization filter needs one dependency rather than two.

## The constant implementations

`ConstantUserAuthorization` is an abstract base that answers the same thing to every question, and
it has two sealed descendants:

| Type | Every check answers | Collections |
|---|---|---|
| `NullUserAuthorization` | `false` | always empty |
| `FullAccessUserAuthorization` | `true` | always empty |

The empty collections on `FullAccessUserAuthorization` are the part worth reading twice. It grants
everything, but it cannot **enumerate** everything: there is no finite list of all permissions to
hand back. Code that decides by asking (`HasPermission("x")`) works; code that decides by iterating
`Permissions` sees nothing and must not treat that as "no access".

`NullUserAuthorization.Instance` is what `AnonymousUser` composes, which is what makes the anonymous
fallback described in [identity](/modules/abstractions/how-it-works-identity/) fail *closed* all the way down.

`FullAccessUserAuthorization` is for trusted system contexts only: a background job, seeding, a
migration. In this repository it has exactly one user: `SystemUser` in `Pragmatic.Identity`.

`IUserAuthorization` also carries async members with default implementations that delegate to the
synchronous ones. An implementation that resolves everything up front implements the four
synchronous members and gets the async surface for free; one that needs to go out for an answer
overrides the async members.

## How permissions get resolved

`IPermissionProvider` is a chain, not a single lookup. Each provider contributes a set of
permissions and the results are merged as a **union**: no provider can take a permission away, so
adding one can only widen, never silently narrow.

Providers run in `Order`, and `CachedPermissionResolver` is what fans out over them. These are the
ones that ship in `Pragmatic.Authorization`:

| Order | Provider | Contributes |
|---|---|---|
| 0 | `ClaimsPermissionProvider` | permissions carried directly on the token |
| 100 | `RoleExpansionProvider` | expands the user's roles into permissions |
| 200 | `GroupExpansionProvider` | expands group membership |

An application with its own source (an external policy service, a per-tenant table) implements
`IPermissionProvider` and picks an order above 200 to run after the built-ins.

## Permissions as generated constants, roles as types

A permission is an `[assembly: Permission]` line; `IRole` uses static abstract members, so a role is a
*type*. The payoff is at build time: the source generator reads both and emits registries (and, for each
permission, a `const` the code names), so a name typed wrong is a compile error rather than a silent deny.

| Contract | Generated |
|---|---|
| `[assembly: Permission]` | `PermissionRegistry`: an `IReadOnlyList<PermissionInfo>` of every permission in the assembly; and a `const` in the boundary's `{Boundary}Permissions` class |
| `IRole` | `RoleRegistry`: the same for `RoleInfo`, plus role seeding |

`PermissionInfo` and `RoleInfo` are what the generated registries are made of, and what the generated
DI registration hands to the catalogue, and the same DTOs the dynamic stores in
`Pragmatic.Authorization.Management` build at runtime.

Two shaping contracts sit above these. `IRoleDefinition` is a reusable fragment of permissions that
is not a role by itself; the host combines several into a real role with
`RoleBuilder.IncludeDefinition<TDefinition>()`. `IGroup` is a strongly typed group with default role
assignments, wired through `AuthorizationBuilder` in the host's authorization configuration.

## The attributes

`[RequirePermission]` requires **all** of the listed permissions; `[RequireAnyPermission]` requires
**one** of them.

What the generator does with them depends on where they land:

- **On an endpoint**, it emits `RequireAuthorization(...)` with a `PragmaticPermissionRequirement`,
  so the check runs inside the ASP.NET Core authorization pipeline. The name is read at compile
  time, so pass a string literal or a constant from a referenced assembly, not one the generator
  itself emits into the same compilation. **One shape, always**: the requirement type ships with
  `Pragmatic.Endpoints.AspNetCore`, which every boundary declaring an endpoint already references,
  so what the endpoint requires is in its metadata whatever else the module carries.

  ⚠️ Not an assertion-based policy: that would refuse through the same pipeline, but with the
  requirement inside a closure nothing reading endpoint metadata could say what the endpoint wants,
  and the 403 would come back without its `requiredPermissions`: the result handler reads those off
  the requirement and answers a bare `ForbiddenError` when it finds none.
- **On a domain action**, `PermissionAuthorizationFilter` in `Pragmatic.Actions` runs at
  `Order = 200`, after validation, before the transaction opens. In a mutation the invoker runs the
  same check itself, before validation. Neither reflects over attributes at runtime: the requirement
  is looked up in the generated `IPermissionRequirementRegistry`, keyed by type.

### Naming a permission

The name you pass is used verbatim: it is the permission the check will require. Give it a `Description`
and the generator also emits it into a `{Boundary}Permissions` constants class, so the rest of the
codebase can refer to it by symbol.

That generation applies to **dotted** names: the segment before the first dot becomes the
permission's category, which is what groups it in the catalogue. `"billing.invoice.refund"` becomes a
constant in category `billing`; a bare `"refund"` stays a working permission string with no constant
and no category. Naming permissions `{boundary}.{entity}.{operation}` is what makes the generated
constants and the catalogue useful.

## Per-instance decisions

Permissions answer "may this user refund invoices". They do not answer "may this user refund *this*
invoice"; that needs the row.

`IResourceAuthorizer<TResource>` is that check. `Pragmatic.Actions` invokes it through
`ResourceAuthorizationFilter` and the mutation invoker, resolving it by the resource's own type, so
every type it guards needs its own registration. Implementations are registered through
`AuthorizationBuilder`.

The type parameter is deliberately **not** contravariant. It reads as though it should be (an
authorizer for a base type could plausibly guard a derived one), but the container keys on the
closed generic and applies no variance, so an authorizer declared for the base would never be found
for the derived type and the check would pass silently. Removing `in` makes that visible at the
declaration instead of at runtime.

What happens when nothing is registered depends on whether anything nearby is. An unguarded resource
is allowed: the interface is opt-in, and treating every unregistered type as forbidden would refuse
every action in an application that uses none. But when an authorizer *is* registered for a base of
the resource, the resource is **refused**: someone stated an intent to guard that hierarchy, and
letting a derived type slip past it is the failure this rule exists to prevent. The base is found by
walking `Type.BaseType`, not by constructing a generic; nothing here uses reflection to build types.

## Scopes, and how they reach the database

`IUserScopeResolver` turns the current user into a set of scope strings. The default resolver emits
three shapes:

```
user:{id}      role:{roleName}      scope:{customScope}
```

These are not decoration: they are what the persistence layer filters on. Three source-generator
templates in the Persistence feature (`DataAccessFilterTemplate`, `ScopedDataFilterTemplate`,
`QueryFilterRegistrationTemplate`) consume the resolver to build EF Core query filters, so an
authorization decision made here becomes a `WHERE` clause rather than a post-filter in memory. This
is the one place where Authorization leaves its own module and lands in Persistence's generated
code.

The synchronous `ResolveAccessScopes` has a default implementation that runs the async work on a
thread-pool thread. A resolver that does I/O should override the synchronous member rather than
inherit that default.

## External references

Named here, described where they live:

- **`Pragmatic.Abstractions/Identity` → `ICurrentUser`**: carries `Authorization`; see
  [identity](/modules/abstractions/how-it-works-identity/).
- **`Pragmatic.Authorization`**: the permission providers, `CachedPermissionResolver`,
  `AuthorizationBuilder` and `RoleBuilder`, `DefaultUserScopeResolver`, `DefaultPermissionCatalog`,
  and `ResourcePolicy`, the composable policy model.
- **`Pragmatic.Authorization.Management`**: dynamic permissions and roles stored in a database,
  behind `IDynamicPermissionStore` / `IDynamicRoleStore`.
- **`Pragmatic.Identity`**: `SystemUser`, the one holder of `FullAccessUserAuthorization`, and
  `ClaimsPermissionChecker`.
- **`Pragmatic.Endpoints.AspNetCore`**: `PragmaticPermissionRequirement` and `PermissionMode`,
  what an endpoint requires, declared where the endpoint is.
- **`Pragmatic.Identity.AspNetCore`**: `PragmaticPermissionHandler` and
  `PragmaticAuthorizationResultHandler`, which evaluate that requirement and write the refusal.
  Host-side: evaluating it needs `IPermissionChecker` from the container.
- **`Pragmatic.Actions`**: `PermissionAuthorizationFilter` (Order 200) and
  `ResourceAuthorizationFilter`.
- **`Pragmatic.Persistence`**: `ComputedScopeFilter` and the generated query filters that consume
  `IUserScopeResolver`.
