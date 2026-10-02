# Pragmatic.SourceGenerator -- Internal Architecture

This document describes the internal architecture of the unified source generator. It is intended for contributors adding new features or modifying existing generation pipelines.

## Design Principles

1. **One generator, all features.** A single `IIncrementalGenerator` (`PragmaticSourceGenerator`) powers the entire Pragmatic ecosystem. Features are activated at compile time based on which runtime packages are referenced.

2. **Incremental and cacheable.** All transforms produce immutable record models that enable Roslyn's incremental caching. When a source file changes, only affected pipelines re-execute.

3. **Zero NuGet dependencies at generation time.** The generator references only the Roslyn SDK and shared code linked from `shared/SourceGen/`. No NuGet packages are consumed -- only linked source files.

4. **Template-based output.** All generated C# code is produced via `CSharpTemplate` subclasses. Raw `StringBuilder` usage is prohibited.

---

## Core Infrastructure

### FeatureDetector

`Core/FeatureDetector.cs` -- a static class called once per compilation:

```csharp
internal static class FeatureDetector
{
    public static DetectedFeatures Detect(Compilation compilation) => new()
    {
        HasActions = TypeExists(compilation, "Pragmatic.Actions.Attributes.DomainActionAttribute"),
        HasValidation = TypeExists(compilation, "Pragmatic.Validation.Attributes.ValidationAttribute"),
        HasCaching = TypeExists(compilation, "Pragmatic.Caching.Attributes.CacheableAttribute"),
        HasMapping = TypeExists(compilation, "Pragmatic.Mapping.Attributes.MapFromAttribute`1"),
        // ... 37 Has* checks in total
        IsHostMode = CompositionDetector.IsHostProject(compilation),
        IsHostCompositionMode = CompositionDetector.IsHostProject(compilation)
            && CompositionDetector.IsCompositionHostReferenced(compilation),
        EfCoreProvider = DetectEfCoreProvider(compilation),
    };

    private static bool TypeExists(Compilation compilation, string fullyQualifiedName)
        => compilation.GetTypeByMetadataName(fullyQualifiedName) is not null;
}
```

**FQN pitfalls:**
- Generic types require backtick-arity: `MapFromAttribute\`1`, not `MapFromAttribute<T>`.
- Attributes in `.Attributes` sub-namespaces use the full path.
- Exception: `HasPersistenceEFCore` checks `Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute` (root namespace).

Detection granularity is **per assembly, not per module**. Temporal contributes three flags (`HasTemporal`, `HasTemporalJson`, `HasTemporalAspNetCore`) and Messaging seven, because a consumer can reference any subset and generating registration code for an absent assembly produces CS0246 in their build.

### DetectedFeatures

`Core/DetectedFeatures.cs` -- an immutable `sealed record` carrying **37 `Has*` boolean flags**, two mode flags, and the provider enum:

```csharp
internal sealed record DetectedFeatures
{
    public bool HasActions { get; init; }
    public bool HasValidation { get; init; }
    // ... 35 more Has* flags

    /// <summary>True when the compilation has an entry point (executable project).</summary>
    public bool IsHostMode { get; init; }

    /// <summary>Executable AND references Pragmatic.Composition.Host.</summary>
    public bool IsHostCompositionMode { get; init; }

    public EfCoreProvider EfCoreProvider { get; init; }
    public static DetectedFeatures None { get; } = new();
}
```

The `None` sentinel is used in tests and fallback scenarios.

### IsHostMode vs IsHostCompositionMode

These are two different questions and features gate on different ones. Getting them confused produces artifacts in projects that cannot compile them.

| Flag | Condition | Meaning |
|------|-----------|---------|
| `IsHostMode` | `CompositionDetector.IsHostProject(compilation)` — `OutputKind` is `ConsoleApplication` or `WindowsApplication` **and** `GetEntryPoint()` is non-null | "This project is an executable." Nothing more. A console tool that references only `Pragmatic.Persistence` sets this. |
| `IsHostCompositionMode` | `IsHostProject(compilation)` **and** `IsCompositionHostReferenced(compilation)` | "This project is a full Pragmatic host." Only then does the Composition feature emit `PragmaticHost.Services.g.cs` with its aggregated DI wiring. |

