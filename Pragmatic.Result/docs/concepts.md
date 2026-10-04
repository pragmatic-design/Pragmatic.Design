# Architecture and Core Concepts

This guide explains **why** Pragmatic.Result exists, how its pieces fit together, and how to choose the right type for each situation. Read this before diving into the API reference or integration guides.

---

## The Problem

Most .NET applications signal failure by throwing exceptions. This creates three cascading problems that get worse as codebases grow.

### Exceptions hide control flow

```csharp
public async Task<User> GetUserAsync(int id)
{
    var user = await _repository.FindByIdAsync(id);
    if (user is null)
        throw new NotFoundException($"User {id} not found");

    if (!user.IsActive)
        throw new BusinessRuleException("User is inactive");

    return user;
}
```

Reading this method, you see it returns `User`. But it can also throw two different exceptions. Nothing in the signature tells you this. The caller has to read the implementation (or hope for documentation) to know what to catch.

### Exceptions are expensive for expected outcomes

"User not found" is not exceptional -- it is a normal, expected business outcome. Yet the CLR allocates a full `Exception` object, captures a stack trace, and unwinds the call stack. On a hot API endpoint serving thousands of requests, this overhead is measurable.

```csharp
// Every 404 allocates an Exception with a full stack trace
try
{
    var user = await _userService.GetUserAsync(id);
    return Ok(user);
}
catch (NotFoundException ex)
{
    return NotFound(new ProblemDetails { Detail = ex.Message });
}
catch (BusinessRuleException ex)
{
    return UnprocessableEntity(new ProblemDetails { Detail = ex.Message });
}
```

### Catch blocks are incomplete by default

Nothing forces the caller to handle all failure modes. Miss a catch block and the exception propagates to a global handler, producing a generic 500 instead of a meaningful error response. The compiler cannot help you because exceptions are not part of the type system.

```csharp
// Forgot to catch BusinessRuleException
// Result: 500 Internal Server Error instead of 422
try
{
    var user = await _userService.GetUserAsync(id);
    return Ok(user);
}
catch (NotFoundException ex)
{
    return NotFound(new ProblemDetails { Detail = ex.Message });
}
// BusinessRuleException? Unhandled. Boom: 500.
```

**The fundamental issue**: exceptions are invisible in signatures, expensive for expected outcomes, and the compiler cannot enforce exhaustive handling.

---

## The Solution

Pragmatic.Result makes error paths visible in the type system. A method that can fail returns a `Result<TValue, TError>` -- a discriminated union that is either a success with a value or a failure with a typed error. The compiler enforces that you handle both cases.

The same "get user" operation:

```csharp
public async Task<Result<User, NotFoundError>> GetUserAsync(int id)
{
    var user = await _repository.FindByIdAsync(id);
    if (user is null)
        return NotFoundError.Create("User", id);

    return user; // Implicit conversion to Result
}
```

The return type `Result<User, NotFoundError>` tells the caller everything:
- This method returns a `User` on success
- It can fail with a `NotFoundError`
- No exceptions to guess about

The caller is forced to handle both paths:

```csharp
var result = await _userService.GetUserAsync(id);

// Option 1: Pattern matching (exhaustive)
var response = result.Match(
    user => Ok(user),
    error => NotFound(error));

// Option 2: Check-then-access
if (result.IsSuccess)
    return Ok(result.Value);
return NotFound(result.Error);

// Option 3: TryGet pattern
if (result.TryGetValue(out var user))
    return Ok(user);
```

**What you get:**
- **Type-safe errors** -- errors are part of the method signature, not hidden in `throws` documentation
- **Exhaustive handling** -- `Match()` requires a handler for every error type, enforced at compile time
- **Zero allocation** -- all Result types are `readonly struct`; the wrapper itself never allocates
- **HTTP-ready** -- built-in error types map directly to HTTP status codes and RFC 7807 ProblemDetails
- **Railway-oriented programming** -- `Map`, `Bind`, `Tap`, `OrElse` for composing pipelines

---

## How It Works: The Discriminated Union

`Result<TValue, TError>` is a `readonly struct` with two private fields and a boolean discriminator. At any point in time, exactly one of the two fields holds a meaningful value.

