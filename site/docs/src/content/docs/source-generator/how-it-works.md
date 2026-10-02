---
title: How It Works
description: The unified Pragmatic Source Generator — detection, transform, template, emit.
---

Pragmatic Design is built around a **unified** source generator — `Pragmatic.SourceGenerator` — **plus a few standalone generators** that live with the modules they serve. Every one of them is an `IIncrementalGenerator`, targets `netstandard2.0`, and ships as a NuGet analyzer reference (no runtime code is shipped by the generator itself).

| Generator | Assembly | Role |
|-----------|----------|------|
| **Unified** `PragmaticSourceGenerator` | `Pragmatic.SourceGenerator` | Detects referenced modules and runs 31 feature pipelines |
| **Result** `ResultSourceGenerator` | `Pragmatic.Result.SourceGenerator` | `Result`/`VoidResult` variants |
| **I18n codes** `Country`/`Currency`/`Language` generators | `Pragmatic.Internationalization.SourceGenerator` | Static ISO code tables from embedded JSON |
| **CSV** `CsvSourceGenerator` | `Pragmatic.Documents.Csv.Generator` | Typed CSV readers and writers |
| **Client** `PragmaticClientGenerator` | `Pragmatic.Client.SourceGenerator` | A typed HTTP client, from the API manifest rather than from your domain |
| **Testing** `ContractTestGenerator`, `MockGenerator`, `ComparerGenerator` | `Pragmatic.Testing.*.SourceGenerator` | Contract tests, mocks and comparers, generated in the **test** project |

These are **separate `[Generator]` assemblies** — not feature pipelines inside the unified generator. Client and Testing stay outside it because their input is not the module's compilation. The rest of this page describes the unified generator; see the [feature catalog](/source-generator/feature-catalog/) for the others.

At compile time the unified generator:

1. Detects **which Pragmatic modules are referenced** in your project (via assembly lookup)
2. Scans your **attributes and partial classes** to find declarations it can act on
3. Runs the **Transform** for each declaration to build an immutable **Model**
4. Hands the model to a **Template** (a subclass of `CSharpTemplate`) that emits C# source
5. Adds the emitted source to the compilation with a stable **hint name**

The generated wiring uses no runtime reflection or service discovery — it's emitted into your assembly at build time. (EF Core, where you use it, keeps its own runtime behaviour — the documented exception.)

## Pipeline

```
┌─ FeatureDetector ────────────┐
│  Scans referenced assemblies │
│  → DetectedFeatures flags    │
└─────────┬────────────────────┘
          │
          ▼
┌─ Syntax providers ──────────────────────┐
│  ForAttributeWithMetadataName           │
│  per feature (Entity, Action, Endpoint, │
│  Query, Handler, Job, …)                 │
└─────────┬────────────────────────────────┘
          │  (per-declaration)
          ▼
┌─ Transform ──────────────────┐   (reads compilation,
│  Symbol → immutable Model    │    emits record values)
│  (records, equatable)        │
└─────────┬────────────────────┘
          │
          ▼
┌─ Compositions (cross-feature) ┐   (e.g. Persistence enriches
│  Enrich models with info      │    Action model with repo info)
│  from other features          │
└─────────┬─────────────────────┘
          │
          ▼
┌─ Template (CSharpTemplate) ──┐   (single responsibility:
│  Model → C# source           │    take model, render)
└─────────┬────────────────────┘
          │
          ▼
┌─ Emit ───────────────────────┐
│  AddSource(hintName, code)   │
└──────────────────────────────┘
```

## Three rules behind the whole architecture

### 1. `IIncrementalGenerator`, not `ISourceGenerator`

The old API re-runs on every keystroke; the incremental API caches per-declaration state and only re-runs what changed. Pragmatic commits to incremental — every pipeline step ends with a **record** type (equatable by value) so the framework can skip unchanged inputs.

### 2. `ForAttributeWithMetadataName`, not `SyntaxProvider.CreateSyntaxProvider`

Attribute-targeted declarations are scanned **once per attribute name** instead of walking the syntax tree. Pragmatic uses this everywhere it can.

### 3. `CSharpTemplate`, not `StringBuilder`

All code generation goes through the shared `CSharpTemplate` base class in `shared/SourceGen/`. It handles:
- indentation and formatting
- namespace / using directives
- class / method / switch / lambda helpers
- access modifiers and attribute lists

A per-feature `Template` subclass just defines `RenderFile()` and emits a well-formed `.g.cs` artifact. No string concatenation, no `StringBuilder` drift.

## What lives where

```
Pragmatic.SourceGenerator/
├── PragmaticSourceGenerator.cs          ← entry point
├── Core/
│   ├── FeatureDetector.cs               ← scans referenced DLLs
│   └── DetectedFeatures.cs              ← flags (HasMessaging, HasJobs, …)
└── Features/
    ├── Actions/
    │   ├── Models/         ← immutable record models
    │   ├── Transforms/     ← Symbol → Model
    │   └── Templates/      ← Model → C# source
    ├── Endpoints/
    ├── Persistence/        ← orchestrator → 6 sub-features
    ├── Messaging/
    ├── Jobs/
    └── …                   ← 31 feature folders
```