The narrower flag is the one that gates host aggregation. Note that a test project can satisfy `IsHostMode`, which is why `CompositionDetector` also exposes `IsTestProject` and a `GeneratorMode` (`Library` / `Host` / `Skip`) that folds all three checks together.

### EfCoreProvider

`Core/EfCoreProvider.cs` -- an enum with 4 values: `Generic`, `PostgreSql`, `SqlServer`, `Sqlite`. Detection priority: PostgreSQL > SQL Server > SQLite > Generic.

### TraitPropertyResolver

`Core/TraitPropertyResolver.cs` -- resolves "virtual" properties that `EntityTraitsTemplate` will generate for entities marked with `[Entity]`. Other features (Mapping, Endpoints) call `GetTraitProperties()` to discover properties like `Id`, `PersistenceId`, `CreatedAt`, `IsDeleted` that are not yet visible on the `INamedTypeSymbol` during the same generator pass.

Key behaviors:
- Only applies to types with `[Entity]` attribute.
- Skips properties already declared manually on the type.
- Detects `[Auditable]` and `[SoftDelete]` attributes via `TraitDetector.Detect()`.

---

## Generation Pipeline

### Three-Step Pattern

Every generated file follows:

```
[Roslyn Symbols] --> Transform --> [Immutable Model] --> Template --> [C# Source]
```

1. **Transform** (`Transforms/*.cs`) -- Extracts data from `INamedTypeSymbol`, `IPropertySymbol`, and `AttributeData` into an immutable model. Must be deterministic and side-effect-free for incremental caching.

2. **Model** (`Models/*.cs`) -- An immutable `record` containing all data the template needs. Models are compared by value for incremental caching, so all properties must participate in equality. Common pattern: `IsValid` flag for validation-gated generation.

3. **Template** (`Templates/*.cs`) -- Extends `CSharpTemplate`. Implements `RenderOutput()` (returns `Artifact` with hint name + source text) and `RenderFile()` (renders the actual content using the structured API).

### Feature Registration

Each feature provides a `static Register(...)` method that:

1. Creates `ForAttributeWithMetadataName` providers with a transform callback.
2. Filters by validity (`Where(m => m.IsValid)`).
3. Combines with the `DetectedFeatures` provider for feature gating.
4. Registers source-output callbacks that instantiate templates and emit artifacts.

```csharp
var cacheableProvider = context.SyntaxProvider
    .ForAttributeWithMetadataName(
        AttributeNames.Cacheable,
        GeneratorHelpers.IsClassOrRecord,
        CachingTransform.TransformCacheable)
    .Where(m => m is not null);

context.RegisterSourceOutputSafe(
    cacheableProvider.Combine(features).Where(x => x.Right.HasCaching),
    static (ctx, x) => GenerateCacheable(ctx, x.Left!));
```

A feature may also *return* a provider so a later feature can consume it — `PersistenceFeature`, `ResourceFeature`, `TraitFeature`, and `EndpointsFeature` all do, and `Initialize` threads their outputs into downstream registrations.

### Crash Isolation: RegisterSourceOutputSafe

Every one of the ~130 output registrations in the generator uses `RegisterSourceOutputSafe`, never `context.RegisterSourceOutput`. This is not optional.

The Pragmatic ecosystem is served by a **single** `IIncrementalGenerator`. Roslyn does not isolate its output registrations from each other: an unhandled exception in any one transform or template surfaces as a single CS8785 ("Generator failed to generate source") and suppresses the output of **every** feature in the compilation. One malformed attribute argument on one entity takes out repositories, endpoints, DI registration, and host wiring at once — and the error names the generator, not the feature.

