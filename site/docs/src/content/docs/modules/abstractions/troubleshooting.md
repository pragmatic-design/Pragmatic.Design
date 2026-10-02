---
title: "Troubleshooting"
description: "Practical problem/solution guide for Pragmatic.Abstractions. Each section covers a common issue, the likely causes, and the fix."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Abstractions/docs/troubleshooting.md
sidebar:
  order: 26
---
Practical problem/solution guide for Pragmatic.Abstractions. Each section covers a common issue, the likely causes, and the fix.

---

## Type Not Found After Adding Abstractions Reference

You added a `<ProjectReference>` or `<PackageReference>` to `Pragmatic.Abstractions`, but types like `ICurrentUser` or `IClock` are not resolving.

### Checklist

1. **Check the namespace.** Types live under `Pragmatic.*`, not `Pragmatic.Abstractions.*`. Use `using Pragmatic.Identity;` for `ICurrentUser`, `using Pragmatic.Temporal.Clock;` for `IClock`, and so on.

2. **Check the project target framework.** Abstractions targets `net10.0`. If your project targets an earlier framework, the reference will not resolve.

3. **Rebuild the solution.** After adding a project reference, Visual Studio sometimes needs a full rebuild to update IntelliSense. Run `dotnet build` from the command line to verify.

4. **Check for conflicting references.** If your project references both `Pragmatic.Abstractions` and a full module (e.g., `Pragmatic.Identity`), both expose types in `Pragmatic.Identity`. This is intentional and works correctly -- the full module re-exports the abstractions. But if you reference incompatible versions, you may get type mismatch errors.

---

## "Ambiguous Reference" Between Abstractions and a Module

You see a compiler error like: `'ICurrentUser' is an ambiguous reference between 'Pragmatic.Identity.ICurrentUser' and 'Pragmatic.Identity.ICurrentUser'`.

### Cause

Two different assembly versions define the same type in the same namespace. This happens when:

- Your project references `Pragmatic.Abstractions` explicitly AND a module that transitively references a different version of Abstractions.
- NuGet package versions are out of sync.

### Fix

1. **Align versions.** All Pragmatic packages are versioned together. Ensure every `Pragmatic.*` reference uses the same version.

2. **Remove the explicit Abstractions reference.** If your project references a full module (e.g., `Pragmatic.Actions`), you do not need to also reference `Pragmatic.Abstractions`. The module transitively includes it.

3. **Check `Directory.Build.props`.** The root build configuration pins versions. Verify that no project overrides the version locally.

---

## Null-Object Not Registered -- Service Returns Null

You inject `ICurrentUser` or `ITenantContext` into a service, but it resolves to `null` at runtime (not the null-object singleton).

### Cause

No module has registered a concrete implementation or fallback. Null-objects like `AnonymousUser.Instance` are not auto-registered -- they are available for manual registration or direct use in code.

### Fix

**Option A: Register the null-object as a fallback.**

```csharp
services.AddSingleton<ICurrentUser>(AnonymousUser.Instance);
services.AddSingleton<ITenantContext>(UnresolvedTenantContext.Instance);
```

When the authentication middleware or tenant resolver runs, it replaces the singleton with a scoped registration for the real implementation. The null-object serves as the fallback when no middleware is configured.

**Option B: Use the null-object directly in code.**

```csharp
var user = serviceProvider.GetService<ICurrentUser>() ?? AnonymousUser.Instance;
```

**Option C: Use Pragmatic.Composition.** When using `PragmaticApp.RunAsync()`, the auto-registration in the generated host registers sensible defaults for all detected modules.

---

## ICacheStack Not Available -- Module Needs Caching

A module depends on `ICacheStack` (defined in Abstractions), but no implementation is registered at runtime.

### Cause

The `ICacheStack` interface lives in Abstractions so that modules like Authorization, Configuration, and Endpoints can depend on it without referencing `Pragmatic.Caching`. But the interface alone does not provide an implementation.

### Fix

1. **Add `Pragmatic.Caching` to the host project.**

   ```xml
   <PackageReference Include="Pragmatic.Caching" />
   ```

2. **Register caching in startup.**

   ```csharp
   services.AddPragmaticCaching(cache => cache.ForCategory<MyCategory>(o => o.KeyPrefix = "my:"));
   ```

   There is no `UseCaching` on `IPragmaticBuilder`: caching is registered on the service collection,
   with or without Composition. The builder is for modules that expose a strategy choice, and caching
   does not.

3. **For tests:** use `NullCacheStack` if the test does not need real caching, or the generated mock:

   ```csharp
   [assembly: GenerateMock<ICacheStack>]   // once per test assembly

   var cache = new CacheStackMock();
   ```

---

## Attribute Not Detected by Source Generator

You applied `[Service]`, `[Module]`, or another composition attribute, but the SG does not generate any code.

### Checklist

1. **Is the source generator referenced?** Your project needs the `Pragmatic.SourceGenerator` analyzer reference:

   ```xml
   <ProjectReference Include="...\Pragmatic.SourceGenerator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
   ```

2. **Is the class `partial`?** Many SG outputs require the class to be `partial` (e.g., entities, endpoints, actions). Check for PRAG diagnostics in the build output.

3. **Is the attribute from the correct namespace?** Composition attributes live in `Pragmatic.Composition.Attributes`. Ensure you are using the correct `using` statement.

4. **Does the project reference Abstractions (directly or transitively)?** The attribute types must be available in the compilation. If Abstractions is not referenced, the attribute resolves to nothing and the SG skips the class.

---

## ICallContext.IsInternalCall Always False

Event handlers or cross-boundary action calls are being blocked by authorization filters because `IsInternalCall` is false.

### Cause

