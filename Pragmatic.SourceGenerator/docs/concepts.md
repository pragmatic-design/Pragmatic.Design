# Architecture and Core Concepts

This guide explains **why** Pragmatic.SourceGenerator exists, how its pieces fit together, and the invariants you must maintain when adding or modifying features. Read this before working on any feature pipeline.

---

## The Problem

.NET frameworks and application code rely heavily on two mechanisms for wiring types together at runtime: **reflection** and **manual boilerplate**. Both have fundamental costs that compound as a codebase grows.

### Reflection: hidden runtime cost

```csharp
// Typical repository registration: discovers types at startup
var entityTypes = Assembly.GetExecutingAssembly()
    .GetTypes()
    .Where(t => t.GetInterfaces().Any(i =>
        i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHasKey<>)));

foreach (var type in entityTypes)
{
    var idType = type.GetInterfaces()
        .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHasKey<>))
        .GetGenericArguments()[0];

    var repoType = typeof(IStore<,>).MakeGenericType(type, idType);
    var implType = typeof(Store<,>).MakeGenericType(type, idType);
    services.AddScoped(repoType, implType);
}
```

This code discovers entities, extracts their ID types, and registers repositories at startup. It works, but it carries non-obvious costs:

- **AOT incompatible.** `MakeGenericType`, `GetTypes()`, and `Assembly` scanning are not supported by Native AOT. The code silently breaks when you enable trimming or ahead-of-time compilation.
- **Startup latency.** Assembly scanning and generic type construction happen on every application start. In serverless environments or microservices with frequent cold starts, this latency is measurable.
- **No compile-time feedback.** A missing interface implementation or a wrongly-typed property is only discovered when the scanning code runs. In production.
- **Invisible wiring.** There is no source file you can navigate to that says "Invoice has a Repository with a Guid key." The wiring exists only as runtime behavior.

### Manual boilerplate: correct but unsustainable

```csharp
// For EVERY entity: repository, entity config, DI registration, query filter, metadata...

public sealed class InvoiceRepository : IRepository<Invoice>
{
    private readonly BillingDbContext _db;
    public InvoiceRepository(BillingDbContext db) => _db = db;
    public async Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.Set<Invoice>().FindAsync([id], ct);
    // ... 20+ methods
}

public sealed class InvoiceEntityConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.HasKey(e => e.PersistenceId);
        builder.HasIndex(e => e.Id).IsUnique();
        builder.Property(e => e.CreatedAt).IsRequired();
        // ... 30+ lines of configuration
    }
}

// In Startup:
services.AddScoped<IRepository<Invoice>, InvoiceRepository>();
```

Multiply this by 50 entities across 8 modules. You now maintain hundreds of nearly identical files that differ only in the type names. Every new entity requires creating 4-6 files and registering them in the DI container. Miss one registration and the application compiles but fails at runtime.

**The fundamental issue**: the compiler already knows the entity types, their properties, their attributes, and their relationships. It knows which modules are referenced. The information needed to generate repositories, entity configurations, DI registrations, query filters, and endpoint handlers exists at compile time -- but traditional .NET forces you to either discover it at runtime (reflection) or transcribe it by hand (boilerplate).

---

## The Solution

Pragmatic.SourceGenerator inverts the approach. A single Roslyn `IIncrementalGenerator` runs at compile time, detects which runtime packages your project references, and generates only the code that your specific dependency graph requires.

The same Invoice entity that required hundreds of lines of manual wiring:

```csharp
namespace Sales;

[Entity]
[BelongsTo<BillingBoundary>]
[Auditable]
[SoftDelete]
public partial class Invoice
{
    public required string Number { get; init; }
    public required decimal Amount { get; init; }
    public required InvoiceStatus Status { get; init; }
}
```

At compile time, the source generator reads this declaration and produces:

| Generated File | Content |
|----------------|---------|
| `Sales.Invoice.Traits.g.cs` | `PersistenceId`, `Id`, `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`, `IsDeleted`, `DeletedAt`, `DeletedBy` properties |
| `Sales.Invoice.Create.g.cs` | Factory `Create()` method with required parameters |
| `Sales.Invoice.Setters.g.cs` | `SetNumber()`, `SetAmount()`, `SetStatus()` internal setter methods |
| `Sales.Invoice.Repository.g.cs` | Nested `Repository` class implementing `IRepository<Invoice>` |
| `Sales.Invoice.SoftDeleteFilter.g.cs` | Nested `SoftDeleteFilter` implementing `IQueryFilter<Invoice>` |
| `EntityConfig.Invoice.g.cs` | `IEntityTypeConfiguration<Invoice>` with key, index, audit, soft delete config |
| `_Infra.Persistence.Registration.g.cs` | DI registration for repository, filters, entity config |
| `_Metadata.Persistence.g.cs` | JSON metadata for cross-assembly entity discovery |

Per-type hint names lead with the namespace: that is what keeps `Sales.Invoice` and `Archive.Invoice` from colliding. Host-level artifacts (`EntityConfig.`, `DbContext.`) lead with the function instead, because in a host you look for them by purpose.

All generated code is plain C#. It appears in the IDE Solution Explorer under **Dependencies > Analyzers > Pragmatic.SourceGenerator**. You can set breakpoints in it. You can inspect it. There is no magic -- just code that the compiler writes instead of you.

### What the generator does NOT do

- **No runtime reflection.** The generated code uses direct type references, not `Type` objects.
- **No runtime code generation.** No `Emit`, no `Expression.Compile()`, no dynamic proxies.
- **No NuGet dependencies at generation time.** The generator assembly references only the Roslyn SDK and linked shared source files.
- **No modification of your source code.** The generator only adds new files via `partial class` extension. Your source files are never touched.

---

## How It Works: Single Assembly, Multi-Feature

Unlike generators that ship one analyzer per NuGet package, Pragmatic uses a **unified generator** -- a single `IIncrementalGenerator` that powers the entire ecosystem. This is a deliberate architectural choice.

### Why one generator?

Multiple generators cannot share state. If the Actions generator and the Persistence generator both need to know about an entity's properties, they each need to independently scan the compilation. With a single generator:

- **Shared providers.** The entity metadata provider is computed once and reused by Persistence, Actions, Endpoints, Mapping, and Composition features.
- **Cross-feature coordination.** The Composition feature can aggregate DI registrations from Actions, Persistence, and Endpoints into a single host wiring file.
- **Consistent detection.** Feature flags are computed once in `FeatureDetector.Detect()` and shared across all pipelines.
- **Single analyzer reference.** The consuming project adds one `<ProjectReference>` with `OutputItemType="Analyzer"`.

### Entry point

`PragmaticSourceGenerator` is the sole `[Generator]`-annotated class. Its `Initialize` registers every feature pipeline and is the map of the whole system, and worth reading top to bottom before touching anything.

Most registrations are a single line. The interesting part is the minority that are not, because they encode the only ordering constraints that exist:

```csharp
[Generator]
public sealed class PragmaticSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Detect which features are available, ONCE per compilation.
        var features = context.CompilationProvider
            .Select(static (compilation, _) => FeatureDetector.Detect(compilation));

        // Resource and Trait produce models that OTHER features consume, so they go first.
        var (resourceModels, resourceQueries, resourceEndpoints, resourceActions)
            = ResourceFeature.Register(context, features);
        var traitOutput = TraitFeature.Register(context, features, resourceModels);
        CachingFeature.Register(context, features);

        var allProgrammaticQueries = /* resourceQueries + traitOutput.Queries */;
        var entityModels = PersistenceFeature.Register(context, features,
            resourceQueries: allProgrammaticQueries);

        // Catalog of generated permission constants (const path -> string value).
        var permissionCatalog = entityModels.Select(static (entities, _) =>
            new EquatableArray<PermissionConstEntry>(PermissionCatalogBuilder.Build(entities)));

        var allProgrammaticActions = /* resourceActions + traitOutput.Actions */;
        ActionsFeature.Register(context, features,
            traitActions: allProgrammaticActions, permissionCatalog: permissionCatalog);

        PatchFeature.Register(context, features);
        ValidationFeature.Register(context, features);
        MappingFeature.Register(context, features);

        var allProgrammaticEndpoints = /* resourceEndpoints + traitOutput.Endpoints */;
        var endpointModels = EndpointsFeature.Register(context, features,
            programmaticEndpoints: allProgrammaticEndpoints);

        ResultFeature.Register(context, features);
        I18nFeature.Register(context, features);
        ReadContractFeature.Register(context);
        RollUpFeature.Register(context);
        CompositionFeature.Register(context, features);
        ConfigurationFeature.Register(context, features);
        IdentityFeature.Register(context, features);
        MessagingFeature.Register(context, features);
        Features.Serialization.SerializationFeature.Register(context, features);
        TemporalFeature.Register(context, features);

        // Standalone features: no runtime package dependency, so no `features` argument.
        FastEnumFeature.Register(context);
        JobsFeature.Register(context);
        LifecycleEventsFeature.Register(context);
        ValueObjectFeature.Register(context);
        Features.Glossary.GlossaryFeature.Register(context);
        Features.Glossary.ArchitectureFeature.Register(context);
        Features.Glossary.AsyncApiFeature.Register(context);
    }
}
```

### Three things this shows

**Some `Register` methods return providers.** `ResourceFeature`, `TraitFeature`, `PersistenceFeature`, and `EndpointsFeature` hand back models that later features consume. This is how cross-feature coordination works: there is no shared mutable state anywhere, only providers threaded through `Initialize`.

**Some take extra named arguments.** `PersistenceFeature(…, resourceQueries:)`, `ActionsFeature(…, traitActions:, permissionCatalog:)`, `EndpointsFeature(…, programmaticEndpoints:)`. These carry *programmatically synthesized* models (actions, queries, and endpoints that no user wrote, produced by the Resource and Trait features) into the pipelines that render them. A `[Resource]` entity and a `[HasComments]` attribute both work this way.

**Registration order matters in exactly one way.** It does not affect execution (Roslyn drives the pipeline), but a feature cannot consume a provider that does not exist yet. The load-bearing case is Persistence before Actions, and the reason is worth quoting from the source:

