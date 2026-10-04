# Design Principles -- Pragmatic.Abstractions

This document explains why `Pragmatic.Abstractions` exists, what rules govern its contents, and how to decide whether a type belongs here or in a specific module.

---

## Why This Package Exists

The Pragmatic.Design ecosystem comprises many modules (Actions, Persistence, Events, Identity, Authorization, Composition, etc.) that need to communicate through shared contracts. Without a central abstractions package, modules would either:

1. **Depend on each other directly** -- creating circular dependencies and forcing modules to carry heavy transitive dependencies they do not need.
2. **Duplicate interfaces** -- leading to incompatible contracts and breaking composition.

`Pragmatic.Abstractions` solves this by providing a single, lightweight package that all modules can reference. It contains **only** the contracts (interfaces, attributes, records, enums) that are consumed across module boundaries. It carries no implementation logic, no ASP.NET Core dependency, and no EF Core dependency.

> **One declared exception:** the package ships four Roslyn analyzers (`Pragmatic.Abstractions.Analyzers`:
> captive dependency, `BuildServiceProvider`, `[Inject]` consistency, JSON context coverage PRAG2800).
> They are compile-time tooling that guards these contracts: they add zero runtime dependency and
> never execute in your app, so they don't violate the no-implementation rule; they enforce it.

---

## The Dependency Rule

```
                 Pragmatic.Abstractions (Layer 0)
                          ^
                          |
         +----------------+------------------+
         |                |                  |
    Pragmatic.Actions  Pragmatic.Events  Pragmatic.Persistence.EFCore
    Pragmatic.Identity Pragmatic.Authorization   ...
         (Layer 1-2 modules)
```

**Rule:** Abstractions depends on nothing except `Pragmatic.Specification` (a pure library with no external dependencies) and three `Microsoft.Extensions.*.Abstractions` packages. Every other module depends on Abstractions, never the reverse.

The three Microsoft abstractions packages are included because `IPragmaticBuilder` and the DI-related types need `IServiceCollection`, `IConfiguration`, and `IHostEnvironment`. These are stable, widely-used .NET contracts with no runtime coupling.

---

## What Belongs in Abstractions

A type belongs in `Pragmatic.Abstractions` if it meets **all** of the following criteria:

| Criterion | Rationale |
|---|---|
| It is consumed by two or more modules that must not depend on each other. | Single-module types belong in that module. |
| It has zero implementation logic (or trivially minimal, like null-object singletons). | Implementation belongs in the module that provides the runtime behavior. |
| It does not depend on ASP.NET Core, EF Core, or any heavy external library. | Abstractions must remain lightweight so even a minimal console app can reference them. |
| It represents a **stable** contract that changes infrequently. | Volatile types would force cascading updates across all modules. |

### Examples of Types That Belong Here

| Type | Why |
|---|---|
| `ICurrentUser` | Consumed by Persistence (auditing), Actions (authorization filter), Endpoints (permission checks), Logging (correlation). |
| `IEntity` | Consumed by Persistence and the Source Generator. Repository generic constraints reference it. |
| `IClock` | Consumed by Temporal (`SystemClock`, `TestClock`), Jobs (scheduling), Identity (authentication actions), and the trait actions the Source Generator emits. |
| `IError` | Consumed by Result, Actions, Endpoints, and the Source Generator. |
| `[Service]` attribute | Consumed by the Source Generator at compile time and by any module that registers services. |
| `ITenantContext` | Consumed by Persistence (tenant filter), Caching (tenant-scoped keys), Configuration (tenant overrides). |
| `ICallContext` | Consumed by Actions (authorization skip for internal calls) and Events (event handlers run as internal calls). |
| Telemetry tag constants | Consumed by every module that instruments with OpenTelemetry. Centralized to avoid tag name drift. |

### Examples of Types That Do NOT Belong Here

| Type | Where It Belongs | Why |
|---|---|---|
| `EfCoreUnitOfWork` | `Pragmatic.Persistence.EFCore` | It is an EF Core implementation detail. |
| `MutationInvoker<T>` | `Pragmatic.Actions` | It is Actions runtime logic. |
| `ClaimsPrincipalUserAccessor` | `Pragmatic.Identity.AspNetCore` | It depends on ASP.NET Core's `ClaimsPrincipal`. |
| `InMemoryEventDispatcher` | `Pragmatic.Events` | It is an implementation of `IDomainEventDispatcher`. |
| `WildcardMatcher` | `Pragmatic.Authorization` | It is authorization-specific utility logic. |
| `HybridCache` adapter | `Pragmatic.Caching` | It wraps a third-party caching library. |

---

## Breaking the Circular Dependency

Several interfaces exist in Abstractions specifically to break circular dependencies:

| Interface | Problem It Solves |
|---|---|
| `ICacheStack`, `CacheEntryOptions`, `CachePriority`, `CacheCategories` | Caching types live here so modules (Authorization, Configuration, `Pragmatic.Endpoints`) can depend on the caching abstraction without referencing `Pragmatic.Caching`; a module that needs more than the contract does reference it, as `Pragmatic.Endpoints.AspNetCore` and `Pragmatic.Persistence.EFCore` do. Replaces the former `IConfigurationCache` with a unified `ICacheStack` + category routing. |
| `IPragmaticBuilder` | Composition.Host provides the concrete builder, but every module needs to add `Use*()` extension methods on the interface. Placing the interface here avoids modules depending on the Host package. |
| `ICallContext` | Actions defines the implementation, but Events needs to check `IsInternalCall` when dispatching. The interface here lets Events reference it without depending on Actions. |

---

## Null-Object Pattern