`shared/SourceGen/SafeSourceOutput.cs` provides a drop-in extension on `IncrementalGeneratorInitializationContext`, with overloads for both `IncrementalValueProvider<T>` and `IncrementalValuesProvider<T>`. It wraps the callback, reports the failure as **PRAG9000**, and lets the other outputs proceed.

PRAG9000 is a **`DiagnosticSeverity.Error`**. A failed output means generated code the consumer's source refers to is simply not there; reporting that as a warning would let the build run on into a cascade of CS0246s whose real cause is buried in warning output. The failure has to surface where it happens.

`OperationCanceledException`, `OutOfMemoryException`, and `StackOverflowException` pass through uncaught — cancellation must propagate for the IDE to stay responsive, and the other two are not recoverable.

### Single Emission Point: SourceOutput.AddSource

```csharp
ctx.AddSource(artifact);          // correct
```

`shared/SourceGen/SourceOutput.cs` extends `SourceProductionContext` with an `AddSource(Artifact)` overload that checks `artifact.IsEmpty` and skips the file. A template whose `Validate()` returns `false` renders to *empty* content rather than to nothing — that is how every template says "there is nothing to generate here" — so the raw two-argument call would write a zero-byte `.g.cs` into the compilation. Harmless to compile, but it pollutes the generated output and erases the distinction between "deliberately nothing" and "the template silently produced nothing". With roughly 200 emission sites, that decision belongs in one place.

### Aggregate Pipelines

Some features need all models to generate aggregate artifacts (e.g., DI registration, metadata). These use `.Collect()` to gather all models into an `ImmutableArray<T>`, then register a single output:

```csharp
var allActions = validActions.Collect();
context.RegisterSourceOutputSafe(allActions, GenerateActionsRegistration);
```

`.Collect()` produces `IncrementalValueProvider<ImmutableArray<T>>` and that is the **one place a raw `ImmutableArray<T>` is correct** — it is pipeline plumbing that Roslyn compares elementwise itself, not a cached model field. See the next section.

### Cacheable Models: EquatableArray

Every collection field on a model that flows through the pipeline must be `EquatableArray<T>` (`shared/SourceGen/EquatableArray.cs`), and every map `EquatableDictionary<TKey, TValue>`.

`ImmutableArray<T>` is a readonly struct over a `T[]` whose `Equals` compares the array **reference**, not the contents. A transform allocates a fresh array on every run, so a record holding one never compares equal to its predecessor. The compiler-generated record `Equals` returns `false`, the stage re-runs, the template re-renders — on every keystroke. Nothing fails, the IDE just degrades. `ImmutableDictionary<TKey, TValue>` has the identical defect.

`EquatableArray<T>` walks the elements with `EqualityComparer<T>.Default`. The conversion is free at the call sites: there is an implicit conversion from `ImmutableArray<T>` (so transforms need no edit), `[CollectionBuilder]` makes collection expressions work, `.AsImmutableArray()` unwraps where an `ImmutableArray`-specific API is needed, and the struct is an `IReadOnlyList<T>` for reading.

### Cacheable Positions: LocationInfo

Models carry `LocationInfo?` (`Core/LocationInfo.cs`), never a Roslyn `Location`.

A `Location` references its `SyntaxTree`, and a `SyntaxTree` belongs to one `Compilation`. A cached model outlives the compilation it was built from, so reporting from a stored `Location` means reporting a diagnostic whose tree is not part of the **current** compilation. Roslyn's suppression filtering then throws "SyntaxTree is not part of the compilation", killing source generation, classification, and CodeLens in the IDE. The CLI never notices — one run, fresh trees — so this reproduces only for the people using the IDE.

`LocationInfo` captures the file path plus both spans as plain values and rebuilds a location on demand. It is deliberately excluded from equality (`Equals` always `true`, `GetHashCode` `0`): a position never changes the generated output, and including it would make the owning model unequal on every re-parse.

Two rebuild overloads, and the difference matters:

