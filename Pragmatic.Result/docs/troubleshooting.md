# Troubleshooting

Practical problem/solution guide for Pragmatic.Result. Each section covers a common issue, the likely causes, and the fix.

---

## InvalidOperationException: Cannot Access Value

Your code throws `InvalidOperationException` with the message "Cannot access Value when result is failure."

### Checklist

1. **Did you check `IsSuccess` before accessing `.Value`?** This is the most common cause. The Result is in a failure state, but you are reading the success value:

   ```csharp
   // Throws if result is failure
   var user = result.Value;

   // Safe: check first
   if (result.IsSuccess)
       var user = result.Value;
   ```

2. **Use `TryGetValue()` for the TryParse pattern:**

   ```csharp
   if (result.TryGetValue(out var user))
       Console.WriteLine(user.Name);
   ```

3. **Use `Match()` for exhaustive handling:**

   ```csharp
   var name = result.Match(
       user => user.Name,
       error => "Unknown");
   ```

4. **Check the PRAG0001 analyzer.** The Roslyn analyzer warns at build time when `.Value` is accessed without a guard. If PRAG0001 is not firing, verify that `Pragmatic.Result.Analyzers` is referenced.

---

## InvalidOperationException: Cannot Access Error

Your code throws `InvalidOperationException` with the message "Cannot access Error when result is success." or "Cannot access Error on an uninitialized Result."

### Checklist

1. **Did you check `IsFailure` before accessing `.Error`?** The Result is in a success state, but you are reading the error:

   ```csharp
   // Throws if result is success
   var error = result.Error;

   // Safe: check first
   if (result.IsFailure)
       Log(result.Error);
   ```

2. **Use `TryGetError()` for safe access:**

   ```csharp
   if (result.TryGetError(out var error))
       logger.LogWarning("Failed: {Code}", error.Code);
   ```

3. **"uninitialized Result" means `default(Result<T, E>)`.** A Result must be created via `Success(...)` or `Failure(...)`. A default/zero-initialized struct — an unassigned field, `default` argument, or `new Result<T, E>()` — is neither success nor failure: `IsSuccess` is false, `TryGetError()` returns false, and reading `.Error` throws. Find where the Result is constructed without a factory method.

---

## Ambiguous Implicit Conversion (CS0457)

The compiler reports `CS0457: Ambiguous user defined conversions` when assigning a value to a Result.

### Cause

**TValue and TError are the same type.** `Result<TValue, TError>` defines implicit conversions from both `TValue` and `TError`. When both type parameters resolve to the same type, both conversions apply and neither is better:

```csharp
// CS0457: the value is convertible to both TValue and TError
Result<NotFoundError, NotFoundError> result = new NotFoundError();
```

**Fix:** Use the explicit factory methods:

```csharp
var result = Result<NotFoundError, NotFoundError>.Success(errorAsValue);
var result = Result<NotFoundError, NotFoundError>.Failure(error);
```

Better: avoid instantiating a Result whose value type is also its error type — it defeats the discriminated-union semantics.

---

## Result Not Converting to ProblemDetails

Your API returns raw Result JSON (`{ "isSuccess": false, "error": {...} }`) instead of ProblemDetails.

Raw Result JSON in the response means the conversion filter never ran. ProblemDetails conversion is performed by `ResultEndpointFilter` (Minimal APIs) or `ResultActionFilter` (MVC).

### Checklist

1. **For Minimal APIs:** Did you add `WithResultHandling()` to the route group?

   ```csharp
   var api = app.MapGroup("").WithResultHandling();
   ```

2. **For Controllers:** Did you register `ResultActionFilter`?

   ```csharp
   builder.Services.AddControllers(options =>
   {
       options.Filters.Add<ResultActionFilter>();
   });
   ```

3. **Did you register `AddPragmaticResult()`?** It registers the `IProblemDetailsFactory` and `IErrorMessageResolver` the filters use. Without it the filters still emit ProblemDetails via a built-in fallback (status, title, and description only — no `code` extension, no error-specific fields, no localization):

   ```csharp
   builder.Services.AddPragmaticResult();
   ```

4. **Is `[SkipResultHandling]` applied?** This attribute explicitly opts out of automatic Result-to-HTTP conversion. Remove it if you want ProblemDetails.

5. **Are you using Pragmatic.Endpoints?** When using `[Endpoint]`, Result-to-HTTP conversion is handled by the SG-generated handler. You do not need `ResultActionFilter` or `WithResultHandling()` -- the SG generates the mapping code directly.

---

## Wrong HTTP Status Code Returned

The API returns an unexpected status code (e.g., 422 instead of 404).

