---
title: "Monorepo Structure"
description: "> Onboarding guide for new developers joining Pragmatic.Design."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/docs/howto/monorepo-structure.md
sidebar:
  order: 2
---
> Onboarding guide for new developers joining Pragmatic.Design.

---

## 1. Repository Layout

```
Pragmatic.Design/
├── Pragmatic.Abstractions/        # Shared interfaces (Layer 0), zero deps
├── Pragmatic.Result/              # Result<T,E> monad (Layer 0)
├── Pragmatic.Ensure/              # Guard clauses (Layer 0)
├── Pragmatic.Validation/          # Fluent validation (Layer 1)
├── Pragmatic.Mapping/             # Object mapping (Layer 1)
├── Pragmatic.Specification/       # Specification pattern (Layer 1)
├── Pragmatic.Internationalization/# i18n, translations (Layer 1)
├── Pragmatic.Temporal/            # Date/time, business days (Layer 1)
├── Pragmatic.Caching/             # Cache abstraction (Layer 1)
├── Pragmatic.Resilience/          # Retry, circuit breaker (Layer 1)
├── Pragmatic.Configuration/       # Config providers (Layer 1)
├── Pragmatic.Logging/             # Structured logging (Layer 1)
├── Pragmatic.Patch/               # JSON Patch support (Layer 1)
├── Pragmatic.Actions/             # Mutations, queries, side effects (Layer 2)
├── Pragmatic.Endpoints/           # HTTP endpoint generation (Layer 2)
├── Pragmatic.Persistence/         # Entity persistence + EF Core (Layer 2)
├── Pragmatic.Events/              # Domain events (Layer 2)
├── Pragmatic.Composition/         # DI + host bootstrap (Layer 2)
├── Pragmatic.Identity/            # User identity, auth (Cross-cutting)
├── Pragmatic.Authorization/       # Permissions, policies (Cross-cutting)
├── Pragmatic.Messaging/           # Message bus, transports (Layer 2)
├── Pragmatic.Jobs/                # Background jobs (Layer 2)
├── Pragmatic.MultiTenancy/        # Tenant resolution (Cross-cutting)
├── Pragmatic.Storage/             # File/blob storage (Layer 2)
├── Pragmatic.Discovery/           # Service discovery (Layer 2)
├── Pragmatic.FeatureFlags/        # Feature toggles (Layer 1)
├── Pragmatic.SourceGenerator/     # Unified Roslyn source generator
├── Pragmatic.Integration.Tests/   # Cross-module integration tests
│
├── shared/                        # Code shared across generators and tests
│   ├── SourceGen/                 # CSharpTemplate, helpers (linked into SG projects)
│   ├── SourceGen.Tests/           # Shared SG test project
│   ├── Testing/                   # Shared test utilities (linked into test projects)
│   ├── Contracts/                 # Contracts shared across modules
│   └── native/                    # Shared Rust code of the native libraries
│
├── examples/
│   ├── showcase/                  # Full E2E demo app (Booking, Billing, Catalog)
│   ├── time-off/ invoicing/ casework/ warehouse/   # Reference applications, one level of scale each
│   ├── conformance/               # The test bench: every shape, end to end
│   ├── aot-smoke/                 # Native AOT smoke applications
│   └── consumer-samples/          # Samples that consume the packages, not the projects
│
├── docs/
│   ├── architecture/              # Cross-cutting architecture
│   ├── howto/                     # Practical guides (you are here)
│   ├── security/                  # OWASP coverage and threat model
│   ├── diagnostics.md             # Every PRAG diagnostic, generated from the descriptors
│   └── TESTING.md                 # The gate, and how to read it
│
├── scripts/                       # The gate (check.mjs) and the repository's tooling
│
├── Directory.Build.props          # Shared MSBuild properties (all projects)
├── Directory.Build.targets        # Shared MSBuild targets (packaging, verify)
├── Directory.Packages.props       # Central Package Management (all NuGet versions)
├── Pragmatic.Design.slnx         # Root solution (XML-based .slnx format)
├── global.json                    # .NET SDK version pin
├── .editorconfig                  # Code style + naming conventions
├── README.md                      # Repo overview
├── CONTRIBUTING.md                # Contribution guidelines
├── LICENSE                        # MIT or PolyForm Small Business, per package (docs/LICENSING.md)
└── SECURITY.md                    # Security policy
```