```
Result<User, NotFoundError>
┌──────────────────────────────────┐
│ IsSuccess = true                 │
│ _value    = User { Id = 42 }    │  ← Only this is populated
│ _error    = default              │
└──────────────────────────────────┘

Result<User, NotFoundError>
┌──────────────────────────────────┐
│ IsSuccess = false                │
│ _value    = default              │
│ _error    = NotFoundError { }    │  ← Only this is populated
└──────────────────────────────────┘
```

### Construction

You never call a constructor directly. Results are created through factory methods or implicit conversions:

```csharp
// Factory methods (explicit)
var success = Result<User, NotFoundError>.Success(user);
var failure = Result<User, NotFoundError>.Failure(NotFoundError.Create("User", id));

// Implicit conversions (preferred -- less ceremony)
Result<User, NotFoundError> result = user;                        // Success
Result<User, NotFoundError> result = NotFoundError.Create("User", id);  // Failure
```

The implicit conversion from `TValue` creates a success. The implicit conversion from `TError` creates a failure. This is why you can write `return user;` or `return NotFoundError.Create("User", id);` directly in methods that return `Result<User, NotFoundError>`.

### Accessing the value

Three patterns, from safest to most direct:

```csharp
// 1. Match -- exhaustive, compiler-enforced
string message = result.Match(
    user => $"Found: {user.Name}",
    error => $"Not found: {error.EntityId}");

// 2. TryGet -- safe, no exceptions
if (result.TryGetValue(out var user))
    Console.WriteLine(user.Name);

// 3. Direct access -- throws if wrong state
if (result.IsSuccess)
{
    var user = result.Value;  // Safe after guard
}
```

Accessing `.Value` on a failure throws `InvalidOperationException`. Accessing `.Error` on a success throws `InvalidOperationException`. The analyzer PRAG0001 (package `Pragmatic.Result.Analyzers`) warns when you access `.Value` without a guard check.

### Deconstruction

Results support C# deconstruction for terse code:

```csharp
var (isSuccess, value, error) = result;
if (isSuccess)
    Console.WriteLine(value!.Name);

// Two-element deconstruction
var (ok, user) = result;
```

---

## The Type Family

Pragmatic.Result provides four core types, each designed for a specific use case.

### Result\<TValue, TError\>

The primary type. Returns a value on success or a single typed error on failure.

```csharp
Result<User, NotFoundError> GetUser(int id);
Result<Order, ConflictError> PlaceOrder(OrderRequest request);
```

**When to use:** The operation has one logical failure mode, or all failures map to the same error type.

### Result\<TValue, TError1, TError2, ...\>

Multi-error variants that ship with the package. Support 2 to 8 error types. Each error type gets its own `Match` parameter, enforcing exhaustive handling.

```csharp
Result<User, NotFoundError, ForbiddenError> GetUser(int id, ClaimsPrincipal user);

var response = result.Match(
    user => Ok(user),
    notFound => NotFound(notFound),      // Must handle NotFoundError
    forbidden => Forbid());              // Must handle ForbiddenError
```

These variants are `readonly struct` types with a `byte` discriminator index. They live in the `Pragmatic.Result` namespace and are available as soon as you reference the package -- no source generator setup needed in your project.

**When to use:** The operation can fail with different error types that have different HTTP status codes or different semantic meaning to the caller.

### VoidResult\<TError\>

For operations that return nothing on success but can fail with a typed error.

```csharp
VoidResult<BusinessRuleError> ValidateAge(int age)
{
    if (age < 0)
        return BusinessRuleError.Create("NegativeAge");  // Implicit conversion to Failure
    return VoidResult<BusinessRuleError>.Success();
}

// In HTTP context: success = 204 No Content, failure = error status code
```

`VoidResult<TError>` has no `.Value` property. It has `IsSuccess`, `IsFailure`, `.Error`, `TryGetError()`, and `Match()`.

**When to use:** DELETE operations, validation-only methods, fire-and-forget commands, any operation where success means "it worked, nothing to return."

### Maybe\<T\>

For optional values where absence is normal, not an error.

```csharp
Maybe<string> FindSetting(string key)
{
    return settings.TryGetValue(key, out var value)
        ? Maybe<string>.Some(value)
        : Maybe<string>.None();
}

var theme = FindSetting("theme").GetValueOrDefault("light");
```

**When to use:** Cache lookups, optional configuration, TryParse patterns, dictionary lookups -- any situation where "not found" is a normal outcome rather than an error condition.

### Result\<TValue\> (untyped error)

