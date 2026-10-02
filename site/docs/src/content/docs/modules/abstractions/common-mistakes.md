---
title: "Common Mistakes"
description: "These are the most common issues developers encounter when working with Pragmatic.Abstractions. Each section shows the wrong approach, the correct approach, and"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Abstractions/docs/common-mistakes.md
sidebar:
  order: 25
---
These are the most common issues developers encounter when working with Pragmatic.Abstractions. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Adding ASP.NET Core Dependencies to Abstractions

**Wrong:**

```csharp
// In Pragmatic.Abstractions — adding an interface that uses ASP.NET Core types
using Microsoft.AspNetCore.Http;

namespace Pragmatic.Identity;

public interface ICurrentUserAccessor
{
    ICurrentUser FromHttpContext(HttpContext context);
}
```

**Result:** Any project that references Abstractions now transitively depends on `Microsoft.AspNetCore.Http`. Console apps, background workers, and test projects that only need the interfaces are forced to pull in the entire ASP.NET Core stack.

**Right:**

```csharp
// In Pragmatic.Identity.AspNetCore — the ASP.NET-specific adapter
using Microsoft.AspNetCore.Http;
using Pragmatic.Identity;

namespace Pragmatic.Identity.AspNetCore;

public class ClaimsPrincipalUserAccessor : ICurrentUser
{
    // Maps ClaimsPrincipal to ICurrentUser
}
```

Keep the interface (`ICurrentUser`) in Abstractions. Keep the ASP.NET-specific implementation in the module that depends on ASP.NET Core.

**Why:** Abstractions is the dependency root. Every module references it. Adding framework-specific types here forces that framework onto every consumer, defeating the purpose of the abstraction layer.

---

## 2. Putting Implementation Logic in Abstractions

**Wrong:**

```csharp
// In Pragmatic.Abstractions — adding caching logic
namespace Pragmatic.Caching;

public class InMemoryCacheStack : ICacheStack
{
    private readonly ConcurrentDictionary<string, object> _cache = new();

    public ValueTask<T> GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory,
        CacheEntryOptions? options = null, CancellationToken ct = default)
    {
        // Full caching implementation...
    }
    // 200+ lines of implementation
}
```

**Result:** Abstractions grows from a lightweight contract package into a runtime dependency. Modules that only need the interface now carry the implementation and its transitive dependencies.

**Right:**

```csharp
// In Pragmatic.Abstractions — only the contract
namespace Pragmatic.Caching;

public interface ICacheStack
{
    ValueTask<T> GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory,
        CacheEntryOptions? options = null, CancellationToken ct = default);
    // ... other members
}

// In Pragmatic.Caching — the implementation
namespace Pragmatic.Caching;

public class HybridCacheStack : ICacheStack
{
    // Full implementation here
}
```

**Why:** Abstractions must contain only contracts (interfaces, attributes, records, enums) and trivially minimal implementations (null-object singletons). Anything with real logic belongs in the module that provides the runtime behavior.

The exceptions, in full — the list is worth keeping honest, because "only contracts" is what people quote back:
- Null-object singletons (`AnonymousUser`, `NullUserAuthorization`, `UnresolvedTenantContext`) that return empty/false/null for every member.
- Stateless extension methods (`CurrentUserExtensions`, `ServiceCollectionDecorateExtensions`).
- Telemetry helpers (`ActivityHelper`).
- `PragmaticJsonOptions` — stateful, with a `Lock`, a mutable context list and a `Build()` that freezes it. It is the shared serialization seam, and a seam every module must observe as one object cannot be a contract with the state kept elsewhere.
- `OutboundUrlGuard` — real logic, classifying addresses against the private and reserved ranges. It lives here so a module can refuse an SSRF without referencing an HTTP package.
- Argument guards on attributes and records (`PagedResult`, `[RequirePermission]`, `[Rule]`): refusing a meaningless construction, not behaviour.

---

## 3. Using `Pragmatic.Abstractions.*` Namespaces

**Wrong:**

