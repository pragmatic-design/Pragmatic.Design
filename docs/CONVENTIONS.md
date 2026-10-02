# Conventions

The rules a change to this repository follows. [AGENTS.md](../AGENTS.md) covers the build, the layout
and the invariants; this is the detail you need once you are inside a module. The gate
(`node scripts/check.mjs`, see [TESTING.md](TESTING.md)) enforces what can be measured; the rest is
reviewed.

## Code

### One type per file

Every file holds one class, struct, record, interface or enum, and the namespace follows the folder.
Files under `shared/` are linked into several projects and carry `// ReSharper disable once CheckNamespace`.

### Size

A file over 400 lines has more than one responsibility. Split it — into `partial` files by category when
the responsibility is one, into separate types when it is not — by responsibility, never to hit the number.

### C# 14

- New locks use `System.Threading.Lock`, never `object` + `lock` — except in generator code (below).
- A setter with a guard uses the `field` keyword, not a single-use backing field.
- Collection expressions (`[]`, `[x, ..y]`) over `Array.Empty<T>()` and `new List<T> { }`, unless a
  concrete `List<T>` or `T[]` is part of a public contract.
- Primary constructors only for plain assignment. A constructor that validates or transforms keeps its
  body, because a guard has nowhere to go in a primary constructor.

### No `[Obsolete]` before 1.0

An API is not deprecated, it is removed, and its callers are updated in the same change. `[Obsolete]`
protects consumers of a released API; before 1.0 there are none, and a deprecation only doubles the
surface the generator has to handle.

### Comments

In English. A comment says **why**: a constraint that is not obvious, a deviation from the idiomatic
solution, an invariant the type system cannot express. It never restates the code, and never tells the
history of a change — that is what commits are for. No commented-out code.

## Principles

### Result over exceptions

A business failure is a value: `Result<User, NotFoundError>`, not `throw new NotFoundException()`.

### Ensure for guards

`Ensure.ThrowIfNull(repository)`, not `repository ?? throw new ArgumentNullException(...)`.

### Decide at compile time

When a decision can be made at compile time, the generator makes it and emits the line that follows
from it. It does not emit a lookup that decides at run time:

| Generated code that looks | Generated code that knows |
|---|---|
| `if (x is ISyncValidator v) v.Validate();` | `x.Validate();` — or nothing, when the type will not have one |
| a speculative `GetService<IAsyncValidator<T>>()` | the call to the validator known to be registered |
| `typeof(T).GetProperty(name)` | the generated accessor |

A branch that is never taken is indistinguishable from one that works. A generator that runs on a module
sees that module only; the **host** sees every referenced assembly. So every generated feature has the
same shape:

1. **The module declares** — metadata, lists and assembly attributes that are true of it on its own.
2. **The level above composes** — the host reads those declarations and generates the final pieces:
   registrations, dispatchers, checks.
3. **What one generator needs from another passes through the pipeline**, never through a lookup that
   answers "no" because the other output does not exist yet.

Recursive composition at run time — `[typeof(Child), ..Child.NestedOperations]` — is legitimate: the same
generator writes both in the same compilation.

### Reflection

The generated pipelines do not reflect. The runtime keeps a few reflective fallbacks, some on by default:
the JSON resolver chain in `PragmaticJsonOptions` (switched off with `DisableReflectionFallback()`), the
template data accessor, the host topology scan when the generator did not register, and a handful of
opt-in paths (assembly scanning, the migrations driver lookup, the grid adapters). The gate counts
reflective calls in runtime source and the number may only go down.

New code does not add `GetProperty`, `GetMethods`, `MakeGenericType`, `Activator.CreateInstance` or
`GetCustomAttributes`. Where reflection is unavoidable — discovering what was not named at compile time —
it is annotated with `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`, so the requirement reaches the
caller.

### Hot paths and observability

`readonly struct` for value types, `Span<T>` for buffers, no LINQ on a hot path. Logging goes through
`[LoggerMessage]`, never an interpolated string. Activities are named `{Module}.{Operation}`.

## Source generators

### One generator

All generation lives in `Pragmatic.SourceGenerator`, entry point `PragmaticSourceGenerator.cs`. Features
are detected by `FeatureDetector` (`GetTypeByMetadataName` on the referenced assemblies). A few
generators stand alone on purpose: Result, the Country/Currency/Language lists, Logging, the typed client
(its input is an API manifest) and Testing (it runs in the test project).

Each feature has `Models/`, `Templates/` and `Transforms/`. The contributor documentation is
[`Pragmatic.SourceGenerator/docs/`](../Pragmatic.SourceGenerator/docs/).

### Generators emit through CSharpTemplate

Every output is a class in `Templates/` deriving from `CSharpTemplate` (`shared/SourceGen/`), rendering an
immutable model from `Models/`. No `StringBuilder`, no string concatenation, no output written outside
`Templates/`.

