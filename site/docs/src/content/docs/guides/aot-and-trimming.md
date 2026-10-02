---
title: "Native AOT & Trimming"
description: "The actual state of Native AOT / trimming compatibility across the Pragmatic.Design ecosystem, and what to expect."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/docs/howto/aot-and-trimming.md
sidebar:
  order: 6
---
The actual state of Native AOT / trimming compatibility across the Pragmatic.Design ecosystem, and what to expect.

## Context

Pragmatic follows the **Source-Generator-First** principle: topology and contracts are resolved at compile time, and the runtime avoids reflection. This makes the **core of the framework** (Actions, Endpoints, Persistence, Result, Mapping, Validation, Composition) essentially reflection-free: the code the SG generates is typed and static.

However **"AOT-ready" is not yet literally true for the whole ecosystem**. Some modules use .NET APIs that require reflection or runtime codegen, and the compiler flags them.

## The warnings you see

A `dotnet build` produces a sizeable number of `IL*` warnings:

| Warning | Meaning | Typical cause in Pragmatic |
|---|---|---|
| `IL2026` | `RequiresUnreferencedCode` — not trim-safe | `JsonSerializer` without a `JsonSerializerContext`; delegate mapping in SignalR endpoints |
| `IL3050` | `RequiresDynamicCode` — not AOT-safe | reflection-based `JsonSerializer`; SignalR proxies |
| `IL2091/IL2087/IL2067/IL2090` | generic parameter/argument without a `DynamicallyAccessedMembers` annotation | `AddSingleton<,>`/`AddScoped<,>`/`ActivatorUtilities.CreateInstance` on unannotated generic types |
| `NETSDK1210` | `IsAotCompatible` not supported for the TFM | analyzer/code-fixer projects (netstandard2.0) — **benign**: analyzers are never AOT-compiled |

## Affected modules

The warnings are not uniform. The areas with reflection-dependent code today:

- **`Pragmatic.Agent.Protocol`** — `JsonWireFormat` uses `JsonSerializer` without a source-gen context (`IL2026`/`IL3050`).
- **`Pragmatic.Logging`** — `PragmaticWindowsEventLogProvider` serializes to JSON through reflection (`IL2026`/`IL3050`).
- **`Pragmatic.Abstractions`, `Pragmatic.Storage`, `Pragmatic.Authorization`, `Pragmatic.Logging`** — DI extensions with unannotated generics (`IL2091` etc.) for `decorator`, `UseStorage<T>`, `AddPermissionProvider<T>`, `UseStorage` audit.

The **SG-driven core** (Actions/Endpoints/Persistence/Result/Mapping/Validation) does not produce this class of warning: the generated code is static.

## Full-AOT JSON: the generated context (opt-in)

The `IL2026`/`IL3050` class of warnings from JSON (message/job/event/saga payloads and HTTP responses) is **solvable**: the source generator can emit for you a `JsonSerializerContext` covering the assembly's boundary types, without reflection and **without a hand-written context**.

### How to enable it

Opt-in per assembly, in one of two ways:

```xml
<!-- .csproj of the boundary project that declares the types -->
<PropertyGroup>
  <PragmaticGenerateJsonContext>true</PragmaticGenerateJsonContext>
</PropertyGroup>
```

```csharp
// or through an assembly attribute
[assembly: Pragmatic.Serialization.PragmaticGenerateJsonContext]
```

`<PublishAot>true</>` enables the feature automatically (zero cost for those who do not use AOT). The SG emits `PragmaticJsonContext` in the assembly and registers it in the `PragmaticJsonOptions` seam, and the host aggregates and activates it. Registration happens **per module** (the shapes need the source symbols, visible only in the assembly that declares the type): enable the opt-in on every boundary project; the host collects the contexts through metadata.

### What it covers

- **Asynchronous boundaries**: `[MessageHandler]` payloads, `[Job]`/`[RecurringJob]` parameters, `[EventHandler]` payloads, **sagas** (`[Saga<TState>]` + `[CompensateWith<T>]` compensation).
- **HTTP boundaries**: `[MapFrom]`/`[MapTo]` mapping DTOs (request/response).
- **Shapes**: flat and nested objects (transitive closure), primitives/BCL/enums, `Nullable<T>`, `List<T>`/arrays/`Dictionary<K,V>`, **polymorphism** (`[JsonPolymorphic]`/`[JsonDerivedType]`), **positional `record`s** and **`init`-only** properties. The latter use the `[UnsafeAccessor]` technique (AOT-safe, reflection-free): `init` setters and record constructors are invoked through statically resolved accessors, which C# cannot express directly but ILC compiles without reflection.

### What it does NOT cover (yet)

These are **deferred** (the `PRAG2800` analyzer flags them; you can add them to a `[JsonSerializable]` context of your own, which STJ supports):

- **`record`/`init`-only types that are `struct`s** — a boxed value type cannot be mutated in place through the accessors; use `class`/`record class`, or a context of your own.
- **Record constructors with validation that throws on default values** — construction happens with `default!` arguments before STJ sets the properties, so a guard in the primary constructor (e.g. `Ensure.ThrowIfNull`) would fail. Cover these with a `[JsonSerializable]` context of your own.
- **Raw returned entities, projections/FilterDtos, Patch DTOs** — they depend on resolving the Actions/boundary return type; phased.

Real end-to-end check: `examples/aot-smoke/Pragmatic.Aot.GeneratedContext` — a native publish with **0 IL warnings** that round-trips job params + DTOs with the reflection fallback disabled.

## What you can do today

- **Standard build (JIT)**: no impact. `IL*` warnings are not errors; the app runs normally.
- **`PublishTrimmed` apps**: trimming can remove types used through reflection in the modules above. Check with an end-to-end test. If there are problems, preserve the types with a `TrimmerRootDescriptor` or avoid the affected module.
- **Native AOT**: enable the generated context (above) on the boundary projects to clear the JSON warnings on the covered types. Only the peripheral paths remain non-AOT (`Agent.Protocol`, the Windows Event Log provider, and the deferred types above): isolate them in a non-AOT process or provide a `[JsonSerializable]` context of your own.

## Known limitations (today)

The framework is **reflection-free in the SG-driven core**; end-to-end AOT is not guaranteed because of a few peripheral paths:

1. **`IL2091` and related warnings** — they come from the generic parameters of some DI extensions. They are not errors and do not affect the standard JIT build/run.
2. **`IL2026`/`IL3050` from JSON** — solvable on boundary types by covering them with the **generated context** (`PragmaticGenerateJsonContext`, see above). They remain on paths not yet covered (e.g. `Agent.Protocol`, the Windows Event Log provider) and on the deferred types (record/init-only): provide a `[JsonSerializable]` context of your own (guided by `PRAG2800`) or isolate the path.

In short: **reflection-free in the core**; full AOT is reachable on the JSON boundaries with the generated context, not yet guaranteed for the peripheral JSON paths (`Agent.Protocol`) and for the deferred types.

## Summary

| Scenario | Status |
|---|---|
| Standard JIT build/run | ✅ fully supported |
| `PublishTrimmed` | ⚠️ check E2E; some modules use reflection |
| JSON boundaries with the generated context (opt-in) | ✅ AOT-clean on the covered types (message/job/event/saga/mapping DTO) |
| End-to-end Native AOT | ⚠️ reachable by covering the JSON boundaries; SignalR + deferred types remain |
| SG-driven core (Actions/Persistence/...) | ✅ the generated code and what consumes it; the «the SG registered nothing» fallbacks are reflective and annotated |