### Layer dependency direction

```
Layer 0 (Foundation)     Layer 1 (Capabilities)     Layer 2 (Integration)
├── Abstractions         ├── Validation             ├── Actions
├── Result               ├── Mapping                ├── Endpoints
├── Ensure               ├── Specification          ├── Persistence + EFCore
├── DependencyInjection  ├── I18n                   ├── Events + EFCore
└── Identifiers          ├── Caching                ├── Composition + Host
                         ├── Resilience             ├── Authorization
                         ├── Configuration          ├── Messaging
                         └── Temporal               └── Jobs
```

Higher layers may depend on lower layers. Same-layer dependencies are allowed when explicitly designed. Never depend upward.

---

## 2. Module Anatomy

Every module follows the same structure:

```
Pragmatic.{Module}/
├── Pragmatic.{Module}.slnx        # Per-module solution (open just this module in IDE)
├── README.md                       # Quick start, API overview
├── SPECIFICATION.md                # Formal spec (where applicable)
├── src/
│   ├── Pragmatic.{Module}/         # Runtime library (net10.0)
│   ├── Pragmatic.{Module}.EFCore/  # EF Core integration (optional)
│   ├── Pragmatic.{Module}.AspNetCore/ # ASP.NET integration (optional)
│   └── Pragmatic.{Module}.SourceGenerator/ # Standalone SG (rare: most use unified SG)
├── tests/
│   └── Pragmatic.{Module}.Tests/
│       ├── Unit/                   # Pure unit tests
│       ├── Generator/              # SG snapshot tests
│       │   └── Snapshots/          # *.verified.cs files
│       └── Integration/            # Cross-module or DB tests
├── samples/
│   └── Pragmatic.{Module}.Samples/ # Runnable code examples
├── benchmarks/                     # BenchmarkDotNet projects (optional)
│   └── Pragmatic.{Module}.Benchmarks/
└── docs/                           # Module-specific documentation
```

### Key rules

- **One file = one class/struct/record.** No exceptions.
- **Namespace = folder path.** A class at `Pragmatic.Actions/src/Pragmatic.Actions/Mutation/Mutation.cs` must be in namespace `Pragmatic.Actions.Mutation`. Exception: files under `shared/` that use `// ReSharper disable once CheckNamespace`.
- **Max 300-400 lines per file.** If larger, split into `partial class` files or refactor responsibilities.
- **File-scoped namespaces** (`namespace Foo;`) enforced by `.editorconfig`.

### Naming conventions (enforced by .editorconfig)

| Element | Convention | Example |
|---------|-----------|---------|
| Class, struct, enum, method, property | `PascalCase` | `CreateReservationMutation` |
| Interface | `IPascalCase` | `IMessageBus` |
| Private field | `_camelCase` | `_repository` |
| Parameter, local | `camelCase` | `userId` |
| Type parameter | `TPascalCase` | `TEntity` |
| Constant | `PascalCase` | `MaxRetryCount` |
| Attribute | `{Name}Attribute`, always generic | `[MapFrom<User>]` not `[MapFrom(typeof(User))]` |
| Error type | `{What}Error` | `NotFoundError` |

---

## 3. Build System

### SDK version

Pinned in `global.json`:

```json
{
  "sdk": {
    "version": "10.0.201",
    "rollForward": "latestPatch"
  }
}
```

All runtime libraries target **net10.0**. Source generators target **netstandard2.0** (Roslyn requirement).

### Directory.Build.props

This file is automatically imported by every project in the repo. It configures:

