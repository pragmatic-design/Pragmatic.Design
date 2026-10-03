# Source Generator Reference

This file is for agents that can inspect or modify the Pragmatic monorepo. Consumer agents usually only need `packages.md`.

## Consumer Analyzer Setup

For external apps, add:

```xml
<PackageReference Include="Pragmatic.SourceGenerator" Version="1.0.0-alpha.1">
  <IncludeAssets>analyzers; build; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

If generated symbols are missing:

1. Confirm the package restored, at the same version as the other `Pragmatic.*` packages (`dotnet list package`).
2. Confirm the source generator package is referenced as an analyzer.
3. Rebuild with `dotnet build`.
4. Read PRAG diagnostics and fix the source pattern.

## Canonical Paths

- Unified generator root: `Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/`
- Feature root: `Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/{Feature}/`
- Shared generator helpers: `shared/SourceGen/`
- Attribute FQN constants: `shared/SourceGen/AttributeNames.cs`
- Feature detection: `Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Core/FeatureDetector.cs`
- Feature flags: `Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Core/DetectedFeatures.cs`
- Entry point: `Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/PragmaticSourceGenerator.cs`
- Host aggregation template: `Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features/Composition/Templates/PragmaticHostTemplate*.cs`

## Feature Layout

```text
Features/{Feature}/
  {Feature}Feature.cs
  Models/
  Transforms/
  Templates/
  Diagnostics/
```

Some mature features have additional subfolders. Preserve the local structure when editing.

## Generator Rules

- Use `IIncrementalGenerator`.
- Prefer `ForAttributeWithMetadataName` for attribute-driven features.
- Models should be immutable sealed records.
- Templates inherit `CSharpTemplate`.
- Generated hint names use `VirtualFolderHints`.
- Derived names use `NamingHelper.AppendSuffix()`.
- Do not store Roslyn symbols in models except `Location?`.
- Add tests with `GeneratorTestHelper` from `shared/SourceGen/Testing/`.

## Detection Rules

- Generic attributes require backtick arity in metadata names, e.g. `MapFromAttribute\`1`.
- Runtime package features should guard on `DetectedFeatures`.
- Standalone features can register without feature flags only when that is already the repo pattern.

## Host Mode

When a feature contributes DI, metadata, endpoints, repositories, or runtime registrations, inspect the Composition host aggregation code before editing. The relevant files are `PragmaticHostTemplate*.cs` and `HostModeGenerator*.cs`.
