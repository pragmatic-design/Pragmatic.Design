# Architecture and Core Concepts

This guide explains **why** Pragmatic.Abstractions exists, how its contracts shape the entire ecosystem, and how to reason about what belongs in this package versus in a specific module.

---

## The Problem

A modular framework needs shared contracts. Without a central place for those contracts, two bad things happen.

### Circular dependencies

Persistence needs `ICurrentUser` for auditing (CreatedBy, UpdatedBy). Identity needs `IRepository` for user storage. If each module owns its own interfaces, they depend on each other:

```
Pragmatic.Persistence ─── depends on ──→ Pragmatic.Identity (for ICurrentUser)
         ↑                                         │
         └──────── depends on ────────────────────┘  (for IRepository)
```

This is a circular dependency. The compiler rejects it. Developers work around it with hacks: reflection, `object` parameters, separate interface packages per module. Each workaround adds complexity.

### Framework coupling

ASP.NET Core provides `ClaimsPrincipal`, `HttpContext`, `IAuthorizationService`. EF Core provides `DbContext`, `IQueryable<T>`. If domain modules reference these directly:

```csharp
// Every service that needs the current user now depends on ASP.NET Core
public class AuditingInterceptor(IHttpContextAccessor accessor)
{
    public void BeforeSave(IAuditable entity)
    {
        var user = accessor.HttpContext?.User;
        entity.CreatedBy = user?.FindFirst("sub")?.Value;  // Tied to claims structure
    }
}
```

- **Console apps, background workers, and test harnesses** cannot use these services without pulling in the entire ASP.NET Core stack.
- **Module portability** is lost. A module written against `HttpContext` cannot run in a gRPC host, a message handler, or a Lambda function.
- **Testing** requires mocking framework types (`ClaimsPrincipal`, `HttpContext`) instead of simple interfaces.

### Interface duplication

Without a shared package, every module that needs "the current user" would define its own interface:

```csharp
// In Pragmatic.Persistence
public interface IAuditUser { string? Id { get; } }

// In Pragmatic.Actions
public interface IActionUser { string Id { get; } bool HasPermission(string p); }

// In Pragmatic.Events
public interface IEventUser { string Id { get; } }
```

Three interfaces for the same concept. They cannot be composed. A single DI registration cannot satisfy all three. Every module re-implements the mapping from `ClaimsPrincipal` to its own interface.

---

## The Solution

`Pragmatic.Abstractions` is a single, lightweight package that contains **only** the contracts (interfaces, attributes, records, enums) consumed across module boundaries. It has zero dependency on ASP.NET Core or EF Core.

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

Every module depends on Abstractions. Abstractions depends on nothing except three minimal packages:

| Dependency | Purpose |
|---|---|
| `Microsoft.Extensions.Configuration.Abstractions` | `IConfiguration` for `IPragmaticBuilder` |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | `IServiceCollection` for `IPragmaticBuilder` and Decorate extensions |
| `Microsoft.Extensions.Hosting.Abstractions` | `IHostEnvironment` for `IPragmaticBuilder` |

These Microsoft packages are stable, widely-used .NET abstractions with no runtime coupling. They are accepted as foundational because `IPragmaticBuilder` needs `IServiceCollection`, `IConfiguration`, and `IHostEnvironment` to function.

`Pragmatic.Specification` is not among them, and the direction is worth being explicit about:
`ISpecification<T>` is declared **here**, and `Pragmatic.Specification` supplies the `Specification<T>`
base class that implements it. `IReadRepository` takes the contract, never the base class, which is
what keeps a repository usable without the specification package.

### What this enables

1. **No circular dependencies.** `Persistence` and `Identity` both reference Abstractions, not each other.
2. **Framework independence.** Domain modules depend on `ICurrentUser`, not `ClaimsPrincipal`. The ASP.NET Core adapter lives in `Pragmatic.Identity.AspNetCore`, and a module that never needs it never references it, which is the property that matters. It is not host-only in practice: the Showcase boundary libraries take it too, and so do `Identity.Local.Jwt` and `Identity.Oidc`.
3. **Single interface per concept.** One `ICurrentUser`, one `IClock`, one `IRepository`. Every module consumes the same contract. One DI registration satisfies all consumers.
4. **Lightweight referencing.** A console app, test harness, or background worker can reference Abstractions without pulling ASP.NET Core or EF Core.