| Section | What it does |
|---------|-------------|
| **Versioning** | `0.1.0-alpha` across all packages |
| **Build settings** | `LangVersion=latest`, nullable enabled, `TreatWarningsAsErrors=true` |
| **Project type detection** | Auto-detects project type by name suffix: `*.Tests`, `*.SourceGenerator`, `*.Analyzers`, `*.Benchmarks`, `*.Samples` |
| **Generator projects** | Sets `netstandard2.0`, auto-includes `shared/SourceGen/*.cs` as linked files |
| **Test projects** | Sets `net10.0`, adds xunit + Verify.Xunit + coverlet, and references Pragmatic.Testing for assertions and generated mocks, auto-includes `shared/Testing/` and `shared/SourceGen/Testing/` |
| **Runtime libraries** | Sets `net10.0`, enables packaging, AOT compatibility analyzers |
| **Generator-Runtime dual-target** | Opt-in `IsGeneratorRuntime=true` for libraries that need both `netstandard2.0` and `net10.0` |

You almost never need to repeat these settings in individual `.csproj` files.

### Directory.Build.targets

Handles:
- **Generator packaging**: packs SG DLL into `analyzers/dotnet/cs` NuGet folder.
- **Combined packages**: when a generator has a sibling runtime library (e.g., `Pragmatic.Mapping` + `Pragmatic.Mapping.SourceGenerator`), both get packed into a single NuGet.
- **Verify snapshot nesting**: `*.verified.cs` files appear nested under their test file in Solution Explorer.
- **Analyzer warnings**: `CA1822`, `CA1826`, `CA2007` are promoted to errors.

### Central Package Management

All NuGet versions are declared once in `Directory.Packages.props`. Individual `.csproj` files reference packages without a `Version` attribute:

```xml
<!-- In .csproj -->
<PackageReference Include="Verify.Xunit" />

<!-- Version resolved from Directory.Packages.props -->
<PackageVersion Include="Verify.Xunit" Version="28.4.0" />
```

To update a package version, change it in `Directory.Packages.props` only.

### Solution format (.slnx)

This repo uses the new XML-based `.slnx` format, not the legacy `.sln`. The root solution `Pragmatic.Design.slnx` organizes all projects into logical folders:

```
/Pragmatic.Result/          # Foundation modules
/Pragmatic.Validation/      # Capability modules
/Pragmatic.Actions/         # Integration modules
/Tests/                     # All test projects (flat)
/Examples/Showcase/         # Showcase app projects
/Integration Tests/         # E2E test projects
/docs/                      # Solution doc items
```

Each module also has its own `.slnx` (e.g., `Pragmatic.Actions/Pragmatic.Actions.slnx`) so you can open just one module in your IDE without loading the entire monorepo.

---

## 4. Source Generator Architecture

### Unified SG

Almost all code generation lives in a single assembly: `Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/`. This is the main `IIncrementalGenerator` entry point (`PragmaticSourceGenerator.cs`).

```
Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/
├── PragmaticSourceGenerator.cs     # Entry point
├── Core/
│   ├── DetectedFeatures.cs         # Flags for which modules are referenced
│   ├── FeatureDetector.cs          # Scans referenced assemblies
│   └── ServiceTypeDetector.cs      # Identifies interfaces, abstracts, services
├── Features/
│   ├── Actions/                    # Mutation invokers, DI registration
│   ├── Caching/                    # Cache key generation
│   ├── Composition/                # Host bootstrap, startup steps
│   ├── Configuration/              # Config binding
│   ├── Endpoints/                  # HTTP endpoint generation
│   ├── FastEnum/                   # Enum extensions
│   ├── I18n/                       # Translation registries
│   ├── Identity/                   # Permission constants
│   ├── Jobs/                       # Job scheduling
│   ├── Mapping/                    # MapFrom/MapTo
│   ├── Messaging/                  # Handler registration, topology
│   ├── Patch/                      # Patch application
│   ├── Persistence/                # Entity config, repos, queries (6 sub-features)
│   ├── Result/                     # Result extensions
│   └── Validation/                 # Validator registration
├── Compositions/                   # Cross-feature enrichers
└── Diagnostics/                    # Diagnostic descriptors (PRAG0001-PRAG1899)
```

**How it works**: when a project references `Pragmatic.Actions`, `FeatureDetector` sees the Actions DLL in the compilation and sets `HasActions = true` in `DetectedFeatures`. The Actions feature then runs its Transform/Template pipeline to generate code. Remove the NuGet reference, and the feature disappears -- no manual wiring needed.