A convenience variant where the error type is the `Error` base class. Useful when you do not need typed error discrimination.

```csharp
Result<User> GetUser(int id);  // Error is Error base class
```

**When to use:** Prototyping, or when the caller does not need to distinguish between error types.

---

## Choosing the Right Type

| Question | Type |
|----------|------|
| Operation returns a value, one failure mode? | `Result<T, TError>` |
| Operation returns a value, multiple distinct failure modes? | `Result<T, TError1, TError2, ...>` |
| Operation returns nothing on success, can fail? | `VoidResult<TError>` |
| Value is optional, absence is normal? | `Maybe<T>` |
| Need aggregate errors from parallel operations? | `Result<IReadOnlyList<T>, AggregateError>` via `CollectAll` |

### Maybe vs Result

The distinction is semantic:

| Scenario | Use | Why |
|----------|-----|-----|
| Cache lookup | `Maybe<T>` | Cache miss is normal, not an error |
| Database entity by ID | `Result<T, NotFoundError>` | 404 is meaningful to the API caller |
| Optional config value | `Maybe<T>` | Missing config = use default |
| Required external service call | `Result<T, DependencyError>` | Failure needs context (service name, retry) |
| Dictionary TryGetValue | `Maybe<T>` | Key absence is expected |

**Rule of thumb:** If the caller needs to know *why* the value is absent, use `Result`. If absence is just "nothing there," use `Maybe`.

---

## The Error Type System

All errors implement `IError` (defined in `Pragmatic.Abstractions`, namespace `Pragmatic.Result`). Only `Code` and `StatusCode` are required -- `Title` and `Description` have default implementations:

```csharp
public interface IError
{
    string Code { get; }              // "NOT_FOUND", "VALIDATION_ERROR", etc.
    int StatusCode { get; }           // HTTP status code
    string Title => string.Empty;     // RFC 7807 title (default: empty)
    string? Description => null;      // Additional context (default: null)
}
```

The framework provides a richer base record `Error` that adds localization support, transient error detection, retry hints, and ProblemDetails extensions:

```csharp
public abstract record Error : IError
{
    public abstract string Code { get; }           // "NOT_FOUND"
    public abstract int StatusCode { get; }        // 404
    public virtual string Title => string.Empty;   // Never null; empty means "no specific title"
    public virtual string MessageKey { get; }      // Derived from Code: NOT_FOUND -> "error.not.found"
                                                   // (cached per Code; override for custom keys)
    public virtual bool IsTransient => false;      // true for retryable errors
    public virtual TimeSpan? RetryAfter { get; init; }  // Suggested retry delay
    public virtual IReadOnlyDictionary<string, object>? Parameters { get; init; }  // Localization parameters

    // Writes custom properties as ProblemDetails extensions.
    // The source generator overrides this per error type -- zero reflection.
    public virtual void WriteExtensions(IDictionary<string, object?> extensions) { }
}
```

### Choosing IError vs Error

| Need | Implement | Example |
|------|-----------|---------|
| Minimal error with just code + status | `IError` | `record InsufficientFundsError : IError` |
| HTTP status, localization, retry support | `Error` (inherit) | `record RateLimitError : Error` |
| Standard HTTP error (404, 409, etc.) | Use built-in types | `NotFoundError`, `ConflictError` |

### Built-in HTTP Error Types

All built-in errors inherit from `Error` and live in the `Pragmatic.Result.Http` namespace.

| Error Type | HTTP Status | Code | Typical Use |
|------------|-------------|------|-------------|
| `BadRequestError` | 400 | `BAD_REQUEST` | Malformed request, missing headers |
| `UnauthorizedError` | 401 | `UNAUTHORIZED` | Missing or invalid authentication |
| `ForbiddenError` | 403 | `FORBIDDEN` | Authenticated but lacking permission |
| `NotFoundError` | 404 | `NOT_FOUND` | Entity not found |
| `ConflictError` | 409 | `CONFLICT` | Duplicate, concurrency conflict |
| `BusinessRuleError` | 422 | `BUSINESS_RULE_VIOLATION` | Business rule prevents processing |
| `InternalServerError` | 500 | `INTERNAL_ERROR` | Unhandled exception |
| `DependencyError` | 502/503/504 | `DEPENDENCY_ERROR` | External service failure |

Each type provides semantic factory methods:

```csharp
// NotFoundError -- Create and For are equivalent; both accept any id type
NotFoundError.Create("User", userId);
NotFoundError.For("User", userId);

// ConflictError
ConflictError.AlreadyExists("User", email);
ConflictError.ConcurrencyConflict("Order", orderId);
ConflictError.DuplicateKey("Email", email);

// BusinessRuleError
BusinessRuleError.InsufficientFunds(requested: 100m, available: 50m);
BusinessRuleError.LimitExceeded("order_items", limit: 10, requested: 15);
BusinessRuleError.Inactive("Account");

// DependencyError
DependencyError.Unavailable("PaymentService", retryAfter: TimeSpan.FromSeconds(30));
DependencyError.Timeout("InventoryService");
DependencyError.InvalidResponse("OrderService");

// UnauthorizedError
UnauthorizedError.InvalidCredentials();
UnauthorizedError.ExpiredToken();
UnauthorizedError.MissingToken();

// ForbiddenError
ForbiddenError.MissingPermission("orders.write", resource: "orders/123");
ForbiddenError.ActionDenied("delete", resource: "invoices");

// BadRequestError
BadRequestError.MalformedJson();
BadRequestError.MissingHeader("X-Api-Key");

// InternalServerError
InternalServerError.From(exception, includeDetails: env.IsDevelopment());
```

### Custom Error Types

**Lightweight custom error (implement `IError` directly):**

```csharp
public record InsufficientFundsError(decimal Requested, decimal Available) : IError
{
    public string Code => "INSUFFICIENT_FUNDS";
    public int StatusCode => 422;
}
```

**Rich custom error (inherit from `Error`):**

```csharp
public sealed record RateLimitError : Error
{
    public override string Code => "RATE_LIMITED";
    public override int StatusCode => 429;
    public override string Title => "Too Many Requests";
    public override bool IsTransient => true;
    public override TimeSpan? RetryAfter { get; init; } = TimeSpan.FromSeconds(60);
}
```

### AggregateError

`AggregateError` collects multiple errors from parallel operations. It inherits from `Error` and contains a list of `IError` instances.

```csharp
// From heterogeneous results (the combinators take Result<T, IError>)
Result<User, IError> userResult = await GetUserAsync(id);
Result<Order, IError> orderResult = await GetOrderAsync(orderId);

var aggregate = AggregateError.From(userResult, orderResult);  // null if all succeeded
if (aggregate is not null)
    return aggregate; // Contains errors from both operations

// From a collection of results
var allOrNothing = ResultExtensions.CollectAll(results);
// All success -> Success(IReadOnlyList<T>)
// Any failure -> Failure(AggregateError with all failures)
```

`From<T1, T2>` through `From<T1..T5>` return `null` when every result succeeded. `FromMany<T>` does the same for any number of same-type results. `FromErrors` builds an aggregate from a pre-filtered error list.

The `StatusCode` of an `AggregateError` is the highest status code among its contained errors (5xx outranks 4xx). `IsTransient` is true only if all contained errors are transient.

---

## Implicit Conversions

Implicit conversions reduce ceremony. Understanding which conversions exist prevents confusion.

### Result\<TValue, TError\>

| From | To | Direction |
|------|----|-----------|
| `TValue` | `Result<TValue, TError>` | Value to success |
| `TError` | `Result<TValue, TError>` | Error to failure |
| `Result<TValue, TError>` | `TValue` | Success to value (throws on failure) |

```csharp
// Value -> Success (safe, always works)
Result<User, NotFoundError> result = user;

// Error -> Failure (safe, always works)
Result<User, NotFoundError> result = NotFoundError.Create("User", id);

// Result -> Value (UNSAFE: throws InvalidOperationException if failure)
User user = result;  // Only safe after IsSuccess check
```

### VoidResult\<TError\>

| From | To | Direction |
|------|----|-----------|
| `TError` | `VoidResult<TError>` | Error to failure |
| `VoidResult<TError>` | `bool` | Result to boolean |

```csharp
// Error -> Failure
VoidResult<BadRequestError> result = BadRequestError.Create("Invalid");

// VoidResult -> bool (true if success)
if (result) { /* success */ }
```

### Maybe\<T\>

| From | To | Direction |
|------|----|-----------|
| `T?` | `Maybe<T>` | Value to Some (null becomes None) |
| `Maybe<T>` | `bool` | Option to boolean |

