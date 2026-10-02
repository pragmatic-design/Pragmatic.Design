---
name: pragmatic-nuget-feed
description: Use when setting up the dockerized local NuGet feed (BaGetter) for Pragmatic.Design packages, or when restore or build fails with NU#### or feed and source errors.
argument-hint: "[consumer-project-path]"
shell: powershell
---

# Pragmatic NuGet Feed

**Covers:** Set up and troubleshoot a dockerized local NuGet feed for consuming Pragmatic.Design packages from BaGetter in external projects.

Use this to make Pragmatic packages consumable by an agent or project that cannot rely on monorepo source access.

## Workflow

1. Read `../pragmatic-ecosystem/references/nuget-feed.md`.
2. Start BaGetter with Docker.
3. Publish from the Pragmatic.Design monorepo if available: `node scripts/publish-local.mjs`.
4. Add `NuGet.config` to the consumer project so `Pragmatic.*` resolves from `local-bagetter`.
5. Restore (`--force-evaluate` after a new version) and build the consumer project.
6. If packages are stale, publish a new version rather than re-pushing the same one, and clear NuGet locals.

## Consumer Contract

The consumer project should not use `ProjectReference` to Pragmatic sources. It should use `PackageReference` and behave like an external user of the packages.

## Troubleshooting

- `NU1301` or feed unreachable: verify `docker ps` and `http://localhost:5555/v3/index.json`.
- Package not found: pack and push from the monorepo, then restore again.
- Old package behavior: `dotnet nuget locals all --clear`.
- Generated members missing: add `Pragmatic.SourceGenerator` analyzer reference and rebuild.