### Standalone generators (exceptions)

A few modules have their own SG assembly because they are designed to work independently:
- `Pragmatic.Result.SourceGenerator`
- `Pragmatic.Internationalization.SourceGenerator` (Country/Currency/Language)
- `Pragmatic.Logging.SourceGenerator`

### Shared SG code

`shared/SourceGen/` contains utility code automatically linked into every generator project:
- `CSharpTemplate` -- the base class for all code generation templates
- `NamingHelper` -- suffix deduplication, name normalization
- `SymbolExtensions` -- Roslyn symbol helpers
- `GeneratorHelpers` -- common patterns for `IIncrementalGenerator`
- `AttributeNames` -- FQN constants for all Pragmatic attributes
- `VirtualFolderHints` -- consistent hint name generation

Every feature follows the **Transform/Model/Template** pattern:
1. **Transform** -- extracts data from Roslyn symbols into an immutable record model
2. **Model** -- plain immutable data, no Roslyn types (enables caching)
3. **Template** -- extends `CSharpTemplate`, renders the model into C# source

---

## 5. Adding New Code

### Where to put a new entity

Entities go in a **boundary module** under the showcase (or your app). Place the entity class at the root of its sub-boundary folder:

```
Showcase.Booking/
├── Reservations/
│   ├── Reservation.cs            # Entity at folder root
│   ├── Mutations/
│   │   ├── CreateReservationMutation.cs
│   │   └── UpdateReservationMutation.cs
│   ├── Queries/
│   │   └── GetReservationQuery.cs
│   ├── Dtos/
│   │   └── ReservationDto.cs
│   └── Validators/
│       └── CreateReservationValidator.cs
```

The entity class is annotated with Pragmatic attributes (`[Entity]`, `[Auditable]`, `[Relation.*]`, etc.) and the SG handles the rest (repository, entity config, create method, setters).

### Where to put a new action (mutation/query/side effect)

- **Mutations** (write operations): `{SubBoundary}/Mutations/{Name}Mutation.cs`
- **Queries** (read operations): `{SubBoundary}/Queries/{Name}Query.cs`
- **Actions** (side effects): `{SubBoundary}/Actions/{Name}Action.cs`

The SG generates invokers, DI registration, and endpoint wiring automatically from these classes.

### Where to put a new endpoint

Custom endpoints go in `{SubBoundary}/Endpoints/`. Most endpoints are auto-generated from mutations and queries via the `[Endpoint]` attribute, so you only need explicit endpoint files for non-standard routes.

### Where to put tests

| Test type | Location |
|-----------|----------|
| Module unit test | `Pragmatic.{Module}/tests/Pragmatic.{Module}.Tests/Unit/` |
| SG snapshot test | `Pragmatic.{Module}/tests/Pragmatic.{Module}.Tests/Generator/` |
| Showcase unit test | `examples/showcase/tests/Showcase.Tests/Unit/` |
| Showcase integration test | `examples/showcase/tests/Showcase.IntegrationTests/` |
| Cross-module integration | `Pragmatic.Integration.Tests/` |

### Namespace conventions

Namespace must match folder path. For a class at:
```
Showcase.Booking/Reservations/Mutations/CreateReservationMutation.cs
```
The namespace is:
```csharp
namespace Showcase.Booking.Reservations.Mutations;
```

---

## 6. Testing Strategy

### Unit tests

Every module has unit tests in `tests/Pragmatic.{Module}.Tests/Unit/`. Test framework stack:
- **xunit** -- test runner
- **Pragmatic.Testing.Assertions** -- assertions (`.Should()`), ours: no external dependency
- **[GenerateMock<T>]** -- mocks generated by Pragmatic.Testing.Mocking.SourceGenerator, no dynamic proxies

Test naming convention: `{Method}_{Scenario}_{Expected}`

```csharp
// Examples:
// Validate_WithNullInput_ThrowsArgumentNull
// CreateReservation_WithValidData_ReturnsSuccess
// MapToDto_WithMissingProperty_SkipsProperty
```

### Source generator snapshot tests

