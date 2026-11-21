# .NET 10 or .NET 11

An application targets `net10.0` (LTS) or `net11.0` (STS). Both are supported by Microsoft until
2028-11-14, and the Pragmatic packages and generator are the same on both, with the same public API.

## Which one to write

1. **The project already has a target**: keep it. Read `TargetFramework` in the project files or in
   `Directory.Build.props`; never move a project from one release to the other unless asked.
2. **The user names one**: use it. `net10.0` is a full choice, not a fallback: the LTS release, with
   every Pragmatic feature except the C# 15 one below.
3. **Neither**: `net11.0`, when `dotnet --list-sdks` shows an 11.0 SDK. Without one, `net10.0`, and
   say why in one line: the project can move later by changing two lines.

## What changes between the two

| | `net10.0` | `net11.0` |
|---|---|---|
| `global.json` | `"version": "10.0.100"` | `"version": "11.0.100"` |
| `TargetFramework` | `net10.0` | `net11.0` |
| C# | 14 | 15 |
| EF Core provider | `10.0.*` | `10.0.*` as well: Pragmatic's EF Core packages are built on EF Core 10 |
| Generated files | `obj/Debug/net10.0/generated/` | `obj/Debug/net11.0/generated/` |

Nothing else: the same `PackageReference` lines, the same `Program.cs`, the same attributes. Every
package ships a `net10.0` and a `net11.0` build, and NuGet picks the one the project targets; do not
reference anything different for it.

Moving an existing project: change `TargetFramework` and the `global.json` version, then build. A
project that pins `LangVersion` keeps its language.

## What `net11.0` adds

- a C# 15 union as an operation's error type,
  `public partial union PlaceOrderError(NotFoundError, ConflictError) : IError;`, beside the
  arities `Result<T, E1, E2>` that stay valid on both targets (`pragmatic-use-foundation`);
- every package's `net11.0` build, compiled against .NET 11; the OpenAPI ones are built against
  `Microsoft.OpenApi` 3, so `Microsoft.AspNetCore.OpenApi` 11 works with `AddResultTypeSupport()` and
  `AddPragmaticOpenApi()`.

A project on an older Pragmatic release and `net11.0` may meet issues fixed since (the guide lists
them): move its `Pragmatic.*` packages to the version these skills name.

The full page, with the measurements and the issues: the guide *Choosing .NET 10 or .NET 11*
(`docs/howto/choose-dotnet-version.md`, published under `/guides/choose-dotnet-version/`).