| Call | Result |
|------|--------|
| `ToLocation()` | Tree-free `Location`. Right file and line, but `#pragma warning disable` cannot suppress it (no tree). |
| `ToLocation(compilation)` | Rebinds to the current compilation's tree for the same file, so source suppressions apply. Falls back to the tree-free form when the file is gone or the span no longer fits mid-edit. |

`LocationInfo.From` returns `null` when the syntax tree's `FilePath` is empty. This is a live trap in tests: a diagnostic guarded on `if (model.Location is null) return;` silently disappears when the test compilation was parsed without a path. `GeneratorTestHelper.RunGenerator` supplies one.

---

## CSharpTemplate API

The `CSharpTemplate` base class (`shared/SourceGen/CSharpTemplate*.cs`) provides a structured API for generating C# code. It is a partial class split across 4 files.

### Abstract Members

| Member | Purpose |
|--------|---------|
| `RenderOutput()` | Returns `Artifact` (hint name + source text). Entry point for generation. |
| `RenderFile()` | Renders the file body. Called by `ToString()` after validation. |
| `Validate()` | Optional override. Return `false` to skip generation. |
| `GeneratorName` | Optional. Adds `// Generated by {name}` header comment. |
| `SourceInfo` | Optional. Adds `// Source: {info}` header comment. |
| `TriggerInfo` | Optional. Adds `// Trigger: {info}` header comment. |

### Core Writing

| Method | Signature | Notes |
|--------|-----------|-------|
| `Append` | `void Append(string text)` | Writes without newline |
| `AppendLine` | `void AppendLine(string line = "")` | Writes with newline |
| `IncreaseIndent` | `void IncreaseIndent()` | Increases indent level |
| `DecreaseIndent` | `void DecreaseIndent()` | Decreases indent level |

### Usings and Namespace

| Method | Purpose |
|--------|---------|
| `AddUsing(namespace)` | Adds a `using` directive (deduplicated) |
| `AddUsings(params namespaces)` | Adds multiple usings |
| `AddAlias(name, definition)` | Adds a `using Alias = Type;` directive |
| `AppendNamespace(namespace)` | Writes `namespace X;` |

### Type Declarations

| Method | Parameters |
|--------|-----------|
| `Class(name, body, baseType?, interfaces?, access, modifiers)` | `ClassModifiers` has `Partial`, `Abstract`, `Sealed`, `IsStatic`, `IsReadOnly` |
| `Struct(name, body, interfaces?, access, modifiers)` | Same modifiers |
| `Record(name, body, parameters?, interfaces?, access, modifiers)` | Primary constructor parameters |
| `RecordStruct(name, body, parameters?, interfaces?, access, modifiers)` | Same as Record |

### Members

| Method | Purpose |
|--------|---------|
| `Field(name, type, access, isReadOnly, isStatic, initializer)` | Field declaration |
| `Property(name, type, access, isStatic, isReadOnly, defaultValue, getBody, setBody)` | Auto or full property |
| `ExpressionProperty(name, type, expression, access, isStatic)` | Expression-bodied property |
| `Method(name, body, returnType, parameters, access, modifiers)` | `MethodModifiers` has `IsStatic`, `IsVirtual`, `IsOverride`, `IsAbstract`, `IsSealed`, `IsAsync` |
| `ExpressionMethod(name, expression, returnType, parameters, access, modifiers)` | Expression-bodied method |
| `Constructor(name, body, parameters, access, baseArgs)` | Constructor declaration |

### Control Flow

| Method | Purpose |
|--------|---------|
| `If(condition, body)` | `if` statement |
| `ElseIf(condition, body)` | `else if` |
| `Else(body)` | `else` |
| `Switch(expression, body)` | `switch` statement |
| `Case(pattern, body)` | `case` label |
| `Default(body)` | `default` label |
| `Block(body, modifier?)` | Braced block with optional modifier |
| `Break()`, `Continue()` | Loop control |

### Comments and Documentation

| Method | Purpose |
|--------|---------|
| `Comment(text)` | `// text` |
| `XmlSummary(text)` | `/// <summary>` block |
| `XmlParam(name, description)` | `/// <param>` tag |
| `XmlReturns(description)` | `/// <returns>` tag |
| `XmlInheritDoc()` | `/// <inheritdoc/>` |

