# Adding a New Feature

This guide walks through the full process of adding a new feature to the Pragmatic unified source generator. Every SG feature follows the same Transform-Model-Template pipeline, and understanding why each step exists will save you from subtle caching bugs and non-deterministic output.

The worked example throughout is **`Features/Temporal/`** — a real, complete, small feature. It has all the moving parts (feature detection, six trigger attributes, a transform, a model, two templates, a diagnostic, a snapshot test) and fits in about 250 lines total, so you can read the whole thing alongside this guide. Every snippet below is the real code.

Where Temporal is not representative, the guide points at **`Features/ValueObject/`**, which is the other common shape: a standalone per-type feature with no runtime package to detect.

---

## 1. Architecture Overview

Source generation in Pragmatic follows a strict three-stage pipeline. Each stage has a single responsibility, and skipping or combining stages leads to incremental caching failures or non-deterministic output.

```
  Roslyn Syntax/Semantic          Immutable record          CSharpTemplate
  ─────────────────────►  Model  ─────────────────►  Generated .cs
        Transform                                       Template
```

### Why Three Stages?

| Stage | Responsibility | Why It Exists |
|-------|---------------|---------------|
| **Transform** | Convert `ISymbol` to a plain data model | Roslyn symbols are not cacheable -- they hold references to the entire compilation. The transform extracts only what is needed into a value-equality record so that the incremental pipeline can skip regeneration when nothing changed. |
| **Model** | Carry all data needed for code generation | Immutable records participate in value equality. If two compilations produce the same model, Roslyn skips the template entirely. This is the core of incremental performance. |
| **Template** | Render C# source text from the model | Templates never touch Roslyn APIs. They receive a fully resolved model and produce an `Artifact` (hint name + `SourceText`). This separation makes templates unit-testable without a compilation. |

### Key Invariants

- Transforms must be **deterministic**: same input symbol, same output model.
- Models must be **immutable records** whose every field is genuinely value-equatable — which for collections means `EquatableArray<T>`, not `ImmutableArray<T>` (see §4.1).
- Templates must be **pure functions of the model**: no `Compilation`, no `ISymbol`, no side effects.

---

## 2. Step 1: Feature Detection

Before your feature can generate anything, the unified generator needs to know the corresponding runtime package is referenced. Feature detection happens once per compilation in `FeatureDetector.Detect()`, and the result — a `DetectedFeatures` record — is threaded to every feature as an `IncrementalValueProvider<DetectedFeatures>`.

Skip this step if your feature has no runtime package to gate on. `ValueObjectFeature`, `FastEnumFeature`, `JobsFeature`, `LifecycleEventsFeature`, and the three `Glossary` features are registered unconditionally and take only `context`. Jump to §3.

### 2.1 Add a Flag to DetectedFeatures

Open `Core/DetectedFeatures.cs` and add a boolean property:

```csharp
// Core/DetectedFeatures.cs
internal sealed record DetectedFeatures
{
    // ... existing flags ...

    /// <summary>True when the compilation references the temporal JSON behaviors registry (Pragmatic.Temporal.Json).</summary>
    public bool HasTemporalJson { get; init; }
}
```

Prefer one flag per *assembly*, not per module. Temporal has three — `HasTemporal` (the core clock), `HasTemporalJson` (the JSON behaviors registry), `HasTemporalAspNetCore` (the middleware) — because a consumer can reference any subset, and generating registration code for an assembly that is not there produces CS0246 in the consumer's build.

### 2.2 Add the Detection Check

Open `Core/FeatureDetector.cs` and add a `TypeExists()` call in `Detect()`. Pick a type that is always present when the package is referenced and unlikely to move — the main attribute, or a well-known public type:

```csharp
// Core/FeatureDetector.cs
public static DetectedFeatures Detect(Compilation compilation) => new()
{
    // ... existing checks ...
    HasTemporalJson = TypeExists(compilation, "Pragmatic.Temporal.Json.Behaviors.TemporalJsonBehaviorRegistry"),
};
```

`Detect` is called once per compilation from `CompilationProvider`, never per syntax node. Never call `compilation.GetTypeByMetadataName` from a transform — that is a per-node compilation query and it shows up directly as IDE lag.

### 2.3 FQN Rules

Getting the fully qualified name wrong is the most common reason a feature is not detected — and it fails **silently**: the flag is `false`, the pipeline is gated off, and nothing is generated or reported.