For interfaces that are consumed in contexts where no real implementation may be registered (anonymous requests, no-tenant scenarios), Abstractions provides singleton null-objects:

| Null Object | Interface | Behavior |
|---|---|---|
| `AnonymousUser` | `ICurrentUser` | `Id` = empty, `IsAuthenticated` = false, all authorization checks = false |
| `NullAuthenticationContext` | `IAuthenticationContext` | All properties = null/false |
| `NullUserAuthorization` | `IUserAuthorization` | All checks return false, all collections empty |
| `FullAccessUserAuthorization` | `IUserAuthorization` | All checks return true (for system-level contexts) |
| `UnresolvedTenantContext` | `ITenantContext` | `TenantId` = null, `IsResolved` = false |

These are sealed classes with private constructors and a `public static readonly Instance` field. They are safe to register as singletons in DI.

**Design rule:** if an interface from Abstractions has a natural "do nothing" or "anonymous" state, provide a null-object here. This prevents consumers from needing to handle `null` service resolution.

---

## RootNamespace Convention

The `.csproj` declares:

```xml
<RootNamespace>Pragmatic</RootNamespace>
```

This means all types live under `Pragmatic.*` namespaces (e.g., `Pragmatic.Identity.ICurrentUser`, `Pragmatic.Events.IDomainEvent`), not under `Pragmatic.Abstractions.*`. This is intentional: when a consumer writes `using Pragmatic.Identity;`, they get the same namespace regardless of whether they reference the Abstractions package or the full Identity package. The abstraction and its implementation share a namespace, which simplifies imports.

---

## Strongly Typed Over Magic Strings

Abstractions favors static abstract interface members (C# 11+) for compile-time safety:

| Contract | Pattern |
|---|---|
| `IRole` | `static abstract string Name { get; }` + `static abstract IReadOnlyList<string> DefaultPermissions { get; }` |
| `IGroup` | `static abstract string Name { get; }` + `static abstract IReadOnlyList<string> DefaultRoles { get; }` |
| `IFeatureFlag` | `static abstract string Name { get; }` |
| `IPackageDefinition` | `static abstract string PackageName { get; }` |

Each role, group, and feature flag is a **type**, not a string. This enables:

- Compile-time validation (the SG reads these types and validates permission names)
- IntelliSense and refactoring support
- Generic extension methods like `store.IsEnabledAsync<LoyaltyDiscount>()`
- SG-generated registries and constants

A permission is not a type: it is an `[assembly: Permission]` line, and the code names the `const` the
generator writes for it. A type per permission would cost a class for three strings.

The generic attribute convention follows: `[RequirePolicy<TPolicy>]` rather than `[RequirePolicy(typeof(TPolicy))]`.

---

## Attribute Design Guidelines

Attributes in Abstractions follow these rules:

1. **Generic over `typeof`:** Always prefer `[Attr<T>]` over `[Attr(typeof(T))]`.
2. **Minimal properties:** Only include properties that affect SG output or runtime behavior. Documentation belongs in XML comments.
3. **No implementation logic, with one admitted exception: validating their own arguments.**
   `[RequirePermission]` and `[RequireAnyPermission]` reject an empty permission list,
   `[RequiresConfig]` and `[Rule]` reject a blank string. That is not behaviour, it is refusing to be
   constructed in a state that means nothing. ⚠️ For an attribute the source generator reads, the
   constructor may never run (the generator reads the *symbol*), so a guard there protects only the
   reflective path. Where the rule has to hold for generated code, it is enforced by a diagnostic
   instead: `PRAG0422` is exactly that, for the empty permission list.
4. **`Inherited = false`:** Most attributes are not inherited because each type opts in explicitly.
5. **`AllowMultiple` only when needed:** `[Include<T>]` and `[IncludeModule<T>]` allow multiple because a host or module may include many modules. Most other attributes are single-use.

---

## Telemetry Conventions

Telemetry tag constants are centralized here to prevent tag name drift across modules. Each module instruments its own `Activity` spans but uses the same tag names.

Naming convention:

| Prefix | Source |
|---|---|
| Standard OTel (`db.*`, `exception.*`, `cache.*`) | OpenTelemetry semantic conventions |
| `pragmatic.*` | Framework-specific extensions |

When adding new tags, check the OpenTelemetry semantic conventions first. Only create `pragmatic.*` tags for concepts that have no OTel equivalent.

---

## Versioning Implications

Because every module in the ecosystem depends on Abstractions:

- **Breaking changes** in Abstractions cascade to all modules. Avoid breaking changes unless absolutely necessary.
- **New interfaces** are additive (non-breaking). Modules only consume the interfaces they need.
- **New members on existing interfaces** should use default interface implementations when possible to avoid breaking existing implementors.
- **No deprecation before v1.** A replaced API is removed in the same change that introduces its
  successor, with every caller updated; `[Obsolete]` protects consumers that do not exist yet.

The package follows the same version as the Pragmatic.Design ecosystem. All packages are versioned together.

---

## Checklist: Adding a New Type to Abstractions

Before adding a type to this package, verify:

- [ ] It is consumed by at least two modules that should not depend on each other.
- [ ] It has no implementation logic (or only trivial null-object logic).
- [ ] It does not require ASP.NET Core, EF Core, or other heavy dependencies.
- [ ] It represents a stable contract unlikely to change frequently.
- [ ] Its namespace follows the folder structure under `Pragmatic.*`.
- [ ] If it is an interface with a natural "empty" state, a null-object singleton is provided.
- [ ] If it is a new marker interface (like `IRole`), it uses static abstract members for compile-time safety.
- [ ] If it is an attribute, it follows the generic-first convention.
