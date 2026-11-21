# Choosing .NET 10 or .NET 11

Pragmatic supports both releases Microsoft supports, with the same public API on each. This page says
how to start a project on either, how to move one from .NET 10 to .NET 11, and what .NET 11 adds, with
the version of the packages each part ships in.

## The two releases

| | .NET 10 | .NET 11 |
|---|---|---|
| Released | 2025-11-11 | 2026-11-10 |
| Support | LTS | STS |
| End of support | 2028-11-14 | 2028-11-14 |

Both are supported until the same day ([support policy](https://dotnet.microsoft.com/platform/support/policy/dotnet-core)),
so staying on .NET 10 costs no support window: a reason to move is what .NET 11 adds.

## What is supported

- **The packages target `net10.0`**, and four also ship (not in 1.0.0-alpha.4) a `net11.0` build, which NuGet picks for an
  application on .NET 11: `Pragmatic.Configuration`, `Pragmatic.Documents.Templates`,
  `Pragmatic.Endpoints.OpenApi` and `Pragmatic.Result.AspNetCore`. Every other package is the `net10.0`
  build on either target. The `pragmatic-migrate` tool also runs on either runtime, so the schema
  snapshot of a `net11.0` host is regenerated on every build
  ([#925](https://github.com/pragmatic-design/Pragmatic.Design/issues/925)).
- **One public API on every target.** Package validation fails the build of a package whose targets
  differ ([#836](https://github.com/pragmatic-design/Pragmatic.Design/issues/836)), so moving an
  application from one target to the next, or the day `net10.0` is dropped, is not a breaking change
  of ours.
- **Measured in CI on every pull request:**
  - the repository's gate also runs on the .NET 11 SDK ([#830](https://github.com/pragmatic-design/Pragmatic.Design/issues/830));
  - the Invoicing reference application runs its suite on `net10.0` and on `net11.0`, at C# 15
    there, with EF Core 10 ([#923](https://github.com/pragmatic-design/Pragmatic.Design/issues/923)).

## Which to choose

- **A project that already targets one keeps it.** Nothing in Pragmatic asks you to move.
- **A new project:** either. On 1.0.0-alpha.4, `net10.0`, unless you need what .NET 11 adds and can
  live with the four issues below until the next release: they are fixed, and the fixes are not in
  1.0.0-alpha.4. From the release that carries them, the newest SDK you have installed.

The agent skills follow the same order: the project's target, then the one you ask for, then this
default.

## Starting a project

| | On .NET 10 | On .NET 11 |
|---|---|---|
| SDK | 10 or 11 | 11 |
| `TargetFramework` | `net10.0` | `net11.0` |
| Language | C# 14, the SDK's default | C# 15, the SDK's default |
| EF Core provider | `10.0.*` (for example `Npgsql.EntityFrameworkCore.PostgreSQL` `10.0.*`) | `10.0.*` as well |

Pragmatic's EF Core packages are built on EF Core 10, and EF Core 10 is what CI measures on both
targets. EF Core 11 under them was measured once, on the PostgreSQL provider's `11.0.0-rc.3`, with an
application's tests green ([#820](https://github.com/pragmatic-design/Pragmatic.Design/issues/820)); it
is not measured in CI.

Nothing else differs: the same packages, the same `Pragmatic.SourceGenerator`, the same host. The
generator writes for the language version and the APIs the project sees, so a `net10.0` project gets
the code it always got ([#834](https://github.com/pragmatic-design/Pragmatic.Design/issues/834)).

## Moving a project to .NET 11

Change `TargetFramework` to `net11.0` and build with the .NET 11 SDK. The language moves to C# 15 with
it, unless the project pins `LangVersion`. No Pragmatic API changes.

.NET 11 changes some runtime behaviour Pragmatic depends on. Each one found is absorbed in the packages
rather than passed on. These four are fixed, and the fixes are not in 1.0.0-alpha.4: on that release
they still apply.

- an application that references `Microsoft.AspNetCore.OpenApi` 11 (which brings `Microsoft.OpenApi` 3)
  gets a 500 from a runtime OpenAPI document with Pragmatic's transformers (`AddResultTypeSupport()`,
  `AddPragmaticOpenApi()`); the compile-time document `MapPragmaticOpenApi` serves is not affected
  ([#832](https://github.com/pragmatic-design/Pragmatic.Design/issues/832));
- a background service that fails once the host is running puts it in maintenance instead of letting it
  exit ([#839](https://github.com/pragmatic-design/Pragmatic.Design/issues/839));
- a `double` stored where the documents engine expects a `decimal` reads back differently
  ([#831](https://github.com/pragmatic-design/Pragmatic.Design/issues/831));
- a host built for `net11.0` that turns the schema snapshot on (`PragmaticSchemaSnapshot`, off by
  default) gets a build warning and no snapshot
  ([#925](https://github.com/pragmatic-design/Pragmatic.Design/issues/925)).

## What .NET 11 adds for an application

None of these is in 1.0.0-alpha.4: they ship in the release that follows it.

- **A C# 15 union as the error type** ([#837](https://github.com/pragmatic-design/Pragmatic.Design/issues/837),
  [#865](https://github.com/pragmatic-design/Pragmatic.Design/issues/865),
  [#866](https://github.com/pragmatic-design/Pragmatic.Design/issues/866)). One declaration for the set
  of errors an operation can return, beside the arities `Result<T, E1, E2>` that stay the contract on
  every target:

  ```csharp
  public partial union PlaceOrderError(NotFoundError, ConflictError, OrderLocked) : IError;
  ```

  The generator implements `IError` by delegating to the case the union holds, and an endpoint
  documents each case with its own status. See [Result's concepts](../../Pragmatic.Result/docs/concepts.md).
- **A `net11.0` build of four packages.** Built with Runtime Async, the configuration binder
  allocates 2,216 B per call instead of 2,720 B, and resolving a document template is 21–34% faster
  ([#850](https://github.com/pragmatic-design/Pragmatic.Design/issues/850)); the OpenAPI packages are
  built against `Microsoft.OpenApi` 3, which ASP.NET Core 11 brings
  ([#832](https://github.com/pragmatic-design/Pragmatic.Design/issues/832)).
- **The generated JSON code on System.Text.Json 11.** The typed `GetTypeInfo<T>()` where the project has
  it, and unions described by the generated context
  ([#834](https://github.com/pragmatic-design/Pragmatic.Design/issues/834),
  [#838](https://github.com/pragmatic-design/Pragmatic.Design/issues/838)).
- **The runtime itself.** On Pragmatic's own Result benchmarks the .NET 11 runtime ran 5–10% faster than
  .NET 10, in both run orders ([#847](https://github.com/pragmatic-design/Pragmatic.Design/issues/847)).
