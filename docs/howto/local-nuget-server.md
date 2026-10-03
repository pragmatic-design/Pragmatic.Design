---
title: Local NuGet server for preview testing
---

# Local NuGet server for preview testing

The released packages are on [nuget.org](https://www.nuget.org/profiles/Pragmatic.Design), and an
application that uses them needs none of this. This guide is for consuming a build that is not
released yet, from a clone of the repository: before a release, or to try a change in a real project.
The cleanest path is a local NuGet server: packages are `dotnet pack`ed from the monorepo, pushed to
the local server, and consumed from test projects exactly as they would be from nuget.org.

This guide covers [BaGetter](https://github.com/bagetter/BaGetter), a
community-maintained fork of BaGet with current dependency updates.

## Prerequisites

- Docker Desktop (Windows) or Docker Engine (Linux/macOS)
- The Pragmatic.Design repository checked out locally

## 1. Run BaGetter

```bash
docker run --rm --name bagetter \
  -p 5555:8080 \
  -v $PWD/.bagetter-data:/data \
  -e ApiKey=dev-key \
  -e Storage__Type=FileSystem \
  -e Storage__Path=/data/packages \
  -e Database__Type=Sqlite \
  -e Database__ConnectionString="Data Source=/data/bagetter.db" \
  -e Search__Type=Database \
  bagetter/bagetter:latest
```

BaGetter is now available at `http://localhost:5555`.

Open it in a browser to verify the UI loads.

## 2. The feed is already configured where it is consumed

There is **no `NuGet.Config` at the repo root**, and there should not be: the repository builds from
project references, so a root config would change how every project here restores in order to serve
a feed only the consumer samples read.

What exists is `examples/consumer-samples/NuGet.config`, scoped to exactly that: the local feed, plus
package source mapping so `Pragmatic.*` comes from BaGetter and everything else from nuget.org.

```xml
<packageSources>
  <clear />
  <add key="local-bagetter" value="http://localhost:5555/v3/index.json" allowInsecureConnections="true" />
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
</packageSources>
<packageSourceMapping>
  <packageSource key="local-bagetter"><package pattern="Pragmatic.*" /></packageSource>
  <packageSource key="nuget.org"><package pattern="*" /></packageSource>
</packageSourceMapping>
```

For a project of your own outside this repository, copy that file beside its solution, or register the
source globally once:

```bash
dotnet nuget add source http://localhost:5555/v3/index.json --name local-bagetter --allow-insecure-connections
```

## 3. Publish, with one command

```bash
node scripts/publish-local.mjs
```

It does the whole thing: a **clean Release build** (a bare `dotnet pack` packs whatever is in `bin/`,
which is how a package once shipped stale bits), the pack, the push, and clearing NuGet's HTTP cache,
without which a consumer restores the previous version from cache and verifies the wrong bits.

The version is `1.0.0-alpha.0.N`: with no argument the script reads the feed for the highest `N` it has
and adds one, and on a feed that has never seen these packages it starts at `1`. Give it a number to
choose: `node scripts/publish-local.mjs 42`.

It also writes `artifacts/local-feed.nuget.config` the first time, which is the config the push reads
its source from. Nothing outside the repository is needed.

## 4. Consume from a test project

In any project under `examples/consumer-samples/`, where the feed is already configured:

```xml
<ItemGroup>
  <PackageReference Include="Pragmatic.Result" Version="1.0.0-alpha.0.*" />
</ItemGroup>
```

⚠️ The floating version must match what the script publishes. The three consumer samples ask for
`1.0.0-alpha.0.*`, and a gate case holds that they and the script agree. They once asked for
`0.1.0-preview.*`, which nothing had ever published, so they proved nothing while restoring cleanly
from nuget.org.

Then:

```bash
dotnet restore
```

NuGet resolves the reference from BaGetter first (local) and falls back to
nuget.org if not found.

## Bumping a version during testing

MinVer derives the version from the most recent `nuget-v*` tag plus commit
height. To test a specific version:

```bash
# Tag locally (not pushed to origin)
git tag nuget-v0.1.0-preview.1

# Pack again: packages now versioned 0.1.0-preview.1
dotnet pack Pragmatic.Design.slnx --configuration Release --output ./artifacts

# Push to BaGetter
for pkg in ./artifacts/*.nupkg; do
  dotnet nuget push "$pkg" --source http://localhost:5555/v3/index.json --api-key dev-key --skip-duplicate
done

# When done, remove the local tag
git tag -d nuget-v0.1.0-preview.1
```

## Clearing the feed

If you need a fresh state (e.g. you pushed a broken build):

```bash
docker stop bagetter
rm -rf .bagetter-data
```

Then restart the container.

## Troubleshooting

**"Package already exists".** BaGetter rejects re-pushes of the exact same
version. Either bump (new commit ⇒ new MinVer height) or use
`--skip-duplicate`.

**NuGet.exe restore finds old version from cache.** Clear the local cache:

```bash
dotnet nuget locals all --clear
```

**Package missing README / LICENSE.** `Directory.Build.props` packs them
automatically. If a module lacks its own README, the repo-root README is
used as fallback.
