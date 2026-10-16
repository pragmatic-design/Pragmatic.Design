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
| `node scripts/native-stamp.mjs check` | this machine's native binaries are there, and each one present carries the hash of the Rust source it was built from | every gate run |

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

### `packages.lock.json` for every project, and CI on the exact SDK

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
  newer patch writes different lockfiles and every locked restore fails. So CI runs the exact SDK in
  `global.json`: an SDK update is a change of its own that rewrites those files in the same commit, and the
  full tier fails when the lockfiles disagree on the ILLink version or `rollForward` would let CI install
  another SDK.
- `rollForward` was first `patch` (2026-10-11). A clone then failed outright on a machine whose only .NET 10
  SDK belonged to a newer feature band, which is any machine updated since. It is now `feature`:
  `setup-dotnet` still installs the exact version (it moves only for the `latest…` policies), and a newer
  local SDK builds. That SDK may rewrite the lockfiles on restore; those changes are not committed, and
  CI's locked restore rejects them if they are.

The decision moved (2026-10-10) because a content hash per package is what makes a restore reproducible,
which the registry signature alone is not: a signature says who published a package, not that it is the
one this commit built against.

### The native libraries are built by CI, not committed

The packages ship six shared libraries: the PDF renderer (built on Typst) and the imaging library, each
for win-x64, linux-x64 and osx-arm64. None of them is in the repository (#159). This is where they come
from, and how far to trust them.

- **The source is in the repository.** The crates are `Pragmatic.Documents/native/pragmatic-pdf` and
  `Pragmatic.Imaging/native/pragmatic-imaging`, plus `shared/native`. Each has its `Cargo.lock`.
- **CI builds them.** The public workflows `pdf-native.yml` and `imaging-native.yml` test each crate,
  build it for the three RIDs (linux-x64 in a `rust:1-bookworm` container, so the library loads on that
  glibc), verify its stamp and upload it as an artifact named after the source hash,
  `<crate>-native-<rid>-<hash>`.
- **Every run uses the build of its own source.** `ci.yml` and `pack-validate.yml` first ask whether a
  run has built the current source (`scripts/fetch-native.mjs --probe`). When none has, they call the
  two workflows in the same run; then the gate, the container shards and the pack download the build by
  its name. The jobs that test install no Rust toolchain.
- **Each binary names the source it came from.** At build time, each crate's `build.rs` writes the
  SHA-256 hash of that crate's source into the library. `fetch-native.mjs` refuses a download whose stamp
  is not the current source's, and every gate run hashes the source again and reads the stamp back out
  of each binary in `runtimes/` (`scripts/native-stamp.mjs`).
- **What a release ships.** `release.yml` packs with `build-native`: the libraries are built in the
  release's own run, from the tagged commit, and the pack takes only those. Each library is attested as a
  file of its own before the push, and every `.nupkg` carries its own build-provenance attestation (see
  "Releases" above).
- **What the stamp does not prove.** It proves that a binary matches the current source as its builder
  stamped it, and it catches a stale binary. It is not a signature, and the builds are not verified to be
  reproducible bit for bit. The attestation is what says which workflow, on which commit, built a released
  library.
- **How to check it yourself.**
  - `gh attestation verify <library> --repo pragmatic-design/Pragmatic.Design`, on a library taken out
    of a released package's `runtimes/`.
  - Or rebuild from source with `node scripts/refresh-native.mjs` (a Rust toolchain, and Docker for
    Linux), then run `node scripts/native-stamp.mjs check`.
- **On a developer's machine.** The gate needs that machine's libraries in `runtimes/`, and fails without
  them, naming two ways to get them: `node scripts/fetch-native.mjs` downloads CI's build of the current
  source (the GitHub CLI, logged in), and `node scripts/refresh-native.mjs` builds them. Both write files
  `.gitignore` keeps out of the repository.

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