### Supporting Types

```csharp
// Method parameters
new MethodParameter("string", "name", nullable: false)
{
    DefaultValue = "\"default\"",
    IsExtension = true,
    Attribute = "FromBody"
}

// Class modifiers
new ClassModifiers { Partial = true, Sealed = true }

// Method modifiers
new MethodModifiers { IsAsync = true, IsOverride = true }
```

---

## Persistence Feature Deep Dive

The Persistence feature is the most complex. `PersistenceFeature.Register` is an orchestrator that owns the shared entity providers and delegates to **six** sub-features, plus two inline outputs of its own. It returns `allEntitiesProvider` so downstream features (Actions, in particular) can consume the entity models:

```
PersistenceFeature.Register()  -> IncrementalValueProvider<ImmutableArray<EntityMetadataModel>>
├── Shared providers: referencedEntityProvider, currentEntityProvider, allEntitiesProvider
├── EntityCoreFeature.Register()      -- per-entity: relations, create, traits, setters, diagnostics
├── RepositoryFeature.Register()      -- per-entity: repos, filters; aggregate: metadata
├── QueryFeature.Register()           -- per-attribute: query apply, grid filter, filter dto, patch
├── ProjectionFeature.Register()      -- per-entity: projectable, computed filters
├── AdvancedFeature.Register()        -- aggregate: inheritance, hierarchy, timeline, lookup
├── DbContextFeature.Register()       -- aggregate: boundary DbContext, migration DbContext
├── GenerateDerivedTypeSetters()      -- inline: TPH/TPT/TPC derived-type setters
└── GenerateEntityCrudPermissions()   -- inline: CRUD permission constants (HasPersistenceEFCore && HasAuthorization)
```

### Two Persistence features that are NOT sub-features

`Features/Persistence/` contains two more `*Feature.cs` files that `PersistenceFeature` does **not** call. Both are registered directly from `PragmaticSourceGenerator.Initialize()` and both take only `context` — no `DetectedFeatures` gate:

| Feature | Trigger | Output |
|---------|---------|--------|
| `ReadContractFeature` | `Pragmatic.Persistence.Query.Attributes.PublishedAttribute` | Groups `[Published]` queries by boundary; emits `I{Module}Reads` interface, implementation, and DI registration for acyclic cross-boundary reads |
| `RollUpFeature` | `Pragmatic.Persistence.Entity.RollUpAttribute\`1` on properties | Per-parent apply method, a per-assembly public entry point registering typed `RollUpRule`s, the `RollUpRules` metadata attribute that gets a host to call it, and a `RegisterRollUpRules` hook delegating there for hand-wired persistence |

If you are counting sub-features from the folder listing, this is the discrepancy: nine `*Feature.cs` files, one orchestrator, six delegated sub-features, two independently registered siblings.

### Boundary readers

Four `*BoundaryReader.cs` classes — `BatchProgressBoundaryReader`, `EventOutboxBoundaryReader`, `MessagingOutboxBoundaryReader`, `SagaPersistenceBoundaryReader` — share one shape:

```csharp
ReadEnabledBoundaries(Compilation, ImmutableArray<EntityMetadataModel>, CancellationToken)
    -> EquatableArray<string>
```

Each resolves a boundary marker attribute (`EnableOutboxAttribute`, `EnableEventOutboxAttribute`, `EnableBatchProgressAttribute`, `EnableSagaPersistenceAttribute`) through `Compilation.GetTypeByMetadataName` rather than `ForAttributeWithMetadataName`. That is deliberate and worth understanding before you copy the pattern: in host mode the boundary markers live in **referenced assemblies**, which a syntax-based provider cannot see. This is one of the few sanctioned places to query the compilation outside `FeatureDetector`.

### Entity Discovery