`PragmaticSourceGenerator.Initialize` registers these feature pipelines (see `PragmaticSourceGenerator.cs`), in this order:

> Resource · Traits · Migrations · Caching · Persistence (orchestrator → EntityCore, Repository, Query, Projection, Advanced, DbContext) · Identity · Actions · Patch · Redaction · Resilience · FeatureFlags · Documents · Validation · Mapping · Endpoints · Result · I18n · Read contracts · Roll-ups · Configuration · Messaging · Serialization · Temporal · Privacy · FastEnum · Jobs · Composition · Lifecycle events · ValueObject · Specification · Glossary (glossary, architecture, AsyncAPI, use-case catalogue)

The order matters: a pipeline whose output another one needs is registered first, and the value passes through the pipeline rather than being looked up. The **Manifest** is not registered separately — it runs inline from `EndpointsFeature`. `Resource` and `Traits` feed their models into the `Actions`, `Endpoints` and `Persistence.Query` pipelines rather than emitting independently. `FastEnum`, `Jobs`, `ValueObject` and the glossary documents are registered **unconditionally** — their attributes live in lightweight packages.

Each feature is self-contained. Adding a new feature means:
1. Add a flag in `DetectedFeatures`
2. Add a probe in `FeatureDetector`
3. Create `Features/{Feature}/` with Models/Transforms/Templates
4. Register the pipeline in `PragmaticSourceGenerator.Initialize`

See [`feature-detection`](/source-generator/feature-detection/) for how detection works in practice.

## Hint names

Every generated file has a deterministic **hint name** that the IDE exposes under `Dependencies / Analyzers / Pragmatic.SourceGenerator`. Naming is dot-separated and prefixed with category markers:

| Pattern | When | Helper |
|---------|------|--------|
| `{Type}.{Artifact}.g.cs` | Per-type output (invoker, repository, factory) | `VirtualFolderHints.ForType()` |
| `_Boundary.{Boundary}.{Artifact}.g.cs` | Per-boundary (endpoint handler aggregates) | `VirtualFolderHints.ForBoundary()` |
| `_Infra.{Category}.{Extension}.g.cs` | Per-assembly infrastructure (DI registration) | `VirtualFolderHints.ForAssembly()` |
| `_Metadata.{Category}.g.cs` | Host metadata aggregation | `VirtualFolderHints.ForMetadata()` |
| `EntityConfig.{Namespace}.{Entity}.g.cs` | Host-level EF Core entity configuration | `VirtualFolderHints.ForEntityConfig()` |
| `DbContext.{Boundary}.g.cs` | Host-level DbContext per boundary | `VirtualFolderHints.ForDbContext()` |
| `Endpoint.{Action}.{Boundary}.g.cs` | Host-level endpoint registration | direct |

The `_` prefix pushes infrastructure files to the bottom of the sorted list, so per-type outputs appear first.

## How to inspect the generated code

Enable emission to disk in your `.csproj`:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

After `dotnet build`, inspect `obj/Debug/net10.0/generated/Pragmatic.SourceGenerator/Pragmatic.SourceGenerator.PragmaticSourceGenerator/`. `obj/` keeps files from earlier compilations, so rebuild clean before counting or reading what is there.

## Authoring a generator (contributor note)

Every template subclasses the shared `CSharpTemplate` (`shared/SourceGen/CSharpTemplate*.cs`, a `partial` class split across Core / ControlFlow / Types / Members). The minimal skeleton:

```csharp
internal sealed class MyFeatureTemplate(MyModel model) : CSharpTemplate
{
    // file name + content — use VirtualFolderHints for the hint name
    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(model.TypeName, "MyFeature"), ToSourceText());

    // skip emission if the model is unusable
    protected override bool Validate() => model.IsValid;

    // the actual rendering — AppendNamespace, Class(...), Method(...), etc.
    public override void RenderFile() { /* build the file */ }
}
```

A few rules that keep the incremental cache correct:

- **No symbols in models.** `Transforms` turn `ISymbol` into plain immutable records; never store `ISymbol`/`Compilation` on a model, or caching breaks (and you leak Roslyn objects).
- **Use `EquatableArray<T>` for collections.** Plain `ImmutableArray<T>` is reference-equal, which defeats value-based incremental caching.
- **Derive names with `NamingHelper.AppendSuffix`** (`shared/SourceGen/NamingHelper.cs`) instead of `$"{TypeName}Invoker"` — it dedupes the suffix so `CancelReservationMutation` + `MutationInvoker` doesn't become `…MutationMutationInvoker`.
- **Always route hint names through `VirtualFolderHints`** — never hardcode `.g.cs` names — so files land in the right virtual folder and sort correctly.

## Reading more

- [Feature catalog](/source-generator/feature-catalog/) — every pipeline, its trigger, and what it generates
- [`Pragmatic.SourceGenerator/docs/feature-development.md`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/docs/feature-development.md) — how to add a new feature
- [`shared/SourceGen/README.md`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/shared/SourceGen/README.md) — `CSharpTemplate` API
- [`shared/SourceGen/Testing/README.md`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/shared/SourceGen/Testing/README.md) — how to write Verify-based tests for generator output
