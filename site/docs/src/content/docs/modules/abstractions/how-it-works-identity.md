---
title: "Identity — who is asking"
description: "> **Scope**: `src/Pragmatic.Abstractions/Identity/` — 9 files, 314 lines."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Abstractions/docs/how-it-works/identity.md
sidebar:
  order: 15
---
> **Scope**: `src/Pragmatic.Abstractions/Identity/` — 9 files, 314 lines.
> `ICurrentUser` · `IAuthenticationContext` · `IUserProfile` · `PrincipalKind` · `AnonymousUser` ·
> `NullAuthenticationContext` · `CurrentUserExtensions` · `PragmaticUserAttribute` ·
> `ProfilePropertyAttribute`
>
> **Not covered here**: `IUserAuthorization` is a property of `ICurrentUser` but a feature of its
> own — see [authorization](/modules/abstractions/how-it-works-authorization/) for roles, permissions and policies. Consumers
> outside Abstractions are named where they matter, not opened.

For the member-by-member catalogue, see [interfaces](/modules/abstractions/interfaces/#4-identity). This document is
about how the pieces fit together, and what has been verified about them.

## The question it answers

One question — **who is making this request** — answered the same way everywhere, whether the
answer came from OIDC, an API key, or a background job that has no caller at all.

`ICurrentUser` is a scoped service. Seven of its members are flat data: `Id`, `DisplayName`,
`IsAuthenticated`, `Kind`, `TenantId`, `Claims` and `ImpersonatedBy`. The other two delegate to
sub-objects, and that is the shape decision worth understanding.

### Why authorization and authentication are separate objects

`Authorization` carries roles and permissions; `Authentication` carries how the authentication
happened — scheme, issuer, MFA, expiry.

Flattened into one interface, `ICurrentUser` would have around thirty members and every consumer
would use three of them. The auditing interceptor wants `Id`. An authorization filter wants
`Authorization`. A security log wants `Authentication.Issuer` and `IsMfaAuthenticated`. Keeping
them apart means a signature tells the reader which world they are in before they read the body.

### The members that carry a decision

**`PrincipalKind`** separates four things that would otherwise all read as "authenticated":
`Anonymous`, `User`, `Service` (machine-to-machine), `System` (jobs, seeding, migrations). It exists
for rules that differ by caller: a nightly job writing audit rows is not a person, and should not be
recorded as one.

**`Claims`** is `IReadOnlyDictionary<string, IReadOnlyList<string>>` — multi-valued by
construction. A user can hold three roles or two tenants under one claim type, and flattening to a
single string would lose that at the first real case.

**`ImpersonatedBy`** is the only member holding two identities at once. When it is set, `Id` is who
you appear to be and `ImpersonatedBy` is who you are. An audit trail that ignores it attributes the
action to the wrong person.

**`Id` is an empty string for anonymous users, never null.** That is why `IdOrNull()` exists — see
below.

## The null objects, and why they are not decoration

`AnonymousUser.Instance` and `NullAuthenticationContext.Instance` are immutable singletons that
answer "nothing" to everything: empty `Id`, `IsAuthenticated` false, `Kind = Anonymous`, no claims.

They are not a convenience for tests. This is where they are actually used:

```csharp
// Pragmatic.Actions/Pipeline/PermissionAuthorizationFilter.cs
var user = provider.GetService<ICurrentUser>() ?? AnonymousUser.Instance;
```

The same line appears in `PolicyEvaluationFilter` and in `MutationInvoker`. It makes **failing open
by omission impossible**: with no `ICurrentUser` registered, the filter does not skip its checks —
it gets a user that fails every one of them. The worst case is access denied, not access granted
because nobody knew who you were.

## `CurrentUserExtensions`

Four helpers, written with C# 14 extension members rather than `this`-parameters.

| Member | Why it exists |
|---|---|
| `IdOrNull()` | `Id` is an empty string when anonymous, but audit columns are nullable. Without this, `CreatedBy` is written as `""` instead of `NULL` — two different things to a query. |
| `DisplayNameOrId()` | Display fallback for UI. |
| `GetClaim(type)` | First value, or null. |
| `GetClaims(type)` | All values, or empty. |

These stay methods and are deliberately **not** extension properties: the change would be
source-breaking for every caller and buys nothing.

## The attributes, and what the generator does with them

`[PragmaticUser]` marks the application's user entity; `[ProfileProperty]` marks the properties that
belong to its profile.

| Generated | Template | When |
|---|---|---|
| `ToProfile()` on the entity, plus a `{Type}Profile : IUserProfile` record | `UserProfileTemplate` | always |
| `{Type}Resolver` with `ResolveAsync()` and `GetProfileAsync()` | `UserResolverTemplate` | only when the project references `Pragmatic.Identity.Persistence` |

Both live in `Pragmatic.SourceGenerator/Features/Identity/`, wired through `IdentityFeature`.

`[PragmaticUser]` takes two settings: `MatchClaim` (default `"sub"`, the OIDC subject) and
`MatchProperty` — which entity property to match the claim against. Left unset, the transform works
it out: a string `ExternalIdentityKey` declared on the entity itself wins; failing that, a
navigation property whose type derives from `IdentityRecord` gives `{Nav}!.ExternalIdentityKey`;
with neither, it falls back to the bare `ExternalIdentityKey` name.

## External references

Named here, described where they live:

- **`Pragmatic.Persistence.EFCore` → `AuditingInterceptor`** takes an optional `ICurrentUser` and
  writes its `IdOrNull()` into `CreatedBy`/`UpdatedBy`. The assignment is **guarded on a known
  user**: with no `ICurrentUser` supplied — or an anonymous one — both columns keep the value they
  already held, so a save from a worker, a job or a CLI leaves the previous attribution standing
  rather than erasing it. Timestamps are written either way.
- **`Pragmatic.Actions`** — `PermissionAuthorizationFilter`, `PolicyEvaluationFilter`,
  `MutationInvoker` all resolve `ICurrentUser` with the anonymous fallback shown above.
- **`Pragmatic.Abstractions/FeatureFlags`** — `IFeatureFlagContextProvider` builds evaluation
  context from the current user.
- **`Pragmatic.Abstractions/Persistence/Entity`** — `IOwnedEntity` and `ISoftDelete` reference the
  current user for ownership and deletion metadata.

## Where the profile is read

By the culture provider the generator emits for it, and nowhere else.

`Pragmatic.Internationalization` resolves the culture by merging every registered
`II18NConfigProvider`, higher priority overriding lower. Only the bottom of the chain ships; the
rest are bands left open for the application:

| Priority | Band | Shipped |
|---|---|---|
| 300+ | request-specific | none — the middleware reads the query string and `Accept-Language` itself, on top of what the providers resolved |
| 200–299 | user preferences | `{User}CultureConfigProvider`, **generated, opt-in** |
| 100–199 | tenant settings | none |
| 0 | system defaults | `SystemConfigProvider`, over `IOptions<I18NOptions>` and `appsettings.json` |

The `RequestConfigProvider` and `TenantConfigProvider` named in the XML doc on `II18NConfigProvider`
illustrate the bands that stay open — they are not types you can reference.

Priority 200 is filled by the generator when the `[PragmaticUser]` entity declares
`[ProfileProperty] PreferredCulture`: it emits `{User}CultureConfigProvider`, and
`UseUserCulture()` on the builder turns it on. **Off by default** — a host that says nothing keeps
resolving the system culture for everyone, which is what it did before this existed.

Two things it deliberately does not do. It reads only `DefaultUICulture`, never `DefaultDataCulture`:
the data culture decides what responses are serialized against, and varying that per user would make
the same endpoint answer in different shapes to different callers. And it defers rather than fails —
anonymous caller, no stated preference, or a stored value that no longer parses all fall through to
the band below, because a culture is a preference and refusing a request over one would be worse than
formatting it in the default.

The provider is scoped and its lookup is cached for five minutes per user, so changing a stored
preference is visible on the next cache window rather than the next request.
