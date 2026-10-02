# Pragmatic.Result

Zero-allocation `Result` types for .NET 10 with railway-oriented programming.

## The Problem

Most .NET code signals failure by throwing. That hides error paths from method signatures, forces
callers to guess what to catch, and allocates expensive stack traces for *expected* outcomes like
"entity not found". The compiler can't enforce handling — miss a `catch` and you ship a generic 500.

```csharp
// Without Pragmatic.Result: error paths are invisible, exceptions are expensive
public async Task<User> GetUserAsync(int id)
{
    var user = await _repository.FindByIdAsync(id);
    if (user is null) throw new NotFoundException($"User {id} not found");   // hidden, expensive
    if (!user.IsActive) throw new BusinessRuleException("User is inactive"); // hidden, expensive
    return user;
}
```

## The Solution

Make error paths explicit in the type system. Return `Result<TValue, TError>` instead of throwing —
the compiler ensures callers handle both outcomes. The wrapper itself is zero-allocation.

```csharp
public async Task<Result<User, NotFoundError>> GetUserAsync(int id)
{
    var user = await _repository.FindByIdAsync(id);
    if (user is null) return NotFoundError.Create("User", id);   // implicit conversion to failure
    return user;                                                  // implicit conversion to success
}

// Caller: exhaustive, compiler-enforced
var message = result.Match(
    user  => $"Found: {user.Name}",
    error => $"Not found: {error.EntityId}");
```

## Features

- **Zero-allocation core** — `Result<TValue, TError>`, `Result<TValue>`, `VoidResult`,
  `VoidResult<TError>`, and `Maybe<TValue>` are all `readonly struct`.
- **Multi-error Results** — `Result<TValue, TError1, …, TError8>` and `VoidResult<TError1, …, TError8>`
  variants for up to 8 typed errors per operation, shipped pre-generated in the package.
- **Railway-oriented composition** — `Map`, `Bind`, `Match`, `Tap`, `Ensure`, `Recover`, `OrElse`,
  each with async counterparts (`MapAsync`, `BindAsync`, `MatchAsync`, …) that chain on `Task<Result>`.
- **Collection operators** — `Combine` (fail-fast), `CollectAll` (accumulate into `AggregateError`),
  `Partition`/`GetSuccesses`/`GetFailures`, plus `IAsyncEnumerable` variants.
- **Exception boundary** — `Result.Try`/`Result.TryAsync` wrap throwing code; `Result.FromNullable`
  and `Maybe.ToResult` lift nullables into the Result world.
- **Rich error model** — `Error` record base with `Code`, `StatusCode`, `Title`, `Parameters`,
  `IsTransient`/`RetryAfter`, and `WriteExtensions` for structured ProblemDetails payloads.
- **8 built-in HTTP error types** — `BadRequestError` (400), `UnauthorizedError` (401),
  `ForbiddenError` (403), `NotFoundError` (404), `ConflictError` (409), `BusinessRuleError` (422),
  `InternalServerError` (500), `DependencyError` (502/503/504), each with factory methods.
- **ASP.NET Core integration** — return `Result` straight from Minimal API endpoints
  (`WithResultHandling()`) or Controllers (`ResultActionFilter`); failures become RFC 7807
  ProblemDetails with the right status code; OpenAPI transformers document Result schemas and
  error responses (`AddResultTypeSupport()`).
- **JSON round-trip** — typed, AOT-safe converters you register for the shapes you serialize
  (`ResultJsonConverter<TValue, TError>`, `VoidResultJsonConverter<TError>`, `MaybeJsonConverter<T>`); a
  multi-error `Result<T, E1, E2, …>` is declared with `[assembly: JsonResultContract<…>]` and the generator
  writes its converter. There is no open-generic factory. The concrete error type travels in the
  `$errorType` discriminator. Endpoints need none of this: they unwrap a `Result` into the value or a
  ProblemDetails.
