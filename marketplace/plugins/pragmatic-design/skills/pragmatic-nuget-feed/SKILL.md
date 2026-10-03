---
name: pragmatic-nuget-feed
description: Use when a restore or build fails with NU#### or feed and source errors, or when consuming Pragmatic.Design builds that are not on nuget.org yet through a dockerized local NuGet feed (BaGetter).
argument-hint: "[consumer-project-path]"
shell: powershell
---

# Pragmatic NuGet Feed

**Covers:** Restore problems with the Pragmatic.Design packages, and a dockerized local NuGet feed (BaGetter) for consuming builds that are not released yet.

The released packages are on nuget.org as prereleases: `dotnet add package Pragmatic.<Name> --prerelease`, with no `NuGet.config` and no feed to run. A local feed is needed only to consume a build from a clone of the repository before it is released.

## Workflow (local feed, unreleased builds)

1. Read `../pragmatic-ecosystem/references/nuget-feed.md`.
2. Start BaGetter with Docker.
3. Publish from the Pragmatic.Design monorepo if available: `node scripts/publish-local.mjs`.
4. Add `NuGet.config` to the consumer project so `Pragmatic.*` resolves from `local-bagetter`.
5. Restore (`--force-evaluate` after a new version) and build the consumer project.
6. If packages are stale, publish a new version rather than re-pushing the same one, and clear NuGet locals.

## Consumer Contract

The consumer project should not use `ProjectReference` to Pragmatic sources. It should use `PackageReference` and behave like an external user of the packages.

## Troubleshooting

- "There are no stable versions available" from `dotnet add package`: every version is a prerelease, so add `--prerelease`. The same cause in a restore is `NU1103`, from a stable range such as `1.*`: name the version (`1.0.0-alpha.1`).
- `NU1301` or feed unreachable: verify `docker ps` and `http://localhost:5555/v3/index.json`.
- Package not found: pack and push from the monorepo, then restore again.
- Old package behavior: `dotnet nuget locals all --clear`.
- Generated members missing: add `Pragmatic.SourceGenerator` analyzer reference and rebuild.