Every SG feature must have snapshot tests using **Verify**. The pattern:

1. Test class extends a module-specific base that wraps `GeneratorTestHelper`.
2. Feed C# source code to the generator.
3. Assert the generated output matches the `*.verified.cs` snapshot.

```csharp
[Fact]
public Task MapFrom_SimpleClass_GeneratesMapping()
{
    var source = """
        [MapFrom(typeof(User))]
        public partial class UserDto { public string Name { get; set; } }
        """;
    var result = RunGenerator(source);
    return Verify(GetGeneratedSource(result, "UserDto.Mapping"));
}
```

Snapshots live in `Generator/Snapshots/` and are version-scrubbed (every test project must have a `ModuleInitializer.cs` with the version scrubber regex).

### Integration tests (Showcase)

End-to-end tests run against a real PostgreSQL database using **Testcontainers**:

```
examples/showcase/tests/Showcase.IntegrationTests/
├── Authorization/     # Permission enforcement tests
├── DomainActions/     # Mutation/query execution tests
├── Endpoints/         # HTTP endpoint tests
├── EntityPersistence/ # CRUD + soft delete + audit tests
├── Queries/           # Query filter + pagination tests
└── Infrastructure/    # Fixture, base classes
```

Every new feature should have at least one integration test in the Showcase.

### Test counts

The repo currently has ~4,400 tests across all modules. Key numbers: SG 210, Persistence 295, EFCore 585, Actions 273, Result 455, Validation 353, Showcase 200, Integration 153.

---

## 7. Common Commands

### Build

```bash
# Build entire monorepo
dotnet build Pragmatic.Design.slnx

# Build a single module
dotnet build Pragmatic.Actions/src/Pragmatic.Actions/Pragmatic.Actions.csproj

# Build with warnings as errors (CI mode)
dotnet build Pragmatic.Design.slnx --warnaserror
```

### Test

```bash
# Run all tests
dotnet test Pragmatic.Design.slnx

# Run a single module's tests
dotnet test Pragmatic.Actions/tests/Pragmatic.Actions.Tests/

# Run showcase integration tests (requires Docker for PostgreSQL)
dotnet test examples/showcase/tests/Showcase.IntegrationTests/

# Run a specific test by name
dotnet test Pragmatic.Actions/tests/Pragmatic.Actions.Tests/ --filter "CreateMutation_WithValidInput_GeneratesInvoker"

# Run SG tests only
dotnet test Pragmatic.SourceGenerator/tests/Pragmatic.SourceGenerator.Tests/
```

### Working with the Showcase

```bash
# Start infrastructure (PostgreSQL, RabbitMQ)
docker compose -f examples/showcase/docker-compose.yml up -d

# Run the showcase host
dotnet run --project examples/showcase/src/Showcase.Host/

# Run integration tests
dotnet test examples/showcase/tests/Showcase.IntegrationTests/
```

### Package restore

```bash
dotnet restore Pragmatic.Design.slnx
```

### Opening in IDE

You can open the full monorepo:
```
Pragmatic.Design.slnx
```

Or open just one module for faster IDE performance:
```
Pragmatic.Actions/Pragmatic.Actions.slnx
```

Or the showcase independently:
```
examples/showcase/Showcase.slnx
```

---

## Quick Reference

| Question | Answer |
|----------|--------|
| What .NET version? | 10.0 (SDK 10.0.201, pinned in `global.json`) |
| What C# version? | `latest` (currently C# 14) |
| Solution format? | `.slnx` (XML-based, not legacy `.sln`) |
| Package versions? | Central in `Directory.Packages.props` |
| Where is the SG? | `Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/` |
| Where is shared SG code? | `shared/SourceGen/` (auto-linked into generator projects) |
| Where is the showcase? | `examples/showcase/` |
| Where is the roadmap? | `docs/ROADMAP.md` |
| Test framework? | xunit + Verify, with our own assertions and generated mocks |
| Mocking? | Generated from `[assembly: GenerateMock<T>]` -- no mocking library |
| Integration test DB? | PostgreSQL via Testcontainers |
| Git workflow? | Always rebase, never merge. Squash PRs. |