- **EF Core integration** — `SaveChangesAsResultAsync` turns `DbUpdateException` into typed database
  errors; `FirstOrDefaultAsResultAsync`/`SingleOrDefaultAsResultAsync`/`FindAsResultAsync` return
  `Result` instead of null; provider packages parse SQL Server, PostgreSQL, MySQL, and SQLite
  exceptions into precise error types.
- **Localization-ready** — every error carries a stable `Code` and derived `MessageKey`;
  `IErrorMessageResolver` plugs in custom/localized ProblemDetails messages.
- **Analyzer** — `PRAG0001` warns when `.Value` is accessed without checking `IsSuccess` first.

## Packages

| Package | Description |
|---------|-------------|
| `Pragmatic.Result` | Core `Result`/`VoidResult`/`Maybe` types, error model, HTTP errors, JSON converters |
| `Pragmatic.Result.AspNetCore` | ProblemDetails, Minimal API & Controller filters, OpenAPI transformers |
| `Pragmatic.Result.EFCore` | `SaveChangesAsResultAsync`, query extensions, database error types |
| `Pragmatic.Result.EFCore.SqlServer` | SQL Server exception parser |
| `Pragmatic.Result.EFCore.PostgreSQL` | PostgreSQL exception parser |
| `Pragmatic.Result.EFCore.MySql` | MySQL exception parser |
| `Pragmatic.Result.EFCore.Sqlite` | SQLite exception parser |
| `Pragmatic.Result.Analyzers` | `PRAG0001` unsafe `.Value` access analyzer |

## Installation

```bash
dotnet add package Pragmatic.Result
dotnet add package Pragmatic.Result.AspNetCore   # optional: ASP.NET Core integration
dotnet add package Pragmatic.Result.EFCore       # optional: EF Core integration
```

## Quick Start

```csharp
using Pragmatic.Result;
using Pragmatic.Result.Http;

Result<User, NotFoundError> result = user;                    // success (implicit)
Result<User, NotFoundError> failed = NotFoundError.Create("User", userId);

var output = result.Match(
    user  => $"Found: {user.Name}",
    error => $"Error: {error.EntityType} not found");

if (result.TryGetValue(out var value))
    Console.WriteLine(value.Name);
```

In ASP.NET Core, return `Result` types straight from endpoints and let the integration map them to
HTTP — success to `200`, typed errors to the right status + an RFC 7807 ProblemDetails:

```csharp
builder.Services.AddPragmaticResult();

var api = app.MapGroup("").WithResultHandling();
api.MapGet("/users/{id}", (int id, UserService svc) => svc.GetByIdAsync(id));
//  Result<User, NotFoundError>  →  200 with JSON, or 404 ProblemDetails
```

Full walkthrough: [Getting Started](docs/getting-started.md).

## Status

**Stable** within 1.0.0-alpha — the core `Result` API, error types, and the ASP.NET Core and EF Core
integrations are settled. See the [roadmap](../docs/ROADMAP.md).

## Documentation

| Guide | What you'll learn |
|-------|-------------------|
| [Concepts](docs/concepts.md) | Railway-oriented model, when Result beats exceptions, decision guide |
| [Getting Started](docs/getting-started.md) | Your first `Result`, matching, ASP.NET Core wiring |
| [API Reference](docs/api-reference.md) | Every extension (`Map`/`Bind`/`Match`/`Tap`/`Ensure`), error types, ProblemDetails, JSON, OpenAPI |
| [Localization](docs/localization.md) | `IErrorMessageResolver`, localized error messages |
| [Migration](docs/migration.md) | Moving from exceptions / other Result libraries |
| [Common Mistakes](docs/common-mistakes.md) | The most frequent Result pitfalls |
| [Troubleshooting](docs/troubleshooting.md) | Problem/solution guide |

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](../README.md) ecosystem — see [Licensing](../docs/LICENSING.md).
Pragmatic.Result is **MIT-licensed**.
