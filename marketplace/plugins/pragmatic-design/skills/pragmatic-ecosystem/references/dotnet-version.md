# .NET 10 or .NET 11

An application targets `net10.0` (LTS) or `net11.0` (STS). Both are supported by Microsoft until
2028-11-14, and the Pragmatic packages and generator are the same on both, with the same public API.

## Which one to write

1. **The project already has a target**: keep it. Read `TargetFramework` in the project files or in
   `Directory.Build.props`; never move a project from one release to the other unless asked.
2. **The user names one**: use it.
3. **Neither**: `net10.0`. Four issues an application meets on `net11.0` are fixed and the fixes are
   not in 1.0.0-alpha.4 (listed below); once the skills name the release after it, the default is the
   newest SDK the user has installed (`dotnet --version`).

## What changes between the two

| | `net10.0` | `net11.0` |
|---|---|---|
| `global.json` | `"version": "10.0.100"` | `"version": "11.0.100"` |
| `TargetFramework` | `net10.0` | `net11.0` |
| C# | 14 | 15 |
| EF Core provider | `10.0.*` | `10.0.*` as well: Pragmatic's EF Core packages are built on EF Core 10 |
| Generated files | `obj/Debug/net10.0/generated/` | `obj/Debug/net11.0/generated/` |

Nothing else: the same `PackageReference` lines, the same `Program.cs`, the same attributes. Four
packages ship a `net11.0` build that NuGet picks on its own (`Pragmatic.Configuration`,
`Pragmatic.Documents.Templates`, `Pragmatic.Endpoints.OpenApi`, `Pragmatic.Result.AspNetCore`); do
not reference anything different for it.

Moving an existing project: change `TargetFramework` and the `global.json` version, then build. A
project that pins `LangVersion` keeps its language.

## Not in 1.0.0-alpha.4

On 1.0.0-alpha.4, an application on `net11.0` meets these, each fixed since:

- with `Microsoft.AspNetCore.OpenApi` 11 referenced (it brings `Microsoft.OpenApi` 3), a runtime
  OpenAPI document with `AddResultTypeSupport()` or `AddPragmaticOpenApi()` answers 500; keep that
  package at `10.0.*`. The compile-time document `MapPragmaticOpenApi` serves is not affected;
- a background service that fails once the host runs leaves the host up and answering 503, instead of
  exiting;
- a `double` stored where the documents engine expects a `decimal` reads back differently;
- with the schema snapshot turned on (`<PragmaticSchemaSnapshot>true</PragmaticSchemaSnapshot>`, off
  by default), the build warns that the snapshot was not regenerated: `pragmatic-migrate` cannot load
  `System.Runtime 11.0.0.0`.

What `net11.0` adds is not in 1.0.0-alpha.4 either:

- a C# 15 union as an operation's error type,
  `public partial union PlaceOrderError(NotFoundError, ConflictError) : IError;`, beside the
  arities `Result<T, E1, E2>` that stay valid on both targets (`pragmatic-use-foundation`);
- the `net11.0` builds of the four packages above.

The full page, with the measurements and the issues: the guide *Choosing .NET 10 or .NET 11*
(`docs/howto/choose-dotnet-version.md`, published under `/guides/choose-dotnet-version/`).