| Scenario | FQN Format | Example |
|----------|-----------|---------|
| Non-generic type | `Namespace.TypeName` | `Pragmatic.Caching.Attributes.CacheableAttribute` |
| Generic type with 1 param | `Namespace.TypeName\`1` | `Pragmatic.Mapping.Attributes.MapFromAttribute\`1` |
| Generic type with 2 params | `Namespace.TypeName\`2` | `Pragmatic.Persistence.Query.Attributes.QueryAttribute\`2` |
| Nested type | `Namespace.Outer+Inner` | `Pragmatic.Persistence.Entity.Relation+OneToMany\`1` |
| Types in `.Attributes` sub-ns | Use full path | `Pragmatic.Temporal.Attributes.AsUtcAttribute` |
| Root namespace exception | No `.Attributes` | `Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute` |

The backtick-arity for generic types is required by Roslyn's `GetTypeByMetadataName`. If you omit it, the type will not be found and the feature will silently remain inactive.

### 2.4 Declare Attribute FQNs in AttributeNames

Add the FQNs your feature triggers on to `shared/SourceGen/AttributeNames.cs` and reference the constants — not string literals — in `ForAttributeWithMetadataName`:

```csharp
// shared/SourceGen/AttributeNames.cs
// Temporal - Timezone conversion behaviors
public const string TemporalAsUtc = "Pragmatic.Temporal.Attributes.AsUtcAttribute";
public const string TemporalFromClientTimezone = "Pragmatic.Temporal.Attributes.FromClientTimezoneAttribute";
public const string TemporalToClientTimezone = "Pragmatic.Temporal.Attributes.ToClientTimezoneAttribute";
// ... three more
```

Be aware that this convention is not yet universally followed: several features still carry their trigger FQN as a private const on the feature class (`ReadContractFeature`, `RollUpFeature`, `GlossaryFeature`, the `*BoundaryReader` classes). Follow the convention for new code; a literal you can only find by grepping is exactly how a typo survives.

---

## 3. Step 2: Create Feature Directory

Each feature lives in its own directory under `Features/`. Every file contains exactly one class.

```
Features/Temporal/
    TemporalFeature.cs                        # Registration (orchestrator)
    Diagnostics/
        TemporalDiagnostics.cs                # PRAG descriptors
    Models/
        TemporalBehaviorPropertyModel.cs      # Immutable data model
    Templates/
        TemporalBehaviorsTemplate.cs          # The generated registration
        TemporalBehaviorsMetadataTemplate.cs  # Composition metadata attribute
    Transforms/
        TemporalBehaviorTransform.cs          # ISymbol -> Model
```

`Diagnostics/` is optional if the feature reports none. Everything else is mandatory: a feature that renders its output inline instead of through a `Templates/` class violates `docs/CONVENTIONS.md` («Generators emit through CSharpTemplate»).

---

## 4. Step 3: Define the Model

Models carry all the information a template needs. They must be records whose equality is a genuine value comparison, because that comparison is the only thing standing between you and a template that re-renders on every keystroke.

### 4.1 Model Design Rules

- **Use `sealed record`.** Records give structural equality; `sealed` prevents a subclass from breaking it.
- **Use `EquatableArray<T>` for every collection.** Never `List<T>`, never `T[]`, and — this is the one that catches people — never `ImmutableArray<T>`. `ImmutableArray<T>` is a struct wrapper over an array and compares the array **reference**, so two models describing identical source are never equal. Use `EquatableDictionary<TKey, TValue>` for maps.
- **Use `LocationInfo?`, never `Location`.** A raw `Location` pins a `SyntaxTree` to a compilation that may be dead by the time the diagnostic is reported.
- **No `ISymbol` or `Compilation` references.** They hold the whole compilation graph and change identity every pass.
- **Mark required properties `required`.** Prevents constructing an incomplete model.
- **Inherit from `GeneratorModel` when the model describes a type.** It supplies `Namespace`, `TypeName`, `Accessibility`, `TypeKind`, and computed `FullTypeName`. Skip it when the model describes something else — Temporal's model describes a *property*, so it inherits nothing and carries its own flat fields.

### 4.2 The Real Model

