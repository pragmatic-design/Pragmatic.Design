---
title: "API Reference"
description: "Complete reference for all Pragmatic.Result types and methods."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Result/docs/api-reference.md
sidebar:
  order: 4
---
Complete reference for all Pragmatic.Result types and methods.

Namespaces:

| Namespace | Contents |
|-----------|----------|
| `Pragmatic.Result` | Core types (`Result`, `VoidResult`, `Maybe`, `Error`, `AggregateError`), factories |
| `Pragmatic.Result.Extensions` | Sync/async extension methods (`Tap`, `Combine`, `MapAsync`, ...) |
| `Pragmatic.Result.Http` | The 8 built-in HTTP error types |
| `Pragmatic.Result.Serialization` | JSON converters, `ErrorTypeRegistry`, `SerializedError` |
| `Pragmatic.Result.AspNetCore` | Filters, ProblemDetails, HTTP conversion extensions |
| `Pragmatic.Result.AspNetCore.OpenApi` | OpenAPI transformers and enrichers |
| `Pragmatic.Result.EntityFrameworkCore` | EF Core `SaveChanges`/query extensions, `Db*` errors (package `Pragmatic.Result.EFCore`) |

`IError` lives in the `Pragmatic.Abstractions` package under the `Pragmatic.Result` namespace.

## Core Types

### Result<TValue, TError>

A discriminated union representing either a success with a value or a failure with an error. Zero-allocation `readonly struct`; the wrapper itself never allocates.

```csharp
public readonly struct Result<TValue, TError> : IResultBase, IEquatable<Result<TValue, TError>>
    where TError : IError
```

`default(Result<TValue, TError>)` is not a valid value: a Result must be created via `Success` or `Failure`. On an uninitialized struct, `TryGetError` returns `false` and reading `Error` throws `InvalidOperationException`.

#### Factory Methods

| Method | Description |
|--------|-------------|
| `Success(TValue value)` | Creates a successful result. Throws `ArgumentNullException` on null value |
| `Failure(TError error)` | Creates a failed result. Throws `ArgumentNullException` on null error |

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsSuccess` | `bool` | True if the result is successful |
| `IsFailure` | `bool` | True if the result is a failure |
| `Value` | `TValue` | The success value (throws `InvalidOperationException` if failure) |
| `Error` | `TError` | The error (throws if success or uninitialized) |

#### Safe Access

| Method | Description |
|--------|-------------|
| `TryGetValue(out TValue value)` | Returns true and sets value if successful |
| `TryGetError(out TError error)` | Returns true only for a genuine failure with a non-null error |

Extension methods from `Pragmatic.Result.Extensions`:

| Method | Description |
|--------|-------------|
| `GetValueOrDefault(TValue defaultValue)` | Returns value if success, otherwise the default |
| `GetValueOrDefault(Func<TValue> defaultFactory)` | Returns value if success, or the lazily-computed default |
| `GetValueOrThrow()` | Returns value or throws `InvalidOperationException` including the error `Code` |

#### Pattern Matching

| Method | Description |
|--------|-------------|
| `Match<TResult>(Func<TValue, TResult> onSuccess, Func<TError, TResult> onFailure)` | Executes the matching function, returns its result |
| `Match(Action<TValue> onSuccess, Action<TError> onFailure)` | Executes the matching action |
| `MatchAsync<TResult>(Func<TValue, Task<TResult>> onSuccess, Func<TError, Task<TResult>> onFailure)` | Async matching returning `Task<TResult>` |
| `MatchAsync(Func<TValue, Task> onSuccess, Func<TError, Task> onFailure)` | Async matching returning `Task` |

#### Transformation (Railway-Oriented)

| Method | Description |
|--------|-------------|
| `Map<TNewValue>(Func<TValue, TNewValue> mapper)` | Transforms success value, preserves error |
| `Bind<TNewValue>(Func<TValue, Result<TNewValue, TError>> binder)` | Chains to another Result-returning function |
| `MapError<TNewError>(Func<TError, TNewError> mapper)` | Transforms error, preserves success (`TNewError : IError`) |

#### Validation (extension)

| Method | Description |
|--------|-------------|
| `Ensure(Func<TValue, bool> predicate, Func<TValue, TError> errorFactory)` | Converts success to failure when the predicate returns false |

#### Side Effects (extensions, don't change the result)

| Method | Description |
|--------|-------------|
| `Tap(Action<TValue> action)` | Executes action on success, returns same result |
| `OnSuccess(Action<TValue> action)` | Alias for `Tap` |
| `OnFailure(Action<TError> action)` | Executes action on failure, returns same result |

#### Error Recovery (extensions)

| Method | Description |
|--------|-------------|
| `OrElse(Func<TError, Result<TValue, TError>> fallback)` | Provides a fallback Result on failure |
| `Recover(Func<TError, TValue> fallback)` | Recovers with a value on failure |

#### Deconstruction

```csharp
var (isSuccess, value, error) = result;   // 3-way
var (isSuccess, value) = result;          // 2-way, success-focused
```

#### Implicit Conversions

```csharp
Result<User, NotFoundError> ok = user;          // TValue → Success
Result<User, NotFoundError> fail = notFound;    // TError → Failure
User u = ok;                                    // Result → TValue; THROWS on failure
```

The `Result → TValue` conversion throws `InvalidOperationException` on a failure (Nullable&lt;T&gt;.Value-style ergonomics). For non-throwing access use `Match`, `TryGetValue`, or `GetValueOrDefault`.

#### Equality and Formatting

Full value equality (`IEquatable<>`, `==`, `!=`). `ToString()` returns `"Success(value)"` / `"Failure(error)"`.

---

### Result<TValue>

Variant with an untyped error: the failure side is the `Error` base record.

```csharp
public readonly struct Result<TValue> : IResultBase, IEquatable<Result<TValue>>
```

Same shape as `Result<TValue, TError>` with these differences:

| Aspect | Behavior |
|--------|----------|
| `Error` property type | `Error` (base record) |
| `Success(TValue value)` | Accepts `null` by design (`TValue` may be a nullable type where null encodes "success, no value") |
| `Failure(Error error)` | Throws `ArgumentNullException` on null |
| Members | `Match` (func/action), `Map`, `Bind`, `TryGetValue`, `TryGetError`, `Deconstruct(out isSuccess, out value, out error)`, full equality |
| Implicit conversions | `TValue → Result` (null becomes `Success(null)`, no validation), `Error → Result`, `Result → TValue` (throws on failure) |

`Result<TValue>` has no `MapError`, no `MatchAsync` members, and no 2-way `Deconstruct`.

---

### VoidResult

Result of a void operation with no error payload.

```csharp
public readonly struct VoidResult
```

| Member | Description |
|--------|-------------|
| `IsSuccess` / `IsFailure` | State |
| `Success()` / `Failure()` | Factories (no error argument) |
| `Match<TResult>(Func<TResult>, Func<TResult>)` | Function matching |
| `Match(Action, Action)` | Action matching |
| `MatchAsync<TResult>(Func<Task<TResult>>, Func<Task<TResult>>)` | Async function matching |
| `MatchAsync(Func<Task>, Func<Task>)` | Async action matching |
| `Then(Func<VoidResult> next)` | Chains another void operation on success |
| `implicit operator bool` | `if (result)` is a success check |
| `explicit operator VoidResult(bool)` | `true` → Success, `false` → Failure |

---

### VoidResult<TError>

Result of a void operation that can fail with a typed error.

```csharp
public readonly struct VoidResult<TError> : IResultBase, IEquatable<VoidResult<TError>>
    where TError : IError
