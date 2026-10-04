# Troubleshooting

Practical problem/solution guide for Pragmatic.SourceGenerator. Each section covers a common issue, the likely causes, and the fix.

---

## Feature Not Activating (No Generated Code)

Your feature's `Register()` method runs, but no `.g.cs` files appear in the build output or IDE.

### Checklist

1. **Is the runtime package referenced?** The consuming project must reference the Pragmatic runtime package (e.g., `Pragmatic.Actions`, `Pragmatic.Caching`). Without it, `FeatureDetector` sets the flag to `false` and the pipeline is gated off.

2. **Is the FQN correct in `FeatureDetector`?** Open `Core/FeatureDetector.cs` and verify the exact string passed to `TypeExists()`. The most common errors:
   - Missing backtick-arity for generic types: `` MapFromAttribute`1 ``, not `MapFromAttribute`
   - Wrong namespace: some types are in `.Attributes` sub-namespaces, others in the root namespace
   - Typo in the type name

   To debug, temporarily add a diagnostic:
   ```csharp
   var type = compilation.GetTypeByMetadataName("Pragmatic.MyModule.Attributes.MyAttribute");
   // If type is null, the FQN is wrong or the package is not referenced
   ```

3. **Is the feature gated on `DetectedFeatures`?** Check the `.Where(x => x.Right.HasMyFeature)` filter in the feature's `Register()` method. The flag name must match the property on `DetectedFeatures`.

4. **Is the `ForAttributeWithMetadataName` FQN correct?** The FQN passed to `ForAttributeWithMetadataName` must match the attribute's metadata name exactly. Cross-check with `AttributeNames.cs`.

5. **Does the user's class have the attribute?** If the attribute is not present on the class, `ForAttributeWithMetadataName` will not find it. Check the user's source code.

6. **Is the source generator referenced correctly?** The `.csproj` must include:
   ```xml
   <ProjectReference Include="...\Pragmatic.SourceGenerator.csproj"
                     OutputItemType="Analyzer"
                     ReferenceOutputAssembly="false" />
   ```

---

## Transform Runs But No Output

The transform executes (verified with `Debugger.Launch()` or a diagnostic), but no `.g.cs` file is produced.

### Checklist

1. **Does the transform return `null`?** If `context.TargetSymbol is not INamedTypeSymbol`, or if any guard condition fails, the transform returns `null`. The pipeline's `.Where(static m => m is not null)` filter discards it silently.

2. **Is `model.IsValid` false?** Many features filter with `.Where(static m => m.IsValid)`. Check what conditions set `IsValid` to `false`. Common causes: the class is not `partial`, the class is `abstract`, a required attribute argument is missing.

3. **Does `Validate()` return `false` in the template?** This is the most likely cause, and it is silent by design. When `Validate()` returns `false`, `ToString()` returns `null`, `ToSourceText()` renders empty content, and `ctx.AddSource(artifact)` sees `artifact.IsEmpty` and skips the file rather than emitting an empty `.g.cs`. No diagnostic, no error: just nothing. Check every condition in the `Validate()` override; `IsPartial` is the usual culprit.

4. **Is there an early return in the `Generate()` method?** Look for `if (!model.IsPartial) return;` or similar guards before `ctx.AddSource()`.

5. **Is `ctx.AddSource(artifact)` actually called?** Set a breakpoint to confirm it executes. If it is reached and the file still does not appear, either the artifact is empty (see #3) or there is a hint name conflict (section below).

6. **Did a PRAG9000 error appear?** The output threw and was caught by `RegisterSourceOutputSafe`. See the PRAG9000 section below.

---

## Incremental Caching Not Working (Template Always Re-Runs)

The template re-executes on every keystroke, even when the source file has not changed.

### Checklist

1. **Does the model use `EquatableArray<T>` for every collection?** This is the first thing to check and by far the most common cause. `List<T>` and `T[]` use reference equality, which is obvious. `ImmutableArray<T>` *also* uses reference equality: it is a struct wrapper over `T[]` and compares the underlying array reference, not the contents. A transform allocates a fresh array on every run, so the model never compares equal. Replace every collection field with `EquatableArray<T>` (and every dictionary with `EquatableDictionary<TKey, TValue>`). See [Common Mistakes #2](common-mistakes.md#2-raw-collections-in-models-listt-or-immutablearrayt-instead-of-equatablearrayt).

2. **Does the model store `ISymbol` references?** Roslyn creates new symbol instances on every compilation pass. Stored symbols always compare as not equal. Extract string/flag data instead.

3. **Does the model store a raw `Location`?** Same problem, plus it pins a `SyntaxTree` from a dead compilation. Use `LocationInfo`, which is deliberately excluded from equality. See [Common Mistakes #16](common-mistakes.md#16-storing-a-roslyn-location-in-a-model).

4. **Is the model a `sealed record`?** Records provide structural equality by default. Classes use reference equality unless you override `Equals`.

5. **Are all model properties value-comparable?** Custom types in the model must implement `IEquatable<T>` correctly. Nested records satisfy this automatically; custom classes do not.

6. **Are lambdas `static`?** Non-static lambdas in the source-output callback can prevent delegate caching. Always use `static` lambdas.

### Diagnosis

Add a temporary counter or `Debug.WriteLine` in the template's `RenderFile()` to verify how often it runs. If it runs on every keystroke without source changes, the model equality check is the culprit.

---

## Hint Name Conflicts

Build error: `"The hint name 'Invoice.Repository.g.cs' was already used."` Two templates produce the same hint name.

Treat this as urgent regardless of which feature caused it. `AddSource` throws, and because the whole ecosystem runs inside one `IIncrementalGenerator`, that exception aborts generation for **every** feature in the compilation. The visible symptom is usually not this message but a flood of CS0246 "type or namespace not found" errors for types the generator was supposed to emit.

### Checklist

1. **Is the namespace missing from a per-type hint?** This is the most common cause. `VirtualFolderHints.ForType(typeName, artifact)` without the third argument produces `Invoice.Repository.g.cs` for both `Sales.Invoice` and `Archive.Invoice`. Always pass `_model.Namespace`.

2. **Are two templates using the same artifact suffix for the same type?** Each template for a given type must use a distinct artifact name in `VirtualFolderHints.ForType()`.

3. **Is a per-type template conflicting with an assembly-level template?** Per-type templates use `ForType()` (e.g., `Sales.Invoice.Repository.g.cs`). Assembly-level templates use `ForAssembly()` (e.g., `_Infra.Persistence.Registration.g.cs`). These should not conflict, but verify the hint name is correct.

4. **Copy-paste error?** Check that you did not copy the `RenderOutput()` method from another template without updating the artifact name.

### Fix

Always use `VirtualFolderHints`, always pass the namespace to `ForType`, and choose distinct artifact names:

```csharp
// Template A
VirtualFolderHints.ForType(_model.TypeName, "Repository", _model.Namespace)
// Sales.Invoice.Repository.g.cs

