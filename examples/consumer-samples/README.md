# Consumer samples

Samples that consume Pragmatic.Design packages via **PackageReference** against
a local [BaGetter](https://github.com/bagetter/BaGetter) feed, exactly as an
external project would consume them from nuget.org.

This is the shakedown tier: it catches packaging regressions — missing asset
paths, analyzer wiring, `InternalsVisibleTo` surprises, transitive dependency
closure — that `ProjectReference` in the in-tree samples cannot catch.

## When to run

After any change that touches packaging (`Directory.Build.props`, generator
csproj, NuGet-related targets). The `samples-smoke.yml` CI matrix runs the
in-tree ProjectReference samples; running these consumer samples locally
is the cheap way to prove the packaging is still clean before publishing.

## Running locally

1. Start BaGetter — see `docs/howto/local-nuget-server.md`.
2. From the repo root: `dotnet pack Pragmatic.Design.slnx -c Release --output ./artifacts`.
3. Push every `.nupkg` to BaGetter (see the howto).
4. `dotnet run --project examples/consumer-samples/Pragmatic.Ensure.Consumer`
   (the repo-root `NuGet.config` already maps `Pragmatic.*` to `local-bagetter`).

## Against the released packages

The samples take local builds (`1.0.0-alpha.0.*`), because their job is to
check a pack before it is published. To run them against what nuget.org
already has instead, remove the `local-bagetter` source and its mapping from
`NuGet.config` here, and set the `Pragmatic.*` versions to a released one
(`1.0.0-alpha.1`). No code changes.

## Layout

| Sample | What it proves |
|--------|----------------|
| `Pragmatic.Ensure.Consumer` | Pure library (no SG, no analyzer) — smallest possible consumer; proves core packaging metadata (LICENSE, README, SourceLink) |
| `Pragmatic.Logging.Consumer` | Library + source generator — proves `[LoggerMessage]` SG is packed as analyzer and fires on external consumption |
| `Pragmatic.Jobs.Consumer` | Library + SG + hosted service — proves runtime + analyzer + transitive Pragmatic.Temporal/Ensure all flow through a `PackageReference` |