```
Persistence registers BEFORE ActionsFeature so its generated entity-permission catalog
can resolve [RequirePermission(BookingPermissions.Entity.Op)] references in the Actions
pipeline — a source generator cannot resolve constants it generates itself in the same
compilation, so those references would otherwise fail-open (permission silently not enforced).
Registration order does not affect execution; it only makes the catalog provider available.
```

A permission check that silently does not run is worse than one that fails loudly. The catalog exists so `ActionsFeature` can resolve a constant the generator itself is emitting in the same pass (something Roslyn's semantic model cannot do), and PRAG0418 fails the build closed when it cannot.

### Feature inventory

| Feature | Gate | Trigger | Emits |
|---------|------|---------|-------|
| `Actions` | `HasActions` | `[DomainAction]`, `[Mutation]`, `[CompositeAction]`, `[Boundary]` | Invokers, `SetDependencies`, boundary interfaces, DI registration |
| `Caching` | `HasCaching` | `[Cacheable]`, `[InvalidatesCache]` | Cache key + invalidator per type |
| `Composition` | `HasComposition` | Module/`[Include]` graph | Host services, startup wiring, module metadata |
| `Configuration` | `HasConfiguration` | `[Configuration]` | Binding, validation, DI registration |
| `Endpoints` | `HasEndpoints` | `[Endpoint]` | Minimal-API mapping; also drives the Manifest (below) |
| `FastEnum` | standalone | enum marker | Allocation-free enum lookup tables |
| `Glossary` | standalone | `[Entity]` / `[Entity]` | `_Infra.Glossary.Generated.g.cs`: `PragmaticGlossary.Markdown`, a Markdown domain glossary as a `const string` |
| `Glossary`/`Architecture` | standalone | `[Include<…>]`, `[RemoteBoundary<T>]` | `_Infra.Architecture.Generated.g.cs`: `PragmaticArchitecture.C4ContainerDiagram`, a Mermaid C4 diagram string |
| `Glossary`/`AsyncApi` | standalone | types implementing `IDomainEvent` | `_Infra.AsyncApi.Generated.g.cs`: `PragmaticAsyncApi.Json`, an AsyncAPI 3.0.0 document string |
| `I18n` | `HasI18n` | `[TranslationKeys]` | The compile-checked `T` class |
| `Identity` | `HasIdentityAspNetCore` / `HasIdentityPersistence` | `[PragmaticUser]`, permission attributes | Permission constants, identity entity wiring |
| `Jobs` | standalone | `[Job]`, `[RecurringJob]` | Job registry, schedules, invokers |
| `Lifecycle` | standalone | `[Raises<T>]` | `{Ns}.{Type}.LifecycleEvents.g.cs`: the `IRaisesLifecycleEvents` partial raising declared events at their transition |
| `Manifest` | n/a | *not registered* | `_Metadata.PragmaticManifest.g.cs`. `ManifestFeature` has **no** `Register`; `EndpointsFeature` calls `ManifestFeature.GenerateManifest(...)` inline through `RegisterSourceOutputSafe`, so a failure reports PRAG9000 |
| `Mapping` | `HasMapping` | `[MapFrom<T>]`, `[MapTo<T>]` | `FromEntity` / `ToEntity` mappers |
| `Messaging` | `HasMessaging` (+6 sub-flags) | `[MessageHandler]`, saga/outbox markers | Handler pipeline, dispatch table, type registry |
| `Patch` | `HasPatch` | `[GeneratePatch<T>]` | Patch DTO and apply logic |
| `Persistence` | `HasPersistence*` | `[Entity]`, `[Query<,>]`, … | See the orchestrator section below |
| `Resource` | n/a | `[Resource]` | `_Resource.{Type}.{Create\|Read\|Update\|Delete\|List\|Search}.g.cs` plus `_Resource.{Type}.{…}Dto.g.cs`; also *returns* synthesized query/endpoint/action models to downstream features. Diagnostics PRAG2602/2603/2605 |
| `Result` | `HasResult` | error/result types | Result plumbing, JSON converters |
| `Serialization` | opt-in | `PragmaticGenerateJsonContext` / `PublishAot` / assembly attribute | `_Infra.Json.Context.g.cs`, `_Infra.Json.Registration.g.cs`, `_Metadata.JsonContexts.g.cs`: an AOT-safe `JsonSerializerContext` |
| `Temporal` | `HasTemporalJson` | six timezone attributes on properties | `_Infra.Temporal.Behaviors.g.cs`: registration into `TemporalJsonBehaviorRegistry`. Diagnostic PRAG0905 |
| `Traits` | `HasPersistenceEFCore` (+`HasActions`) | `[HasComments]`, `[HasTags]`, `[HasNotes<T>]`, `[HasAttachments]` | Child entity + EF config + navigations + CRUD actions + DTOs + queries + permission constants per trait; aggregate `_Metadata.{Trait\|Tag\|Note\|Attachment}Entities.g.cs`. Diagnostic PRAG2601 when the parent lacks `[Resource]` |
| `Validation` | `HasValidation` | `[Validation]` attributes | Validators, validation metadata |
| `ValueObject` | standalone | `[ValueObject]` | `{Ns}.{Type}.ValueObject.g.cs`: `Create` / `CreateUnsafe` factories mirroring the user's `Validate` |

`Traits` and `Resource` are the two features that generate *other features' inputs* rather than final code, which is why they are registered first and why their `Register` methods return tuples.

---

## Feature Detection

Before any feature generates code, the generator must know which runtime packages are referenced. This happens once per compilation in `FeatureDetector.Detect()`.

### How it works

`FeatureDetector` calls `compilation.GetTypeByMetadataName()` for a well-known type from each runtime package. If the type exists in the compilation's references, the feature is active.

```csharp
internal static class FeatureDetector
{
    public static DetectedFeatures Detect(Compilation compilation) => new()
    {
        HasActions = TypeExists(compilation, "Pragmatic.Actions.Attributes.DomainActionAttribute"),
        HasMapping = TypeExists(compilation, "Pragmatic.Mapping.Attributes.MapFromAttribute`1"),
        HasPersistenceEFCore = TypeExists(compilation, "Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute"),
        HasCaching = TypeExists(compilation, "Pragmatic.Caching.Attributes.CacheableAttribute"),
        // ... 37 Has* checks in total
    };

    private static bool TypeExists(Compilation compilation, string fullyQualifiedName)
        => compilation.GetTypeByMetadataName(fullyQualifiedName) is not null;
}
```

### DetectedFeatures record

The result is a `sealed record` carrying **37 `Has*` boolean flags**, two mode flags, and the `EfCoreProvider` enum:

```csharp
internal sealed record DetectedFeatures
{
    public bool HasActions { get; init; }
    public bool HasValidation { get; init; }
    public bool HasCaching { get; init; }
    public bool HasMapping { get; init; }
    public bool HasEndpoints { get; init; }
    public bool HasPersistence { get; init; }
    public bool HasPatch { get; init; }
    public bool HasComposition { get; init; }
    public bool HasPersistenceEFCore { get; init; }
    public bool HasI18n { get; init; }
    public bool HasResult { get; init; }
    public bool HasConfiguration { get; init; }
    public bool HasResilience { get; init; }
    public bool HasIdentityAspNetCore { get; init; }
    public bool HasMultiTenancy { get; init; }
    public bool HasFeatureFlags { get; init; }
    public bool HasDiscovery { get; init; }
    public bool HasTemporal { get; init; }
    public bool HasTemporalJson { get; init; }
    public bool HasTemporalAspNetCore { get; init; }
    public bool HasAuthorization { get; init; }
    public bool HasEventsEFCore { get; init; }
    public bool HasMessaging { get; init; }
    public bool HasMessagingEFCore { get; init; }
    public bool HasMessagingChannels { get; init; }
    public bool HasMessagingRabbitMq { get; init; }
    public bool HasMessagingAuditing { get; init; }
    public bool HasMessagingSagas { get; init; }
    public bool HasMessagingBatch { get; init; }
    public bool HasIdentityPersistence { get; init; }
    public bool HasJobs { get; init; }
    public bool HasMessagingJobs { get; init; }
    public bool HasMigrations { get; init; }
    public bool HasComments { get; init; }
    public bool HasControlPlane { get; init; }
    public bool HasNotifications { get; init; }
    public bool HasSerialization { get; init; }

    public bool IsHostMode { get; init; }
    public bool IsHostCompositionMode { get; init; }
    public EfCoreProvider EfCoreProvider { get; init; }

    public static DetectedFeatures None { get; } = new();
}
```

Detection granularity is **per assembly, not per module**. Temporal contributes three flags and Messaging seven, because a consumer can reference any subset, and emitting registration code for an assembly that is not there produces CS0246 in *their* build, not yours.

Not every flag is consumed. `HasNotifications` and `HasControlPlane` exist in the record and are set by the detector, but no feature currently reads them. Adding a flag is cheap; wiring a pipeline to it is the actual work.

### Feature flag categories

| Category | Flags | Purpose |
|----------|-------|---------|
| **Feature pipelines** | `HasActions`, `HasMapping`, `HasValidation`, `HasEndpoints`, `HasPersistence`, `HasPersistenceEFCore`, `HasCaching`, `HasComposition`, `HasI18n`, `HasResult`, `HasConfiguration`, `HasPatch`, `HasIdentityAspNetCore`, `HasIdentityPersistence`, `HasMessaging`, `HasJobs`, `HasTemporalJson`, `HasSerialization` | Gate feature-specific generation pipelines |
| **Behavioral modifiers** | `HasMultiTenancy`, `HasTemporal`, `HasTemporalAspNetCore`, `HasAuthorization`, `HasEventsEFCore`, `HasFeatureFlags`, `HasDiscovery`, `HasResilience`, `HasMigrations`, `HasComments` | Modify behavior within other features (e.g., `HasMultiTenancy` triggers tenant filter generation in Persistence; `HasAuthorization` gates CRUD permission constants) |
| **Sub-capability flags** | `HasMessagingEFCore`, `HasMessagingChannels`, `HasMessagingRabbitMq`, `HasMessagingAuditing`, `HasMessagingSagas`, `HasMessagingBatch`, `HasMessagingJobs` | Enable optional parts of a module that ship as separate assemblies |
| **Mode flags** | `IsHostMode`, `IsHostCompositionMode` | Switch between module-level and host-level generation; see below, they are not interchangeable |
| **Provider detection** | `EfCoreProvider` | Generate provider-specific code (PostgreSQL `xmin`, SQL Server `rowversion`, etc.) |

### FQN rules for GetTypeByMetadataName

Getting the fully qualified name wrong is the most common reason a feature silently fails to activate. Roslyn's `GetTypeByMetadataName` has strict formatting requirements:

| Scenario | FQN Format | Example |
|----------|-----------|---------|
| Non-generic type | `Namespace.TypeName` | `Pragmatic.Caching.Attributes.CacheableAttribute` |
| Generic type (1 param) | `Namespace.TypeName\`1` | `Pragmatic.Mapping.Attributes.MapFromAttribute\`1` |
| Generic type (2 params) | `Namespace.TypeName\`2` | `Pragmatic.Persistence.Query.Attributes.QueryAttribute\`2` |
| Nested type | `Namespace.Outer+Inner` | `Pragmatic.Persistence.Entity.Relation+OneToMany\`1` |