```csharp
// Value -> Some (a null value converts to None)
Maybe<string> option = "hello";

// Maybe -> bool (true if has value)
if (option) { /* has value */ }
```

---

## Pattern Matching

`Match()` is the primary way to consume Result types. It enforces exhaustive handling -- the compiler requires a handler for every possible outcome.

### Single-error Result

```csharp
Result<User, NotFoundError> result = ...;

// Functional: returns a value
string message = result.Match(
    user => $"Found: {user.Name}",
    error => $"Not found: {error.EntityId}");

// Imperative: executes side effects
result.Match(
    user => Console.WriteLine(user.Name),
    error => logger.LogWarning("User {Id} not found", error.EntityId));
```

### Multi-error Result

Each error type gets its own handler:

```csharp
Result<Order, NotFoundError, ForbiddenError, ConflictError> result = ...;

var response = result.Match(
    order => Ok(order),
    notFound => NotFound(),
    forbidden => Forbid(),
    conflict => Conflict());
// Removing any handler is a compile error
```

### VoidResult

No success value parameter -- just a parameterless function:

```csharp
VoidResult<ValidationError> result = ...;

string message = result.Match(
    () => "Valid",
    error => $"Invalid: {error.Code}");
```

### Maybe

```csharp
Maybe<string> option = ...;

string display = option.Match(
    value => $"Got: {value}",
    () => "Nothing");
```

---

## Railway-Oriented Programming

`Map`, `Bind`, and their async variants let you compose operations without nested if-else checks. If any step fails, subsequent steps are skipped and the error propagates.

### Map -- transform the success value

```csharp
Result<string, NotFoundError> result = GetUsername(id);

// Map transforms the value if success, passes through the error if failure
Result<int, NotFoundError> length = result.Map(name => name.Length);
```

### Bind -- chain Result-returning operations

```csharp
Result<User, NotFoundError> user = GetUser(id);

// Bind chains another Result-returning operation
Result<Order, NotFoundError> order = user.Bind(u => GetLatestOrder(u.Id));
```

The difference: `Map` takes `Func<T, TNew>` (value in, value out). `Bind` takes `Func<T, Result<TNew, TError>>` (value in, Result out). Use `Bind` when the next step can also fail.

### MapError -- transform the error

```csharp
Result<User, NotFoundError> result = GetUser(id);

// Convert error type for a different context
Result<User, ApiError> mapped = result.MapError(e => new ApiError(e.Code));
```

### Async pipeline

Chain async operations in a single expression:

```csharp
var result = await userService.GetByIdAsync(userId)
    .MapAsync(user => enrichmentService.EnrichAsync(user))
    .BindAsync(user => authService.CheckAccessAsync(user))
    .TapAsync(user => auditService.LogAccessAsync(user))
    .OnFailureAsync(error => alertService.NotifyAsync(error));
```

Each method in the chain:

| Method | Purpose | Signature |
|--------|---------|-----------|
| `MapAsync` | Transform success value (async) | `Func<T, Task<TNew>>` |
| `BindAsync` | Chain another Result-returning async operation | `Func<T, Task<Result<TNew, TError>>>` |
| `TapAsync` | Side effect on success (logging, metrics) | `Func<T, Task>` |
| `OnFailureAsync` | Side effect on failure | `Func<TError, Task>` |
| `EnsureAsync` | Validate with async predicate, fail if false | `Func<T, Task<bool>>` + `Func<T, TError>` |
| `OrElseAsync` | Recover from failure with fallback | `Func<TError, Task<Result<T, TError>>>` |
| `MatchAsync` | Extract final value from async chain | Both handlers |

### Factory methods for bridging

Convert existing patterns into Results:

```csharp
// Nullable -> Result
User? user = await repository.FindByIdAsync(id);
var result = Result.FromNullable(user, NotFoundError.Create("User", id));

// Lazy error (factory only called on null)
var result = Result.FromNullable(user, () => NotFoundError.For("User", id));

// Exception-throwing code -> Result
var result = Result.Try(
    () => JsonSerializer.Deserialize<Order>(json)!,
    ex => InternalServerError.From(ex));

// Async variant (the operation receives a CancellationToken)
var result = await Result.TryAsync(
    async ct => (await httpClient.GetFromJsonAsync<Order>(url, ct))!,
    ex => DependencyError.Unavailable("OrderService"),
    cancellationToken);
```