```csharp
// Features/Temporal/Models/TemporalBehaviorPropertyModel.cs
namespace Pragmatic.SourceGenerator.Features.Temporal.Models;

/// <summary>
///     A DTO property annotated with a timezone conversion attribute
///     ([AsUtc], [From/ToClientTimezone], [From/ToBusinessTimezone], [KeepTimezone]).
///     Flat and value-equatable for incremental caching.
/// </summary>
internal sealed record TemporalBehaviorPropertyModel
{
    /// <summary>Fully qualified containing type (no global:: prefix), e.g. "App.Dtos.OrderResponse".</summary>
    public required string ContainingTypeFqn { get; init; }

    /// <summary>Containing namespace, used to derive the assembly prefix. Empty for global namespace.</summary>
    public required string ContainingNamespace { get; init; }

    /// <summary>The CLR property name.</summary>
    public required string PropertyName { get; init; }

    /// <summary>The TemporalJsonBehavior enum member name (e.g. "ToClientTimezone").</summary>
    public required string Behavior { get; init; }

    /// <summary>Whether the property type is DateTimeOffset/DateTime (or their nullable forms).</summary>
    public required bool IsSupportedPropertyType { get; init; }

    /// <summary>Display string of the property type, for the PRAG0905 message.</summary>
    public required string PropertyTypeDisplay { get; init; }
}
```

Six `string`/`bool` fields, all value types or strings. The record's generated `Equals` is a correct value comparison with no help needed — this is the shape to aim for.

Note `IsSupportedPropertyType`. The transform does **not** drop unsupported properties; it flags them, so the feature can report PRAG0905 against them before filtering them out. Dropping invalid input in the transform means you can never tell the user why.

### 4.3 When You Do Need a Collection

```csharp
using Pragmatic.SourceGen;

internal sealed record ValueObjectModel
{
    public required string TypeName { get; init; }

    // EquatableArray, with Empty as the default so a default-constructed model is safe.
    public EquatableArray<ValueObjectParameter> ValidateParameters { get; init; }
        = EquatableArray<ValueObjectParameter>.Empty;
}
```

Assigning is unchanged — there is an implicit conversion from `ImmutableArray<T>`, so a transform that ends in `.ToImmutableArray()` needs no edit. Collection expressions work (`[a, b]`), because `EquatableArray<T>` carries `[CollectionBuilder]`. Where a template needs an `ImmutableArray`-specific API, call `.AsImmutableArray()` — which is exactly what `ValueObjectTemplate` does.

The `GeneratorModel` base provides these inherited properties:

| Property | Type | Description |
|----------|------|-------------|
| `Namespace` | `string` | Target type namespace (empty for global) |
| `TypeName` | `string` | Target type name |
| `Accessibility` | `string` | `"public"`, `"internal"`, etc. |
| `TypeKind` | `string` | `"class"`, `"record"`, `"struct"`, etc. |
| `FullTypeName` | `string` | Computed: `Namespace.TypeName` |

---

## 5. Step 4: Write the Transform

The transform converts Roslyn symbols into your model. It runs inside Roslyn's incremental pipeline on the compiler's thread pool, so it must be deterministic, fast, and free of side effects.

### 5.1 Transform Rules

- A `static` class with a `static` method taking `GeneratorAttributeSyntaxContext`.
- Return `null` for input you cannot model at all — the pipeline's `.Where(m => m is not null)` discards it. Return a model with a validity flag when you want to report a diagnostic first.
- Extract attribute arguments with `GetNamedArgument<T>()` (from `SymbolExtensions`).
- Never store `ISymbol`, `Location`, or `Compilation`. Extract strings, flags, and `LocationInfo`.
- Do not call `compilation.GetTypeByMetadataName` here. That belongs in `FeatureDetector`.

### 5.2 The Real Transform

```csharp
// Features/Temporal/Transforms/TemporalBehaviorTransform.cs
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Temporal.Models;

namespace Pragmatic.SourceGenerator.Features.Temporal.Transforms;

internal static class TemporalBehaviorTransform
{
    public static TemporalBehaviorPropertyModel? Transform(GeneratorAttributeSyntaxContext context, string behavior)
    {
        if (context.TargetSymbol is not IPropertySymbol property)
            return null;

        var containingType = property.ContainingType;
        if (containingType is null)
            return null;

        var type = property.Type;
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];

        var isSupported = type.SpecialType == SpecialType.System_DateTime
                          || (type.Name == "DateTimeOffset"
                              && type.ContainingNamespace is { Name: "System", ContainingNamespace.IsGlobalNamespace: true });

        return new TemporalBehaviorPropertyModel
        {
            ContainingTypeFqn = containingType.ToDisplayString(),
            ContainingNamespace = containingType.ContainingNamespace is { IsGlobalNamespace: false } ns
                ? ns.ToDisplayString()
                : "",
            PropertyName = property.Name,
            Behavior = behavior,
            IsSupportedPropertyType = isSupported,
            PropertyTypeDisplay = property.Type.ToDisplayString()
        };
    }
}
```