The backtick-arity suffix is mandatory for generic types. Without it, `GetTypeByMetadataName` returns `null` and the feature is silently inactive.

### Centralized FQN constants

Attribute FQNs belong in `shared/SourceGen/AttributeNames.cs`, roughly 80 `public const string` members, grouped by layer:

```csharp
internal static class AttributeNames
{
    public const string Resource = "Pragmatic.Persistence.Entity.ResourceAttribute";
    public const string HasNotes = "Pragmatic.Notes.HasNotesAttribute`1";
    public const string Raises = "Pragmatic.Authoring.RaisesAttribute`1";
    // ...
}
```

Feature classes reference these constants in their `ForAttributeWithMetadataName` calls rather than using string literals.

This is the convention, not yet the state of the code. Several features still carry their trigger FQN as a private const on the feature class (`ReadContractFeature`, `RollUpFeature`, `GlossaryFeature`, `ArchitectureFeature`, `SerializationFeature`, and the four `*BoundaryReader` classes), and `FeatureDetector` holds its own detection strings inline. Put new FQNs in `AttributeNames.cs`: a literal you can only find by grepping is precisely how a typo survives review, and a typo here fails silently.

### IsHostMode vs IsHostCompositionMode

Two flags, two different questions. They are not interchangeable, and gating on the wrong one puts artifacts into projects that cannot compile them.

**`IsHostMode`** is `CompositionDetector.IsHostProject(compilation)` and nothing else:

1. The compilation's `OutputKind` is `ConsoleApplication` or `WindowsApplication`.
2. The compilation has an entry point (`GetEntryPoint()` returns non-null).

That is the whole test. It means "this project is an executable": a console tool referencing only `Pragmatic.Persistence` satisfies it.

**`IsHostCompositionMode`** adds the reference check:

```csharp
IsHostCompositionMode = CompositionDetector.IsHostProject(compilation)
    && CompositionDetector.IsCompositionHostReferenced(compilation),
