# Pragmatic.SourceGenerator

The unified incremental source generator for the entire Pragmatic.Design ecosystem — one Roslyn
analyzer that detects which runtime packages your project references and activates only the
corresponding generation pipelines.

> **Audience:** framework developers working on Pragmatic.Design internals. If you're *building an app*
> with Pragmatic, you consume this generator indirectly through the runtime packages — add the
> `Pragmatic.SourceGenerator` package and the features for the modules you reference light up.

## The Problem

.NET frameworks wire types together via **runtime reflection** (hidden startup cost, AOT-hostile) or
**manual boilerplate** (verbose, drifts). Both compound as the codebase grows.

```csharp
// Discovery-based registration — runs at startup, reflection-heavy, not AOT-safe
var entityTypes = Assembly.GetExecutingAssembly().GetTypes()
    .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHasKey<>)));
```

## The Solution

One incremental generator that reads your attributes/partial classes at compile time and emits the
wiring as plain, debuggable C#. **Feature detection** (`FeatureDetector` scanning referenced
assemblies) activates a pipeline only when its runtime package is present — add a package, the
generation appears; remove it, the code disappears. No runtime discovery, AOT-ready output.

It is a single `netstandard2.0` analyzer with **29 feature folders** under `Features/` (Persistence,
Actions, Endpoints, Mapping, Validation, Composition, Caching, Messaging, Jobs, Traits, Resource,
Temporal, Privacy, Redaction, …) sharing the `CSharpTemplate` infrastructure in `shared/SourceGen/`.

## Layout

| Project | What it is |
|---------|------------|
| `src/Pragmatic.SourceGenerator` | The unified `IIncrementalGenerator`, plus four `DiagnosticSuppressor`s (PRAGS001-004) that stop the IDE from warning about members the generator supplies |
| `src/Pragmatic.SourceGenerator.Analyzers` | Design-time analyzers: `NotPartialClassAnalyzer` (13 "must be partial" IDs), `EventCycleAnalyzer` (PRAG0822), `BoundaryActionsInjectionAnalyzer` (PRAG0441), `IgnoredValidationAttributeAnalyzer` (PRAG0210), `VisibilityRuleAnalyzer` (PRAG0717-0721). **Packed inside the `Pragmatic.SourceGenerator` package**, so they arrive wherever the generator does |
| `src/Pragmatic.SourceGenerator.CodeFixers` | `MakeClassPartialCodeFixProvider`, with a batch fix-all — its own package |

Tests live in the matching `tests/` projects — 2,149 for the generator alone at the last gate run
(`node scripts/check.mjs --tier full` prints the current count).

## Installation

```bash
dotnet add package Pragmatic.SourceGenerator
```

## Status

The unified generator and its feature pipelines are functional within 1.0.0-alpha. See the
[roadmap](../docs/ROADMAP.md).

## Documentation

| Guide | What you'll learn |
|-------|-------------------|
| [Concepts](docs/concepts.md) | Incremental generation, feature detection, the feature inventory, cacheable models, host vs module mode |
| [Architecture](docs/architecture.md) | Pipelines, crash isolation, the Persistence orchestrator, suppressors, the sibling analyzer projects |
| [Template API](docs/template-api.md) | `CSharpTemplate`, `Artifact`, `VirtualFolderHints`, `NamingHelper`, `EquatableArray` |
| [Feature Development](docs/feature-development.md) | Adding a feature end to end, walked through the real `Features/Temporal/` |
| [Common Mistakes](docs/common-mistakes.md) | The pitfalls that silently break generation or caching |
| [Troubleshooting](docs/troubleshooting.md) | Problem/solution guide, including PRAG9000 |

**Start here if you are new:** [Concepts](docs/concepts.md) for the model, then
[Common Mistakes](docs/common-mistakes.md) — three of those mistakes (raw `ImmutableArray<T>` on a model,
plain `RegisterSourceOutput`, a hint name without its namespace) compile cleanly and fail silently.

## Requirements

- .NET 10.0 SDK (the generator targets `netstandard2.0`; generated code targets `net10.0`)

## License

Part of the [Pragmatic.Design](../README.md) ecosystem — see [Licensing](../docs/LICENSING.md).
Pragmatic.SourceGenerator is **MIT-licensed**.