---

## Area Map

Abstractions is organized in 26 areas. This is the orientation map; for member-level
signatures, usage snippets and caveats of EVERY type, see [interfaces.md](interfaces.md)
(section numbers below).

| Area | What it gives you | Key types | Catalog |
|---|---|---|---|
| Result | Error contract for the Result pattern | `IError` | §1 |
| Persistence: Entities | Entity capabilities the SG implements for you | `IEntity`, `IAuditable`, `ISoftDelete`, `ICreatable`, `IOwnedEntity`/`IScopedEntity`, `IChangeTracking` | §2 |
| Persistence: Repositories | Data access without EF coupling | `IUnitOfWork`, `ITransaction` here; `IRepository`, `IReadRepository` in `Pragmatic.Persistence` | §3 |
| Identity | Who is calling | `ICurrentUser`, `IAuthenticationContext`, `IUserProfile`, `[PragmaticUser]` | §4 |
| Authorization | What they may do (types and generated constants, not strings) | `IUserAuthorization`, `[assembly: Permission]`, `IRole`/`IGroup`, `[RequirePermission]`, `IResourceAuthorizer`, `IUserScopeResolver` | §5 |
| Events | Two-tier domain/integration events | `IDomainEvent`, `IIntegrationEvent`/`[PublicEvent]`, `IDomainEventHandler`, `IRaisesLifecycleEvents` | §6 |
| Temporal | Testable time | `IClock` | §7 |
| Multi-Tenancy | Tenant context, resolution, metadata | `ITenantContext`/`IMutableTenantContext`, `ITenantResolver`, `ITenantEntity`, `ITenantStore` | §8 |
| Caching | Unified cache with stampede/tags | `ICacheStack`, `CacheCategories` | §9 |
| Configuration | Runtime config/secrets/audit stores | `IConfigurationStore`, `ISecretStore`, `IConfigurationAuditStore`, `EnvironmentProfile` | §10 |
| Feature Flags | Context-aware flags (typed) | `IFeatureFlagStore`, `IFeatureFlag`, `IFeatureFlagContextProvider` | §11 |
| Internationalization | Ambient culture/timezone/currency | `IGlobalizationContext` | §12 |
| Pipeline | System-call flag (auth skip) | `ICallContext` | §13 |
| Composition | Host builder, packages, endpoint hook, metadata | `IPragmaticBuilder`, `IPackageDefinition`, `IEndpointRouteBuilderConfigurator`, `AssemblyMetadataRegistry` | §14 |
| Composition Attributes | The declarative DI/module surface | `[Service]`, `[Decorator]`, `[Inject]`, `[Module]`, `[Include<...>]`, `[StartupStep]`, `[UsePackage]`, `[RemoteBoundary]` | §15–16 |
| Telemetry | OTel helpers + tag conventions | `ActivityHelper`, `TelemetryOptions`, `*Tags` | §17 |
| Authoring | Living-spec markers | `Behavior.Pending()`, `[Raises<T>]`, `[Rule]`/`[UseCase]` | §18 |
| Maintenance | Runtime maintenance switch + progress stream | `IMaintenanceMode`, `IMigrationProgressStream` | §19 |
| Control Plane | Fleet coordination contracts | `IControlPlane`, `HostCommand`, health contributors | §20 |
| Serialization | AOT-first shared JSON seam | `PragmaticJsonOptions`, `[PragmaticGenerateJsonContext]` | §21 |
| Specification | Composable predicates | `ISpecification<T>` | §22 |
| Pagination | Plain page | `Page<T>` | §23 |
| Http | Endpoint metadata | `MaxBodySizeMetadata` | §24 |
| Top-level attributes | Enum helpers + sensitive-data marker | `[FastEnum]`, `[NotLogged]` | §25 |
| Analyzers | Compile-time guards shipped with the package | captive dependency, BuildServiceProvider, `[Inject]`, JSON coverage (PRAG2800) | §26 |

> This table is deliberately a MAP, not a copy: the catalog is the single source of detail.

## Layer Architecture

Pragmatic.Design organizes packages into three layers based on their dependency direction.

