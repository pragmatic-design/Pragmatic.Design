---
title: Versioning & Compatibility
description: Release status, supported runtimes, and the package-version policy for Pragmatic.Design.
---

Pragmatic.Design's first public release is **`1.0.0-alpha`**. This page states what
that means for adopters: which runtimes are supported, how packages are versioned,
and what stability to expect before the 1.0 release.

## Release status

:::caution[Alpha]
Pragmatic.Design is in **public alpha (`1.0.0-alpha`)**. Public APIs are largely
settled but **may still change before 1.0** without a long deprecation cycle. Pin
exact package versions and read the per-module `CHANGELOG.md` before upgrading.
:::

## Supported runtimes

| Component | Target framework | Notes |
|-----------|------------------|-------|
| Runtime libraries & generated code | **net10.0** | .NET 10 (LTS) is required to consume the packages. |
| Source generators / analyzers | **netstandard2.0** | Required by Roslyn; runs inside the compiler regardless of your app's TFM. |
| SDK used to build | **10.0.302** (`rollForward: latestPatch`) | See `global.json`. |
| C# language version | **14** (`latest`) | Primary constructors, `field`, extension members, collection expressions. |

You need the **.NET 10 SDK** to build and the **.NET 10 runtime** to run. The
generators target `netstandard2.0` only so they load in the compiler — this does
not change your app's runtime requirement.

## Database providers

The EF Core integration supports **PostgreSQL**, **SQL Server**, and **SQLite**.
Provider-specific behavior (e.g. recursive-CTE dialect, bulk SQL) is selected at
runtime from the active provider.

## Package versioning

- Versions are driven by **MinVer** from git tags (e.g. `nuget-v1.0.0-alpha.1`),
  so every package in a release shares one coherent version.
- Pre-release builds carry an `-alpha.N` suffix. **Do not** rely on `*` floating
  version ranges during the alpha — pin exact versions.
- All Pragmatic.* packages are intended to be used at the **same version**; mixing
  versions across modules is unsupported.

## Stability expectations before 1.0

- **Source-breaking changes** to public APIs are possible between alpha/pre-release versions.
  They will be called out in the affected module's `CHANGELOG.md`.
- **Generated-code shape** (DTOs, invokers, endpoints) may change; treat generated
  files as build output, never edit them.
- **Diagnostic IDs** (`PRAG####`) are stable once published; see
  [Diagnostics](/reference/diagnostics/).

## Upgrading

There is no automated migration tooling yet. When upgrading a pre-release:

1. Update all `Pragmatic.*` packages to the same new version together.
2. Re-build with `dotnet build` and address any new analyzer diagnostics.
3. Review each touched module's `CHANGELOG.md` for breaking notes.

A consolidated upgrade guide will land closer to v1.0.