Three things worth copying:

- **The target is not always a type.** `context.TargetSymbol is not IPropertySymbol` — Temporal triggers on properties. Match the symbol kind your predicate allows.
- **The extra `behavior` parameter.** Six attributes share one transform; the feature passes a discriminator string per pipeline instead of writing six near-identical transforms.
- **Nullable unwrapping before the type check.** `DateTime?` must be recognized as supported.

### 5.3 What Can Go Wrong

| Symptom | Cause | Fix |
|---------|-------|-----|
| Template re-runs on every keystroke | `ImmutableArray<T>` / `List<T>` / `Location` on the model | `EquatableArray<T>`, `LocationInfo` |
| Feature never triggers | Wrong FQN in `ForAttributeWithMetadataName` | Check `AttributeNames.cs`; verify backtick-arity |
| Null reference in transform | Assumed `context.Attributes` is non-empty or the symbol is a type | Match the symbol kind first, guard `context.Attributes` |
| Diagnostic never appears in tests but works in the build | `LocationInfo.From` returned `null` because the test's syntax tree has no `FilePath` | Parse test sources with an explicit path |

---

## 6. Step 5: Create the Template

Templates extend `CSharpTemplate` and produce an `Artifact` containing the hint name and source text. They are pure functions of the model.

### 6.1 Template Structure

| Member | Required? | Purpose |
|--------|-----------|---------|
| `RenderOutput()` | yes | Returns the `Artifact` — hint name plus `ToSourceText()` |
| `RenderFile()` | yes | Writes the file body using the structured API |
| `Validate()` | optional | Return `false` to generate nothing (default `true`) |
| `GeneratorName` | optional | `// Generated by {name} v{version}` header |
| `SourceInfo` | optional | `// Source: {info}` header |
| `TriggerInfo` | optional | `// Trigger: {info}` header |

`Validate()` returning `false` does not throw or report — `ToString()` returns `null`, `ToSourceText()` renders empty content, and the emission helper skips the file. That is the sanctioned way to say "nothing to generate here", and it is also why a missing output is silent (see [Troubleshooting](troubleshooting.md)).

### 6.2 The Real Template

```csharp
// Features/Temporal/Templates/TemporalBehaviorsTemplate.cs
internal sealed class TemporalBehaviorsTemplate : CSharpTemplate
{
    private const string RegistryFqn = "global::Pragmatic.Temporal.Json.Behaviors.TemporalJsonBehaviorRegistry";
    private const string BehaviorEnumFqn = "global::Pragmatic.Temporal.Json.Behaviors.TemporalJsonBehavior";

    private readonly ImmutableArray<TemporalBehaviorPropertyModel> _models;
    private readonly string _namespacePrefix;
    private readonly string _className;
    private readonly string _methodName;

    public TemporalBehaviorsTemplate(ImmutableArray<TemporalBehaviorPropertyModel> models)
    {
        _models = models;
        _namespacePrefix = DeriveNamespacePrefix(models);
        // ... derive _className / _methodName from the prefix
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Temporal";
    protected override string? SourceInfo =>
        $"Timezone behaviors for {_models.Length} propert{(_models.Length == 1 ? "y" : "ies")}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Temporal", _namespacePrefix, "Behaviors"),
        ToSourceText());

    protected override bool Validate() => _models.Length > 0;

    public override void RenderFile()
    {
        AddUsings("Microsoft.Extensions.DependencyInjection");

        AppendNamespace(_targetNamespace);
        AppendLine();

        XmlSummary($"Registers the timezone conversion behaviors declared in this assembly ({_models.Length} total).");

        Class(_className, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true, IsStatic = true });
    }

    private void RenderMethodBody()
    {
        // Deterministic order — see below.
        foreach (var model in _models.OrderBy(m => m.ContainingTypeFqn).ThenBy(m => m.PropertyName))
        {
            AppendLine(
                $"{RegistryFqn}.Register(typeof(global::{model.ContainingTypeFqn}), \"{model.PropertyName}\", "
                + $"{BehaviorEnumFqn}.{model.Behavior});");
        }

        AppendLine();
        AppendLine("return services;");
    }
}
```