```csharp
// Consumer code — wrong namespace
using Pragmatic.Abstractions.Identity;

public class MyService(ICurrentUser user)
{
    // Compile error: ICurrentUser is not in Pragmatic.Abstractions.Identity
}
```

**Right:**

```csharp
using Pragmatic.Identity;

public class MyService(ICurrentUser user)
{
    // Works — ICurrentUser lives in Pragmatic.Identity
}
```

**Why:** The `.csproj` declares `<RootNamespace>Pragmatic</RootNamespace>`. All types live under `Pragmatic.*` namespaces, not `Pragmatic.Abstractions.*`. The namespace matches the domain (Identity, Events, Persistence), not the package name. This is intentional: when you later add the full `Pragmatic.Identity` package, you do not need to change any `using` statements.

---

## 4. Depending on Concrete Modules Instead of Abstractions

**Wrong:**

```csharp
// In a domain service library — referencing the full module for just the interface
<ProjectReference Include="...\Pragmatic.Persistence.EFCore.csproj" />
```

```csharp
// Only uses IRepository, but now depends on EF Core transitively
using Pragmatic.Persistence.Repository;

public class OrderService(IRepository<Order> orders)
{
    // ...
}
```

**Result:** The domain service library now transitively depends on EF Core, even though it only uses the `IRepository` interface. If you ever want to swap the persistence provider (or test without a database), you carry the EF Core dependency.

**Right:**

```csharp
// Reference Abstractions, not the full module
<ProjectReference Include="...\Pragmatic.Abstractions.csproj" />
```

```csharp
using Pragmatic.Persistence.Repository;

public class OrderService(IRepository<Order> orders)
{
    // Same code, but no EF Core dependency
}
```

**Why:** Domain service libraries and module runtime packages should depend on Abstractions for interface types. Only the host project (the final executable) should reference the concrete implementation packages like `Pragmatic.Persistence.EFCore` or `Pragmatic.Identity.AspNetCore`.

---

## 5. Using `typeof()` Instead of Generic Attributes

**Wrong:**

```csharp
[Include(typeof(BookingModule), typeof(AppDatabase))]
[UsePackage(typeof(LocalIdentityPackage))]
[RemoteBoundary(typeof(BillingModule))]
public class AppHost { }
```

**Compile result:** These non-generic overloads do not exist. The Abstractions package only provides generic versions.

**Right:**

```csharp
[Include<BookingModule, AppDatabase>]
[UsePackage<LocalIdentityPackage>]
[RemoteBoundary<BillingModule>]
public class AppHost { }
```

**Why:** Abstractions follows the "generic over typeof" convention for all attributes. Generic attributes provide:
- **Compile-time type checking.** `[Include<T>]` constrains `T : class`. `[Include<T, TDb>]` constrains `TDb : PragmaticDatabase`. The compiler catches type errors immediately.
- **IntelliSense support.** You get autocomplete for the type parameter.
- **No runtime reflection.** The SG reads generic type arguments directly from the syntax tree.

---

## 6. Adding a Single-Module Type to Abstractions

**Wrong:**

```csharp
// In Pragmatic.Abstractions — an interface only used by Pragmatic.Authorization
namespace Pragmatic.Authorization;

public interface IWildcardMatcher
{
    bool Matches(string pattern, string value);
}
```

**Result:** The type is consumed by exactly one module. It does not break any circular dependency. It clutters Abstractions with a type that has no cross-module consumers.

**Right:**

```csharp
// In Pragmatic.Authorization — where it belongs
namespace Pragmatic.Authorization;

internal static class WildcardMatcher
{
    public static bool Matches(ReadOnlySpan<char> pattern, ReadOnlySpan<char> value) { /* ... */ }
}
```

**Why:** A type belongs in Abstractions only if it is consumed by two or more modules that must not depend on each other. Single-module types belong in that module, even if they are "general purpose." The decision to promote a type to Abstractions should be driven by actual cross-module consumption, not by speculation that other modules might need it.

---

## 7. Forgetting the Null-Object When Adding a New Interface

