# Contributing to Pragmatic.Design

Thank you for considering contributing to Pragmatic.Design! This document explains how to get started.

## Getting Started

### Prerequisites

- .NET 10.0 SDK (see `global.json` for exact version)
- Git

### Getting the source

```bash
git clone https://github.com/pragmatic-design/Pragmatic.Design.git
cd Pragmatic.Design
dotnet restore Pragmatic.Design.slnx
```

### Building and Running Tests

One script defines the build and the test run, and CI executes the same one:

```bash
# Everything: clean build plus all suites. This is the gate.
node scripts/check.mjs --tier all

# While working: build and hermetic tests for the modules you changed
node scripts/check.mjs --tier fast
```

Please do not substitute `dotnet build` or `dotnet test` over the whole solution.
Both mislead here, in opposite directions. Without `--no-incremental` MSBuild skips
projects it considers up to date, so the build can report no errors on sources that
report fourteen when compiled clean. And `dotnet test` on the solution starts the
Testcontainers suites in parallel, which saturates Docker and fails tests that pass
in isolation. The script does a clean build and runs the container suites one at a
time. See [docs/TESTING.md](docs/TESTING.md).

## How to Contribute

### Reporting Bugs

Open an issue with:
- Module name and version
- Minimal reproduction steps
- Expected vs actual behavior
- .NET SDK version (`dotnet --info`)

### Suggesting Features

Open a discussion or issue describing:
- The problem you're solving
- Your proposed approach
- Which modules are affected

### Submitting Pull Requests

1. Fork the repository
2. Create a feature branch from `main`
3. Follow the code conventions below
4. Add tests for new functionality
5. Ensure the gate is green (`node scripts/check.mjs --tier all`)
6. Submit a PR with a clear description

### Code Conventions

- **One file = one class** (use `partial` for large classes)
- **Source generators** must use `CSharpTemplate` (no `StringBuilder`)
- **Attributes are always generic**: `[MapFrom<Order>]` not `[MapFrom(typeof(Order))]`
- **Result over exceptions** for business logic
- **`Ensure.ThrowIfNull()`** for guard clauses
- **Modern C# 14** idioms (primary constructors, pattern matching, field keyword)
- **Namespace = folder structure**

Coding agents should start at [AGENTS.md](AGENTS.md), which covers the build, the layout, and
the invariants that are easiest to break. [docs/CONVENTIONS.md](docs/CONVENTIONS.md) holds the
complete standard for both agents and humans.

### Commit Messages

We use conventional commits:

```
feat(persistence): add bulk upsert support
fix(mapping): handle nullable nested DTOs
docs(actions): add cross-module integration guide
test(validation): add async validator edge cases
refactor(sg): extract ProjectionFeature from PersistenceFeature
```

Format: `type(scope): description` — a type among `feat`, `fix`, `docs`, `test`, `refactor`, `perf`,
`build`, `ci`, `chore`, `style`, `revert`; the description in lowercase and the imperative mood; the
whole header at most 72 characters; `!` before the colon for a breaking change. The body says why.

A message never carries:

- a key from an issue tracker, a review tag or a reference to a document outside this repository —
  link the GitHub issue in the pull request instead;
- a path from your machine, or a link to a private session or tool.

The rules are checked, not only written: enable the hook once per clone with
`git config core.hooksPath .githooks`, and the `Commit Messages` workflow checks every pull request —
its title too, since a pull request is merged by squash and the title becomes the commit.

### Testing Requirements

- Every public API must have unit tests
- Source generator output must have snapshot tests (Verify)
- Edge cases: null, empty, boundary values
- Generator tests use `GeneratorTestHelper` from `shared/SourceGen/Testing/`

## Project Structure

```
Pragmatic.Design/
├── Pragmatic.{Module}/
│   ├── src/Pragmatic.{Module}/       # Runtime library
│   ├── tests/Pragmatic.{Module}.Tests/
│   ├── samples/                       # Optional
│   └── docs/                          # Optional detailed guides
├── Pragmatic.SourceGenerator/         # Unified source generator
├── examples/                          # Showcase, reference applications, Conformance
├── shared/SourceGen/                  # Shared SG infrastructure
└── docs/                              # Repo-level documentation
```

## License

Pragmatic.Design is dual-licensed per package — see [docs/LICENSING.md](docs/LICENSING.md). By
contributing, you agree your contribution is licensed to the project under the same terms as the
package it targets ([MIT](licenses/LICENSE-MIT.txt) for the MIT packages;
[PolyForm Small Business](licenses/LICENSE-PolyForm-Small-Business-1.0.0.txt) for the framework-runtime
packages), and that the maintainer may also offer it under the commercial license that applies above
the small-business threshold.