- `IIncrementalGenerator`, never `ISourceGenerator`; `ForAttributeWithMetadataName` for triggers.
- Hint names go through `VirtualFolderHints` and always include the namespace: two types with the same
  simple name in different namespaces would otherwise produce the same hint, and Roslyn answers a
  duplicate hint by discarding the generator's entire output behind a warning.
- Names derived from a type name go through `NamingHelper.AppendSuffix`, so `UpdateAmenityMutation` does
  not become `UpdateAmenityMutationMutationInvoker`.
- A generated `.g.cs` holds at most one `partial` class and one nested class. Nested types the host
  references are `public`.
- Registration is one `_Infra.{Category}.Registration.g.cs` per assembly, exposing
  `Add{Prefix}{Feature}()` on `IServiceCollection`.

### netstandard2.0 in the generator

The generator and `shared/SourceGen/` compile for `netstandard2.0`; the code they emit runs on `net10.0`
and has no such limit.

| In generator code | |
|---|---|
| Allowed | `field`, extension members, primary constructors, collection expressions, partial members, null-conditional assignment, target-typed `new`, `init`/`required` (polyfilled) |
| Not available | `System.Threading.Lock` (use `lock(object)`), `params ReadOnlySpan<T>`, inline arrays, `System.Index` |
| Only with a polyfill | `[CollectionBuilder]` (present), `[OverloadResolutionPriority]` |

Models flowing through the incremental pipeline are value-equatable: collections are `EquatableArray<T>`,
never a raw `ImmutableArray<T>`, whose equality is by reference and breaks caching.

### Configuration in three tiers

Topology is decided at compile time by the generator; a module's strategy is chosen in `Program.cs`
through `IPragmaticBuilder` (`PragmaticBuilder{Module}Extensions.Use{Module}()`); business wiring —
services, filters, the HTTP pipeline — goes in `IStartupStep`s, ordered by `Order`.

## Modules

```
Pragmatic.{Module}/
├── Pragmatic.{Module}.slnx
├── README.md
├── src/Pragmatic.{Module}/
├── tests/Pragmatic.{Module}.Tests/
└── samples/Pragmatic.{Module}.Samples/
```

A new module also needs its `.slnx`, generator tests with the snapshot scrubber below, a README with a
quick start, and — when its strategy is configurable — a `PragmaticBuilder{Module}Extensions` and a flag
in `FeatureDetector`/`DetectedFeatures`.

### Layers

```
Layer 0 (Foundation)     Layer 1 (Capabilities)     Layer 2 (Integration)
├── Abstractions         ├── Validation             ├── Actions
├── Result               ├── Mapping                ├── Endpoints
├── Ensure               ├── Specification          ├── Persistence + EFCore
                         ├── I18n                   ├── Events + EFCore
                         ├── Caching                ├── Composition + Host
                         ├── Resilience             ├── Authorization
                         ├── Configuration          ├── MultiTenancy
                         └── Temporal               └── Logging
```

A module depends only on its own layer or below.

## Testing

- Every public method has a unit test; every generator output has a Verify snapshot; a feature has at
  least one end-to-end test in the Showcase, over HTTP against a real database.
- Generator tests go through `GeneratorTestHelper` (`shared/SourceGen/Testing/`) behind a thin per-module
  base class that supplies the module's references.
- Every test project using Verify registers the version scrubber in its `ModuleInitializer`, so a
  snapshot does not change with the package version.
- Test names read `{Method}_{Scenario}_{Expected}`, or as a sentence that states the behaviour.

## Naming

| Kind | Pattern |
|---|---|
| Types, methods, constants | `PascalCase` |
| Parameters | `camelCase` |
| Private fields | `_camelCase` |
| Attributes | `{Name}Attribute`, and **generic**: `[MapFrom<Order>]`, never `[MapFrom(typeof(Order))]` |
| Errors | `{What}Error` |
| Builder extension | `PragmaticBuilder{Module}Extensions.Use{Module}()` |
| DI extension | `{Prefix}{Feature}Extensions.Add{Prefix}{Feature}()` |

### Diagnostic IDs

Every diagnostic is `PRAG####`, and each module owns a range: the range belongs to the module that
**emits** the diagnostic, not to the one its subject belongs to. The ranges, and every ID in use with the
next free one, are in [diagnostics.md](diagnostics.md), generated from the descriptors.

Before claiming an ID, search the whole repository: some diagnostics are emitted by standalone analyzers
(`Pragmatic.{Module}.Analyzers`), by the companion `Pragmatic.SourceGenerator.Analyzers`, or by
`Pragmatic.Documents.Csv.Generator`. A retired ID is never reused, because a reused ID silently changes
what someone's suppression means.

## Git

History is linear: `git pull --rebase`, `git rebase main`, squash and merge. Never a merge commit.