Three habits on display:

- **Fully qualified type references with `global::`.** Generated code lands in the user's namespace, where any short name can be shadowed. Only well-known framework namespaces get an `AddUsing`.
- **`OrderBy` before rendering.** The pipeline hands you models in whatever order Roslyn collected them, which is not stable across runs. Unsorted output produces spurious diffs in generated files and flapping snapshot tests.
- **`Validate()` guarding the aggregate.** With zero models the extension method would be an empty registration nobody calls; rendering nothing is better than rendering noise.

### 6.3 Hint Name Convention

Always use `VirtualFolderHints` (`Core/VirtualFolderHints.cs`). Never build a hint name with string concatenation.

| Helper | Pattern | Example |
|--------|---------|---------|
| `ForType(typeName, artifact, namespacePrefix)` | `{Ns}.{Type}.{Artifact}.g.cs` | `Sales.Invoice.Repository.g.cs` |
| `ForAssembly(category, ns, ext)` | `_Infra.{Category}.{Ext}.g.cs` | `_Infra.Temporal.Behaviors.g.cs` |
| `ForMetadata(ns, category)` | `_Metadata.{Category}.g.cs` | `_Metadata.Persistence.g.cs` |
| `ForBoundary(name, artifact)` | `_Boundary.{Name}.{Artifact}.g.cs` | `_Boundary.Billing.Interface.g.cs` |
| `ForEntityConfig(entity)` | `EntityConfig.{Entity}.g.cs` | `EntityConfig.Invoice.g.cs` |
| `ForDbContext(boundary)` | `DbContext.{Boundary}.g.cs` | `DbContext.Billing.g.cs` |

**Always pass `namespacePrefix` to `ForType`.** It is optional in the signature but not in practice: two same-named types in different namespaces collide on the hint, `AddSource` throws on a duplicate, and that exception kills generation for every feature in the compilation. `ForAssembly` and `ForMetadata` accept the parameter and genuinely ignore it — their outputs are one per assembly, so there is nothing to disambiguate. Do not generalize from those two to `ForType`.

### 6.4 Suffix Deduplication

When deriving class or method names from a type name plus a suffix, always use `NamingHelper.AppendSuffix()`:

```csharp
// TypeName "OrderNotification" + "NotificationHandler"
//   -> "OrderNotificationHandler", not "OrderNotificationNotificationHandler"
var handlerName = NamingHelper.AppendSuffix(_model.TypeName, "NotificationHandler");
```

---

## 7. Step 6: Feature Registration

The feature class wires transform, model, and template together using Roslyn's incremental APIs.

### 7.1 The Real Feature Class

```csharp
// Features/Temporal/TemporalFeature.cs
internal static class TemporalFeature
{
    public static void Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features)
    {
        var asUtc = Pipeline(context, AttributeNames.TemporalAsUtc, "AsUtc");
        var fromClient = Pipeline(context, AttributeNames.TemporalFromClientTimezone, "FromClientTimezone");
        var toClient = Pipeline(context, AttributeNames.TemporalToClientTimezone, "ToClientTimezone");
        // ... three more

        var all = asUtc.Collect()
            .Combine(fromClient.Collect()).Select(static (p, _) => p.Left.AddRange(p.Right))
            .Combine(toClient.Collect()).Select(static (p, _) => p.Left.AddRange(p.Right));
            // ... three more

        context.RegisterSourceOutputSafe(all.Combine(features), static (ctx, x) =>
        {
            var (models, detected) = x;
            if (!detected.HasTemporalJson || models.IsDefaultOrEmpty)
                return;

            Generate(ctx, models, detected.HasComposition);
        });
    }

    private static IncrementalValuesProvider<TemporalBehaviorPropertyModel> Pipeline(
        IncrementalGeneratorInitializationContext context,
        string attributeFqn,
        string behavior)
    {
        return context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeFqn,
                static (node, _) => node is PropertyDeclarationSyntax,
                (ctx, _) => TemporalBehaviorTransform.Transform(ctx, behavior))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);
    }

    private static void Generate(
        SourceProductionContext ctx,
        ImmutableArray<TemporalBehaviorPropertyModel> models,
        bool hasComposition)
    {
        // Diagnostics first, against the models the transform flagged as invalid.
        foreach (var invalid in models
                     .Where(m => !m.IsSupportedPropertyType)
                     .OrderBy(m => m.ContainingTypeFqn)
                     .ThenBy(m => m.PropertyName))
        {
            ctx.ReportDiagnostic(TemporalDiagnostics.UnsupportedPropertyType, (Location?)null,
                invalid.ContainingTypeFqn, invalid.PropertyName, invalid.PropertyTypeDisplay);
        }

        var valid = models.Where(m => m.IsSupportedPropertyType).ToImmutableArray();
        if (valid.IsDefaultOrEmpty)
            return;

        var template = new TemporalBehaviorsTemplate(valid);
        ctx.AddSource(template.RenderOutput());

        if (!hasComposition)
            return;

        ctx.AddSource(new TemporalBehaviorsMetadataTemplate(
            template.RegistrationMethodFqn, valid.Length, template.NamespacePrefix).RenderOutput());
    }
}
```