```

It means "this project is a full Pragmatic host": executable **and** referencing `Pragmatic.Composition.Host`. Only then does the Composition feature emit `PragmaticHost.Services.g.cs` with its aggregated DI wiring.

Note also that a test project can satisfy `IsHostMode`, which is why `CompositionDetector` additionally exposes `IsTestProject()` and a `GeneratorMode` enum (`Library` / `Host` / `Skip`) that folds all three checks together.

### EfCoreProvider detection

The generator detects the EF Core database provider to generate provider-specific code:

| Provider | Detection Type | Generated Behavior |
|----------|---------------|-------------------|
| PostgreSQL | `NpgsqlDbContextOptionsBuilderExtensions` | `xmin` system column for concurrency |
| SQL Server | `SqlServerDbContextOptionsBuilderExtensions` | `rowversion` timestamp type |
| SQLite | `SqliteDbContextOptionsBuilderExtensions` | `uint` + `IsConcurrencyToken()` with manual increment |
| Generic | (fallback) | Portable `IsConcurrencyToken()` |

Detection priority is PostgreSQL > SQL Server > SQLite > Generic.

---

## The Three-Stage Pipeline

Every generated file in Pragmatic follows the same three-stage pipeline. This is the most important architectural invariant in the generator.

```
[Roslyn Symbols] ──► Transform ──► [Immutable Model] ──► Template ──► [C# Source]
                     (static)       (sealed record)       (CSharpTemplate)
```

### Why three stages?

| Stage | Responsibility | Why It Exists |
|-------|---------------|---------------|
| **Transform** | Convert `ISymbol` into a plain data record | Roslyn symbols hold references to the entire compilation graph. They are not value-comparable and break incremental caching if stored. The transform extracts only the data needed into an immutable record that Roslyn can compare by value. |
| **Model** | Carry all data needed for code generation | Immutable records participate in structural equality. When source code changes but produces the same model, Roslyn skips the template entirely. This is the core of incremental performance. |
| **Template** | Render C# source text from the model | Templates never touch Roslyn APIs. They receive a fully resolved model and produce an `Artifact`. This separation makes templates independently testable -- you can construct a model by hand and verify the output without a compilation. |

### Key invariants

These invariants must hold for every feature pipeline. Violating any of them causes either incorrect output, broken incremental caching, or non-deterministic behavior.

**Transforms must be deterministic.** Same `ISymbol` input produces the same model output. No randomness, no timestamps, no environment-dependent logic.

**Models must be immutable sealed records whose fields are genuinely value-equatable.** Use `EquatableArray<T>` for collections and `EquatableDictionary<TKey, TValue>` for maps; never `List<T>`, never `T[]`, and never a raw `ImmutableArray<T>`. See "Value-Equatable Model Fields" below; this is the invariant most often broken by accident.

**Templates must be pure functions of the model.** No `Compilation`, no `ISymbol`, no `SemanticModel`, no side effects. If a template needs information that is not in the model, the model is incomplete -- fix the transform.

**Transforms must not store `ISymbol` references.** Extract string names, type names, flags, and lists. A stored `ISymbol` defeats incremental caching because the symbol's identity changes on every compilation, even when the underlying source has not changed.

**Transforms must not store a raw `Location`.** Use `LocationInfo`; see "Diagnostic Positions" below. A stored `Location` pins a `SyntaxTree` to a dead compilation and breaks the IDE.

---

## Value-Equatable Model Fields

This is the single most commonly broken invariant in the generator, and it is broken by writing something that looks correct.

### Why `ImmutableArray<T>` is wrong

`ImmutableArray<T>` is a readonly struct wrapping a `T[]`. Its `Equals` compares the **array reference**, not the elements: that is the equality it inherits from the array it wraps. Immutability and value equality are different properties, and it only has the first.

A transform allocates a fresh array every run. So a record field of type `ImmutableArray<T>` makes the compiler-generated record `Equals` return `false` on every pass, even when the source is byte-identical. Roslyn concludes the model changed, re-runs the stage, re-renders the template. On every keystroke. Nothing errors: the IDE simply gets slower as the model grows, and the cause is invisible because the code reads as correct.

`ImmutableDictionary<TKey, TValue>` has the identical defect.

### The fix

```csharp
using Pragmatic.SourceGen;

internal sealed record ValueObjectModel
{
    public required string TypeName { get; init; }

    public EquatableArray<ValueObjectParameter> ValidateParameters { get; init; }
        = EquatableArray<ValueObjectParameter>.Empty;
}
```

`EquatableArray<T>` (`shared/SourceGen/EquatableArray.cs`) walks the elements with `EqualityComparer<T>.Default`. `EquatableDictionary<TKey, TValue>` compares order-insensitively, matching dictionary semantics. A default-constructed `EquatableArray<T>` reads as empty, so an unset field is safe.

### It costs nothing at the call sites

| Situation | What to write |
|-----------|---------------|
| Transform ends in `.ToImmutableArray()` | Nothing: implicit conversion wraps it on assignment |
| Transform has an `IEnumerable<T>` | `.ToEquatableArray()` |
| Collection expression | `Imports = ["System", "System.Linq"]` works, via `[CollectionBuilder]` |
| Template needs an `ImmutableArray`-specific API | `.AsImmutableArray()` |
| Reading it | It is an `IReadOnlyList<T>`: indexer, `Count`, `Length`, `IsDefaultOrEmpty`, `foreach` |

### The one legitimate exception

`IncrementalValueProvider<ImmutableArray<T>>` (what `.Collect()` returns) stays as it is. That is pipeline plumbing, not a cached model field, and Roslyn compares those elementwise itself. Wrap the fields *inside* the elements, not the provider. The same goes for a template constructor parameter that receives a collected array directly: the template is not cached, so its parameter type is irrelevant to caching.

---

## Diagnostic Positions: LocationInfo

Models carry `LocationInfo?` (`Core/LocationInfo.cs`), never a Roslyn `Location`.

A `Location` references its `SyntaxTree`, and a `SyntaxTree` belongs to one specific `Compilation`. A cached model outlives the compilation it was built from, so reporting through a stored `Location` reports a diagnostic whose tree is not part of the **current** compilation. Roslyn's suppression filtering then throws "SyntaxTree is not part of the compilation", which kills source generation, classification, and CodeLens in the IDE. The CLI never notices (one run, fresh trees), so this reproduces only for the people who have to work in the editor all day.

Separately, `Location` is not usefully value-equatable, so including it in a record defeats caching on every re-parse.

```csharp
// Transform:
Location = LocationInfo.From(symbol.Locations.FirstOrDefault()),

// Report time:
ctx.ReportDiagnostic(MyDiagnostics.MustBePartial, model.Location?.ToLocation(compilation), model.TypeName);
```

`LocationInfo` captures the file path plus both spans as plain values and is deliberately excluded from equality (`Equals` always returns `true`, `GetHashCode` returns `0`) because a position never changes the generated output.

| Rebuild call | Result |
|--------------|--------|
| `ToLocation()` | Tree-free `Location`. Correct file and line, but `#pragma warning disable` and `[SuppressMessage]` cannot suppress it. |
| `ToLocation(compilation)` | Rebinds to the current compilation's tree for the same file, so source suppressions apply. Falls back to the tree-free form when the file is gone or the span no longer fits mid-edit. |

Prefer the compilation overload wherever a `Compilation` is in scope.

**One trap.** `LocationInfo.From` returns `null` when the syntax tree's `FilePath` is empty. Any diagnostic guarded on `if (model.Location is null) return;` therefore vanishes in a test whose compilation was parsed without a path: the test fails while production works fine. `GeneratorTestHelper.RunGenerator` parses with an explicit path for exactly this reason; if you build a compilation by hand, pass one.

---

## Transform Details

Transforms are `static` classes with a `static` method matching the `GeneratorAttributeSyntaxContext` signature that Roslyn's `ForAttributeWithMetadataName` expects.

### Typical transform structure

```csharp
internal static class ActionTransform
{
    public static ActionModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        var attribute = context.Attributes[0];
        var ns = symbol.GetNamespaceOrEmpty();
        var typeName = symbol.Name;

        // Extract attribute arguments
        var isQuery = attribute.GetNamedArgument<bool>("IsQuery");

        // Extract type information
        var dependencies = symbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => f.DeclaredAccessibility == Accessibility.Private
                        && f.Type.TypeKind == TypeKind.Interface)
            .Select(f => new DependencyModel
            {
                TypeName = f.Type.GetFullyQualifiedName(),
                FieldName = f.Name,
            })
            .ToImmutableArray();

        return new ActionModel
        {
            Namespace = ns,
            TypeName = typeName,
            Accessibility = symbol.GetAccessibilityKeyword(),
            TypeKind = symbol.GetTypeKindKeyword(),
            IsQuery = isQuery,
            Dependencies = dependencies,
            IsPartial = symbol.IsPartial(),
            IsValid = symbol.IsPartial() && !symbol.IsAbstract,
        };
    }
}
```

### Key helper methods

Transforms use extension methods from `shared/SourceGen/SymbolExtensions.cs`:

| Method | Purpose |
|--------|---------|
| `GetNamespaceOrEmpty()` | Returns the symbol's namespace or `""` for global |
| `GetAccessibilityKeyword()` | Returns `"public"`, `"internal"`, etc. |
| `GetTypeKindKeyword()` | Returns `"class"`, `"record"`, `"struct"` |
| `GetFullyQualifiedName()` | Returns `"global::Namespace.TypeName"` |
| `GetFullName()` | Returns `"Namespace.TypeName"` without `global::` |
| `HasAttribute(fqn)` | Checks if the symbol has a specific attribute |
| `GetAttribute(fqn)` | Gets attribute data by FQN |
| `GetNamedArgument<T>(name)` | Extracts a named attribute argument |
| `IsPartial()` | Checks if the type declaration is `partial` |
| `ToRenderName()` | Returns the display name for code generation |

### Return null for invalid input

When the transform encounters invalid input (wrong symbol type, missing required data), it returns `null`. The pipeline's `.Where(static m => m is not null)` filter discards it. This is cheaper than throwing an exception and simpler than returning a sentinel value.

### The IsValid pattern

Many models include an `IsValid` flag that gates generation. This allows the transform to always produce a model (so diagnostics can be reported), while the template only runs for valid models:

```csharp
// In the feature:
var allModels = provider.Where(static m => m is not null).Select(static (m, _) => m!);

// Report diagnostics for ALL models (including invalid ones)
context.RegisterSourceOutputSafe(allModels, ReportDiagnostics);

// Generate code only for valid models
var validModels = allModels.Where(static m => m.IsValid);
context.RegisterSourceOutputSafe(validModels, GenerateOutput);
```

---

## Model Details

Models are the data contract between transforms and templates. They inherit from `GeneratorModel`, which provides four base properties.

### GeneratorModel base

```csharp
internal abstract record GeneratorModel
{
    public string Namespace { get; init; } = "";
    public required string TypeName { get; init; }
    public required string Accessibility { get; init; }
    public required string TypeKind { get; init; }
    public string FullTypeName =>
        string.IsNullOrEmpty(Namespace) ? TypeName : $"{Namespace}.{TypeName}";
}
```

### Model design rules

1. **Inherit from `GeneratorModel`.** This provides `Namespace`, `TypeName`, `Accessibility`, `TypeKind`, and `FullTypeName`.

2. **Use `sealed record`.** Records provide structural equality by default. `sealed` prevents inheritance that could break equality semantics.

3. **Use `EquatableArray<T>` for collections, `EquatableDictionary<TKey, TValue>` for maps.** `List<T>` and `T[]` use reference equality, and so does `ImmutableArray<T>`, which is the trap. See "Value-Equatable Model Fields" above.

4. **All properties must participate in equality.** Records include all properties by default. If you override `Equals`, ensure all properties are compared.

5. **No `ISymbol` or `Compilation` references.** These hold the entire compilation graph and change identity on every compilation pass.

6. **Positions are `LocationInfo?`, never `Location`.**

7. **Mark required properties with `required`.** This prevents accidentally constructing an incomplete model.

8. **Inherit `GeneratorModel` only when the model describes a type.** Some models describe a property or a relationship instead (`TemporalBehaviorPropertyModel` is one) and are flat records with no base.

### Example model

```csharp
internal sealed record CacheableModel : GeneratorModel
{
    public required string CacheKeyType { get; init; }
    public required string CacheValueType { get; init; }
    public required EquatableArray<CacheKeyPropertyModel> KeyProperties { get; init; }
    public required string ExpirationPolicy { get; init; }
    public bool IsPartial { get; init; }
    public string? CustomKeyExpression { get; init; }
    public LocationInfo? Location { get; init; }

    public bool IsValid => IsPartial && KeyProperties.Length > 0;
}

internal sealed record CacheKeyPropertyModel
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public int Order { get; init; }
}
```

The nested record needs no explicit `IEquatable<T>`: the compiler generates a correct value `Equals` for a record whose fields are all strings and value types, and `EquatableArray<T>` uses `EqualityComparer<T>.Default`, which picks it up.

---

## Template Details

Templates extend `CSharpTemplate` (`shared/SourceGen/CSharpTemplate*.cs`) and produce an `Artifact` -- a readonly struct pairing a hint name with rendered `SourceText`.

### CSharpTemplate overview

`CSharpTemplate` is an abstract partial class split across four files:

| File | API Surface |
|------|-------------|
| `CSharpTemplate.cs` | Core writing (`Append`, `AppendLine`, `IncreaseIndent`, `DecreaseIndent`), usings (`AddUsing`, `AddAlias`), namespace (`AppendNamespace`), blocks, comments (`Comment`, `XmlSummary`, `XmlParam`, `XmlReturns`, `XmlInheritDoc`), rendering (`RenderOutput`, `RenderFile`, `Validate`, `ToSourceText`, `ToString`) |
| `CSharpTemplate.Types.cs` | `Class()`, `Struct()`, `Record()`, `RecordStruct()` with `ClassModifiers` and `AccessModifier` |
| `CSharpTemplate.Members.cs` | `Field()`, `Property()`, `ExpressionProperty()`, `PropertyWithGetter()`, `Method()`, `ExpressionMethod()`, `GenericMethod()`, `Constructor()`, `ImplicitOperator()` with `MethodParameter` and `MethodModifiers` |
| `CSharpTemplate.ControlFlow.cs` | `If()`, `ElseIf()`, `Else()`, `Switch()`, `Case()`, `Default()`, `Block()`, `Return()`, `Break()`, `Continue()` |

### Required overrides

| Member | Signature | Purpose |
|--------|-----------|---------|
| `RenderOutput()` | `public abstract Artifact RenderOutput()` | Returns the hint name and rendered source text. Call `ToSourceText()` to convert the rendered content to `SourceText`. |
| `RenderFile()` | `public abstract void RenderFile()` | Writes the file body using template methods. Called internally by `ToString()` after validation passes. |

### Optional overrides

| Member | Default | Purpose |
|--------|---------|---------|
| `Validate()` | `return true` | Return `false` to skip generation entirely. `ToString()` returns `null`, `ToSourceText()` renders empty content, and `ctx.AddSource(artifact)` skips the file. Nothing is reported: a deliberately missing output is silent. |
| `GeneratorName` | `null` | Appears in `// Generated by {GeneratorName} v{version}` header comment. |
| `SourceInfo` | `null` | Appears in `// Source: {SourceInfo}` header comment. |
| `TriggerInfo` | `null` | Appears in `// Trigger: {TriggerInfo}` header comment. |

### Template lifecycle

1. The feature's `Generate()` method constructs the template with the model.
2. `RenderOutput()` is called.
3. Inside `RenderOutput()`, `ToSourceText()` triggers `ToString()`.
4. `ToString()` calls `Validate()`. If `false`, returns `null` (empty source).
5. `ToString()` calls `RenderFile()`, which writes the file body.
6. The base class assembles the final output: auto-generated header, `#nullable enable`, usings, aliases, namespace, then the rendered body.
7. `ToSourceText()` converts the assembled string to `SourceText` with UTF-8 encoding and normalized CRLF line endings.
8. The `Artifact` (hint name + source text) is returned to the feature.
9. The feature calls `ctx.AddSource(artifact)`, which emits it, or skips it when it is empty.

### Artifact struct

```csharp
internal readonly struct Artifact
{
    public string HintName { get; init; }
    public SourceText Source { get; init; }

    /// True when the template produced no content — the normal outcome when Validate() is false.
    public bool IsEmpty => Source is null || Source.Length == 0;

    public Artifact(string hintName, SourceText source);
    public static Artifact FromString(string hintName, string content);
}
```

### Emission: `ctx.AddSource(artifact)`

There is one way to hand an artifact to Roslyn, and it is not the two-argument call:

```csharp
ctx.AddSource(artifact);                                 // correct
ctx.AddSource(artifact.HintName, artifact.Source);       // wrong
```

`SourceOutput.AddSource` (`shared/SourceGen/SourceOutput.cs`) is an extension on `SourceProductionContext`. It checks `artifact.IsEmpty` and skips the file, otherwise forwarding to Roslyn's own two-argument method. (An instance method beats an extension in overload resolution, so the forwarding call does not recurse.)

The reason it exists is step 4 of the lifecycle above. A template that fails `Validate()` renders to *empty* content, not to nothing, and that is how every template in the generator says "there is nothing to generate here". The raw call writes that empty content into the compilation as a real zero-byte `.g.cs`: harmless to compile, but it pollutes the generated-file list and destroys the distinction between "deliberately nothing" and "the template silently produced nothing", which is exactly the distinction you need when an output goes missing. With roughly 200 emission sites, the check belongs in one place rather than in 200 `if` statements only a handful of which would ever be written.

### Example template

```csharp
internal sealed class RepositoryTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;

    public RepositoryTemplate(EntityMetadataModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Entity] on {_model.TypeName}";

    protected override bool Validate() => _model.IsPartial;

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "Repository", _model.Namespace),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsing("System.Threading");
        AddUsing("System.Threading.Tasks");

        AppendNamespace(_model.Namespace);
        AppendLine();

        // Outer partial class (the entity)
        Class(_model.TypeName, RenderNestedRepository,
            accessModifier: TemplateHelpers.ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderNestedRepository()
    {
        XmlSummary($"Generated repository for <see cref=\"{_model.TypeName}\"/>.");

        // Nested class must be public for cross-assembly DI access
        Class("Repository", RenderRepositoryBody,
            interfaces: [$"global::Pragmatic.Persistence.IRepository<{_model.TypeName}, {_model.IdType}>"],
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderRepositoryBody()
    {
        // ... method bodies
    }
}
```

### Key API methods

| Category | Methods |
|----------|---------|
| **Header** | `AddUsing()`, `AddUsings()`, `AddAlias()`, `AppendNamespace()` |
| **Types** | `Class()`, `Struct()`, `Record()`, `RecordStruct()` |
| **Members** | `Field()`, `Property()`, `ExpressionProperty()`, `PropertyWithGetter()`, `Method()`, `ExpressionMethod()`, `GenericMethod()`, `Constructor()`, `ImplicitOperator()` |
| **Control flow** | `If()`, `ElseIf()`, `Else()`, `Switch()`, `Case()`, `Default()`, `Block()`, `Return()`, `Break()`, `Continue()` |
| **Documentation** | `XmlSummary()`, `XmlParam()`, `XmlReturns()`, `XmlInheritDoc()`, `Comment()` |
| **Low-level** | `Append()`, `AppendLine()`, `IncreaseIndent()`, `DecreaseIndent()` |

### ClassModifiers and MethodModifiers

```csharp
protected struct ClassModifiers
{
    public bool Partial { get; init; }
    public bool Abstract { get; init; }
    public bool Sealed { get; init; }
    public bool IsStatic { get; init; }
    public bool IsReadOnly { get; init; }
}

protected struct MethodModifiers
{
    public bool IsStatic { get; init; }
    public bool IsVirtual { get; init; }
    public bool IsOverride { get; init; }
    public bool IsAbstract { get; init; }
    public bool IsSealed { get; init; }
    public bool IsAsync { get; init; }
}
```

### MethodParameter

```csharp
protected readonly struct MethodParameter
{
    public string Type { get; init; }
    public string Name { get; init; }      // Auto-converted to camelCase in output
    public bool Nullable { get; init; }
    public bool IsExtension { get; init; }  // Adds "this" keyword
    public string? DefaultValue { get; init; }
    public string? Attribute { get; init; } // e.g., "FromBody"
    public bool IsRef { get; init; }
    public bool IsOut { get; init; }

    public MethodParameter(string type, string name, bool nullable = false);
}
```

### AccessModifier enum

```csharp
internal enum AccessModifier
{
    Public,
    Private,
    Protected,
    Internal,
    ProtectedInternal,
    PrivateProtected,
    NotApplicable       // No access modifier emitted
}
```

Use `TemplateHelpers.ParseAccessibility()` to convert the string from the model (e.g., `"public"`) to this enum.

---

## VirtualFolderHints

`VirtualFolderHints` is the single source of truth for Roslyn hint names. Hint names determine the file name under which generated source appears in the IDE and build output.

It lives in `Core/VirtualFolderHints.cs` (inside the generator project, not `shared/SourceGen/`) and is `internal` to that assembly. The standalone generators that do not link it (Result, Country/Currency/Language) build their hint names themselves.

Slashes in hint names are unreliable across IDE versions (dotnet/roslyn#70859), which is why every pattern is flat and dot-separated rather than a real path.

### Module mode (type-first)

In module projects, generated files are sorted by the trigger type name. You search for `Sales.Invoice.Repository.g.cs`, not `Persistence.Invoice.Repository.g.cs`.

| Method | Pattern | Example |
|--------|---------|---------|
| `ForType(typeName, artifact, namespacePrefix)` | `{Ns}.{Type}.{Artifact}.g.cs` | `Sales.Invoice.Repository.g.cs` |
| `ForBoundary(name, artifact)` | `_Boundary.{Name}.{Artifact}.g.cs` | `_Boundary.Billing.Interface.g.cs` |
| `ForAssembly(category, ns, ext)` | `_Infra.{Category}.{Ext}.g.cs` | `_Infra.Actions.Registration.g.cs` |
| `ForMetadata(ns, category)` | `_Metadata.{Category}.g.cs` | `_Metadata.Persistence.g.cs` |

### Host mode (function-first)

In host projects, generated files are sorted by purpose. You search for `EntityConfig.Invoice.g.cs`, not `Invoice.EntityConfig.g.cs`.

| Method | Pattern | Example |
|--------|---------|---------|
| `ForEntityConfig(entity)` | `EntityConfig.{Entity}.g.cs` | `EntityConfig.Invoice.g.cs` |
| `ForDbContext(boundary)` | `DbContext.{Boundary}.g.cs` | `DbContext.Billing.g.cs` |

### Sorting convention

The underscore prefix (`_Boundary`, `_Infra`, `_Metadata`) sorts after letters in case-insensitive order. This pushes infrastructure and metadata files to the bottom of the file listing, keeping per-type files front and center:

```
Booking.Amenity.Create.g.cs            <- per-type (top)
Booking.Amenity.Repository.g.cs
Sales.Invoice.Invoker.g.cs
Sales.Invoice.Setters.g.cs
_Boundary.Billing.Interface.g.cs       <- infra (bottom)
_Infra.Actions.Registration.g.cs
_Metadata.Persistence.g.cs
```

### Usage rules

- **Always use `VirtualFolderHints`.** Never construct hint names by string concatenation.
- **Artifact names must be unique per type.** Two templates for the same type must use different artifact suffixes.
- **Always pass `namespacePrefix` to `ForType`.** See below; this one is load-bearing.

### The `namespacePrefix` parameter

Three helpers accept it, and they do **not** treat it the same way:

| Helper | Uses it? |
|--------|----------|
| `ForType` | **Yes**: it becomes the first segment of the hint name |
| `ForAssembly` | No: one output per assembly, nothing to disambiguate |
| `ForMetadata` | No: same reason |

For `ForType` the parameter is optional in the signature but not in practice:

```csharp
VirtualFolderHints.ForType("Invoice", "Repository", "Sales")   // Sales.Invoice.Repository.g.cs
VirtualFolderHints.ForType("Invoice", "Repository")            // Invoice.Repository.g.cs  <- collides
```

`Sales.Invoice` and `Archive.Invoice` produce the same hint without the prefix. `AddSource` throws on a duplicate hint name, and since the entire ecosystem is one `IIncrementalGenerator`, that exception aborts generation for **every** feature in the compilation, the visible symptom being a wall of CS0246 errors rather than the duplicate-hint message itself.

The helper drops a null, empty, or `"<global namespace>"` prefix on its own, so there is no case in which passing the model's namespace is wrong. Pass it unconditionally.

---

## NamingHelper: Suffix Deduplication

When generating derived names (class names, method names, DI registration calls), naive string concatenation produces doubled suffixes. `NamingHelper.AppendSuffix()` solves this.

### The problem

```csharp
// Type name: "CancelReservationMutation"
// Desired suffix: "MutationInvoker"
// Naive concatenation: "CancelReservationMutationMutationInvoker"  <-- doubled "Mutation"
```

### The solution

```csharp
var name = NamingHelper.AppendSuffix("CancelReservationMutation", "MutationInvoker");
// Result: "CancelReservationMutationInvoker"
```

### How it works

1. If the name already ends with the full suffix, return as-is.
2. For each uppercase letter (word boundary) in the suffix, check if the name ends with that prefix of the suffix.
3. If a match is found, append only the remaining portion.
4. If no overlap, append the full suffix.

### Deduplication examples

| Input Name | Suffix | Result |
|-----------|--------|--------|
| `CancelReservation` | `MutationInvoker` | `CancelReservationMutationInvoker` |
| `CancelReservationMutation` | `MutationInvoker` | `CancelReservationMutationInvoker` |
| `GetOrder` | `Invoker` | `GetOrderInvoker` |
| `GetOrderInvoker` | `Invoker` | `GetOrderInvoker` |
| `Order` | `Repository` | `OrderRepository` |
| `OrderRepository` | `Repository` | `OrderRepository` |

### Where to use

`NamingHelper.AppendSuffix()` must be used everywhere a type name is combined with a suffix:

- Generated class names (`{Type}Invoker`, `{Type}Repository`)
- DI registration method names (`Add{Type}Invoker`)
- Extension class names (`{Type}Extensions`)
- Metadata entries with type name + suffix

---

## Feature System

Each feature is an `internal static class` (or `internal static partial class` for large features) with a `public static void Register()` method. Features follow one of two patterns: **per-type** or **aggregate**.

### Per-type pattern

Most features use `ForAttributeWithMetadataName` to process individual attributed types:

```csharp
internal static class CachingFeature
{
    public static void Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features)
    {
        var provider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.Cacheable,
                GeneratorHelpers.IsClassOrRecord,
                CachingTransform.TransformCacheable)
            .Where(static m => m is not null);

        context.RegisterSourceOutputSafe(
            provider.Combine(features).Where(x => x.Right.HasCaching),
            static (ctx, x) => GenerateCacheable(ctx, x.Left!));
    }

    private static void GenerateCacheable(SourceProductionContext ctx, CacheableModel model)
    {
        ctx.AddSource(new CacheableTemplate(model).RenderOutput());
    }
}
```

Key elements:
- `ForAttributeWithMetadataName` -- Roslyn indexes attributes by metadata name. Only files containing the attribute are scanned.
- `.Where(static m => m is not null)` -- Discards invalid transforms.
- `.Combine(features).Where(x => x.Right.HasCaching)` -- Gates the pipeline on the feature flag.
- `static` lambdas -- Required for incremental generators. Instance-capturing lambdas break caching.
- **`RegisterSourceOutputSafe`, not `RegisterSourceOutput`** -- crash isolation, see below.
- **`ctx.AddSource(artifact)`, not the two-argument form** -- single emission point, see above.

### Crash isolation: RegisterSourceOutputSafe

Every one of the ~130 output registrations in the generator uses `RegisterSourceOutputSafe`. This is not a style preference.

Because the whole ecosystem is served by one `IIncrementalGenerator`, Roslyn gives its output registrations no isolation from each other. An unhandled exception in any single transform or template surfaces as one CS8785 ("Generator failed to generate source") and suppresses the output of **every** feature in the compilation. A malformed attribute argument on one entity takes out repositories, endpoints, DI registration, and host wiring simultaneously, and the diagnostic names the generator, not the feature that broke.

`shared/SourceGen/SafeSourceOutput.cs` provides a drop-in extension on `IncrementalGeneratorInitializationContext`, with overloads for both `IncrementalValueProvider<T>` and `IncrementalValuesProvider<T>`. It wraps the callback in a `try/catch`, reports the failure as **PRAG9000**, and lets every other output proceed.

Two details:

- **PRAG9000 is a `DiagnosticSeverity.Error`.** The code that output should have generated is simply not there; a warning would let the build run on into a cascade of CS0246s whose real cause is buried in warning output. The failure has to surface where it happens.
- **`OperationCanceledException`, `OutOfMemoryException`, and `StackOverflowException` are not caught.** Cancellation must propagate for the IDE to stay responsive; the other two are not recoverable.

### Aggregate pattern

Some features need all models to generate assembly-level artifacts (DI registration, metadata, host wiring). These use `.Collect()`:

```csharp
var allActions = validActions.Collect();

context.RegisterSourceOutputSafe(allActions, static (ctx, models) =>
{
    if (models.IsDefaultOrEmpty) return;
    ctx.AddSource(new ActionsRegistrationTemplate(models).RenderOutput());
});
```

`.Collect()` gathers all models into an `ImmutableArray<T>`. The trade-off: any change to any model triggers regeneration of the aggregate artifact. Use per-type pipelines when possible.

This `IncrementalValueProvider<ImmutableArray<T>>` is the one place a raw `ImmutableArray<T>` is correct: it is pipeline plumbing that Roslyn compares elementwise itself, not a cached model field.

Aggregate templates must sort their input before rendering. The pipeline delivers models in whatever order Roslyn collected them, which is not stable across runs; unsorted output produces spurious diffs in generated files and flapping snapshot tests.

### Orchestrator pattern (Persistence)

The Persistence feature is the largest. `PersistenceFeature` owns the shared entity providers, delegates to **six** sub-features, adds two inline outputs of its own, and returns the merged entity provider so downstream features can consume it:

```csharp
internal static class PersistenceFeature
{
    public static IncrementalValueProvider<ImmutableArray<EntityMetadataModel>> Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features,
        IncrementalValueProvider<ImmutableArray<QueryModel>> resourceQueries)
    {
        // Shared providers (computed once, reused by all sub-features)
        var referencedEntityProvider = ...;   // from referenced assemblies
        var currentEntityProvider = ...;      // from this compilation
        var allEntitiesProvider = referencedEntityProvider
            .Combine(currentEntityProvider)
            .Select(static (combined, _) => MergeEntities(combined.Left, combined.Right));

        // Per-entity fan-out
        var perEntityProvider = allEntitiesProvider.SelectMany(static (e, _) => e);

        EntityCoreFeature.Register(context, perEntityWithFeatures, entitiesWithFeatures);
        RepositoryFeature.Register(context, perEntityWithFeatures, entitiesWithFeatures, isDebugProvider);
        QueryFeature.Register(context, features, resourceQueries: resourceQueries);
        ProjectionFeature.Register(context, allEntitiesProvider, features);
        AdvancedFeature.Register(context, entitiesWithFeatures, features);
        DbContextFeature.Register(context, allEntitiesProvider, features);

        // Inline outputs owned by the orchestrator itself
        GenerateDerivedTypeSetters(...);        // TPH/TPT/TPC derived-type setters
        GenerateEntityCrudPermissions(...);     // HasPersistenceEFCore && HasAuthorization

        return allEntitiesProvider;
    }
}
```

Each sub-feature owns its templates and registers its own outputs, keeping every file under 300 lines while the orchestrator manages the shared providers.

**Two files in `Features/Persistence/` are not sub-features.** `ReadContractFeature` and `RollUpFeature` are registered directly from `PragmaticSourceGenerator.Initialize()` and take only `context`, with no feature gate. If you count `*Feature.cs` files in that folder you get nine: one orchestrator, six delegated sub-features, two independent siblings.

**Four `*BoundaryReader.cs` classes** (`BatchProgressBoundaryReader`, `EventOutboxBoundaryReader`, `MessagingOutboxBoundaryReader`, `SagaPersistenceBoundaryReader`) resolve boundary marker attributes through `Compilation.GetTypeByMetadataName` rather than `ForAttributeWithMetadataName`. That is deliberate: in host mode the markers live in **referenced assemblies**, which a syntax-based provider cannot see. It is one of the few sanctioned compilation queries outside `FeatureDetector`; do not take it as licence for the general case.

---

## Host Mode vs Module Mode

The generator produces different artifacts depending on whether the compilation is a module (library) or a host (executable).

### Module mode

The default when the compilation is a class library. Generates:

- Per-type artifacts with namespace-then-type hint names (`Sales.Invoice.Repository.g.cs`)
- Boundary interfaces (`_Boundary.Billing.Interface.g.cs`)
- Assembly-level infrastructure (`_Infra.Actions.Registration.g.cs`)
- JSON metadata for cross-assembly discovery (`_Metadata.Persistence.g.cs`)

### Host mode

Two flags, and they gate different things: do not conflate them.

**`IsHostMode`** is true when the project is an executable with an entry point, and nothing more (`CompositionDetector.IsHostProject`). It gates the host-level *persistence* artifacts, which cannot be generated in module assemblies because they depend on EF Core types the module may not reference:

- **EntityConfig** files (`EntityConfig.{Entity}.g.cs`) -- `IEntityTypeConfiguration<T>` implementations
- **DbContext** files (`DbContext.{Boundary}.g.cs`) -- Boundary-scoped `DbContext` classes
- **Endpoint** registration (`Endpoint.{Action}.{Boundary}.g.cs`)

**`IsHostCompositionMode`** adds the requirement that the project reference `Pragmatic.Composition.Host`. Only with this flag does the Composition feature emit its aggregated DI wiring (`PragmaticHost.Services.g.cs`).

The narrower flag exists because "is an executable" is a much weaker statement than "is a Pragmatic host". A console utility, a benchmark project, or a test host can satisfy `IsHostMode` while having no composition root to wire; emitting host aggregation into those produces code that does not compile. `CompositionDetector` also exposes `IsTestProject()` and a `GeneratorMode` enum (`Library` / `Host` / `Skip`) folding all three checks together.

### Cross-assembly entity discovery

When the host references a module assembly, the generator needs to discover entities defined in that module. This works via a two-step mechanism:

1. **At module build time**: The generator emits `_Metadata.Persistence.g.cs` containing a JSON-serialized list of entities with their types, properties, relationships, and trait flags.
2. **At host build time**: `EntityMetadataReader.ReadFromPersistenceMetadata()` reads this JSON from the referenced assembly's embedded metadata and returns `EntityMetadataModel` records.

The host generator then processes these models exactly as if the entities were declared locally.

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

### Rules

- Each `.g.cs` file contains at most 1 `partial class` with at most 1 nested class.
- Nested classes referenced from the host (filters, repositories, invokers) must be `public`, not `internal`.
- Invokers are nested: `PlaceOrder.Invoker`, not `PlaceOrderInvoker`.
- Filters are nested: `Invoice.SoftDeleteFilter`, not `SoftDeleteFilter_Invoice`.

### Why nested?

Nested classes keep the generated type physically close to the trigger type in the IDE. When you expand `Invoice` in Solution Explorer, you see `Repository`, `SoftDeleteFilter`, `TenantFilter`, and `Create` right there. This is more discoverable than top-level classes scattered across the generated files list.

---

## Cross-Feature Enrichers

The `Compositions/` directory holds four enrichers that operate across feature boundaries, each paired with a contribution model in `Compositions/Models/`:

| Enricher | Contribution | What it adds |
|----------|--------------|--------------|
| `ComputedDefaultEnricher` | `ComputedDefaultContribution` | Computed default values onto entity/DTO models |
| `PresetEnricher` | `PresetContribution` | Preset configurations composed across modules |
| `ResilienceEnricher` | `ResilienceContribution` | Resilience policy wiring onto invoker models (`HasResilience`) |
| `SoftDeleteEnricher` | `SoftDeleteContribution` | Soft-delete awareness into queries and filters |

Enrichers modify models produced by one feature to incorporate information from another feature. They run during the transform phase, before templates are invoked. An enricher is the right tool when one feature must *influence* another's output; when one feature must *supply* whole models to another (as Resource and Trait do), pass a provider from `Initialize` instead.

---

## TraitPropertyResolver

`Core/TraitPropertyResolver.cs` resolves "virtual" properties that `EntityTraitsTemplate` will generate for entities marked with `[Entity]`. Other features (Mapping, Endpoints) call `GetTraitProperties()` to discover properties like `Id`, `PersistenceId`, `CreatedAt`, `IsDeleted` that are not yet visible on the `INamedTypeSymbol` during the same generator pass.

This is necessary because a source generator cannot see the output of its own generation within the same pass. If the Mapping feature needs to map `CreatedAt` (which is generated by the Persistence feature's `EntityTraitsTemplate`), it must ask `TraitPropertyResolver` whether that property will exist.

### Key behaviors

- Only applies to types with `[Entity]` attribute.
- Skips properties already declared manually on the type (manual declarations take precedence).
- Detects `[Auditable]` and `[SoftDelete]` attributes via `TraitDetector.Detect()`.
- Returns `ImmutableArray<VirtualProperty>` with `Name` and `TypeFullName` for each generated property.

---

## Diagnostics

Features report diagnostics via `ctx.ReportDiagnostic()` when they detect issues in the user's source code. Each feature has a `Diagnostics/` subdirectory with `DiagnosticDescriptor` constants.

### Diagnostic ID ranges

| Range | Module |
|-------|--------|
| `PRAG0001`--`PRAG0099` | Result |
| `PRAG0100`--`PRAG0199` | Ensure |
| `PRAG0200`--`PRAG0299` | Validation |
| `PRAG0300`--`PRAG0399` | Mapping |
| `PRAG0400`--`PRAG0449` | Actions |
| `PRAG0500`--`PRAG0599` | Endpoints |
| `PRAG0600`--`PRAG0699` | Persistence / Persistence.EFCore |
| `PRAG0700`--`PRAG0799` | Persistence: query pipeline |
| `PRAG0800`--`PRAG0899` | Messaging |
| `PRAG0900`--`PRAG0999` | Temporal |
| `PRAG1000`--`PRAG1099` | Identity / Authorization |
| `PRAG1100`--`PRAG1199` | Persistence: data ownership |
| `PRAG1400`--`PRAG1499` | DependencyInjection |
| `PRAG1600`--`PRAG1699` | Composition |
| `PRAG1700`--`PRAG1799` | Caching |
| `PRAG1800`--`PRAG1899` | Internationalization |
| `PRAG1900`--`PRAG1999` | Documents |
| `PRAG2000`--`PRAG2099` | Configuration |
| `PRAG2200`--`PRAG2249` | Patch |
| `PRAG2500`--`PRAG2549` | Jobs |
| `PRAG2600`--`PRAG2699` | Traits + Resource (shared) |
| `PRAG2700`--`PRAG2749` | ValueObject |
| `PRAG2750`--`PRAG2799` | Lifecycle events |
| `PRAG2800`--`PRAG2899` | Serialization / AOT |

The authoritative map is [`docs/diagnostics.md`](../../docs/diagnostics.md), generated from the descriptors, with the next free ID of every range.

Two ranges sit outside the per-module scheme:

| ID | Meaning |
|----|---------|
| `PRAG9000` | A source generator output failed and was caught by `RegisterSourceOutputSafe`. **Error.** |
| `PRAGS001`--`PRAGS004` | The four diagnostic **suppressors** (see below); a suppression ID, not a diagnostic |

### Building descriptors

Use `DiagnosticFactory` (`shared/SourceGen/DiagnosticDescriptors.cs`) rather than constructing `DiagnosticDescriptor` by hand. It supplies the `"Pragmatic.Design"` category and the correct help link for every descriptor:

```csharp
internal static class TemporalDiagnostics
{
    public static readonly DiagnosticDescriptor UnsupportedPropertyType = DiagnosticFactory.Warning(
        "PRAG0905", "Timezone conversion attribute on unsupported property type",
        "Property '{0}.{1}' has type '{2}' — timezone conversion attributes only apply to "
        + "DateTimeOffset or DateTime (and their nullable forms); the attribute is ignored",
        "Apply timezone conversion attributes to DateTimeOffset/DateTime properties, or remove the attribute.");
}
```

`Error`, `Warning`, `Info`, and `Hidden` factory methods exist.

### Reporting pattern

`DiagnosticExtensions` adds `ReportDiagnostic` overloads taking a descriptor plus a `Location?`, a `SyntaxNode`, or an `ISymbol`, so `Diagnostic.Create` rarely appears in feature code:

```csharp
private static void ReportDiagnostics(SourceProductionContext ctx, ActionModel model)
{
    if (!model.IsPartial)
        ctx.ReportDiagnostic(ActionsDiagnostics.MustBePartial, model.Location?.ToLocation(), model.TypeName);
}
```

`model.Location` is a `LocationInfo?`, never a raw `Location`; see "Diagnostic Positions" above.

Two habits that matter more than they look:

**Report from a separate output callback, or at least before filtering.** The point is to keep invalid models visible. If the transform drops invalid input, or the pipeline filters it out before anyone looks at it, the user gets silence instead of an explanation. `TemporalFeature` shows the minimal version: the transform flags unsupported property types rather than discarding them, and `Generate` reports PRAG0905 against them *before* filtering to the valid set.

**Sort before reporting.** Diagnostics arrive in pipeline order, which is not stable. Ordering them (`OrderBy(...).ThenBy(...)`) keeps build output and test assertions deterministic.

### Design-time counterparts

Some diagnostics are worth reporting before a build happens. `Pragmatic.SourceGenerator.Analyzers` ships `NotPartialClassAnalyzer`, which mirrors the generator's 13 "must be `partial`" IDs at design time, and `EventCycleAnalyzer` (PRAG0822, domain-event cascade cycles). `Pragmatic.SourceGenerator.CodeFixers` ships `MakeClassPartialCodeFixProvider` with a batch fix-all.

If you add an attribute that requires `partial`, register its descriptor in `NotPartialDiagnosticDescriptors.cs` **and** add the ID to the fixer's `FixableDiagnosticIds`; otherwise the user gets a build error with no squiggle and no lightbulb.

---

## Diagnostic Suppressors

`Suppressors/` holds four `DiagnosticSuppressor` implementations that ship with the generator. They exist because the IDE analyzes the user's source *without* seeing the generated partial halves, producing warnings that are wrong by construction.

| Class | ID | Suppresses | Why |
|-------|----|------------|-----|
| `EntityPropertyNullabilitySuppressor` | PRAGS001 | CS8618 | Entity properties are initialized by the generated `Create()` factory and trait templates |
| `PartialMethodStaticSuppressor` | PRAGS002 | CA1822 | The method is in a partial class whose generated half may use instance data |
| `GeneratedParameterValidationSuppressor` | PRAGS003 | CA1062 | Parameters in Pragmatic-generated types are validated by the generated DI constructor |
| `UnusedMemberSuppressor` | PRAGS004 | IDE0051 | A private member may be used only by generated partial-class code |

All four share `SuppressionHelper`, which decides whether the containing symbol is a Pragmatic-decorated type. Keep that check narrow: a suppressor that fires outside Pragmatic types hides real bugs in the user's own code, and it does so invisibly.

---

## Testing with GeneratorTestHelper

Generator tests use the shared `GeneratorTestHelper` from `shared/SourceGen/Testing/`. It provides a minimal compilation environment for running the generator against inline source code.

### Test helper API

| Method | Purpose |
|--------|---------|
| `RunGenerator<T>(source, refs)` | Create a compilation from `source`, run generator `T`, return `SourceGenRunResult` |
| `GetGeneratedSource(result, hint)` | Get generated source by partial hint name match |
| `GetGeneratedSourcesAsDictionary(result)` | All generated files as `Dictionary<string, string>` |
| `HasCompilationErrors(result)` | Check for compilation errors after generation |
| `GetCompilationErrors(result)` | List all compilation errors |
| `GetGeneratorDiagnostics(result, prefix)` | Get diagnostics filtered by ID prefix (e.g., `"PRAG04"`) |
| `HasDiagnostic(result, id)` | Check if a specific diagnostic was emitted |
| `FromType<T>()` | Create a `MetadataReference` from a type's assembly |
| `FromTypeAssembly(type)` | Same, but for `static` classes that cannot be type arguments |
| `TryGetAssemblyReference(name)` | Optional reference by assembly name |

### SourceGenRunResult

```csharp
public sealed record SourceGenRunResult(
    GeneratorDriverRunResult RunResult,
    Compilation OutputCompilation,
    ImmutableArray<Diagnostic> Diagnostics)
{
    public ImmutableArray<SyntaxTree> GeneratedTrees => RunResult.GeneratedTrees;
    public bool HasGeneratedFiles => GeneratedTrees.Length > 0;
}
```

### Test base pattern

Each feature creates a thin wrapper that adds module-specific assembly references:

```csharp
public abstract class CachingGeneratorTestBase
{
    protected static SourceGenRunResult RunGenerator(string source)
    {
        var references = new[]
        {
            GeneratorTestHelper.FromType<CacheableAttribute>(),
            GeneratorTestHelper.FromType<IError>(),
        };
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references);
    }

    protected static string? GetGeneratedSource(SourceGenRunResult result, string hintName)
        => GeneratorTestHelper.GetGeneratedSource(result, hintName);

    protected static bool HasCompilationErrors(SourceGenRunResult result)
        => GeneratorTestHelper.HasCompilationErrors(result);
}
```

### Example test

```csharp
public class CachingGeneratorTests : CachingGeneratorTestBase
{
    [Fact]
    public void Cacheable_SimpleClass_GeneratesCacheKey()
    {
        var source = """
            using Pragmatic.Caching.Attributes;
            namespace MyApp;

            [Cacheable]
            public partial class GetOrderQuery
            {
                [CacheKey]
                public Guid OrderId { get; init; }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();
        var generated = GetGeneratedSource(result, "GetOrderQuery.CacheKey");
        generated.Should().NotBeNull();
        generated.Should().Contain("OrderId");
    }
}
```

### Testing a template without a compilation

Because templates are pure functions of the model, the cheapest test builds a model by hand and renders it, with no compilation and no generator driver. This is how most of the 40 snapshots in the suite are written:

```csharp
public class TemporalSnapshotTests
{
    private static ImmutableArray<TemporalBehaviorPropertyModel> SampleModels() =>
    [
        new TemporalBehaviorPropertyModel
        {
            ContainingTypeFqn = "MyApp.Orders.Dtos.OrderResponse",
            ContainingNamespace = "MyApp.Orders.Dtos",
            PropertyName = "CreatedAt",
            Behavior = "ToClientTimezone",
            IsSupportedPropertyType = true,
            PropertyTypeDisplay = "System.DateTimeOffset"
        }
    ];

    [Fact]
    public Task BehaviorsRegistration_MatchesSnapshot()
    {
        var source = new TemporalBehaviorsTemplate(SampleModels()).RenderOutput().Source.ToString();
        return Verify(source);
    }
}
```

Note the `Task` return type, returned directly from `Verify(...)`.

Reserve the full generator run for what template tests cannot cover: transform behaviour, feature gating, and diagnostics.

### Snapshot scrubbing is global: configure nothing

There is no `ModuleInitializer.cs` in `tests/Pragmatic.SourceGenerator.Tests/`, and none should be added. Verify's configuration lives once, repo-wide, in `shared/Testing/VerifyHelpers.cs` (`Pragmatic.Testing.VerifyConfiguration`). It carries its own `[ModuleInitializer]` and `Directory.Build.props` globs `$(SharedTestingPath)**\*.cs` into every project with `IsTest=true`, so it is already compiled into your test assembly before you write a line.

Four scrubbers run:

| Scrubber | Effect |
|----------|--------|
| Attribution header | Collapses the 3-line `// Generated by Pragmatic.Design, a framework by …` / `// https://pragmaticdesign.net` / `// This file belongs to your project…` block, and the tool line after it, to a stable marker |
| Generator banner | Collapses any other `// Generated by …` line to the same marker |
| Inline version | `v1.2.3`, `v1.2.3-beta.1+abc` → `v*` |
| ISO timestamp | `2026-01-31T12:00:00Z` → `<timestamp>` |

It also calls `DontScrubDateTimes()` and `DontScrubGuids()` so Verify's aggressive defaults do not mangle dates and GUIDs that are genuinely part of the generated output.

Write the test, run it, accept the `.verified.txt`. If some header form still leaks into a diff, extend `VerifyHelpers.cs`; a per-project scrubber would fix your suite and leave the other snapshots across the repo exposed.

---

## Shared Source Code (Zero NuGet Dependencies)

The generator assembly references no NuGet packages at generation time. Shared code lives in `shared/SourceGen/` and is linked in by `Directory.Build.props`, with no per-project wiring:

```xml
<!-- Auto-include shared SourceGen code for generators (excluding Testing subfolder) -->
<ItemGroup Condition="'$(IsGenerator)' == 'true' AND Exists('$(SharedSourceGenPath)')">
  <Compile Include="$(SharedSourceGenPath)*.cs" LinkBase="Shared" />
</ItemGroup>
```

Note the glob is `*.cs`, not `**\*.cs`: only the top level is linked, which is why `Testing/` is excluded here and picked up separately by the `IsTest` condition instead. Setting `IsGenerator` on a project is all it takes to get `CSharpTemplate`, `EquatableArray`, `SourceOutput`, and the rest.

All of this code compiles as **`netstandard2.0`**, which constrains the C# you can use in the generator itself: `System.Threading.Lock`, `params ReadOnlySpan<T>`, and inline arrays are unavailable, while `field`, extension members, primary constructors, collection expressions, and `init`/`required` (via `Polyfills.cs`) all work. The code the generator *emits* runs on `net10.0` and has no such limits. See `docs/CONVENTIONS.md`.

### Key shared files

| File | Purpose |
|------|---------|
| `CSharpTemplate*.cs` | Template base class (4 partial files) |
| `EquatableArray.cs` | Value-equatable collection wrapper, **mandatory** on model fields |
| `EquatableDictionary.cs` | Same, for maps; order-insensitive equality |
| `SafeSourceOutput.cs` | `RegisterSourceOutputSafe`: crash isolation, PRAG9000 |
| `SourceOutput.cs` | `ctx.AddSource(artifact)`, the single emission point |
| `Artifact.cs` | Readonly struct for hint name + source text, with `IsEmpty` |
| `NamingHelper.cs` | Suffix deduplication |
| `GeneratorModel.cs` | Base record for type-describing models |
| `GeneratorHelpers.cs` | Common `static` predicates (`IsClass`, `IsClassOrRecord`, `IsRecord`, `IsStruct`) |
| `AttributeNames.cs` | FQN constants for trigger attributes |
| `SymbolExtensions.cs` | Extension methods for `ISymbol` (namespace, accessibility, attributes) |
| `CompositionDetector.cs` | Host project, composition reference, test project, `GeneratorMode` |
| `DiagnosticDescriptors.cs` | `DiagnosticFactory` + the `ReportDiagnostic` extension overloads |
| `AccessModifier.cs` | `AccessModifier` enum + `ToKeyword()` |
| `TrackingNames.cs` | Stable `WithTrackingName` step names, so incrementality tests can assert a stage stayed cached |
| `CodeWriterOptions.cs` | Indentation configuration (spaces vs tabs, indent width) |
| `Polyfills.cs` | `init` / `required` support for `netstandard2.0` |
| `Testing/GeneratorTestHelper.cs` | Test runner for generator tests |
| `Testing/SourceGenRunResult.cs` | Test result record |

Two clarifications the table above implies but does not state:

- **`CSharpTemplate` is the only source-emitting API.** There is no builder alternative and no `CSharpBuilder` type; a comment naming one is pointing at nothing.
- **`TemplateHelpers` is not here.** `ParseAccessibility()` and `ToCamelCase()` live in `Pragmatic.SourceGenerator/Core/TemplateHelpers.cs`, alongside `VirtualFolderHints` and `LocationInfo`, and are internal to the generator project.

### Why linked source instead of a NuGet package?

Roslyn source generators run inside the compiler process. They can only reference assemblies that are compatible with the compiler's runtime. Linked source avoids version conflicts, assembly loading issues, and the overhead of packaging shared utilities.

---

## Directory Structure

```
Pragmatic.SourceGenerator/
├── src/
│   ├── Pragmatic.SourceGenerator/               # The generator (own NuGet package)
│   │   ├── PragmaticSourceGenerator.cs          # Entry point (IIncrementalGenerator)
│   │   ├── Core/
│   │   │   ├── DetectedFeatures.cs              # 37 Has* flags + 2 mode flags + provider
│   │   │   ├── FeatureDetector.cs               # Scans referenced assemblies for runtime types
│   │   │   ├── EfCoreProvider.cs                # Database provider enum
│   │   │   ├── VirtualFolderHints.cs            # Hint name helpers (internal to this assembly)
│   │   │   ├── LocationInfo.cs                  # Cache-safe diagnostic position
│   │   │   ├── TemplateHelpers.cs               # ParseAccessibility, ToCamelCase
│   │   │   └── TraitPropertyResolver.cs         # Virtual property resolution for traits
│   │   ├── Features/                            # one directory per feature
│   │   │   ├── Actions/                         # DomainAction, Mutation, CompositeAction, Boundary
│   │   │   ├── Caching/       ├── Composition/  ├── Configuration/
│   │   │   ├── Endpoints/     ├── FastEnum/     ├── Glossary/     # Glossary + Architecture + AsyncApi
│   │   │   ├── I18n/          ├── Identity/     ├── Jobs/
│   │   │   ├── Lifecycle/     ├── Manifest/     ├── Mapping/
│   │   │   ├── Messaging/     ├── Patch/        ├── Resource/
│   │   │   ├── Result/        ├── Serialization/├── Temporal/
│   │   │   ├── Traits/        ├── Validation/   ├── ValueObject/
│   │   │   └── Persistence/
│   │   │       ├── PersistenceFeature.cs        # Orchestrator -> 6 sub-features
│   │   │       ├── EntityCoreFeature.cs         # Entity relations, create, traits, setters
│   │   │       ├── RepositoryFeature.cs         # Repos, query filters, metadata
│   │   │       ├── QueryFeature.cs              # Query apply, GridFilter, FilterDto, Patch
│   │   │       ├── ProjectionFeature.cs         # Projectable DTOs, computed filters
│   │   │       ├── AdvancedFeature.cs           # Inheritance, hierarchy, timeline, lookup
│   │   │       ├── DbContextFeature.cs          # BoundaryDbContext, MigrationDbContext
│   │   │       ├── ReadContractFeature.cs       # registered from Initialize, NOT a sub-feature
│   │   │       ├── RollUpFeature.cs             # registered from Initialize, NOT a sub-feature
│   │   │       └── *BoundaryReader.cs           # 4 readers: markers in referenced assemblies
│   │   ├── Compositions/                        # Cross-feature enrichers (4) + contributions
│   │   ├── Suppressors/                         # PRAGS001-004 DiagnosticSuppressors
│   │   ├── Diagnostics/
│   │   └── shared/ (linked)                     # CSharpTemplate, EquatableArray, SourceOutput, ...
│   ├── Pragmatic.SourceGenerator.Analyzers/     # Design-time analyzers (own NuGet package)
│   └── Pragmatic.SourceGenerator.CodeFixers/    # "Make class partial" fixer (own NuGet package)
└── tests/
    ├── Pragmatic.SourceGenerator.Tests/         # the generator suite, with Verify snapshots
    ├── Pragmatic.SourceGenerator.Analyzers.Tests/
    └── Pragmatic.SourceGenerator.CodeFixers.Tests/
```

Every feature directory follows the same internal shape: `{Name}Feature.cs` at the root, plus `Models/`, `Templates/`, `Transforms/`, and `Diagnostics/` where needed.

The three `src/` projects are **three separate NuGet packages**. The generator project has no project reference to either sibling; `Pragmatic.SourceGenerator.CodeFixers` references `Pragmatic.SourceGenerator.Analyzers` with `PrivateAssets="all"` so the analyzer assembly is not packed twice.

---

## See Also

- [Adding a New Feature](feature-development.md) -- Step-by-step guide for adding a feature to the unified generator
- [CSharpTemplate API Reference](template-api.md) -- Complete reference for all template methods
- [Common Mistakes](common-mistakes.md) -- Mistakes that silently break generation or incremental caching
- [Troubleshooting](troubleshooting.md) -- Problem/solution guide for debugging generator issues
- Project rules: [`docs/CONVENTIONS.md`](../../docs/CONVENTIONS.md)
