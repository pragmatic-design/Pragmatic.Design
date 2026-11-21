---
title: "Choosing .NET 10 or .NET 11"
description: "Pragmatic supports both releases Microsoft supports, with the same public API on each. This page says"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/docs/howto/choose-dotnet-version.md
sidebar:
  order: 2
---
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

- **Every package ships a `net10.0` and a `net11.0` build** since 1.0.0-alpha.6, and NuGet picks the
  one the application targets ([#931](https://github.com/pragmatic-design/Pragmatic.Design/issues/931)).
  In 1.0.0-alpha.5 four did (`Pragmatic.Configuration`, `Pragmatic.Documents.Templates`,
  `Pragmatic.Endpoints.OpenApi`, `Pragmatic.Result.AspNetCore`), and the others gave a `net11.0`
  application their `net10.0` build. The generators and analyzers run in the compiler and stay
  `netstandard2.0`. The `pragmatic-migrate` tool also runs on either runtime, so the schema
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
- **A new project:** either, and both are first-class. `net11.0` gets what .NET 11 adds (below);
  `net10.0` is the LTS release, supported until the same day, with every Pragmatic feature except the
  C# 15 union. Without a reason to pick, `net11.0`.

The agent skills follow the same order: the project's target, then the one you ask for, then
`net11.0` when an 11.0 SDK is installed (`net10.0` otherwise).

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
rather than passed on. These four are fixed in 1.0.0-alpha.5; a project on 1.0.0-alpha.4 and
`net11.0` still meets them, and moving its packages to 1.0.0-alpha.5 removes them:

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

All of them since 1.0.0-alpha.5.

- **A C# 15 union as the error type** ([#837](https://github.com/pragmatic-design/Pragmatic.Design/issues/837),
  [#865](https://github.com/pragmatic-design/Pragmatic.Design/issues/865),
  [#866](https://github.com/pragmatic-design/Pragmatic.Design/issues/866)). One declaration for the set
  of errors an operation can return, beside the arities `Result<T, E1, E2>` that stay the contract on
  every target:

  ```csharp
  public partial union PlaceOrderError(NotFoundError, ConflictError, OrderLocked) : IError;
  ```

  The generator implements `IError` by delegating to the case the union holds, and an endpoint
  documents each case with its own status. See [Result's concepts](/modules/result/concepts/).
- **A `net11.0` build of every package**, compiled against .NET 11 with Runtime Async (#931). Where it
  was measured it pays: the configuration binder allocates 2,216 B per call instead of 2,720 B, and
  resolving a document template is 21–34% faster
  ([#850](https://github.com/pragmatic-design/Pragmatic.Design/issues/850)); the OpenAPI packages are
  built against `Microsoft.OpenApi` 3, which ASP.NET Core 11 brings
  ([#832](https://github.com/pragmatic-design/Pragmatic.Design/issues/832)). The other packages were not
  benchmarked on their own.
- **The generated JSON code on System.Text.Json 11.** The typed `GetTypeInfo<T>()` where the project has
  it, and unions described by the generated context
  ([#834](https://github.com/pragmatic-design/Pragmatic.Design/issues/834),
  [#838](https://github.com/pragmatic-design/Pragmatic.Design/issues/838)).
- **The runtime itself.** On Pragmatic's own Result benchmarks the .NET 11 runtime ran 5–10% faster than
  .NET 10, in both run orders ([#847](https://github.com/pragmatic-design/Pragmatic.Design/issues/847)).