### 7.2 Non-Negotiables

**`RegisterSourceOutputSafe`, never `context.RegisterSourceOutput`.** The entire ecosystem is one `IIncrementalGenerator` with roughly 130 output registrations, and Roslyn does not isolate them: an unhandled exception in any one transform or template surfaces as a single CS8785 that suppresses the output of **every** feature. `RegisterSourceOutputSafe` (`shared/SourceGen/SafeSourceOutput.cs`) is a drop-in extension — same shape, overloads for both `IncrementalValueProvider<T>` and `IncrementalValuesProvider<T>` — that catches the failure, reports it as **PRAG9000**, and lets the other outputs proceed. PRAG9000 is an *error*: the code that output should have generated is missing, so a warning would just bury the cause under a cascade of CS0246s. Cancellation, `OutOfMemoryException`, and `StackOverflowException` are deliberately not caught.

**`ctx.AddSource(artifact)`, never `ctx.AddSource(artifact.HintName, artifact.Source)`.** `SourceOutput.AddSource` (`shared/SourceGen/SourceOutput.cs`) is the single emission point. A template whose `Validate()` returned `false` renders to *empty* content rather than to nothing; the two-argument Roslyn call would happily write that into the compilation as a zero-byte `.g.cs`. The helper checks `artifact.IsEmpty` and skips it. Across roughly 200 emission sites, only a handful would ever remember to guard by hand — that is precisely why the decision lives in one place.