```
Layer 0 (Foundation)        Layer 1 (Capabilities)           Layer 2 (Integration)
├── Result                  ├── Validation                   ├── Actions
├── Ensure                  ├── Mapping                      ├── Endpoints
├── Abstractions ◄──────────┤── Internationalization         ├── Persistence.EFCore
├── DependencyInjection     ├── Resilience                   ├── Composition.Host
└── Identifiers             ├── Configuration                └── Identity.AspNetCore
                            ├── Caching
                            └── Specification
```

**Layer 0** has no upward dependencies. Every module in the ecosystem can reference Layer 0 packages.

**Layer 1** modules reference Layer 0 and provide specific capabilities. They do not depend on each other unless explicitly declared.

**Layer 2** modules integrate multiple capabilities and may reference Layer 1 packages. They provide the "glue" between the domain and the infrastructure (HTTP, database, hosting).

Abstractions sits at Layer 0. It is the foundation that all other layers build upon.

---

## Design Principles

### 1. No ASP.NET Core references

Abstractions must never reference `Microsoft.AspNetCore.*`. This ensures:
- Console apps and background workers can reference Abstractions without the web stack.
- Domain modules remain portable across hosting models (Kestrel, IIS, Lambda, gRPC).
- Test projects can mock interfaces without importing ASP.NET Core test infrastructure.

The three `Microsoft.Extensions.*.Abstractions` packages are explicitly allowed because they are part of the .NET platform (not ASP.NET) and are required by `IPragmaticBuilder`.

### 2. No EF Core references

Abstractions must never reference `Microsoft.EntityFrameworkCore`. Repository interfaces (`IRepository<T>`) are defined here; their EF Core implementations live in `Pragmatic.Persistence.EFCore`.

### 3. Interfaces over implementations

The package contains interfaces, attributes, records, and enums. Implementation logic is prohibited except for:
- **Null-object singletons** (`AnonymousUser`, `NullUserAuthorization`, `UnresolvedTenantContext`, etc.) -- trivial implementations that return empty/false/null for every member.
- **Extension methods** (`CurrentUserExtensions`, `ServiceCollectionDecorateExtensions`, `FeatureFlagStoreExtensions`) -- stateless utility methods that compose existing interfaces.
- **Telemetry helpers** (`ActivityHelper`) -- stateless extension methods on `System.Diagnostics.Activity`.

### 4. Stable contracts

Because every module depends on Abstractions, breaking changes cascade to the entire ecosystem. Types added here should represent **stable** concepts that change infrequently. Volatile or experimental types belong in the specific module until they stabilize.

When adding new members to existing interfaces, use **default interface implementations** to avoid breaking existing implementors:

```csharp
public interface IError
{
    string Code { get; }
    int StatusCode { get; }
    string Title => string.Empty;       // Default implementation -- non-breaking
    string? Description => null;        // Default implementation -- non-breaking
}
```

### 5. Strongly typed over magic strings