```

`default(VoidResult<TError>)` is not a valid value; the same uninitialized-detection as `Result<TValue, TError>` applies.

#### Factory Methods

| Method | Description |
|--------|-------------|
| `Success()` | Creates a successful void result |
| `Failure(TError error)` | Creates a failed void result. Throws on null |

#### Members

| Member | Description |
|--------|-------------|
| `IsSuccess` / `IsFailure` | State |
| `Error` | The error (throws if success or uninitialized) |
| `TryGetError(out TError error)` | Safe error access |
| `Match<TResult>(Func<TResult> onSuccess, Func<TError, TResult> onFailure)` | Function matching |
| `Match(Action onSuccess, Action<TError> onFailure)` | Action matching |
| `MatchAsync<TResult>(Func<Task<TResult>>, Func<TError, Task<TResult>>)` | Async function matching |
| `MatchAsync(Func<Task>, Func<TError, Task>)` | Async action matching |
| `Tap(Action action)` | Side effect on success |
| `OnSuccess(Action action)` | Alias for `Tap` |
| `OnFailure(Action<TError> action)` | Side effect on failure |
| `OrElse(Func<TError, VoidResult<TError>> fallback)` | Fallback on failure |
| `MapError<TNewError>(Func<TError, TNewError> mapper)` | Transforms the error type |
| `Map<T>(Func<T> valueFactory)` | Promotes a void success to `Result<T, TError>`; propagates the error |
| `Then(Func<VoidResult<TError>> next)` | Chains another void operation on success |
| `implicit operator VoidResult<TError>(TError error)` | Error → Failure |
| `implicit operator bool` | Success check |

---

### Maybe<T>

Represents an optional value (Some or None). Use when absence is normal, not exceptional; use `Result<T, NotFoundError>` when absence is a semantic error.

```csharp
public readonly struct Maybe<T> : IEquatable<Maybe<T>>
```

#### Factory Methods

| Method | Description |
|--------|-------------|
| `Some(T value)` | Creates a Maybe with a value. Throws `ArgumentNullException` on null |
| `None()` | Creates an empty Maybe |

#### Members

| Member | Description |
|--------|-------------|
| `HasValue` / `IsNone` | State |
| `Value` | The value (throws `InvalidOperationException` if None) |
| `TryGetValue(out T value)` | Safe value access |
| `GetValueOrDefault(T defaultValue)` | Returns value or the default |
| `GetValueOrDefault(Func<T> defaultFactory)` | Returns value or the lazily-computed default |
| `Match<TResult>(Func<T, TResult> onSome, Func<TResult> onNone)` | Function matching |
| `Match(Action<T> onSome, Action onNone)` | Action matching |
| `Map<TResult>(Func<T, TResult> mapper)` | Transform value if present |
| `Bind<TResult>(Func<T, Maybe<TResult>> binder)` | Chain to another Maybe |
| `implicit operator Maybe<T>(T? value)` | null → None, non-null → Some |
| `implicit operator bool` | `if (maybe)` is a Some check |

#### Maybe ↔ Result (extensions, `Pragmatic.Result.Extensions.MaybeExtensions`)

| Method | Description |
|--------|-------------|
| `maybe.ToResult<TError>(TError error)` | Some → Success, None → Failure with the error |
| `maybe.ToResult<TError>(Func<TError> errorFactory)` | Lazy error construction; factory called only on None |
| `result.ToMaybe()` | Success → Some, Failure → None (error discarded) |

---

## Static Factories: `Result`

The `Result` static class (`Result.Factories.cs`) bridges exception-throwing code and nullable values into the Result pattern.

### Try / TryAsync

```csharp
var read = Result.Try(
    () => File.ReadAllText(path),
    ex => InternalServerError.From(ex));