Entities are discovered from two sources:
1. **Referenced assemblies** -- via JSON metadata (`PersistenceMetadata` with schema >= 1.1) or full type scan.
2. **Current compilation** -- via syntax-based `CreateSyntaxProvider` scanning for potential entity classes.

Both sources are merged, deduplicated by `FullTypeName`, and processed through `RelationGraphBuilder` to establish entity relationships (1:N, N:N, ownership, etc.).

### Per-Entity vs Aggregate Generation

- **Per-entity**: Uses `SelectMany` to fan out, so changing one entity only regenerates that entity's artifacts.
- **Aggregate**: Uses `Collect()` for artifacts that need all entities (e.g., metadata, DbContext registration).

---

## Nested Class Pattern

Many generated types are nested inside the trigger type's `partial class`:

```csharp
// Invoice.Repository.g.cs
public partial class Invoice
{
    public sealed class Repository : IRepository<Invoice> { ... }
}
```

**Rules:**
- Each `.g.cs` contains at most 1 `partial class` with 1 nested class.
- Nested classes referenced from the host (filters, repositories) must be `public`.
- Invokers are nested: `PlaceOrder.Invoker`, not `PlaceOrderInvoker`.

---

## Diagnostic Suppressors

`src/Pragmatic.SourceGenerator/Suppressors/` holds four `DiagnosticSuppressor` implementations. They ship in the same package as the generator and exist because the IDE analyzes the user's source *without* seeing the generated partial halves, producing warnings that are wrong by construction.

| Class | ID | Suppresses | Fires only when |
|-------|----|------------|-----------------|
| `EntityPropertyNullabilitySuppressor` | PRAGS001 | CS8618 | The member is a **property** whose setter is non-public or `init`, on a `partial` `[Entity]` type — exactly what the generated `Create()` factory and the trait templates assign. A public setter or a field is hand-written surface, so the warning stays. |
| `PartialMethodStaticSuppressor` | PRAGS002 | CA1822 | The containing type is `partial` and Pragmatic-decorated **and** either the member is in a generated file, or it is one half of a `partial` method. CA1822 reasons about what the member touches, not about its callers, so a plain hand-written method that ignores instance state is a genuine finding the generated half does not change. |
| `GeneratedParameterValidationSuppressor` | PRAGS003 | CA1062 | The location is inside a `*.g.cs` / `*.generated.cs` file of a Pragmatic-decorated type, where dependencies come from the generated DI constructor. In a hand-written file CA1062 is real, even in the other half of the same partial type. |
| `UnusedMemberSuppressor` | PRAGS004 | IDE0051 | The containing type is `partial` and Pragmatic-decorated, so the generated half may reference the member. Without `partial` no such half exists and an unused private member is genuinely dead. |

All four share `SuppressionHelper`, which recognizes `[Entity]`, `[DomainAction]`, `[Mutation]`, `[Query]`, `[MapFrom]` and `[MapTo]` (walking the base-type chain), plus the `IsPartialType` and `IsGeneratedLocation` predicates the table above relies on.

Suppress narrowly. The justification for every suppression here is "the generator contributes the other half of this type" — so each predicate has to establish that the other half can exist (`partial`) and that the member is one the generator actually owns. A suppressor that fires more broadly hides real bugs in the user's own code, and it does so silently.

---

## Sibling Projects: Analyzers and CodeFixers

The generator is one of **three** shipped Roslyn components, each its own NuGet package. `Pragmatic.SourceGenerator.csproj` has no project reference to either sibling.

### Pragmatic.SourceGenerator.Analyzers

Design-time diagnostics — they report as you type, without waiting for a build.