### Collection operations

Work with collections of Results:

```csharp
Result<User, NotFoundError>[] results = await Task.WhenAll(
    ids.Select(id => userService.GetByIdAsync(id)));

// Extract successes only
IEnumerable<User> users = results.GetSuccesses();

// Extract failures only
IEnumerable<NotFoundError> errors = results.GetFailures();

// Split into both
var (users, errors) = results.Partition();

// All-or-nothing: all succeed or aggregate errors
var allOrNothing = ResultExtensions.CollectAll(results);
// Success -> Result<IReadOnlyList<User>, AggregateError> with all users
// Failure -> AggregateError containing all failures (never fails fast)
```

---

## Code Generation

Two generators are involved: one builds the package itself, one runs in your project.

### Multi-error Result variants (included in the package)

`Result<TValue, TError1, TError2>` through `Result<TValue, TError1, ..., TError8>` are `readonly struct` types compiled into the `Pragmatic.Result` assembly. They are produced by the standalone `Pragmatic.Result.SourceGenerator` when the package is built -- you reference the package, the types are there. Each variant has:

- A `byte _index` discriminator (0 = success, 1-N = error type index)
- Separate fields for each error type (no boxing)
- `Match()` with N+1 parameters (success + one per error type)
- `TryGetValue()` and `TryGetError1()` through `TryGetErrorN()`
- Implicit conversions from `TValue` and each `TError`
- `IResultBase` implementation for ASP.NET Core integration

The same pattern applies to `VoidResult<TError1, TError2>` through `VoidResult<TError1, ..., TError8>`.

### Generated in your project (unified Pragmatic.SourceGenerator)

In applications that use the unified `Pragmatic.SourceGenerator`, declaring an error type as `partial` triggers two outputs:

```csharp
public sealed partial record OutOfStockError : Error
{
    public override string Code => "OUT_OF_STOCK";
    public override int StatusCode => 422;

    public string? Sku { get; init; }
    public int? Requested { get; init; }
}
```

- **`WriteExtensions` override**: every `partial` record extending `Error` with custom properties gets a generated `WriteExtensions` that writes each property (`sku`, `requested`) into ProblemDetails extensions. Zero reflection at runtime. Localization itself is driven by `Code`/`MessageKey` and `IErrorMessageResolver` (see [Localization](localization.md)); no per-type key properties are generated.

Generated files appear under `obj/Debug/net10.0/generated/` in the IDE and are fully debuggable.

### Analyzer

The `Pragmatic.Result.Analyzers` package ships **PRAG0001** (warning): accessing `.Value` without an `IsSuccess`/`TryGetValue`/pattern guard.

---

## Ecosystem Integration

Pragmatic.Result is the error handling foundation for the entire Pragmatic ecosystem. Other modules consume and produce Result types.

### ASP.NET Core (Pragmatic.Result.AspNetCore)

Automatic conversion from Result types to HTTP responses:

- **Minimal APIs:** `WithResultHandling()` on route groups converts `Result<T, E>` to `200 OK` or `ProblemDetails` with the error's status code
- **Controllers:** `ResultActionFilter` does the same for controller actions returning Result types
- **VoidResult** maps to `204 No Content` on success
- **ProblemDetails** follow RFC 7807, with error-specific extensions (`entityType`, `entityId`, etc.)

### Entity Framework Core (Pragmatic.Result.EFCore)

Query extensions (namespace `Pragmatic.Result.EntityFrameworkCore`) that return Result types instead of null:

```csharp
var result = await dbContext.Users.FindAsResultAsync(id, "User");
// Returns Result<User, NotFoundError> instead of User?

var active = await dbContext.Users
    .Where(u => u.IsActive)
    .FirstOrDefaultAsResultAsync("User");
// Also: SingleOrDefaultAsResultAsync, SaveChangesAsResultAsync
```

Provider packages (`.SqlServer`, `.PostgreSQL`, `.MySql`, `.Sqlite`) translate database exceptions into typed errors using native error codes.

### Pragmatic.Endpoints

Endpoints declare error types in their base class generics. The SG generates HTTP status mapping and OpenAPI response schemas automatically:

```csharp
[Endpoint(HttpVerb.Get, "/users/{id}")]
public partial class GetUser : Endpoint<UserDto, NotFoundError>
{
    // SG generates: 200 OK (UserDto) + 404 Not Found (ProblemDetails)
}
```