The status code always comes from the error instance's `StatusCode` property — there is no separate mapping table to configure.

### Possible Causes

**Wrong error type returned.** Verify the error instance you are actually returning. If a shared helper returns `BusinessRuleError` (422) where you expected `NotFoundError` (404), the response reflects the actual error. A 422 usually means a `BusinessRuleError` reached the boundary.

**Custom error's `StatusCode` returns the wrong value.** For errors extending `Error`, `StatusCode` is an abstract override — check the override on your error type:

```csharp
public sealed record PaymentRequiredError : Error
{
    public override string Code => "PAYMENT_REQUIRED";
    public override int StatusCode => 402;
}
```

**Built-in error with correct status.** Verify the error type's status code:

| Error Type | Status Code |
|------------|-------------|
| `BadRequestError` | 400 |
| `UnauthorizedError` | 401 |
| `ForbiddenError` | 403 |
| `NotFoundError` | 404 |
| `ConflictError` | 409 |
| `BusinessRuleError` | 422 |
| `InternalServerError` | 500 |
| `DependencyError` | 502, 503, or 504 |

---

## Multi-Error Result Types Not Available

You reference `Result<T, E1, E2>` but the compiler does not recognize the type.

The multi-error variants (`Result<TValue, TError1, TError2>` up to 8 error types, and `VoidResult<TError1, TError2>` up to 8) are compiled into the `Pragmatic.Result` package itself — they arrive ready-made with the package. The files `Result3_Generated.g.cs` through `Result9_Generated.g.cs` exist only in the Pragmatic.Result package build, not in your project.

### Checklist

1. **Is the Pragmatic.Result package referenced and restored?** Verify the package reference in your `.csproj` and run `dotnet restore`.

2. **Is the namespace imported?** All variants live in the `Pragmatic.Result` namespace:

   ```csharp
   using Pragmatic.Result;
   ```

3. **Maximum 8 error types.** Variants go up to `Result<TValue, TError1, ..., TError8>`. If you need more than 8 error types, aggregate related errors into a single type or use `AggregateError`.

---

## Match Not Compiling (Wrong Number of Handlers)

The call to `Match()` does not compile because the number of handler lambdas does not match the number of type parameters.

### Fix

`Match()` requires exactly N+1 handlers: one for the success value, plus one per error type.

```csharp
// Result<T, E1, E2> requires 3 handlers:
result.Match(
    value => ...,   // Success handler
    error1 => ...,  // TError1 handler
    error2 => ...); // TError2 handler

// Result<T, E1> requires 2 handlers:
result.Match(
    value => ...,    // Success handler
    error => ...);   // TError handler

// VoidResult<E> requires 2 handlers, but success has no parameter:
result.Match(
    () => ...,       // Success (no value)
    error => ...);   // TError handler
```

Multi-error variants also offer a two-handler overload with a generic error handler for when you don't need to distinguish error types:

```csharp
// Result<T, E1, E2>: one handler for the value, one for any error
result.Match(
    value => ...,
    error => ...);   // receives the error as the common base
```

---

## JSON Serialization Not Working

Result types serialize as `{}` or throw `JsonException`.

### Checklist

1. **Register the typed converter for each closed result type you serialize** (namespace
   `Pragmatic.Result.Serialization`):

   ```csharp
   using Pragmatic.Result.Serialization;

   // For Minimal APIs
   builder.Services.ConfigureHttpJsonOptions(options =>
   {
       options.SerializerOptions.Converters.Add(new ResultJsonConverter<OrderDto, NotFoundError>());
       options.SerializerOptions.Converters.Add(new VoidResultJsonConverter<ConflictError>());
   });

   // For standalone serialization
   var options = new JsonSerializerOptions();
   options.Converters.Add(new ResultJsonConverter<OrderDto, NotFoundError>());
   ```

   There is no factory that discovers them for you: one would have to materialize converters with
   `MakeGenericType`, so it could never run under Native AOT, and the framework does not need one —
   endpoints unwrap a result into its value or a ProblemDetails, and the remote invoker sends its own
   envelope.