var fetch = await Result.TryAsync(
    async ct => await httpClient.GetStringAsync(url, ct),
    ex => DependencyError.Unavailable("remote-api"),
    cancellationToken);
```

| Method | Description |
|--------|-------------|
| `Try<TValue, TError>(Func<TValue> operation, Func<Exception, TError> errorMapper)` | Wraps a throwing operation into `Result<TValue, TError>` |
| `TryAsync<TValue, TError>(Func<CancellationToken, Task<TValue>> operation, Func<Exception, TError> errorMapper, CancellationToken ct = default)` | Async variant; a cancellation surfaces as `OperationCanceledException` passed to the mapper |
| `Try<TError>(Action operation, Func<Exception, TError> errorMapper)` | Void variant returning `VoidResult<TError>` |
| `TryAsync<TError>(Func<CancellationToken, Task> operation, Func<Exception, TError> errorMapper, CancellationToken ct = default)` | Async void variant |

### FromNullable

```csharp
var user = await repository.FindByIdAsync(id);          // User?
var result = Result.FromNullable(user, () => NotFoundError.Create("User", id));
```

| Method | Description |
|--------|-------------|
| `FromNullable<TValue, TError>(TValue? value, TError error)` where `TValue : class` | Non-null → Success, null → Failure |
| `FromNullable<TValue, TError>(TValue? value, Func<TError> errorFactory)` where `TValue : class` | Lazy error construction |
| `FromNullable<TValue, TError>(TValue? value, TError error)` where `TValue : struct` | `Nullable<T>` variant |
| `FromNullable<TValue, TError>(TValue? value, Func<TError> errorFactory)` where `TValue : struct` | `Nullable<T>` variant, lazy |

---

## Multi-Error Types

`Result<TValue, TError1..TError8>` and `VoidResult<TError1..TError8>` variants ship pre-generated inside the `Pragmatic.Result` package, for operations that can fail in multiple typed ways. Maximum 8 error types.

```csharp
Result<User, NotFoundError, ForbiddenError> GetUser(int id);
VoidResult<DbConflictError, DbConstraintError> saveResult;
```

Each variant provides:

| Member | Description |
|--------|-------------|
| `Success(TValue value)` | Success factory (value-bearing variants) / `Success()` (void variants) |
| `Failure(TError1 error)` ... `Failure(TErrorN error)` | One typed failure factory per error type |
| `Failure(IError error)` | Dispatches on the runtime error type |
| `Match<TResult>(onSuccess, onError1, ..., onErrorN)` | Exhaustive matching: one handler per error type |
| `Match<TResult>(onSuccess, Func<IError, TResult> onError)` | Generic error handler when per-type handling isn't needed |
| `Map<TNewValue>` / `Bind<TNewValue>` | Transformation preserving all error types |
| Implicit conversions | From `TValue` and from each error type |
| `IResultBase` | Runtime identification for the ASP.NET Core filters |

```csharp
return result.Match(
    user => Results.Ok(user),
    (NotFoundError nf) => Results.NotFound(),
    (ForbiddenError fb) => Results.Forbid());