Abstractions favors static abstract interface members (C# 11+) for compile-time safety:

```csharp
public interface IRole
{
    static abstract string Name { get; }
    static abstract string? Description { get; }
    static abstract IReadOnlyList<string> DefaultPermissions { get; }
}
```

Each role, group, and feature flag is a **type**, not a string. This enables:
- Compile-time validation via the source generator.
- IntelliSense and refactoring support.
- Generic extension methods like `store.IsEnabledAsync<LoyaltyDiscount>()`.
- SG-generated registries and constants.

A permission is the exception, and on purpose: it is one line, `[assembly: Permission("billing.invoice.refund",
"…")]`, and what the code names is the `const` the generator writes for it: `BillingPermissions.Invoice.Refund`.
A type per permission would cost a class for a name, a description and a category.

The generic attribute convention follows: `[RequirePolicy<TPolicy>]` rather than `[RequirePolicy(typeof(TPolicy))]`.

### 6. Null-object pattern

For interfaces consumed in contexts where no real implementation may exist (anonymous requests, no-tenant scenarios), Abstractions provides singleton null-objects:

| Null Object | Interface | Behavior |
|---|---|---|
| `AnonymousUser` | `ICurrentUser` | `Id` = empty, `IsAuthenticated` = false, all authorization = false |
| `NullAuthenticationContext` | `IAuthenticationContext` | All properties = null/false |
| `NullUserAuthorization` | `IUserAuthorization` | All checks return false, all collections empty |
| `FullAccessUserAuthorization` | `IUserAuthorization` | All checks return true (system-level contexts) |
| `UnresolvedTenantContext` | `ITenantContext` | `TenantId` = null, `IsResolved` = false |

These are sealed classes with private constructors and a `public static readonly Instance` field. They are safe to register as singletons in DI. This prevents consumers from needing to handle `null` service resolution.

### 7. Attribute design guidelines

Attributes in Abstractions follow these rules:

1. **Generic over `typeof`:** Always `[Attr<T>]` over `[Attr(typeof(T))]`.
2. **Minimal properties:** Only properties that affect SG output or runtime behavior.
3. **No implementation logic:** Attributes are pure data carriers.
4. **`Inherited = false`:** Most attributes are not inherited (each type opts in explicitly).
5. **`AllowMultiple` only when needed:** `[Include<T>]` allows multiple (a host may include many modules). Most other attributes are single-use.

---

## RootNamespace Convention

The `.csproj` declares:

```xml
<RootNamespace>Pragmatic</RootNamespace>
```

All types live under `Pragmatic.*` namespaces (e.g., `Pragmatic.Identity.ICurrentUser`, `Pragmatic.Events.IDomainEvent`), **not** under `Pragmatic.Abstractions.*`. This is intentional.

When a consumer writes `using Pragmatic.Identity;`, they get the same namespace regardless of whether they reference the Abstractions package or the full Identity package. The abstraction and its implementation share a namespace, which simplifies imports and means switching from the abstraction to the full module requires no `using` changes.

---

## How Modules Reference Abstractions

### Module runtime packages

Module runtime packages take a `<ProjectReference>` on Abstractions; 31 projects under `src/` declare
one. This gives them access to all shared contracts:

```xml
<!-- In Pragmatic.Persistence.EFCore.csproj -->
<ProjectReference Include="..\..\..\Pragmatic.Abstractions\src\Pragmatic.Abstractions\Pragmatic.Abstractions.csproj" />
```

Not every module declares it: `Pragmatic.Actions`, for one, receives it transitively. Which is the
point: the contracts arrive either way, and a module only names the reference when it is the one
that needs them.

### Host projects

Host projects (the final executable) transitively receive Abstractions through module references. A host that references `Pragmatic.Composition.Host` and `Pragmatic.Persistence.EFCore` automatically has access to all Abstractions types.

### Source generator

The source generator (`Pragmatic.SourceGenerator`) reads attribute definitions from Abstractions at compile time. It does not take a direct reference but resolves types via `GetTypeByMetadataName` in the Roslyn compilation model. The attribute types must be available in the compilation (which they are when the consuming project references Abstractions or any module that transitively references it).

### Test projects

Test projects can reference Abstractions directly to mock interfaces without importing runtime modules:

```csharp
// Declared once per test assembly; the generator emits CurrentUserMock from the interface.
[assembly: GenerateMock<ICurrentUser>]

// Test can mock ICurrentUser without referencing Pragmatic.Identity.AspNetCore
var user = new CurrentUserMock();
user.Id.Returns("test-user-123");
user.IsAuthenticated.Returns(true);
```

The mock is generated, not a runtime proxy: no reflection, and it works under Native AOT. The
repository ships no mocking library: `Pragmatic.Testing.Mocking.SourceGenerator` produces the type
from the interface, so what a test doubles is checked by the compiler.

---

## Breaking Circular Dependencies

Several interfaces exist in Abstractions specifically to break circular dependencies between modules:

| Interface | Cycle Broken |
|---|---|
| `ICacheStack`, `CacheEntryOptions`, `CachePriority`, `CacheCategories` | Authorization, Configuration, and `Pragmatic.Endpoints` cache without depending on `Pragmatic.Caching`. A module that needs more than the contract references the implementation and pays for it: `Pragmatic.Endpoints.AspNetCore` and `Pragmatic.Persistence.EFCore` both do. |
| `IPragmaticBuilder` | Every module adds `Use*()` extension methods. The interface lives here so modules do not depend on `Pragmatic.Composition.Host`. |
| `ICallContext` | Actions provides `ActionCallContext`; Events checks `IsInternalCall` when dispatching. Without Abstractions, Events would depend on Actions. |
| `ICurrentUser` | Persistence uses it for auditing; Identity implements it. Without Abstractions, Persistence would depend on Identity. |
| `IClock` | Jobs uses it for scheduling; Identity for its authentication actions; the generated trait actions take it by injection. Without Abstractions, all three would need to depend on `Pragmatic.Temporal`. |
| `IDomainEvent`, `IHasDomainEvents` | Persistence entities raise events; the Events module dispatches them. Without Abstractions, Persistence would depend on Events. |

---

## What Belongs in Abstractions

A type belongs in `Pragmatic.Abstractions` if it meets **all** of these criteria:

| Criterion | Rationale |
|---|---|
| Consumed by two or more modules that must not depend on each other | Single-module types belong in that module |
| Zero implementation logic (or trivially minimal null-object singletons) | Implementation belongs in the module that provides runtime behavior |
| No dependency on ASP.NET Core, EF Core, or heavy external libraries | Abstractions must remain lightweight |
| Represents a stable contract that changes infrequently | Volatile types force cascading updates across all modules |

### Types that DO belong

| Type | Why |
|---|---|
| `ICurrentUser` | Used by Persistence (auditing), Actions (authorization), Authorization (permission resolution), Identity, Comments and Attachments (attribution), Configuration, Messaging |
| `IClock` | Used by Temporal (`SystemClock`, `TestClock`), Jobs (scheduling), Identity (authentication actions), and the trait actions the SG emits |
| `IError` | Used by Result, Actions, Endpoints, and the Source Generator |
| `[Service]` attribute | Used by the SG at compile time and by any module registering services |
| `ITenantContext` | Used by Persistence (tenant filter), Configuration (tenant overrides), MultiTenancy, and every module that must restore the tenant on a background path: Events, Jobs, Messaging, Notifications |
| Telemetry tag constants | Used by every module that instruments with OpenTelemetry |

### Types that DO NOT belong

| Type | Where It Belongs | Why |
|---|---|---|
| `AuditingInterceptor` | `Pragmatic.Persistence.EFCore` | EF Core implementation detail; the repositories themselves are generated per entity, there is no shared base type to place |
| `MutationInvoker<TMutation, TEntity>` | `Pragmatic.Actions` | Actions runtime logic |
| `ClaimsPrincipalUserAccessor` | `Pragmatic.Identity.AspNetCore` | Depends on ASP.NET Core's `ClaimsPrincipal` |
| `InMemoryEventDispatcher` | `Pragmatic.Events` | Implementation of `IDomainEventDispatcher` |
| `WildcardMatcher` | `Pragmatic.Authorization` | Authorization-specific utility logic |
| `HybridCacheStack` | `Pragmatic.Caching` | Wraps a third-party caching library |

---

## Checklist: Adding a New Type

Before adding a type to this package:

- [ ] It is consumed by at least two modules that should not depend on each other.
- [ ] It has no implementation logic (or only trivial null-object logic).
- [ ] It does not require ASP.NET Core, EF Core, or other heavy dependencies.
- [ ] It represents a stable contract unlikely to change frequently.
- [ ] Its namespace follows the folder structure under `Pragmatic.*`.
- [ ] If it is an interface with a natural "empty" state, a null-object singleton is provided.
- [ ] If it is a marker interface (like `IRole`), it uses static abstract members.
- [ ] If it is an attribute, it follows the generic-first convention (`[Attr<T>]` over `[Attr(typeof(T))]`).

---

## Versioning

Because every module in the ecosystem depends on Abstractions:

- **Breaking changes** cascade to all modules. Avoid them unless absolutely necessary.
- **New interfaces** are additive (non-breaking). Modules only consume the interfaces they need.
- **New members on existing interfaces** should use default interface implementations when possible.
- **No deprecation before v1.** A replaced API is removed in the same change that introduces its
  successor, with every caller updated; `[Obsolete]` protects consumers that do not exist yet.

The package follows the same version as the Pragmatic.Design ecosystem. All packages are versioned together.

---

## See Also

- [interfaces.md](interfaces.md) -- Complete member-level signatures for every type
- [design-principles.md](design-principles.md) -- Detailed dependency rules and decision framework
- [common-mistakes.md](common-mistakes.md) -- Common mistakes when working with Abstractions
- [troubleshooting.md](troubleshooting.md) -- Problem/solution guide and FAQ
