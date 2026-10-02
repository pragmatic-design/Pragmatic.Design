# Dockerized Local NuGet Feed

Use BaGetter to consume Pragmatic.Design packages from a real NuGet feed before public publishing.

## Start BaGetter

PowerShell:

```powershell
docker run --rm --name bagetter `
  -p 5555:8080 `
  -v "${PWD}\.bagetter-data:/data" `
  -e ApiKey=dev-key `
  -e Storage__Type=FileSystem `
  -e Storage__Path=/data/packages `
  -e Database__Type=Sqlite `
  -e Database__ConnectionString="Data Source=/data/bagetter.db" `
  -e Search__Type=Database `
  bagetter/bagetter:latest
```

Feed URL: `http://localhost:5555/v3/index.json`

⚠️ From Git Bash, prefix the command with `MSYS_NO_PATHCONV=1`: otherwise `/data/packages` is rewritten
into a Windows path outside the volume, and every package is lost when the container goes.

## Consumer NuGet.config

Place this beside the consumer solution/project:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-bagetter" value="http://localhost:5555/v3/index.json" allowInsecureConnections="true" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local-bagetter">
      <package pattern="Pragmatic.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

## Pack And Push From Pragmatic Monorepo

From the Pragmatic.Design repo root:

```powershell
node scripts/publish-local.mjs          # next version after the highest on the feed
node scripts/publish-local.mjs 3958     # or an exact build number
```

It builds Release from clean, packs, pushes, and clears NuGet's HTTP cache. ⚠️ Do not replace it with
`dotnet pack` + `dotnet nuget push --skip-duplicate`: a pack without a clean build can publish stale
binaries, and `--skip-duplicate` turns a push of a version already on the feed into a silent skip, so
the consumer keeps restoring the old package while everything reports success.

## Consumer Restore

From the consumer project:

```powershell
dotnet restore
dotnet build
```

After a new version is published, a consumer on floating versions (`1.0.0-alpha.*`) has to re-resolve:

```powershell
dotnet restore <consumer>.slnx --force-evaluate
```

If restore still uses stale packages: `dotnet nuget locals all --clear`.