```

---

## Async Extensions

`Pragmatic.Result.Extensions.ResultAsyncExtensions`. Every method accepts a `CancellationToken` (default `default`). Overloads exist both on `Task<Result<...>>` (continue a pipeline) and on the sync `Result<...>` (start a pipeline).

### Result<TValue, TError>

| Method | Overloads | Description |
|--------|-----------|-------------|
| `MapAsync` | `Task<Result>` + sync mapper; `Task<Result>` + async mapper; `Result` + async mapper | Transform success value |
| `BindAsync` | `Task<Result>` + sync binder; `Task<Result>` + async binder; `Result` + async binder | Chain to a Result-returning function |
| `MatchAsync` | `Task<Result>` + sync handlers | Pattern matching on an awaited result |
| `TapAsync` | `Result`/`Task<Result>` × `Func<TValue, Task>`/`Func<TValue, CancellationToken, Task>` (4) | Async side effect on success |
| `OnSuccessAsync` | Same 4 shapes | Alias for `TapAsync` |
| `OnFailureAsync` | `Result`/`Task<Result>` × `Func<TError, Task>`/`Func<TError, CancellationToken, Task>` (4) | Async side effect on failure |
| `EnsureAsync` | `Result` + async predicate; `Task<Result>` + async predicate | Async validation with sync `errorFactory` |
| `OrElseAsync` | `Result` + async fallback; `Task<Result>` + async fallback | Async fallback on failure |

### VoidResult<TError>

| Method | Overloads | Description |
|--------|-----------|-------------|
| `TapAsync` / `OnSuccessAsync` | `VoidResult` / `Task<VoidResult>` | Async side effect on success |
| `OnFailureAsync` | `VoidResult` / `Task<VoidResult>` | Async side effect on failure |
| `OrElseAsync` | `VoidResult` / `Task<VoidResult>` | Async fallback on failure |
| `MatchAsync<TResult>(Func<TResult> onSuccess, Func<TError, TResult> onFailure, CancellationToken ct = default)` | on `Task<VoidResult<TError>>` | Pattern matching on an awaited void result |
| `ThenAsync(Func<Task<VoidResult<TError>>> next, CancellationToken ct = default)` | on `Task<VoidResult<TError>>` | Chain another async void operation |

### IAsyncEnumerable<Result<T, TError>>

`Pragmatic.Result.Extensions.ResultAsyncEnumerableExtensions`:

| Method | Returns | Description |
|--------|---------|-------------|
| `CollectAsync(ct)` | `Task<Result<IReadOnlyList<T>, AggregateError>>` | All values, or an `AggregateError` with every failure |
| `FilterSuccessesAsync(ct)` | `IAsyncEnumerable<T>` | Streams success values, skips failures |
| `FilterFailuresAsync(ct)` | `IAsyncEnumerable<TError>` | Streams errors, skips successes |
| `PartitionAsync(ct)` | `Task<(IReadOnlyList<T> Successes, IReadOnlyList<TError> Failures)>` | Splits into both lists |

---

## Aggregation Extensions

`Pragmatic.Result.Extensions.ResultExtensions`.

### Combine (Fail-Fast)

Returns the first error encountered.

```csharp
var combined = ResultExtensions.Combine(r1, r2);        // Result<(T1, T2), TError>
var combined3 = ResultExtensions.Combine(r1, r2, r3);   // Result<(T1, T2, T3), TError>
// Tuple overloads up to 5 results.

var many = ResultExtensions.Combine(results);           // IEnumerable<Result<T, TError>>
                                                        // → Result<IReadOnlyList<T>, TError>
```

An empty sequence returns Success with an empty list.

### CollectAll (Error Accumulation)

Collects all errors instead of stopping at the first. Returns `Result<IReadOnlyList<T>, AggregateError>`.

```csharp
var collected = ResultExtensions.CollectAll(new[] { r1, r2, r3 });
// params ReadOnlySpan<Result<T, TError>> overload avoids the array allocation:
var collected2 = ResultExtensions.CollectAll(r1, r2, r3);

if (collected.TryGetError(out var aggregate))
    foreach (var error in aggregate.Errors)
        Console.WriteLine(error.Code);