### Pragmatic.Actions

`DomainAction<T>` and `Mutation<T>` use `Result<T, IError>` as their return type. The invoker pipeline handles error propagation through filters, validators, and processors.

### Pragmatic.Validation

`ValidationError` is a `readonly struct` implementing `IError` -- the standard error for input validation failures. The validation SG generates `Validate()` methods that return a `ValidationError` carrying success/failure state and the collected issues.

### Pragmatic.Persistence

Repository methods return nullable entities (`Task<TEntity?>`); bridge them into Results with `Result.FromNullable(entity, () => NotFoundError.For("User", id))` or query with the `Pragmatic.Result.EFCore` extensions directly. `SaveChangesAsResultAsync` converts `DbUpdateException` into typed database errors (`DbConflictError`, `DbConstraintError`, etc.).

---

## JSON Serialization

The package includes typed `System.Text.Json` converters for `Result<TValue, TError>`, `VoidResult<TError>`, and `Maybe<T>`. Register the ones you serialize; there is no factory that discovers them at run time, because discovery needs `MakeGenericType` and that never works under Native AOT.

### Registration

```csharp
// ASP.NET Core
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new ResultJsonConverter<OrderDto, NotFoundError>());
});
```

### Wire format

```json
// Result<T, E> -- success
{ "isSuccess": true, "value": { "id": 1, "name": "Alice" } }

// Result<T, E> -- failure (the "$errorType" discriminator enables polymorphic round-trip)
{ "isSuccess": false, "error": { "$errorType": "Pragmatic.Result.Http.NotFoundError", "code": "NOT_FOUND", "statusCode": 404, "entityType": "User", "entityId": "42" } }

// VoidResult<E> -- success
{ "isSuccess": true }

// VoidResult<E> -- failure
{ "isSuccess": false, "error": { "$errorType": "...", "code": "BUSINESS_RULE_VIOLATION" } }

// Maybe<T> -- Some serializes as the bare value
"cached-data"

// Maybe<T> -- None
null
```

Built-in error types round-trip out of the box. Register custom error types with `ErrorTypeRegistry.Register<MyError>()` so a `Result<T, IError>` failure deserializes back to the concrete type; unregistered discriminators fall back gracefully to `SerializedError` (code, status code, and title preserved).

Register the typed converters directly: `new ResultJsonConverter<OrderDto, IError>()`, `new VoidResultJsonConverter<IError>()`, `new MaybeJsonConverter<string>()`. For a multi-error result (`Result<T, E1, E2, …>`, which has no fixed-arity converter), declare it with `[assembly: JsonResultContract<Result<T, E1, E2>>]` and the generator emits one, registered in bulk through `json.AddPragmaticResultConverters()`.

---

## Performance

All Result types are `readonly struct` and designed for zero allocation on the hot path.

| Design decision | Impact |
|-----------------|--------|
| `readonly struct` for all types | No heap allocation for the Result wrapper |
| `[MethodImpl(AggressiveInlining)]` on hot methods | JIT inlines `IsSuccess`, `Value`, `Match`, `TryGetValue` |
| Private constructors + static factories | Prevents invalid states, enables optimization |
| `byte` index for multi-error variants | Single byte discriminator instead of per-error bool fields |
| `[DoesNotReturn]` on throw helpers | JIT eliminates dead code paths after guards |

### What allocates

- **The Result struct itself:** Never. It lives on the stack or inline in the containing object.
- **The payload (TValue, TError):** Only if the type is a reference type (`class`, `record class`). Using `readonly struct` or `record struct` for errors makes the entire operation allocation-free.
- **`Match` with lambdas:** May allocate a delegate if the lambda captures local variables. For zero-allocation `Match`, use `TryGetValue`/`TryGetError` or static lambdas.

---

## See Also

- [Getting Started](getting-started.md) -- Install and create your first Result in 5 minutes
- [API Reference](api-reference.md) -- Complete method documentation
- [Common Mistakes](common-mistakes.md) -- Pitfalls and how to avoid them
- [Troubleshooting](troubleshooting.md) -- Problem/solution guide with diagnostics reference
- [Migration Guide](migration.md) -- Migrate from exceptions or other libraries
- [Localization](localization.md) -- Localize error messages with IErrorMessageResolver
