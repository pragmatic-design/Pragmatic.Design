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
- Every `.nupkg` pushed carries a signed build-provenance attestation. The signed bundle is attached to
  the GitHub Release as `provenance-<version>.intoto.jsonl`, next to `sbom-<version>.zip`. Verify a
  package with `gh attestation verify <file>.nupkg --repo pragmatic-design/Pragmatic.Design`, or
  offline with `--bundle provenance-<version>.intoto.jsonl`.
- The publishing job holds no token that can write to the repository. Every workflow starts from a
  read-only token, and a write is granted on the one job that needs it.

## Decisions

### No `packages.lock.json`

OpenSSF Scorecard counts a `dotnet restore` as pinned only with `--locked-mode`, which needs a lockfile
per project. Measured on 2026-10-03, with the solution as it was:

- 371 lockfiles, 4.6 MB, about 118,000 lines.
- A version bump rewrites a large share of them: `Microsoft.Extensions.DependencyInjection.Abstractions`
  269, xunit 128, `Microsoft.EntityFrameworkCore` 98.
- 174 of them record `Microsoft.NET.ILLink.Tasks`, whose version follows the installed SDK. `global.json`
  rolls forward to the latest patch, so two machines on different SDK patches write different lockfiles,
  and `--locked-mode` fails on one of them. Making it hold means pinning the SDK exactly for every
  contributor and rewriting those 174 files on every SDK update.

What a lockfile would add is a content hash for every package and transitive changes visible in a diff.
The first is largely covered by restoring from nuget.org alone, where every package carries the
registry's signature; the second does not pay for the cost above. Revisit if the SDK is pinned exactly
for another reason, or after a supply-chain incident on NuGet.

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