**Wrong:**

```csharp
// In Pragmatic.Abstractions — new interface without a null-object
namespace Pragmatic.FeatureFlags;

public interface IFeatureFlagStore
{
    Task<bool> IsEnabledAsync(string flagName, FeatureFlagContext? context, CancellationToken ct);
    // ...
}
// No null-object fallback provided
```

```csharp
// Consumer must handle null or throw
public class MyService(IFeatureFlagStore? store)
{
    public async Task DoWork()
    {
        if (store is null)
        {
            // What do we do? Default to enabled? Disabled? Throw?
        }
    }
}
```

**Result:** Every consumer must decide independently how to handle a missing registration. Some will throw, some will default to true, some to false. Behavior is inconsistent.

**Right:**

When an interface has a natural "do nothing" or "anonymous" state, provide a null-object in Abstractions:

```csharp
// Null-object: all flags disabled, no definitions, no watches
public sealed class NullFeatureFlagStore : IFeatureFlagStore
{
    public static readonly NullFeatureFlagStore Instance = new();
    private NullFeatureFlagStore() { }

    public Task<bool> IsEnabledAsync(string flagName, FeatureFlagContext? context, CancellationToken ct)
        => Task.FromResult(false);
    // ...
}
```

**Why:** The null-object pattern ensures consistent behavior when a service is not registered. Consumers can depend on the interface without null checks. The DI container can register the null-object as a fallback.

Not every interface needs a null-object. `IRepository<T>` does not have one because there is no sensible "do nothing" behavior for persistence. But identity, authorization, tenant context, and feature flags all have natural default states.

---

## 8. Breaking an Existing Interface Without Default Implementation

**Wrong:**

```csharp
// Adding a required member to an existing interface
public interface ICurrentUser
{
    string Id { get; }
    string? DisplayName { get; }
    bool IsAuthenticated { get; }
    PrincipalKind Kind { get; }
    string? TenantId { get; }
    IReadOnlyDictionary<string, IReadOnlyList<string>> Claims { get; }
    string? ImpersonatedBy { get; }
    IUserAuthorization Authorization { get; }
    IAuthenticationContext Authentication { get; }

    // NEW: required member without default
    IUserProfile Profile { get; }  // Breaks all existing implementations!
}
```

**Result:** Every existing `ICurrentUser` implementation (`AnonymousUser`, `ClaimsPrincipalUserAccessor`, test mocks, etc.) stops compiling because it does not implement `Profile`.

**Right:**

```csharp
public interface ICurrentUser
{
    // ... existing members ...

    // NEW: with default implementation — non-breaking
    IUserProfile? Profile => null;
}
```