// Template B
VirtualFolderHints.ForType(_model.TypeName, "SoftDeleteFilter", _model.Namespace)
// Sales.Invoice.SoftDeleteFilter.g.cs
```

---

## PRAG9000: "A Pragmatic source generator output failed"

An output registration threw. `RegisterSourceOutputSafe` caught it, reported PRAG9000 with the exception type and message, and let the other outputs proceed.

PRAG9000 is an **error**, so the build stops here. That is deliberate: the code that output should have generated is missing, and downgrading it to a warning would let the build run on into a cascade of CS0246s whose real cause is buried in warning output.

### What the message tells you

The message carries the exception type and text, but not the feature: `SourceProductionContext` has no way to report which registration failed, and the location is `Location.None`. To find it:

1. **Read the exception type.** `NullReferenceException` and `ArgumentOutOfRangeException` in a template almost always mean the model has a field the transform left unset or empty for this particular input.
2. **Narrow by the triggering code.** Comment out or minimize the user code until the error disappears; the last construct you removed drives the failing pipeline.
3. **Reproduce in a generator test.** `GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references)` runs the same pipelines in-process, where the exception surfaces with a real stack trace instead of a message string.

### If PRAG9000 does NOT appear but generation is missing

The failure escaped the guard. Three possibilities:

- The registration uses `context.RegisterSourceOutput` instead of `RegisterSourceOutputSafe`. The symptom is CS8785 instead of PRAG9000, and *all* features go dark. Fix the registration.
- The exception is `OperationCanceledException`, `OutOfMemoryException`, or `StackOverflowException`, which are deliberately not caught.
- The output did not throw at all: the template's `Validate()` returned `false`, so the artifact rendered empty and `ctx.AddSource(artifact)` correctly skipped it. See "Transform Runs But No Output" above.

---

## Generated Code Has Compilation Errors

The generated `.g.cs` file is produced, but it causes build errors in the consuming project.

### Read the errors in the hand-written files first

A `CS0246` or a `CS0400` **inside a generated file** is usually not a generator bug. When a type an
operation names does not resolve (one missing `using` in a hand-written file), Roslyn hands the
transform an **error symbol**: still an `INamedTypeSymbol`, so every check passes, and its
`ToDisplayString(FullyQualifiedFormat)` is the **bare name with no namespace**. Written back out it
becomes `global::CallerDto`, a name that cannot exist, in every artifact the generator produces about
that operation.

Measured: removing one `global using` from a module turned **one** `CS0246` in the author's own file
into a page of them across the endpoint contract, the three boundary facades, the invoker, the query
and its invoker. Such a page reads as four broken templates that are in fact correct, because the loud
errors are the generated ones.

Since then the transforms check `TypeKind.Error` and generate **nothing** for such an operation,
reporting `PRAG9001` with the type's name and the operation's. So:

- **`PRAG9001` in the output** → fix the name it points at; everything else follows.
- **A `CS0246` in a generated file with no `PRAG9001`** → that one is worth investigating, and a new
  read of a type argument that does not check `TypeKind.Error` is the first suspect.

### Common causes

**Missing `using` in generated code.** The generated code references types without fully qualifying them.

Fix: Either fully qualify with `global::` prefix (preferred for Pragmatic types), or call `AddUsing()` in `RenderFile()`:
```csharp
// Fully qualified (preferred)
AppendLine("global::Pragmatic.Persistence.IRepository<Invoice>");