```

An empty input returns Success with an empty list.

### Partition / GetSuccesses / GetFailures

On `IEnumerable<Result<T, TError>>`:

| Method | Returns | Description |
|--------|---------|-------------|
| `GetSuccesses()` | `IEnumerable<T>` | Lazily yields success values |
| `GetFailures()` | `IEnumerable<TError>` | Lazily yields errors |
| `Partition()` | `(IReadOnlyList<T> Successes, IReadOnlyList<TError> Failures)` | Splits into both lists |

---

## Error Model

### IError (Pragmatic.Abstractions, namespace `Pragmatic.Result`)

The only error interface. Every error implements it.

```csharp
public interface IError
{
    string Code { get; }                    // UPPER_SNAKE_CASE, e.g. "NOT_FOUND"
    int StatusCode { get; }                 // HTTP status code
    string Title => string.Empty;           // RFC 7807 title; non-null, empty = no specific title
    string? Description => null;            // Optional additional context
}
```

### Error (abstract record)

Base record for error types. Struct-based errors (e.g. `ValidationError`) implement `IError` directly instead.

```csharp
public abstract record Error : IError
```

| Member | Description |
|--------|-------------|
| `Code` (abstract) | Semantic error code, UPPER_SNAKE_CASE |
| `StatusCode` (abstract) | HTTP status code |
| `Title` (virtual) | Defaults to `string.Empty`; check emptiness, never null |
| `MessageKey` (virtual) | Localization key derived from `Code` (`error.not.found` from `NOT_FOUND`); cached per code |
| `Parameters` (virtual) | `IReadOnlyDictionary<string, object>?` for message interpolation |
| `IsTransient` (virtual) | `false` by default; `true` signals retry may succeed |
| `RetryAfter` (virtual) | Suggested retry delay; meaningful only when `IsTransient` |
| `WriteExtensions(IDictionary<string, object?> extensions)` (virtual) | Writes custom properties as ProblemDetails extensions; the source generator overrides it per error type, with zero reflection |

```csharp
public sealed record OutOfStockError : Error
{
    public override string Code => "OUT_OF_STOCK";
    public override int StatusCode => 422;
    public required string Sku { get; init; }
}
```

### AggregateError

Container for heterogeneous errors from multiple operations. Non-generic.

```csharp
public sealed record AggregateError : Error
```

| Member | Description |
|--------|-------------|
| `AggregateError(params IError[] errors)` / `AggregateError(IEnumerable<IError> errors)` | Constructors; reject empty input |
| `Code` | `"AGGREGATE_ERROR"` |
| `StatusCode` | The highest status code among the contained errors |
| `Title` | `"Multiple Errors (n)"` |
| `Errors` | `IReadOnlyList<IError>` |
| `Count` | Number of errors |
| `IsTransient` | True only if all contained `Error`-based errors are transient |
| `RetryAfter` | The maximum `RetryAfter` among the contained errors |

Static factories:

| Method | Description |
|--------|-------------|
| `FromErrors(IEnumerable<IError> errors)` | Wraps a pre-filtered error list |
| `FromFailures<TValue>(params Result<TValue, IError>[] results)` | Collects failures; throws `ArgumentException` if none failed |
| `From<T1, T2>(r1, r2)` ... `From<T1..T5>(...)` | Returns `AggregateError?`: null when every result succeeded |
| `FromMany<T>(params Result<T, IError>[] results)` | Same-type variadic variant, returns `AggregateError?` |

---

## HTTP Error Types

`Pragmatic.Result.Http`: eight built-in `Error` records with HTTP semantics. Each overrides `WriteExtensions` to expose its properties as ProblemDetails extensions.

### BadRequestError (400)

Properties: `Reason`, `Field`.

```csharp
BadRequestError.Create("Invalid date format");
BadRequestError.MalformedJson();
BadRequestError.MissingHeader("X-Api-Key");
BadRequestError.UnsupportedLocale("xx-XX");
BadRequestError.InvalidParameter("startDate", "Invalid value");
```

### UnauthorizedError (401)

Property: `Reason`.

```csharp
UnauthorizedError.Create("Custom reason");
UnauthorizedError.MissingToken();
UnauthorizedError.InvalidToken();
UnauthorizedError.ExpiredToken();
UnauthorizedError.InvalidCredentials();
```

### ForbiddenError (403)

Properties: `Resource`, `Action`, `RequiredPermissions` (always a list), `PermissionMatch` (`All` or `Any`).

```csharp
ForbiddenError.Create(resource: "admin/users", action: "delete");
ForbiddenError.MissingPermission("users.delete", resource: "admin/users");
ForbiddenError.MissingPermissions(["users.read", "users.admin"], PermissionMatch.Any);
ForbiddenError.ActionDenied("delete", resource: "admin/users");
```

On the wire a missing permission is the same body whichever layer refuses, the HTTP authorization
policy or the action pipeline:

```json
{
  "status": 403, "title": "Forbidden", "code": "FORBIDDEN",
  "detail": "The current user is missing the 'users.delete' permission.",
  "requiredPermissions": ["users.delete"],
  "permissionMatch": "all"
}
```

`requiredPermissions` is an array even for one permission; `permissionMatch` says whether all of them were
required or any one would have been enough. For localization the error's parameters are `permissions`
(the list joined by ", ") and `permissionMatch`: an `error.forbidden.detail` translation can read
`{permissions}`.

### NotFoundError (404)

Properties: `EntityType`, `EntityId`.

```csharp
NotFoundError.Create("User", "42");            // Create(string entityType, string? entityId = null)
NotFoundError.Create("User", userId);          // Create<TId>(string entityType, TId entityId)
NotFoundError.For("User", "42");               // For(string entityName, string? entityId = null)
NotFoundError.For("User", userId);             // For<TId>(string entityName, TId entityId)
NotFoundError.ForAll("User", missingIds);      // ForAll<TId>(string entityName, IEnumerable<TId> entityIds); every key, comma-separated
```

### ConflictError (409)

Properties: `EntityType`, `EntityId`, `Reason`, `Field`.

```csharp
ConflictError.AlreadyExists("User", email);
ConflictError.AlreadyExists("User", userId);           // typed id overload
ConflictError.ConcurrencyConflict("Order", orderId);
ConflictError.DuplicateKey("Email", email);
```

### BusinessRuleError (422)

Properties: `Rule`, `Details`. The rule name drives `MessageKey` (`error.business_rule.{rule}`, sanitized).

```csharp
BusinessRuleError.Create("OrderLimitExceeded", details: "Max 10 items");
BusinessRuleError.Create("OrderLimitExceeded",
    new Dictionary<string, object> { ["limit"] = 10 });
BusinessRuleError.InsufficientFunds(requested: 100m, available: 40m);
BusinessRuleError.LimitExceeded("items", limit: 10, requested: 12);
BusinessRuleError.Inactive("Account");
```

### InternalServerError (500)

Properties: `ExceptionType`, `Message`, `StackTrace`, `InnerExceptionType`, `InnerMessage`.

```csharp
InternalServerError.Create("Database connection failed");
InternalServerError.From(ex);                              // production: generic message only
InternalServerError.From(ex, includeDetails: true);        // development ONLY: full exception details
```

With `includeDetails: false` (the default) only the generic "An unexpected error occurred" message is exposed. Never pass `true` on a response that can reach an untrusted client.

### DependencyError (502/503/504)

Properties: `ServiceName`, `Reason`. `IsTransient` is `true`; `RetryAfter` is settable.

```csharp
DependencyError.Unavailable("payment-api", retryAfter: TimeSpan.FromSeconds(30));  // 503
DependencyError.Timeout("payment-api");                                            // 504
DependencyError.InvalidResponse("payment-api");                                    // 502
```

---

## ASP.NET Core Integration

Package `Pragmatic.Result.AspNetCore`.

### Service Registration

```csharp
// Default: IProblemDetailsFactory + null IErrorMessageResolver
builder.Services.AddPragmaticResult();