2. **For a multi-error result, declare it and let the generator write the converter.**
   `Result<T, E1, E2, …>` and `VoidResult<E1, E2, …>` have no fixed arity, so there is no typed
   converter to register by hand:

   ```csharp
   [assembly: JsonResultContract<Result<OrderDto, NotFoundError, ConflictError>>]

   // then, once per assembly
   json.AddPragmaticResultConverters();
   ```

   The generated converter switches over the declared error slots, so an error round-trips to its
   concrete type without anything having to be registered with `ErrorTypeRegistry` first.

   The untyped `Result<T>` and the multi-error variants have **no** fixed-arity typed converter to
   register. If you must serialize them under NativeAOT, project them to a two-arg
   `Result<T, IError>` (or `VoidResult<IError>`) first — a multi-error result carries a single active
   error (reachable via its `Error` / `IError` member), so widening to the common `IError` slot loses
   nothing on the wire — then register `ResultJsonConverter<T, IError>` for that shape. Automatic
   source-generated converters for these variants are tracked as future work; today they are
   reflection-only.

3. **Check the JSON structure for deserialization.** The expected wire format is:

   ```json
   // Success
   { "isSuccess": true, "value": { ... } }

   // Failure
   { "isSuccess": false, "error": { "code": "NOT_FOUND", ... } }
   ```

   If the `isSuccess` property is missing, deserialization will fail.

4. **Custom error types must be serializable.** Properties on your custom error must have public getters for `System.Text.Json` to include them.

---

## ProblemDetails Missing Error-Specific Fields

The ProblemDetails response has `type`, `title`, `status`, and `code` but is missing fields like `entityType`, `entityId`, or `reason`.

Error-specific fields are written by `Error.WriteExtensions()`, which the ProblemDetails factory calls for every error extending the `Error` base record.

### Checklist

1. **Use the factory methods on built-in errors.** Fields like `EntityType` and `EntityId` are populated by the factory methods:

   ```csharp
   // These populate ProblemDetails extensions
   NotFoundError.Create("User", userId);        // entityType + entityId
   ConflictError.AlreadyExists("User", email);  // entityType + entityId + reason
   BadRequestError.MissingHeader("X-Api-Key");  // reason + field
   ```

2. **For custom errors: declare the type `partial`.** The source generator emits a `WriteExtensions` override for every partial `Error`-derived type with custom properties — each property becomes a camelCase extension, zero reflection:

   ```csharp
   public sealed partial record OrderLimitError : Error
   {
       public override string Code => "ORDER_LIMIT_EXCEEDED";
       public override int StatusCode => 422;
       public int MaxItems { get; init; }
       public int RequestedItems { get; init; }
   }
   // Generated WriteExtensions writes "maxItems" and "requestedItems"
   ```

   Without `partial` (or without the generator), override `WriteExtensions` manually:

   ```csharp
   public override void WriteExtensions(IDictionary<string, object?> extensions)
   {
       extensions["maxItems"] = MaxItems;
       extensions["requestedItems"] = RequestedItems;
   }
   ```

3. **Errors implementing `IError` directly (not extending `Error`) produce no extensions.** `WriteExtensions` is defined on the `Error` base record, and the factory only calls it for `Error` instances. Extend `Error` to get extension fields, or register a custom `IProblemDetailsFactory` that handles your error type.

---

## Hiding a Sensitive Error Property from the Wire

By default **every** public custom property on an `Error`-derived type flows into ProblemDetails (via the generated `WriteExtensions`) and is documented in OpenAPI (via `ErrorSchemaEnricher`). To keep a property in the domain model but off the wire, annotate it with `[JsonIgnore]` (`System.Text.Json.Serialization`):

```csharp
using System.Text.Json.Serialization;

public sealed partial record PaymentDeclinedError : Error
{
    public override string Code => "PAYMENT_DECLINED";
    public override int StatusCode => 402;

    public string PublicReason { get; init; } = "";   // → problemDetails.Extensions["publicReason"]

    [JsonIgnore]
    public string ProcessorDebugToken { get; init; } = ""; // omitted from ProblemDetails AND OpenAPI
}
```

The source generator skips `[JsonIgnore]` properties when emitting `WriteExtensions`, and the OpenAPI schema enricher excludes them symmetrically — so the property never appears in either the response body or the documented schema.

---

## High Allocations from Result Operations

Profiling shows heap allocations where you expect zero-allocation behavior.

### Common Causes

1. **Boxing.** Casting `Result<T, E>` to `object` or `IResultBase` boxes the struct:

   ```csharp
   // Allocates: boxing
   object obj = result;
   IResultBase baseResult = result;

   // No allocation: use concrete type
   var result = GetUser(id);
   ```

2. **Lambda closures.** Lambdas that capture local variables allocate a closure object:

   ```csharp
   // Allocates: captures 'id'
   var id = GetId();
   result.Map(x => ProcessWith(x, id));

   // No allocation: static lambda or method group
   result.Map(static x => x.ToString());
   result.Map(Process);
   ```