// Using directive (for common framework types)
AddUsing("System.Collections.Immutable");
```

**Duplicate member in partial class.** The generated partial class defines a member that the developer already defined manually.

Fix: Check the model for flags like `HasManualImplementation` and skip generating that member.

**Wrong indentation.** `IncreaseIndent()` and `DecreaseIndent()` calls are not balanced.

Fix: Prefer structured methods (`Class()`, `Method()`, `Block()`) which manage indentation automatically. Reserve manual indent calls for truly custom formatting.

**Accessibility mismatch.** A nested class is `internal` but referenced from another assembly.

Fix: Use `AccessModifier.Public` for nested classes that need cross-assembly access (see [Common Mistakes #13](common-mistakes.md#13-forgetting-to-make-nested-classes-public-for-cross-assembly-access)).

---

## Snapshot Tests Failing on Header Noise

A Verify snapshot fails and the diff shows only the generated-file header changed: a version bump, a copyright year, a timestamp.

### This should not happen: scrubbing is already global

There is no `ModuleInitializer.cs` in `tests/Pragmatic.SourceGenerator.Tests/`, and you should not add one. The scrubbers live in `shared/Testing/VerifyHelpers.cs` (`Pragmatic.Testing.VerifyConfiguration`), which carries its own `[ModuleInitializer]` and is compiled into **every** test project in the repo automatically: `Directory.Build.props` globs `$(SharedTestingPath)**\*.cs` into any project with `IsTest=true`. Nothing is wired up per project.

Four scrubbers run. Verify decides their order, so none of them may depend on another having run
first:

| Scrubber | Effect |
|----------|--------|
| Attribution header | Collapses the 3-line `// Generated by Pragmatic.Design, a framework by …` / `// https://pragmaticdesign.net` / `// This file belongs to your project…` block, and the tool line after it, to `// Generated by Pragmatic.SourceGenerator` |
| Generator banner | Collapses any other `// Generated by …` line to `// Generated by Pragmatic.SourceGenerator` |
| Inline version | `v1.2.3`, `v1.2.3-beta.1+abc` → `v*` |
| ISO timestamp | `2026-01-31T12:00:00Z` → `<timestamp>` |

