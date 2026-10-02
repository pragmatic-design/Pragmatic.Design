# Pragmatic.Abstractions

Cross-cutting contracts for the **Pragmatic.Design** ecosystem — pure interfaces, attributes, and
records shared by all modules, with zero dependency on ASP.NET Core or EF Core. This is **Layer 0**.

## The Problem

A modular framework needs shared contracts. Without a central home for them:

- **Circular dependencies** emerge — Persistence needs `ICurrentUser` for auditing; Identity needs
  `IRepository` for user storage. If each module owns its interfaces, they depend on each other and the
  build fails.
- **Framework coupling** spreads — if domain modules depend on `ClaimsPrincipal`, `HttpContext`, or
  `DbContext`, they can't run in console apps, workers, tests, or non-HTTP hosts.
- **Interface duplication** fragments the ecosystem — without one shared `ICurrentUser`, every module
  defines its own, and a single DI registration can't satisfy all consumers.

## The Solution

One lightweight package that every module references and that depends on (almost) nothing — only the
contracts crossing module boundaries: interfaces, attributes, records, enums, null-object singletons.

```
                 Pragmatic.Abstractions (Layer 0)
                          ▲
        ┌─────────────────┼──────────────────┐
   Pragmatic.Actions   Pragmatic.Events   Pragmatic.Persistence
   Pragmatic.Identity  Pragmatic.Authorization   …
```

No circular dependencies, no framework coupling, one interface per concept. You rarely reference it
directly — it arrives transitively through the modules you use; reference it explicitly when you author
a module that must expose or consume a cross-cutting contract.

## Installation

```bash
dotnet add package Pragmatic.Abstractions
```

## Status

**Stable** within 1.0.0-alpha — the contract surface is the ecosystem's foundation. See the
[roadmap](../docs/ROADMAP.md).

## Documentation

| Guide | What you'll learn |
|-------|-------------------|
| [Concepts](docs/concepts.md) | Why Layer 0 exists, what belongs here, what doesn't |
| [Getting Started](docs/getting-started.md) | Referencing the contracts from a module |
| [Interfaces](docs/interfaces.md) | The full interface/attribute catalog + which module implements each |
| [Design Principles](docs/design-principles.md) | Layer dependency rules, framework-neutrality |
| [Common Mistakes](docs/common-mistakes.md) | The most frequent pitfalls |
| [Troubleshooting](docs/troubleshooting.md) | Problem/solution guide |

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](../README.md) ecosystem — see [Licensing](../docs/LICENSING.md).
Pragmatic.Abstractions is **MIT-licensed**.