`ICallContext` is the abstraction. The implementation (`ActionCallContext`) lives in `Pragmatic.Actions`. If Actions is not referenced by the host, no `ICallContext` is registered, and the default behavior is "not internal."

### Fix

1. **Ensure `Pragmatic.Actions` is referenced by the host** and the DI registration includes `ActionCallContext`.

2. **With Pragmatic.Composition:** This is auto-registered when the SG detects Actions. Verify that the host includes the Actions module.

3. **For manual registration:**

   ```csharp
   services.AddScoped<ICallContext, ActionCallContext>();
   ```

---

## Default Interface Implementation Not Working

You added a new member to an interface with a default implementation, but existing implementations report a compile error.

### Possible Causes

1. **Target framework too old.** Default interface implementations require C# 8+ and .NET Core 3.0+. Verify your project targets `net10.0` or later.

2. **Implementation explicitly declares the member.** If an existing class declares the member (even with the same signature), it overrides the default. If the declared signature does not match, the compiler reports an error.

3. **Struct implementation.** Default interface implementations are not supported on value types. If the type implementing the interface is a `struct`, the member must be explicitly implemented.

---

## Telemetry Tags Not Appearing in Traces

You are using `ActivityHelper` and tag constants from `Pragmatic.Telemetry.Conventions`, but tags do not appear in your observability backend.

### Checklist

1. **Is an ActivityListener configured?** `System.Diagnostics.Activity` events are only captured when a listener is registered. In ASP.NET Core, OpenTelemetry registers listeners. In console apps, you must register one explicitly.

2. **Is the Activity non-null?** `ActivityHelper` methods are null-safe -- they silently no-op when the activity is null. If no listener is active, `Activity.Current` is null. This is by design (zero overhead when not observed), but it means tags are silently dropped.

3. **Are you using the correct tag names?** Use constants from `Pragmatic.Telemetry.Conventions` (e.g., `ActionTags.Name`, `DbTags.Operation`) instead of hand-typed strings. Mismatched tag names will not correlate in your backend.

---

## PRAG2800: Type Not Covered by a JSON Context

**Symptom:** the `Info`-level diagnostic PRAG2800 on a type serialized through the Pragmatic JSON
seam (message payload, job args, saga state). It does not fail the build, and it only fires once the
project already declares a `JsonSerializerContext` of its own.

**Cause:** the shared `PragmaticJsonOptions` pipeline is AOT-first: every serialized type must be
covered by a source-generated `JsonSerializerContext`, and this one isn't.

**Solution:** either add `[assembly: PragmaticGenerateJsonContext]` to the module (the generator
emits a context covering your boundary types automatically), or apply the analyzer's code-fix,
which adds the missing `[JsonSerializable(typeof(...))]` to your context. Only as a last resort —
and never for native AOT — leave the reflection fallback enabled.

## FAQ

### When should I reference Abstractions directly versus through a module?

Reference Abstractions directly when:
- You are building a module runtime package that only needs contracts.
- You are writing a domain service library that depends on interfaces but not implementations.
- You are writing tests and want to mock interfaces without importing runtime dependencies.

Reference through a module when:
- You are building a host or application that needs both the contract and the implementation.
- A module already transitively provides Abstractions (no need to add it again).

### Can I add new types to Abstractions?

Yes, if the type meets all criteria in the [checklist](/modules/abstractions/concepts/#checklist-adding-a-new-type): consumed by 2+ modules, no implementation logic, no framework dependency, and stable. New types are additive and non-breaking.

### Why are the `Microsoft.Extensions.*.Abstractions` packages allowed?

`IPragmaticBuilder` exposes `IServiceCollection`, `IConfiguration`, and `IHostEnvironment`. These three types come from `Microsoft.Extensions.*.Abstractions` packages which are:
- Part of the .NET platform (not ASP.NET Core).
- Stable, widely-used contracts.
- Lightweight with no runtime coupling.
- Used by non-web hosts (console apps, workers, Lambda functions).

### Why do null-objects have private constructors?

To enforce the singleton pattern. `AnonymousUser.Instance` is the only way to obtain an `AnonymousUser`. This prevents accidental creation of multiple instances and ensures identity equality checks (`ReferenceEquals`) work correctly.

### Can I override a null-object in DI?

Yes. DI uses last-registration-wins. Register your implementation after the null-object:

```csharp
services.AddSingleton<ICurrentUser>(AnonymousUser.Instance);       // Fallback
services.AddScoped<ICurrentUser, ClaimsPrincipalUserAccessor>();    // Wins at runtime
```

### Why is `ServiceCollectionDecorateExtensions` in Abstractions?

So that domain modules can use the decorator pattern (`services.Decorate<TService, TDecorator>()`) without referencing ASP.NET Core or `Pragmatic.Composition.Host`. The extension method only needs `IServiceCollection` (from `Microsoft.Extensions.DependencyInjection.Abstractions`), which is an allowed dependency.

### How do I find which module implements a specific interface?

[interfaces.md](/modules/abstractions/interfaces/) names the implementation next to each contract, with full member signatures — `ICurrentUser`, for one, is implemented by `ClaimsPrincipalUserAccessor` in `Pragmatic.Identity.AspNetCore`. The README is a summary by area and has no per-interface sections.

---

## Getting Help

- **GitHub Issues**: [github.com/pragmatic-design/Pragmatic.Design/issues](https://github.com/pragmatic-design/Pragmatic.Design/issues)
- **Interface Catalog**: See [interfaces.md](/modules/abstractions/interfaces/) for complete member-level signatures.
- **Design Principles**: See [design-principles.md](/modules/abstractions/design-principles/) for the full decision framework on what belongs in Abstractions.
- **Concepts**: See [concepts.md](/modules/abstractions/concepts/) for architecture, layer model, and interface catalog overview.