It also calls `DontScrubDateTimes()` and `DontScrubGuids()`, so Verify's own aggressive default scrubbing does not mangle GUIDs or dates that are genuinely part of the generated code.

### So if a header still leaks into a diff

The output contains a form none of the four patterns match. Add the pattern to `VerifyHelpers.cs` rather than to the test project; a per-project scrubber would fix one suite and leave the other forty snapshots exposed. Then re-accept the affected `.verified.txt` files once.

---

## IDE Sluggish When Editing Files

The IDE becomes slow when editing files in a project that references the generator.

### Common causes

| Cause | Impact | Fix |
|-------|--------|-----|
| Using `CreateSyntaxProvider` instead of `ForAttributeWithMetadataName` | Predicate runs on every syntax node | Switch to `ForAttributeWithMetadataName` |
| Heavy computation in transform | Blocks the Roslyn pipeline thread | Move computation to the template |
| Large model with many unused properties | Equality check is expensive and changes often | Only include properties the template needs |
| Not filtering with `DetectedFeatures` | Pipeline runs even when feature is not referenced | Combine with `features` and filter |
| Calling `compilation.GetTypeByMetadataName` per-type | Repeated compilation queries | Move to `FeatureDetector` (runs once) |
| Non-static lambdas in the source-output callback | Prevents delegate caching | Use `static` lambdas |
| Raw `ImmutableArray<T>` on a model | Reference equality: the stage never caches | `EquatableArray<T>` |

### Diagnosis

Enable Roslyn's generator timing by adding to the `.csproj`:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

Generated files appear on disk under `obj/generated/`. Inspect file timestamps to see which files are regenerated on each keystroke.

---

## Debugging the Generator

### Attaching a debugger

Add `Debugger.Launch()` at the start of your transform or feature:

```csharp
public static MyModel? Transform(
    GeneratorAttributeSyntaxContext context,
    CancellationToken ct)
{
    #if DEBUG
    if (!System.Diagnostics.Debugger.IsAttached)
        System.Diagnostics.Debugger.Launch();
    #endif

    // ... transform logic
}
```

When the generator runs (via `dotnet build` or IDE), a dialog appears asking you to attach a debugger. Select your Visual Studio instance.

**Remove `Debugger.Launch()` before committing.** It halts CI builds.

### Inspecting generated output

Enable generated files on disk:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)\generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

Files appear under `obj/generated/Pragmatic.SourceGenerator/Pragmatic.SourceGenerator.PragmaticSourceGenerator/`.

### Logging from generator code

Generators cannot write to `Console.Out`. Instead, emit an informational diagnostic:

```csharp
var descriptor = new DiagnosticDescriptor(
    "PRAGDBG", "Debug", "Transform produced: {0}",
    "Debug", DiagnosticSeverity.Warning, isEnabledByDefault: true);

ctx.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None, model.TypeName));
```

Build warnings will show your debug message. Remove before committing.

### Unit testing without a full build

Use `GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references)` to run the generator in isolation:

```csharp
[Fact]
public void Debug_InspectAllGeneratedFiles()
{
    var source = """
        using Pragmatic.Caching.Attributes;
        namespace Test;

        [Cacheable]
        public partial class DebugQuery
        {
            [CacheKey]
            public Guid Id { get; init; }
        }
        """;

    var result = RunGenerator(source);

    var allFiles = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
    foreach (var (name, content) in allFiles)
    {
        // Set breakpoint here to inspect
        _ = name;
        _ = content;
    }
}
```

---

## FAQ

### Why is there only one generator for all features?

Multiple generators cannot share state. With a single generator, shared providers (entity metadata, feature detection) are computed once and reused across all features. Cross-feature coordination (DI registration aggregation, host wiring) is possible. See [Concepts: Why one generator?](concepts.md#why-one-generator).

### Can I test a template without running the full generator?

Yes. Construct the model by hand and call `template.ToString()` directly:

```csharp
var model = new CacheableModel
{
    TypeName = "GetOrderQuery",
    Namespace = "MyApp",
    Accessibility = "public",
    TypeKind = "class",
    // ... other properties
};

var template = new CacheableTemplate(model);
var output = template.ToString();
// Inspect output string directly
```

### How do I find out which features are active for a project?

Look at the `DetectedFeatures` in the generator output. You can emit it as a diagnostic or inspect it in the debugger. Alternatively, check which `_Infra.*.Registration.g.cs` files are generated -- each one corresponds to an active feature.

### Why does ForAttributeWithMetadataName not find my attribute?

Three common reasons:
1. The FQN is wrong (missing backtick-arity for generics).
2. The runtime package is not referenced.
3. The attribute is defined in a referenced assembly but not exported as a public type.

Add the FQN as a constant in `AttributeNames.cs` and verify it matches the attribute's actual metadata name.

### How do I handle the case where the user's class is not partial?

Report a diagnostic explaining that the class must be `partial`, then return without generating code:

```csharp
if (!model.IsPartial)
{
    // DiagnosticExtensions overload: descriptor + location + args, no Diagnostic.Create ceremony.
    ctx.ReportDiagnostic(MyDiagnostics.MustBePartial, model.Location?.ToLocation(), model.TypeName);
    return;
}
```

`model.Location` is a `LocationInfo?`, never a raw `Location`; see [Common Mistakes #16](common-mistakes.md#16-storing-a-roslyn-location-in-a-model).

Design-time coverage for this case already exists: `NotPartialClassAnalyzer` in `Pragmatic.SourceGenerator.Analyzers` reports the same "must be partial" IDs without waiting for a build, and `MakeClassPartialCodeFixProvider` offers the fix. If you add a new attribute that requires `partial`, register its descriptor in `NotPartialDiagnosticDescriptors.cs` and add the ID to the fixer's `FixableDiagnosticIds` so users get the lightbulb.

### Why do generated files show stale content in the IDE?

This is a known Roslyn issue. Close and reopen the file, or rebuild the project. In rare cases, restarting the IDE is needed. Enabling `EmitCompilerGeneratedFiles` and inspecting the on-disk files gives a reliable view of the current output.

---

## Getting Help

- **Existing docs**: [Architecture](architecture.md), [Feature Development](feature-development.md), [Template API Reference](template-api.md)
- **Project rules**: `docs/CONVENTIONS.md` -- mandatory conventions for generators
- **Showcase**: The `Showcase` project demonstrates all features working together end-to-end
- **Test suite**: `tests/Pragmatic.SourceGenerator.Tests/` covers the feature pipelines, with Verify snapshots for the larger outputs. Search it for a scenario close to yours before writing a new base class -- most features already have one. The sibling suites `Pragmatic.SourceGenerator.Analyzers.Tests` and `Pragmatic.SourceGenerator.CodeFixers.Tests` cover the design-time analyzers and the "make class partial" fixer.
