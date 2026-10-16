# Contributing to Pragmatic.Design

Thank you for considering contributing to Pragmatic.Design! This document explains how to get started.

## Getting Started

### Prerequisites

- .NET 10.0 SDK, the version in `global.json` or any newer 10.0 SDK. CI uses that exact version. A newer
  SDK may rewrite the NuGet lock files when it restores: leave those changes out of your commit, as CI
  would reject them ([docs/TESTING.md](docs/TESTING.md#lock-files)).
- Git

### Getting the source

```bash
git clone https://github.com/pragmatic-design/Pragmatic.Design.git
cd Pragmatic.Design
dotnet restore Pragmatic.Design.slnx
```

The PDF and Imaging modules load native libraries that are not in the repository: CI builds them from the
Rust source under each module's `native/`. The gate fails until your machine's are in place, and says so.
Either of these puts them there:

```bash
# CI's build of the source you checked out (needs the GitHub CLI, logged in)
node scripts/fetch-native.mjs

# Or build them yourself (needs a Rust toolchain)
node scripts/refresh-native.mjs
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

A question is not an issue: ask it in
[Discussions → Q&A](https://github.com/pragmatic-design/Pragmatic.Design/discussions/categories/q-a).
[SUPPORT.md](SUPPORT.md) says where everything else goes.

### Reporting Bugs

Open an issue with:
- Module name and version
- Minimal reproduction steps
- Expected vs actual behavior
- .NET SDK version (`dotnet --info`)

### Suggesting Features

Start in [Discussions → Ideas](https://github.com/pragmatic-design/Pragmatic.Design/discussions/categories/ideas),
describing:
- The problem you're solving
- Your proposed approach
- Which modules are affected

### Taking an Issue

Every issue somebody is working on is assigned to them, maintainers included: an assigned issue is
taken, an unassigned one is free.

1. **Ask on the issue.** Comment that you would like to work on it; for anything beyond a small fix, add
   a line on how you would approach it.
2. **Wait for the assignment.** A maintainer replies either way: they assign the issue to you, or say
   why they are keeping it (it may already be planned, or depend on other work). Work started before
   the assignment may overlap with somebody else's.
3. **Keep it moving.** Once assigned, the issue is yours. A draft pull request early, or a comment with
   where you are, is what tells everybody else it is still taken.
4. **Quiet means free again.** After **14 days** without a comment or a pull request from you, a
   maintainer asks for an update; with no reply **7 days** later, the issue is unassigned so somebody
   else can take it. Saying you cannot carry on is always fine, and frees it at once.

A pull request for an issue assigned to somebody else is not merged ahead of them.

### Submitting Pull Requests

1. Take the issue the change closes, as above (a typo or a broken link needs no issue)
2. Fork the repository
3. Create a feature branch from `main`
4. Follow the code conventions below
5. Add tests for new functionality
6. Ensure the gate is green (`node scripts/check.mjs --tier all`)
7. Submit a PR with a clear description

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

Pragmatic.Design is dual-licensed per package — see [docs/LICENSING.md](docs/LICENSING.md).

Contributions are accepted under the [Contributor License Agreement](CLA.md). You keep the copyright in
what you wrote; the agreement lets the maintainer distribute it under every license the project is
offered under — MIT, PolyForm Small Business, and the commercial license above the threshold. On your
first pull request the CLA Assistant bot asks you to sign it, once, with your GitHub account; a pull
request is merged only after that.