**`static` lambdas.** An instance-capturing lambda prevents delegate caching and, on some Roslyn versions, forces recomputation regardless of model equality. Where you must capture (Temporal's `Pipeline` captures the `behavior` string), keep the capture to an immutable value and keep the inner callbacks `static`.

**Gate on `DetectedFeatures`.** Either with `.Where(x => x.Right.HasTemporalJson)` on the provider, or with an early return inside the callback as Temporal does. Both work; the `.Where` form lets Roslyn short-circuit slightly earlier, the early-return form is easier to read when you also need other flags (Temporal reads `HasComposition` in the same callback).

**Report diagnostics from the model, not from a filter.** Note the order in `Generate`: report against invalid models first, *then* filter. Filtering in the transform would make the invalid input invisible and the user would get silence instead of PRAG0905.

### 7.3 Register in PragmaticSourceGenerator

Open `PragmaticSourceGenerator.cs` and add the call:

```csharp
TemporalFeature.Register(context, features);
```

Registration order does not affect *execution* order — Roslyn drives the pipeline — but it does determine which providers are in scope for later features. Where one feature consumes another's provider, the producer must be registered first. The clearest case in `Initialize` is Persistence before Actions:

```csharp
// Persistence registers BEFORE ActionsFeature so its generated entity-permission catalog
// can resolve [RequirePermission(BookingPermissions.Entity.Op)] references in the Actions
// pipeline — a source generator cannot resolve constants it generates itself in the same
// compilation, so those references would otherwise fail-open (permission silently not enforced).
var entityModels = PersistenceFeature.Register(context, features, resourceQueries: allProgrammaticQueries);

var permissionCatalog = entityModels.Select(static (entities, _) =>
    new EquatableArray<PermissionConstEntry>(PermissionCatalogBuilder.Build(entities)));

ActionsFeature.Register(context, features, traitActions: allProgrammaticActions, permissionCatalog: permissionCatalog);
```

If your feature is self-contained, append the call at the end and ignore this. If it produces something another feature needs, return a provider from `Register` (as `PersistenceFeature`, `ResourceFeature`, `TraitFeature`, and `EndpointsFeature` do) rather than reaching across features at generation time.

---

## 8. Step 7: Write Tests

### 8.1 Test the Template Directly

The cheapest and most common test in this suite constructs the model by hand and renders the template — no compilation, no generator driver, milliseconds per test. Because templates are pure functions of the model, this covers most rendering logic:

```csharp
// tests/.../Features/Temporal/TemporalSnapshotTests.cs
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

Note the return type: `Task`, returned directly from `Verify(...)`, not `async void` and not `.GetAwaiter().GetResult()`.

### 8.2 Test the Full Pipeline

For transform behaviour, feature gating, and diagnostics you need the generator driver. Use `GeneratorTestHelper` from `shared/SourceGen/Testing/` (auto-compiled into every test project by `Directory.Build.props`; opt out with `<ExcludeSharedTesting>true</ExcludeSharedTesting>`).

Create a thin base class that supplies the module's assembly references:

```csharp
public abstract class TemporalGeneratorTestBase
{
    private static MetadataReference[] GetReferences() =>
    [
        GeneratorTestHelper.FromType<Pragmatic.Temporal.Attributes.AsUtcAttribute>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Temporal.Json.Behaviors.TemporalJsonBehaviorRegistry)),
    ];

    protected static SourceGenRunResult RunGenerator(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, GetReferences());

    protected static string? GetGeneratedSource(SourceGenRunResult result, string hintName)
        => GeneratorTestHelper.GetGeneratedSource(result, hintName);

    protected static bool HasCompilationErrors(SourceGenRunResult result)
        => GeneratorTestHelper.HasCompilationErrors(result);
}
```

The references are what drive `FeatureDetector`. Omit the assembly that carries your detection type and the flag is `false`, the pipeline is gated off, and the test fails with "no generated source" — which is also the cheapest way to *test* the gate deliberately.

```csharp
[Fact]
public void ToClientTimezone_OnDateTimeOffset_GeneratesRegistration()
{
    var result = RunGenerator("""
        using Pragmatic.Temporal.Attributes;
        namespace MyApp.Dtos;

        public class OrderResponse
        {
            [ToClientTimezone]
            public System.DateTimeOffset CreatedAt { get; set; }
        }
        """);

    HasCompilationErrors(result).Should().BeFalse();
    GetGeneratedSource(result, "_Infra.Temporal.Behaviors")
        .Should().Contain("TemporalJsonBehavior.ToClientTimezone");
}
```

### 8.3 Snapshots: Nothing to Configure

There is no `ModuleInitializer.cs` in `tests/Pragmatic.SourceGenerator.Tests/`, and you should not add one. Verify's scrubbers are configured once, repo-wide, in `shared/Testing/VerifyHelpers.cs` (`Pragmatic.Testing.VerifyConfiguration`), which carries its own `[ModuleInitializer]` and is globbed into every `IsTest=true` project by `Directory.Build.props`.

Four scrubbers run: the attribution header and its tool line collapse to a stable marker, as does any other `// Generated by …` banner, inline `v1.2.3` becomes `v*`, and ISO timestamps become `<timestamp>`. It also calls `DontScrubDateTimes()` and `DontScrubGuids()` so Verify's defaults do not mangle dates and GUIDs that are genuinely part of the generated code.

Write the test, run it, accept the `.verified.txt`. If some header form still leaks into a diff, add the pattern to `VerifyHelpers.cs` — a per-project scrubber would fix your suite and leave the other 40 snapshots exposed.

### 8.4 GeneratorTestHelper API Reference

| Method | Purpose |
|--------|---------|
| `RunGenerator<T>(source, refs)` | Run the generator against source code |
| `GetGeneratedSource(result, hint)` | Get generated source by partial hint name match |
| `GetGeneratedSourcesAsDictionary(result)` | All generated files as `Dictionary<string, string>` |
| `HasCompilationErrors(result)` | Check for compilation errors after generation |
| `GetCompilationErrors(result)` | List all compilation errors |
| `GetGeneratorDiagnostics(result, prefix)` | Get diagnostics filtered by ID prefix (e.g., `"PRAG09"`) |
| `HasDiagnostic(result, id)` | Check if a specific diagnostic was emitted |
| `FromType<T>()` | Create a `MetadataReference` from a type's assembly |
| `FromTypeAssembly(type)` | Same, but for `static` types that cannot be type arguments |
| `TryGetAssemblyReference(name)` | Optional reference by assembly name |

