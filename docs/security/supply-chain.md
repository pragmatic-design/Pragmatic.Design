# Supply chain

What the repository depends on, how each dependency reaches a build, and what is checked on the way.
Where a common control is not used, the reason is here, so the question is not reopened on every
OpenSSF Scorecard run.

## Where packages come from

| Ecosystem | Pinned in | Source |
|---|---|---|
| NuGet | `Directory.Packages.props` (central package management) | nuget.org only: the root `nuget.config` clears the machine's feeds and fallback folders, and maps every package to nuget.org |
| npm (the two sites) | `site/pnpm-lock.yaml` | the npm registry |
| Rust (the native libraries) | `Cargo.lock` of each crate | crates.io |
| GitHub Actions | every `uses:` line, by commit SHA | GitHub |

`examples/consumer-samples/NuGet.config` adds a local feed for `Pragmatic.*`, for testing unreleased
builds; nothing else in the repository reads it.

## What is checked, and when

| Check | Covers | Runs |
|---|---|---|
| `node scripts/check.mjs` (`supply-chain.mjs`) | NuGet, direct and transitive: a CycloneDX SBOM per shipped package and `dotnet list package --vulnerable` | every gate run; CI always scans, a local run reuses a clean scan for 12 hours while the graph is unchanged |
| OpenSSF Scorecard (`scorecard.yml`) | every lockfile it can read, through OSV, plus workflow and repository settings | every push to `main` and weekly |
| CodeQL (`codeql.yml`) | the C# sources and the workflows | every pull request, every push to `main`, weekly |
| Dependabot version updates (`.github/dependabot.yml`) | NuGet, npm and Actions, grouped | weekly |
| Dependabot alerts (the dependency graph) | every ecosystem above, Cargo included, against the GitHub Advisory Database | when an advisory is published |
| `node scripts/native-stamp.mjs check` | each committed native binary carries the hash of the Rust source it was built from | every gate run |

**Gap.** Nothing in the gate scans the Rust crates or the npm packages, and Dependabot proposes no
version updates for Cargo. Their advisories reach us as Dependabot alerts, for those with a GitHub
advisory, and through Scorecard, which also reports RustSec notices such as "unmaintained". 23 had
accumulated in the native crates before Scorecard was added, while the dependency graph was off.

## Accepted advisories

An advisory with no release to move to is recorded in an `osv-scanner.toml` next to its lockfile, with
the reason, and an expiry date so it is looked at again. OpenSSF Scorecard and OSV-Scanner read them
from there; a Dependabot alert for the same advisory is dismissed with the same reason.

- `Pragmatic.Documents/native/pragmatic-pdf/osv-scanner.toml`: quick-xml, held below its fix by
  citationberg and reached only through bibliographies, citations and highlighted code, which the
  renderer cannot produce; and the unmaintained notices of crates Typst depends on.
- `Pragmatic.Imaging/native/pragmatic-imaging/osv-scanner.toml`: paste, a compile-time macro.
- `site/osv-scanner.toml`: http-cache-semantics, which matters to a shared HTTP cache, while both sites
  are built to static files.

## Releases

- Packages are published by `release.yml` through nuget.org Trusted Publishing: no API key is stored.
- Every `.nupkg` is attested twice. `release.yml` attests the file it builds, before the push. nuget.org
  then adds its repository signature inside the package, which changes the file's digest, so
  `attest-published.yml` waits until nuget.org serves each package, checks that it is the built package
  plus that signature and nothing else, and attests the file as served. A package downloaded from
  nuget.org is verified with `gh attestation verify <file>.nupkg --repo pragmatic-design/Pragmatic.Design`,
  or offline with `--bundle provenance-<version>.intoto.jsonl`, the bundle attached to the GitHub Release
  next to `sbom-<version>.zip`.
- The publishing job holds no token that can write to the repository. Every workflow starts from a
  read-only token, and a write is granted on the one job that needs it.

## Decisions

### `packages.lock.json` for every project, and the SDK pinned to the patch

Every project in the solution commits a lockfile, and CI restores in locked mode (`docs/TESTING.md`,
"Lock files"). Central package management fixes the versions a project asks for, not what a restore
brings back; a lockfile records each package's content hash, and a locked restore fails when what it
would resolve differs. OpenSSF Scorecard counts a `dotnet restore` as pinned only with `--locked-mode`.

This was first decided the other way (2026-10-03), and the costs measured then still hold:

- 372 lockfiles, about 118,000 lines.
- A version bump rewrites a large share of them: `Microsoft.Extensions.DependencyInjection.Abstractions`
  269, xunit 128, `Microsoft.EntityFrameworkCore` 98. They are generated, and the rewrite is one command.
- The trimmable projects record `Microsoft.NET.ILLink.Tasks`, whose version follows the SDK's bundled
  runtime. With `global.json` rolling forward to the latest patch, a runner image or a contributor on a
  newer patch writes different lockfiles and every locked restore fails. So `global.json` now pins the
  patch (`rollForward: patch`): an SDK update is a change of its own that rewrites those files in the same
  commit, and the full tier fails when the lockfiles disagree on the ILLink version or the pin is loosened.

The decision moved (2026-10-10) because a content hash per package is what makes a restore reproducible,
which the registry signature alone is not: a signature says who published a package, not that it is the
one this commit built against.

### Minimum versions, not exact ones

`Directory.Packages.props` uses `1.2.3`, not `[1.2.3]`. Scorecard counts only the second as pinned, but
these versions become the dependency ranges of the published packages: exact ones would lock every
application that uses Pragmatic to one version of each dependency, including one with a security fix
waiting.

### No `signatureValidationMode=require`

In the default mode NuGet already verifies the signature of every signed package it restores on Windows
and Linux, where CI and the release run (not on macOS, where the .NET SDK leaves it off), and every
package on nuget.org is signed by the registry. `require` would only reject unsigned packages,
which nuget.org does not serve, at the cost of keeping nuget.org's certificate fingerprints in
`nuget.config` and failing every restore when they rotate.