// With a custom message resolver (localization)
builder.Services.AddPragmaticResult<ResourceErrorMessageResolver>();
```

### Automatic Result Handling

```csharp
// Minimal APIs: per group or per endpoint
app.MapGroup("api").WithResultHandling();          // RouteGroupBuilder
app.MapGet("/users/{id}", GetUser).WithResultHandling();  // RouteHandlerBuilder

// Controllers: global filter
builder.Services.AddControllers(opt => opt.Filters.Add<ResultActionFilter>());
```

`ResultEndpointFilter` (`IEndpointFilter`) and `ResultActionFilter` (`IAsyncResultFilter`) detect `IResultBase` return values and convert them: success value → response body with the appropriate status code, failure → RFC 7807 ProblemDetails with the error's `StatusCode`.

Opt out per endpoint/action or per class with `[SkipResultHandling]`:

```csharp
app.MapGet("/raw", [SkipResultHandling] () => GetResult());
```

### Manual Conversion: Minimal APIs (`ResultHttpExtensions`)

On `Result<TValue, TError>` where `TError : IError`:

| Method | Description |
|--------|-------------|
| `ToHttpResult(IProblemDetailsFactory factory, int successStatusCode = 200)` | Localized ProblemDetails on failure |
| `ToHttpResult(int successStatusCode = 200)` | Static ProblemDetails, no localization |
| `ToCreatedResult(IProblemDetailsFactory factory, string? location = null)` / `ToCreatedResult(string? location = null)` | 201 Created |
| `ToNoContentResult(IProblemDetailsFactory factory)` / `ToNoContentResult()` | 204 No Content |

On `VoidResult<TError>`:

| Method | Description |
|--------|-------------|
| `ToHttpResult(IProblemDetailsFactory factory, int successStatusCode = 204)` | Localized |
| `ToHttpResult(int successStatusCode = 204)` | Static |

### Manual Conversion: Controllers (`ResultControllerExtensions`)

MVC extensions constrain `TError : Error` (not `IError`) and always take the factory:

| Method | Description |
|--------|-------------|
| `ToActionResult(IProblemDetailsFactory factory, int successStatusCode = 200)` | On `Result<TValue, TError>` |
| `ToCreatedActionResult(IProblemDetailsFactory factory, string? location = null)` | 201 Created |
| `ToCreatedAtActionResult(IProblemDetailsFactory factory, string actionName, object? routeValues = null)` | 201 with location from route values |
| `ToNoContentActionResult(IProblemDetailsFactory factory)` | 204 No Content |
| `ToActionResult(IProblemDetailsFactory factory, int successStatusCode = 204)` | On `VoidResult<TError>` |

### IProblemDetailsFactory

```csharp
public interface IProblemDetailsFactory
{
    ProblemDetails Create(IError error, string? instance = null);
}
```

The default implementation (registered by `AddPragmaticResult`) resolves title and detail through `IErrorMessageResolver` and copies the error's custom properties into ProblemDetails extensions via `WriteExtensions`. The static `ProblemDetailsFactory.Create(IError error, string? instance = null)` is the DI-free fallback used by the non-factory extension overloads.

### IErrorMessageResolver

```csharp
public interface IErrorMessageResolver
{
    string? Resolve(string code, object? context = null);          // ProblemDetails "detail"
    string? ResolveTitle(string code, object? context = null) => null;  // ProblemDetails "title"
}
```

Return `null` to fall back to default behavior. Example:

```csharp
public class ResourceErrorMessageResolver(IStringLocalizer localizer) : IErrorMessageResolver
{
    public string? Resolve(string code, object? context = null)
        => localizer[code]?.Value;
}
```

---

## JSON Serialization

`Pragmatic.Result.Serialization`.

### Registration

```csharp
// Minimal APIs
builder.Services.ConfigureHttpJsonOptions(opt =>
    opt.SerializerOptions.Converters.Add(new ResultJsonConverter<OrderDto, NotFoundError>()));

// Controllers
builder.Services.AddControllers()
    .AddJsonOptions(opt =>
        opt.JsonSerializerOptions.Converters.Add(new ResultJsonConverter<OrderDto, NotFoundError>()));
```

`ResultJsonConverter<TValue, TError>`, `VoidResultJsonConverter<TError>` and `MaybeJsonConverter<T>` are typed and AOT-safe; register the ones you serialize. A multi-error `Result<T, E1, E2, …>` has no fixed arity, so declare it with `[assembly: JsonResultContract<…>]` and the generator writes the converter.

```csharp
options.Converters.Add(new ResultJsonConverter<OrderDto, IError>());
options.Converters.Add(new VoidResultJsonConverter<IError>());
options.Converters.Add(new MaybeJsonConverter<OrderDto>());
```

### Wire Format

```json
// Success
{ "isSuccess": true, "value": { "id": 42, "name": "John" } }