`RunGenerator` parses the source with an explicit file path. That is not incidental: `LocationInfo.From` returns `null` when a syntax tree has an empty `FilePath`, so a path-less test compilation makes every location-guarded diagnostic vanish. If you build a compilation by hand, pass a path.

---

## 9. Complete Checklist

### Feature Detection
- [ ] Added `Has{Feature}` property to `DetectedFeatures` (one per runtime assembly, not per module)
- [ ] Added `TypeExists()` check in `FeatureDetector.Detect()` with correct FQN
- [ ] Verified FQN uses backtick-arity for generic types (e.g., `` `1 ``, `` `2 ``)
- [ ] Trigger attribute FQNs added to `AttributeNames.cs`, referenced as constants

### Directory Structure
- [ ] Created `Features/{Name}/` with `Models/`, `Templates/`, `Transforms/` (and `Diagnostics/` if needed)
- [ ] One file per class; no file over 400 lines

### Model
- [ ] Sealed record; inherits `GeneratorModel` if it describes a type
- [ ] **Every collection is `EquatableArray<T>`** — no `List<T>`, no `T[]`, no raw `ImmutableArray<T>`
- [ ] Dictionaries are `EquatableDictionary<TKey, TValue>`
- [ ] Positions are `LocationInfo?`, not `Location`
- [ ] No `ISymbol` or `Compilation` references
- [ ] Invalid input is flagged, not dropped, so diagnostics can be reported

### Transform
- [ ] Static class with static transform method
- [ ] Uses `ForAttributeWithMetadataName` with a `static` predicate from `GeneratorHelpers`
- [ ] Matches the symbol kind before dereferencing
- [ ] Deterministic: no side effects, no randomness, no `GetTypeByMetadataName`
- [ ] Extracts only the data the template needs

### Template
- [ ] Extends `CSharpTemplate`; lives in `Templates/`
- [ ] `RenderOutput()` uses `VirtualFolderHints` — and passes `namespacePrefix` to `ForType`
- [ ] Collections are sorted before rendering (deterministic output)
- [ ] Generated type references are `global::`-qualified
- [ ] Uses `NamingHelper.AppendSuffix()` for derived names
- [ ] Uses `TemplateHelpers.ParseAccessibility()` for access modifiers
- [ ] Overrides `GeneratorName` / `SourceInfo` / `TriggerInfo`
- [ ] Overrides `Validate()` when generation is conditional

### Feature Registration
- [ ] Static `Register()` in `{Name}Feature.cs`
- [ ] **`RegisterSourceOutputSafe`, not `context.RegisterSourceOutput`**
- [ ] **`ctx.AddSource(artifact)`, not `ctx.AddSource(hintName, source)`**
- [ ] `static` lambdas
- [ ] Gated on `DetectedFeatures`
- [ ] Called from `PragmaticSourceGenerator.Initialize()`, after any feature whose provider it consumes

### Diagnostics
- [ ] IDs from the correct range (see `docs/diagnostics.md`)
- [ ] `{Name}Diagnostics.cs` built via `DiagnosticFactory.Error/Warning/Info/Hidden`
- [ ] Reported through the `ReportDiagnostic(descriptor, location, args)` extension
- [ ] If the diagnostic means "must be partial", also registered in `NotPartialDiagnosticDescriptors.cs` and in the code fixer's `FixableDiagnosticIds`
- [ ] `node scripts/sync-diagnostics.mjs` run, and a row added to the published reference `site/docs/src/content/docs/reference/diagnostics.md` — the gate refuses a descriptor that has no row there (`scripts/site-diagnostics.mjs`)

### Tests
- [ ] Template rendered directly from a hand-built model (fast path)
- [ ] Full-pipeline test through `GeneratorTestHelper` for transform, gating, and diagnostics
- [ ] Negative test: feature produces nothing when the runtime reference is absent
- [ ] Edge cases (null, empty, unsupported types)
- [ ] Snapshots accepted — no per-project scrubber added

### Integration
- [ ] Feature works end-to-end with the runtime package (Showcase)
- [ ] `dotnet build --warnaserror` passes
- [ ] `dotnet test` passes