- **`NotPartialClassAnalyzer`** is the only source of the "this type must be `partial`" diagnostics — the generator skips a non-partial type without reporting it, so each ID has one owner. It reports on the declaration that lacks `partial`, where the code fix acts, and it maps attribute simple names to descriptors declared in `NotPartialDiagnosticDescriptors.cs`, covering 14 IDs, all errors: PRAG0200 (Validation), PRAG0300 (Mapping), PRAG0400 (Mutation/DomainAction), PRAG0406 (Boundary), PRAG0500 (Endpoint), PRAG0600 (Entity/Repository), PRAG0602 (Database/PragmaticDbContext), PRAG0712 (Query), PRAG0801 (MessageHandler), PRAG1100 (OwnedEntity), PRAG1700 (Caching), PRAG2000 (Configuration), PRAG2200 (Patch), PRAG2502 (Jobs). For a message handler or a job the generator emits no `partial` part of the type either, so the diagnostic is the only thing the author sees.
- **`EventCycleAnalyzer`** reports PRAG0822, "domain-event cascade cycle". It builds an event → handler → operation → event graph from `[Raises<T>]` and `IMessageHandler<T>` / `IDomainEventHandler<T>` and reports cycles. It is tagged `WellKnownDiagnosticTags.CompilationEnd`, since the graph is only complete once the whole compilation is seen.

**If you add an attribute that requires `partial`**, register its descriptor here too. Otherwise the user gets a build error from the generator with no design-time feedback and no fix.

### Pragmatic.SourceGenerator.CodeFixers

One provider: `MakeClassPartialCodeFixProvider` ("Make class partial"), with `WellKnownFixAllProviders.BatchFixer` so a whole file or project can be fixed at once. Its `FixableDiagnosticIds` is exactly the 13 IDs above — adding a descriptor to the analyzer without adding its ID here gives the user a squiggle with no lightbulb.

It references the Analyzers project with `PrivateAssets="all"` so the analyzer DLL is not packed twice, and sets `IsAotCompatible=false` because it depends on `Microsoft.CodeAnalysis.CSharp.Workspaces`.

---

## Testing

Generator tests live in `tests/Pragmatic.SourceGenerator.Tests/` — its own suite, with Verify snapshots for the larger outputs (the gate prints the current count). The Analyzers and CodeFixers projects have their own suites.

Two shapes, and most features want both.

**Render the template directly.** Templates are pure functions of the model, so the fastest test builds a model by hand and calls `RenderOutput()` — no compilation, no generator driver:

```csharp
[Fact]
public Task BehaviorsRegistration_MatchesSnapshot()
{
    var source = new TemporalBehaviorsTemplate(SampleModels()).RenderOutput().Source.ToString();
    return Verify(source);
}
```

**Run the whole pipeline** when you need to cover the transform, the feature gate, or a diagnostic. Use `GeneratorTestHelper` from `shared/SourceGen/Testing/`, auto-compiled into every test project by `Directory.Build.props`:

```csharp
public class MyFeatureTests : MyFeatureTestBase
{
    [Fact]
    public void GeneratesExpectedOutput()
    {
        var result = RunGenerator("""
            using Pragmatic.MyModule;
            [MyAttribute]
            public partial class MyType { }
            """);

        HasCompilationErrors(result).Should().BeFalse();
        GetGeneratedSource(result, "MyNs.MyType.MyArtifact").Should().Contain("expected content");
    }
}
```

The `MetadataReference[]` the test base supplies is what drives `FeatureDetector`. Omitting the assembly that carries the detection type is the cheapest way to test the gate deliberately.

### Snapshots need no per-project setup

There is no `ModuleInitializer.cs` in the SG test project, and none should be added. Verify's configuration lives once, repo-wide, in `shared/Testing/VerifyHelpers.cs` (`Pragmatic.Testing.VerifyConfiguration`), which carries its own `[ModuleInitializer]` and is globbed into every `IsTest=true` project by `Directory.Build.props`.

It registers four scrubbers — the attribution header and its tool line collapse to a stable marker, as does any other `// Generated by …` banner, inline `v1.2.3` becomes `v*`, ISO timestamps become `<timestamp>` — and calls `DontScrubDateTimes()` / `DontScrubGuids()` so Verify's own defaults do not mangle dates and GUIDs that are genuinely part of the generated code. If a new header form leaks into a diff, extend `VerifyHelpers.cs`, not the test project.