3. **Lazy iterators in hot paths.** `GetSuccesses()` and `GetFailures()` are lazy iterators — each call allocates an iterator state machine. For hot paths, iterate manually:

   ```csharp
   // Iterator (allocates the state machine)
   var users = results.GetSuccesses().ToList();

   // Manual (no iterator allocation)
   var users = new List<User>(results.Count);
   foreach (var r in results)
   {
       if (r.TryGetValue(out var user))
           users.Add(user);
   }
   ```

4. **Reference-type errors.** If your error type is a `record class` (the default for records), the error itself allocates on the heap. For zero-allocation errors on failure paths, use `record struct`:

   ```csharp
   // Allocates: record class (default)
   public record OrderLimitError : Error { ... }

   // Zero-allocation: record struct
   public readonly record struct OrderLimitError : IError { ... }
   ```

   Note: the built-in errors (`NotFoundError`, `ConflictError`, etc.) are `record class` types because they inherit from the `Error` abstract record. This is acceptable because failure paths are not the hot path.

---

## Diagnostics Reference

| ID | Severity | Cause | Fix |
|----|----------|-------|-----|
| PRAG0001 | Warning | Unsafe `.Value` access without `IsSuccess`/`IsFailure` check | Add guard check, use `TryGetValue()`, or use `Match()` |

The PRAG0001 analyzer is included in the `Pragmatic.Result.Analyzers` package. It detects direct access to `.Value` without a preceding state check in the same scope. Safe patterns that suppress the warning:

```csharp
// Guard with if
if (result.IsSuccess)
    var value = result.Value;

// Guard with property pattern
if (result is { IsSuccess: true })
    var value = result.Value;

// Guard with ternary
var value = result.IsSuccess ? result.Value : default;

// Guard-return pattern
if (result.IsFailure) return;
var value = result.Value;

// Match (no .Value access needed)
var value = result.Match(v => v, e => default);
```

---

## FAQ

### Can I use Result with async methods?

Yes. Wrap the Result in a `Task`:

```csharp
public async Task<Result<User, NotFoundError>> GetUserAsync(int id)
{
    var user = await _repository.FindByIdAsync(id);
    if (user is null) return NotFoundError.Create("User", id);
    return user;
}
```

Async extension methods (`MapAsync`, `BindAsync`, `TapAsync`) work on `Task<Result<T, E>>` for pipeline composition.

### How do I handle AggregateError?

`AggregateError.Errors` gives you the list of individual errors:

```csharp
if (result.TryGetError(out var aggregate))
{
    foreach (var error in aggregate.Errors)
    {
        logger.LogWarning("Sub-error: {Code}", error.Code);
    }
}
```

### Is Result compatible with FluentValidation?

Yes. Map FluentValidation results into Pragmatic errors:

```csharp
var validation = await validator.ValidateAsync(request);
if (!validation.IsValid)
    return ValidationError.Single(validation.Errors.First().ErrorMessage);
```

For deeper integration, see [Pragmatic.Validation](../../Pragmatic.Validation/docs/concepts.md) which generates validators automatically.

### Can I use Result in library code without ASP.NET Core?

Yes. The core `Pragmatic.Result` package has no ASP.NET Core dependency. It targets `net10.0` and works in any .NET 10 project — class libraries, console apps, workers. The ASP.NET Core integration (`Pragmatic.Result.AspNetCore`) is a separate package.

### How do I test methods that return Result?

```csharp
// Assert success
var result = service.GetUser(42);
result.IsSuccess.Should().BeTrue();
result.Value.Id.Should().Be(42);

// Assert failure
var result = service.GetUser(999);
result.IsFailure.Should().BeTrue();
result.Error.Code.Should().Be("NOT_FOUND");

// For VoidResult
var result = service.ValidateInput(input);
result.IsSuccess.Should().BeTrue();

// Mock a Result-returning method
mock.Setup(s => s.GetUserAsync(42))
    .ReturnsAsync(Result<User, NotFoundError>.Success(testUser));

mock.Setup(s => s.GetUserAsync(999))
    .ReturnsAsync(NotFoundError.Create("User", 999));
```

---

## Getting Help

- **GitHub Issues**: [github.com/pragmatic-design/Pragmatic.Design/issues](https://github.com/pragmatic-design/Pragmatic.Design/issues)
- **Concepts Guide**: See [concepts.md](concepts.md) for architecture and design decisions.
- **Common Mistakes**: See [common-mistakes.md](common-mistakes.md) for patterns to avoid.
- **API Reference**: See [api-reference.md](api-reference.md) for complete method documentation.
- **Migration Guide**: See [migration.md](migration.md) for migrating from exceptions or other Result libraries.