// Failure: the error carries a "$errorType" discriminator
{ "isSuccess": false,
  "error": { "$errorType": "Pragmatic.Result.Http.NotFoundError",
             "code": "NOT_FOUND", "statusCode": 404,
             "entityType": "User", "entityId": "42" } }
```

`Maybe<T>` serializes as the bare value for Some and `null` for None.

### ErrorTypeRegistry

Maps the `$errorType` discriminator (the concrete type's `Type.FullName`) to a CLR type so errors deserialize polymorphically even when the declared error type is `IError` or the abstract `Error`.

| Member | Description |
|--------|-------------|
| `DiscriminatorProperty` | `"$errorType"` |
| `Register<TError>()` | Registers under `typeof(TError).FullName` |
| `Register<TError>(string discriminator)` | Explicit discriminator that survives type renames |
| `GetDiscriminator(Type errorType)` | Discriminator written to JSON |
| `TryResolve(string? discriminator, out Type? errorType)` | Resolves a registered type |
| `RegisterDefaults()` | Registers the built-in framework errors; idempotent, invoked by the converters' static constructors |
| `Clear()` | Test-only; hidden from IntelliSense |

### SerializedError

Concrete `Error` used as a graceful fallback when a discriminator is missing or unregistered on read. Preserves `Code`, `StatusCode`, `Title`, the original discriminator (`OriginalErrorType`), and every extension property (`Extensions`, `[JsonExtensionData]`): no wire information is lost. An error that round-trips through this carrier loses its CLR type identity; register the type in `ErrorTypeRegistry` to get the concrete type back.

---

## OpenAPI

`Pragmatic.Result.AspNetCore.OpenApi`.

```csharp
builder.Services.AddOpenApi(opt => opt.AddResultTypeSupport());
```

`AddResultTypeSupport()` registers:

| Transformer | Role |
|-------------|------|
| `ResultOpenApiTransformer` (document) | Adds the ProblemDetails schema and error responses to Result-returning operations |
| `ResultSchemaTransformer` (schema) | Correct schema for Result types (success/failure shape) |
| `ErrorSchemaEnricher` (schema) | Documents error extension properties: reads `ErrorSchemaRegistry` (AOT-safe), falls back to reflection for unregistered types |
| `CommonSchemaEnricher` (schema) | Format annotations for common types (Guid, DateTime, ...) |

### ErrorSchemaRegistry

AOT-safe metadata store the source generator populates via module initializers:

```csharp
public static class ErrorSchemaRegistry
{
    public static void Register<TError>(ErrorSchemaMetadata metadata) where TError : IError;
    public static bool TryGet(Type errorType, out ErrorSchemaMetadata? metadata);
    public static void Clear();   // test-only
}

public sealed record ErrorSchemaMetadata(
    string Code, int StatusCode, string Title,
    IReadOnlyList<ErrorPropertyDescriptor> ExtensionProperties);

public readonly record struct ErrorPropertyDescriptor(
    string CamelCaseName, string JsonSchemaType, string Description,
    bool IsNullable = false, IReadOnlyList<string>? EnumValues = null);
```

---

## Entity Framework Core

Package `Pragmatic.Result.EFCore`, namespace `Pragmatic.Result.EntityFrameworkCore`.

### SaveChanges as Result (`DbContextResultExtensions`)

| Method | Returns |
|--------|---------|
| `SaveChangesAsResultAsync(ct)` | `Task<VoidResult<DbConflictError, DbConstraintError>>` |
| `SaveChangesWithCountAsResultAsync(ct)` | `Task<Result<int, DbConflictError, DbConstraintError>>`: affected row count on success |
| `SaveChangesDetailedAsResultAsync(ct)` | `Task<VoidResult<DbConflictError, DbNullConstraintError, DbMaxLengthError, DbNumericOverflowError, DbConstraintError, DbTransientError>>`; uses `DbExceptionParserRegistry.Default` |
| `SaveChangesDetailedAsResultAsync(DbExceptionParserRegistry parserRegistry, ct)` | Same, with an explicit parser registry |

```csharp
var result = await context.SaveChangesDetailedAsResultAsync();
return result.Match(
    () => Results.NoContent(),
    conflict => Results.Conflict(conflict.Reason),
    nullError => Results.BadRequest($"Required field missing: {nullError.ColumnName}"),
    maxLength => Results.BadRequest($"Value too long: {maxLength.ColumnName}"),
    overflow => Results.BadRequest($"Numeric overflow: {overflow.ColumnName}"),
    constraint => Results.BadRequest(constraint.Details),
    transient => Results.StatusCode(503));