**Why:** Abstractions is the dependency root of the entire ecosystem. A breaking change to any interface forces every module and every consumer to update simultaneously. Default interface implementations (C# 8+) allow adding new members without breaking existing implementors. The default should represent the "before this feature existed" state -- typically null, false, or empty.

---

## 9. Mismatching Namespace and Folder Structure

**Wrong:**

```
File: src/Pragmatic.Abstractions/Authorization/IPermissionChecker.cs
```

```csharp
namespace Pragmatic.Identity;  // Wrong namespace — file is in Authorization/ folder

public interface IPermissionChecker { /* ... */ }
```

**Result:** The type compiles but lives in an unexpected namespace. Consumers find it via IntelliSense under `Pragmatic.Identity` while the file is in the `Authorization/` folder. This causes confusion when navigating the source.

**Right:**

```csharp
namespace Pragmatic.Authorization;  // Matches the folder structure

public interface IPermissionChecker { /* ... */ }
```

**Why:** Abstractions follows the convention: namespace = folder structure under the `Pragmatic` root namespace. The `Authorization/` folder maps to `Pragmatic.Authorization`. The `Identity/` folder maps to `Pragmatic.Identity`. When a type is moved between folders, its namespace must be updated to match.

---

## 10. Registering AnonymousUser as Scoped Instead of Singleton

**Wrong:**

```csharp
// In a startup step — registering the fallback as scoped
services.AddScoped<ICurrentUser>(_ => AnonymousUser.Instance);
```

**Result:** Works, but creates unnecessary overhead. A new scope resolution occurs per request even though the object is immutable. When authentication middleware later replaces the registration with a real scoped `ICurrentUser`, the scoped fallback may interfere with DI ordering.

**Right:**

```csharp
// Register as singleton — it is an immutable singleton by design
services.AddSingleton<ICurrentUser>(AnonymousUser.Instance);
```

Or, more typically, do not register `AnonymousUser` at all. The authentication middleware registers the real `ICurrentUser` per request. `AnonymousUser.Instance` is used directly in code that needs a fallback:

```csharp
var user = serviceProvider.GetService<ICurrentUser>() ?? AnonymousUser.Instance;
```

**Why:** All null-objects in Abstractions (`AnonymousUser`, `NullUserAuthorization`, `NullAuthenticationContext`, `UnresolvedTenantContext`, `FullAccessUserAuthorization`) are sealed classes with private constructors and a `public static readonly Instance` field. They are immutable. If registered in DI, they should be singletons.

---

## 11. Relying on `[NotLogged]` for Compliance

`[NotLogged]` marks a property/parameter as sensitive. **It marks; nothing enforces it today.**

```csharp
// ❌ WRONG assumption: "it's marked, so it never reaches any log"
_logger.LogInformation("Registering {User}", request);   // your own log call is NOT rewritten
```

The generator reads the attribute in one place — message types — and emits an `IRedactionMap` per
messaging assembly. No component consults that map: message auditing moved onto the framework audit
trail, whose entries carry no payload field, so there is no serialized payload left to redact there.

What *does* redact is pattern-based and independent of this attribute: `PragmaticDataRedactor`
(Pragmatic.Logging) matches configured property-name patterns, and the audit trail applies
`PersonalDataRedactor` to what it stores. Both match on names and values, so a field called `Pwd`
slips through unless a pattern covers it — which is precisely what a declarative marker would fix,
and does not yet.

For personal data with a declared category, and the erasure/retention/Article 30 machinery behind
it, use `[PersonalData]` from `Pragmatic.Privacy.Abstractions`: that one is wired end to end.
See [interfaces.md §25](/modules/abstractions/interfaces/).

## 12. `Page<T>` Is Not `PagedResult<T>`

Two different things, two names:

| Type | What it is |
|---|---|
| `Pragmatic.Pagination.Page<T>` (this package) | A plain page: `Items`, `TotalCount`, `Number`, `PageSize`, never a failure. Returned by `ToPagedDtoAsync()` in Mapping.EFCore, which is why it lives in Abstractions: Mapping cannot depend on Persistence. |
| `Pragmatic.Persistence.Query.Results.PagedResult<T,TError>` | Result-pattern paged result with an explicit error type. |
| `Pragmatic.Persistence.Query.Results.PagedResult<T>` | The same, with `QueryError` as the error type. **This is what generated grid queries and endpoints return** (`PagedResultOf{T}` in OpenAPI). |

A generated query answers with a `PagedResult<T>` — a result you check for success before reading
`Items`. A `Page<T>` is already the page.

## Quick Reference

| Mistake | Symptom |
|---------|---------|
| ASP.NET Core reference in Abstractions | Console apps and test projects pull the web stack |
| Implementation logic in Abstractions | Package grows, carries runtime dependencies |
| `Pragmatic.Abstractions.*` namespace | Compile error: type not found |
| Concrete module dependency for interfaces | Unnecessary transitive dependencies (EF Core, etc.) |
| `typeof()` in attributes | Compile error: non-generic overloads do not exist |
| Single-module type in Abstractions | Package clutter, no cross-module benefit |
| Missing null-object | Consumers handle missing registrations inconsistently |
| Breaking interface without default | All existing implementations stop compiling |
| Namespace/folder mismatch | Confusing navigation, unexpected type locations |
| Scoped registration for null-objects | Unnecessary per-request overhead for immutable singletons |