```

Unclassifiable non-transient exceptions are re-thrown, preserving the original stack.

### Query Extensions (`QueryableResultExtensions`)

On `IQueryable<T>` where `T : class`:

| Method | Description |
|--------|-------------|
| `FirstOrDefaultAsResultAsync(string entityName, ct)` | First element or `NotFoundError` |
| `FirstOrDefaultAsResultAsync(Expression<Func<T, bool>> predicate, string entityName, ct)` | Filtered variant |
| `SingleOrDefaultAsResultAsync(string entityName, ct)` | Single element or `NotFoundError`; throws `InvalidOperationException` on more than one |
| `SingleOrDefaultAsResultAsync(Expression<Func<T, bool>> predicate, string entityName, ct)` | Filtered variant |

On `DbSet<T>`:

| Method | Description |
|--------|-------------|
| `FindAsResultAsync<TKey>(TKey id, string entityName, ct)` | Primary-key lookup, `NotFoundError` with the id when missing |
| `FindAsResultAsync(string entityName, params object?[]? keyValues)` | Composite-key lookup |

```csharp
var result = await context.Users.FindAsResultAsync(id, "User");
```

### Database Error Types

All extend `Error`:

| Type | Code | Status | Properties / Factories |
|------|------|--------|------------------------|
| `DbConflictError` | `DB_CONFLICT` | 409 | `EntityType`, `EntityId`, `Reason`, `FieldName`; `ConcurrencyConflict(entityType, id?)`, `UniqueViolation(entityType, fieldName?)` |
| `DbConstraintError` | `DB_CONSTRAINT` | 400 | `ConstraintName`, `ConstraintType`, `TableName`, `Details`; `ForeignKeyViolation(tableName, constraintName?)`, `NotNullViolation(tableName, columnName?)`, `FromDetails(details?)` |
| `DbInUseError` | `ENTITY_IN_USE` | 409 | `EntityType`, `UsedBy` (both written as problem extensions). A delete refused by a restricting relation: produced by the unit of work in `Pragmatic.Persistence.EFCore`, which has the model to name both sides; the `SaveChanges*AsResultAsync` extensions here still answer such a violation with `DbConstraintError` |
| `DbNullConstraintError` | `DB_NULL_CONSTRAINT` | 400 | `TableName`, `ColumnName`; `Create(tableName, columnName?)` |
| `DbMaxLengthError` | `DB_MAX_LENGTH` | 400 | `TableName`, `ColumnName`, `MaxLength`; `Create(tableName, columnName?, maxLength?)` |
| `DbNumericOverflowError` | `DB_NUMERIC_OVERFLOW` | 400 | `TableName`, `ColumnName`, `Details`; `Create(tableName, columnName?, details?)` |
| `DbTransientError` | `DB_TRANSIENT` | 503 | `IsTransient = true`, `RetryAfter`, `ErrorType` (`DbTransientErrorType`), `Details`, `IsDefinitelyTransient`; `Deadlock()`, `Timeout(details?)`, `ConnectionFailure(details?)`, `TemporarilyUnavailable(details?)` |

### Provider-Aware Error Detection

The core package classifies exceptions with `HeuristicDbExceptionParser` (message/code heuristics covering SQL Server, PostgreSQL, MySQL, SQLite). Provider packages replace the heuristics with exact error-code parsing:

| Package | Registration | Parser |
|---------|--------------|--------|
| `Pragmatic.Result.EFCore.SqlServer` | `services.AddSqlServerResultErrorHandling()` | `SqlServerExceptionParser` (SqlException.Number) |
| `Pragmatic.Result.EFCore.PostgreSQL` | `services.AddPostgreSqlResultErrorHandling()` | `PostgreSqlExceptionParser` (SqlState) |
| `Pragmatic.Result.EFCore.MySql` | `services.AddMySqlResultErrorHandling()` | `MySqlExceptionParser` (error number, from MySqlConnector's or MySql.Data's exceptions) |
| `Pragmatic.Result.EFCore.Sqlite` | `services.AddSqliteResultErrorHandling()` | `SqliteExceptionParser` (extended result codes) |

Custom parsers implement `IDbExceptionParser`:

```csharp
public interface IDbExceptionParser
{
    string ProviderName { get; }
    bool CanParse(Exception exception);
    DbErrorInfo? Parse(Exception exception);
}
```

`DbErrorInfo` (`readonly record struct`) carries `ErrorType` (required, enum `DbErrorType`), `TableName`, `ColumnName`, `ConstraintName`, `MaxLength`, `Details`, `ErrorCode`, `SqlState`. Parsers register on `DbExceptionParserRegistry` (`Default` singleton, `Register(IDbExceptionParser)`, `Parse(Exception)`, `RegisteredProviders`).

---

## Interfaces

### IResultBase

Runtime identification of Result types, used by the ASP.NET Core filters to convert return values.

```csharp
public interface IResultBase
{
    bool IsSuccess { get; }
    bool IsFailure { get; }
    bool HasValueType { get; }      // true for value-bearing results, false for void results
    object? ValueAsObject { get; }  // success value, or null if failed/void
    IError? ErrorAsObject { get; }  // error, or null if successful
}
```

`HasValueType` distinguishes the result *shape* (value-bearing vs void), not whether `TValue` is a value type. Implemented by `Result<TValue>`, `Result<TValue, TError>`, `VoidResult<TError>`, and all multi-error variants. `VoidResult` (non-generic) and `Maybe<T>` do not implement it.

---

## Analyzer

Package `Pragmatic.Result.Analyzers`.

| ID | Severity | Description |
|----|----------|-------------|
| `PRAG0001` | Warning | `Unsafe Result.Value access`: accessing `.Value` without checking `IsSuccess`/`IsFailure` may throw `InvalidOperationException`. Guarded access (if/ternary/pattern matching/early exit), `Match()`, or `TryGetValue()` suppresses it |
