# Common Mistakes

These are the most common issues developers encounter when using Pragmatic.Result. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Throwing Exceptions Instead of Returning Errors

**Wrong:**

```csharp
public Result<User, NotFoundError> GetUser(int id)
{
    var user = _repository.FindById(id);
    if (user is null)
        throw new KeyNotFoundException($"User {id} not found");

    if (!user.IsActive)
        throw new InvalidOperationException("User is inactive");

    return user;
}
```

**Runtime result:** The exceptions bypass the Result pipeline entirely. Callers expecting `NotFoundError` get a raw 500 Internal Server Error instead. The typed error in the method signature is a lie.

**Right:**

```csharp
public Result<User, NotFoundError> GetUser(int id)
{
    var user = _repository.FindById(id);
    if (user is null)
        return NotFoundError.Create("User", id);

    return user;
}
```

**Why:** The entire point of the Result pattern is to make error paths explicit in the type system. Throwing exceptions instead of returning typed errors defeats this purpose. Exceptions should be reserved for truly unexpected failures (out of memory, database connection lost), not for business logic outcomes like "entity not found."

If you need to represent multiple failure modes, use a multi-error Result:

```csharp
public Result<User, NotFoundError, BusinessRuleError> GetUser(int id)
{
    var user = _repository.FindById(id);
    if (user is null)
        return NotFoundError.Create("User", id);

    if (!user.IsActive)
        return BusinessRuleError.Inactive("User");

    return user;
}
```

---

## 2. Accessing Value Without Checking IsSuccess

**Wrong:**

```csharp
var result = await userService.GetUserAsync(id);
Console.WriteLine(result.Value.Name);  // InvalidOperationException if failure!
```

**Runtime result:** If the operation failed, accessing `.Value` throws `InvalidOperationException: "Cannot access Value when result is failure."` The Roslyn analyzer PRAG0001 warns about this at build time.

**Right:**

```csharp
// Option A: Match (safest, exhaustive)
var name = result.Match(
    user => user.Name,
    error => "Unknown");

// Option B: TryGet pattern
if (result.TryGetValue(out var user))
    Console.WriteLine(user.Name);

// Option C: Guard check
if (result.IsSuccess)
    Console.WriteLine(result.Value.Name);

// Option D: Guard-return pattern
if (result.IsFailure)
    return BadRequest(result.Error);
var user = result.Value;  // Safe after early return
```

**Why:** `Result<T, E>` is a discriminated union. Only one of the two fields holds a meaningful value at any time. Accessing the wrong field is a programming error, similar to dereferencing null. The PRAG0001 analyzer catches this statically. Use `Match()` for exhaustive handling, or `TryGetValue()` for the TryParse pattern.

---

## 3. Using Result for Programming Errors (Use Ensure Instead)

**Wrong:**

```csharp
public Result<Order, BadRequestError> CreateOrder(OrderRequest? request)
{
    if (request is null)
        return BadRequestError.Create("Request cannot be null");

    if (request.Items is null)
        return BadRequestError.Create("Items cannot be null");

    // ...
}
```

**Design problem:** Null arguments are programming errors, not expected business outcomes. Returning a Result for null checks hides bugs that should fail fast with a clear exception and stack trace.

**Right:**

```csharp
using Pragmatic.Ensure;

public Result<Order, BusinessRuleError> CreateOrder(OrderRequest request)
{
    Ensure.ThrowIfNull(request);  // Fail fast for programming errors

    if (request.Items.Count == 0)
        return BusinessRuleError.Create("EmptyOrder", "Order must have at least one item");

    // ...
}
```

**Why:** There are two categories of failures:

| Category | Mechanism | Example |
|----------|-----------|---------|
| **Programming errors** | `Ensure.ThrowIfNull`, `ArgumentException` | Null argument, invalid state, contract violation |
| **Business outcomes** | `Result<T, E>` | Entity not found, insufficient funds, permission denied |

Programming errors should crash loudly so the developer fixes them. Business outcomes are expected and should be returned as typed errors. Do not use Result to make programming errors "recoverable" -- they are not.

---

## 4. Forgetting That Implicit Conversions Exist

**Wrong:**

```csharp
public Result<User, NotFoundError> GetUser(int id)
{
    var user = _repository.FindById(id);
    if (user is null)
        return Result<User, NotFoundError>.Failure(NotFoundError.Create("User", id));

    return Result<User, NotFoundError>.Success(user);
}
```

**Design problem:** This is correct but unnecessarily verbose. Every factory call repeats the full generic type.

**Right:**

```csharp
public Result<User, NotFoundError> GetUser(int id)
{
    var user = _repository.FindById(id);
    if (user is null)
        return NotFoundError.Create("User", id);  // Implicit: TError -> Failure

    return user;  // Implicit: TValue -> Success
}
```

**Why:** `Result<TValue, TError>` has implicit conversions from both `TValue` and `TError`. The compiler infers the correct factory call. Use the explicit factories (`Result<T,E>.Success()`, `Result<T,E>.Failure()`) only when the compiler cannot infer the type, such as in ternary expressions or when the value and error types overlap.

---

## 5. Empty or Generic Error Messages

**Wrong:**

```csharp
return NotFoundError.Create("Entity", null);
return BadRequestError.Create("Error occurred");
return BusinessRuleError.Create("Validation failed");
```

**Runtime result:** The ProblemDetails response contains no actionable information. The API consumer cannot tell what went wrong:

```json
{ "code": "NOT_FOUND", "entityType": "Entity" }
```

**Right:**

```csharp
return NotFoundError.Create("User", userId);  // Create<TId> accepts any id type
return BadRequestError.MissingHeader("X-Api-Key");
return BusinessRuleError.InsufficientFunds(requested: 100m, available: 50m);
```

**Why:** Error messages exist for two audiences: API consumers (who read the ProblemDetails response) and developers (who debug from logs). Both need specifics. Use the semantic factory methods on built-in errors -- they enforce meaningful context. For custom errors, always include the relevant domain data:

```csharp
public record OrderLimitError(int MaxItems, int RequestedItems) : IError
{
    public string Code => "ORDER_LIMIT_EXCEEDED";
    public int StatusCode => 422;
}
```

---

## 6. Mixing VoidResult and Result\<T\>

**Wrong:**

```csharp
public Result<bool, BadRequestError> ValidateInput(InputDto input)
{
    if (string.IsNullOrWhiteSpace(input.Name))
        return BadRequestError.InvalidParameter("Name", "Name is required");

    return true;  // What does "true" mean here?
}
```

**Design problem:** Using `Result<bool, E>` for an operation that returns no meaningful value. The `bool` is always `true` on success -- it carries no information.

**Right:**

```csharp
public VoidResult<BadRequestError> ValidateInput(InputDto input)
{
    if (string.IsNullOrWhiteSpace(input.Name))
        return BadRequestError.InvalidParameter("Name", "Name is required");

    return VoidResult<BadRequestError>.Success();
}
```

**Why:** `VoidResult<TError>` communicates intent: "this operation succeeds or fails, with no return value." Using `Result<bool, E>` or `Result<Unit, E>` adds a meaningless success value. The HTTP integration also handles `VoidResult` correctly: success maps to 204 No Content, not 200 OK with `true` in the body.

---

## 7. Not Implementing IError Correctly on Custom Errors

**Wrong:**

```csharp
public class PaymentFailedError
{
    public string Reason { get; set; } = "";
}
```

**Compile result:** This type cannot be used as a `TError` parameter. The constraint `where TError : IError` rejects it.

**Right:**

```csharp
// Minimal: implement IError directly
public record PaymentFailedError(string Reason) : IError
{
    public string Code => "PAYMENT_FAILED";
    public int StatusCode => 422;
}

// Rich: inherit from Error for localization + retry support
public sealed record PaymentFailedError : Error
{
    public override string Code => "PAYMENT_FAILED";
    public override int StatusCode => 422;
    public override string Title => "Payment Failed";
    public string? Reason { get; init; }
}
```

**Why:** All error types used with Result must implement `IError`, which requires two members: `Code` (semantic identifier for serialization and localization lookup) and `StatusCode` (HTTP mapping for the ASP.NET Core integration). `Title` and `Description` have default implementations and are optional. If you need localization, retry semantics, or ProblemDetails extensions, inherit from `Error`, which provides `MessageKey`, `Parameters`, `IsTransient`, `RetryAfter`, and `WriteExtensions`.

---

## 8. Catching Exceptions That Should Be Result Errors

**Wrong:**

```csharp
public async Task<Result<Order, NotFoundError>> GetOrderAsync(int id)
{
    try
    {
        var order = await _externalService.FetchOrderAsync(id);
        return order;
    }
    catch (HttpRequestException ex)
    {
        // Silently converts to NotFoundError, losing context
        return NotFoundError.Create("Order", id);
    }
}
```

**Runtime result:** An HTTP timeout or 503 from the external service is reported as "not found" -- a completely misleading error. The caller has no way to distinguish a genuine 404 from a service outage.

**Right:**

```csharp
public async Task<Result<Order, NotFoundError, DependencyError>> GetOrderAsync(int id)
{
    try
    {
        var order = await _externalService.FetchOrderAsync(id);
        if (order is null)
            return NotFoundError.Create("Order", id);
        return order;
    }
    catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
    {
        return NotFoundError.Create("Order", id);
    }
    catch (HttpRequestException)
    {
        return DependencyError.Unavailable("OrderService");
    }
    catch (TaskCanceledException)
    {
        return DependencyError.Timeout("OrderService");
    }
}
```

Or use the `Result.TryAsync` factory:

```csharp
var result = await Result.TryAsync(
    async ct => await _externalService.FetchOrderAsync(id, ct),
    ex => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.NotFound }
            => (IError)NotFoundError.Create("Order", id),
        TaskCanceledException
            => DependencyError.Timeout("OrderService"),
        _   => DependencyError.Unavailable("OrderService")
    },
    cancellationToken);
```

**Why:** Different exception types represent different failure modes. Collapsing them into a single error type loses information that the caller needs to make decisions (retry? show 404? show 503?). Use multi-error Result types to express the distinct failure modes, and catch exceptions precisely.

---

## 9. Using Result\<T\> Where Maybe\<T\> Is Correct

**Wrong:**

```csharp
public Result<CachedUser, NotFoundError> GetCachedUser(string key)
{
    if (_cache.TryGetValue(key, out CachedUser? user))
        return user;
    return NotFoundError.Create("CachedUser", key);
}
```

**Design problem:** A cache miss is not an error. It is a normal, expected outcome. Returning `NotFoundError` for a cache miss conflates "absence is normal" with "absence is an error." The caller might log warnings or return 404 for what is simply a cold cache.

**Right:**

```csharp
public Maybe<CachedUser> GetCachedUser(string key)
{
    if (_cache.TryGetValue(key, out CachedUser? user))
        return Maybe<CachedUser>.Some(user);
    return Maybe<CachedUser>.None();
}

// Caller
var user = GetCachedUser(key).GetValueOrDefault(LoadFromDatabase(key));
```

**Why:** `Maybe<T>` signals "this value might not exist, and that is fine." `Result<T, NotFoundError>` signals "this value should exist, and its absence is a meaningful error." Use the type that matches the semantic:

| Scenario | Type | Reason |
|----------|------|--------|
| Cache lookup | `Maybe<T>` | Miss is normal |
| Database entity by ID | `Result<T, NotFoundError>` | 404 is meaningful |
| Optional config | `Maybe<T>` | Default applies |
| User profile lookup | `Result<T, NotFoundError>` | Missing profile is a bug |

---

## 10. Choosing a Generic StatusCode on Custom Errors

**Wrong:**

```csharp
public record PaymentRequiredError(string Reason) : IError
{
    public string Code => "PAYMENT_REQUIRED";
    public int StatusCode => 500;  // Placeholder chosen without thought
}
```

**Runtime result:** The ASP.NET Core integration reads `StatusCode` from the error to build the HTTP response. The API consumer gets 500 Internal Server Error -- which signals a server bug and may trigger client-side retries -- for what is actually a payment-required condition. Monitoring and alerting also misclassify the failure.

**Right:**

```csharp
public record PaymentRequiredError(string Reason) : IError
{
    public string Code => "PAYMENT_REQUIRED";
    public int StatusCode => 402;
}
```

Or, when inheriting from `Error`:

```csharp
public sealed record PaymentRequiredError : Error
{
    public override string Code => "PAYMENT_REQUIRED";
    public override int StatusCode => 402;
    public override string Title => "Payment Required";
    public string? Reason { get; init; }
}
```

**Why:** `IError` makes `StatusCode` a required member precisely so the HTTP mapping is a deliberate choice, not an afterthought. The status code is part of your API contract: clients branch on it, proxies cache by it, and monitoring classifies by it. Built-in errors (`NotFoundError` 404, `ConflictError` 409, `BusinessRuleError` 422, etc.) already carry the correct codes -- prefer them when they fit. For custom errors, pick the status code that matches the error semantics.

---

## 11. Awaiting Each Step Separately in Async Pipelines

**Wrong:**

```csharp
var r1 = await GetUserAsync(id);
if (r1.IsFailure) return r1.Error;

var r2 = await EnrichUserAsync(r1.Value);
if (r2.IsFailure) return r2.Error;

var r3 = await ValidateAccessAsync(r2.Value);
if (r3.IsFailure) return r3.Error;

return r3.Value;
```

**Design problem:** This is correct but verbose. Each step repeats the same check-and-propagate pattern. With more steps, the noise-to-signal ratio worsens.

**Right:**

```csharp
var result = await GetUserAsync(id)
    .BindAsync(user => EnrichUserAsync(user))
    .BindAsync(user => ValidateAccessAsync(user));

return result;
```

**Why:** The async extension methods (`BindAsync`, `MapAsync`, `TapAsync`) implement the railway pattern: if any step fails, subsequent steps are skipped and the error propagates. This eliminates the repetitive guard-and-return boilerplate while preserving the same behavior. Each `BindAsync` call chains a `Result`-returning operation; each `MapAsync` transforms the success value.

---

## 12. Boxing Result Types in Hot Paths

**Wrong:**

```csharp
object resultObj = GetUser(id);  // Boxes the struct onto the heap
IResultBase resultBase = GetUser(id);  // Also boxes
```

**Runtime result:** `Result<T, E>` is a `readonly struct`. Casting it to `object` or an interface (`IResultBase`) boxes it onto the heap, negating the zero-allocation design.

**Right:**

```csharp
var result = GetUser(id);  // Stays on the stack

// If you need runtime type detection (e.g., middleware), use IResultBase
// but only at the boundary, not in hot loops
if (result is IResultBase baseResult && baseResult.IsFailure)
{
    // This path is for middleware, not business logic
}
```

**Why:** The entire performance model of Pragmatic.Result depends on struct semantics. In business logic, always work with the concrete `Result<T, E>` type. The `IResultBase` interface exists for ASP.NET Core filters and middleware that need to handle Result types generically at the HTTP boundary -- not for application code on hot paths.

---

## Quick Reference

| Mistake | Symptom |
|---------|---------|
| Throwing exceptions | 500 instead of typed HTTP status |
| Accessing Value without check | `InvalidOperationException` at runtime, PRAG0001 warning |
| Result for programming errors | Hides bugs, use `Ensure.ThrowIfNull` instead |
| Ignoring implicit conversions | Verbose code with unnecessary factory calls |
| Empty error messages | Unhelpful ProblemDetails for API consumers |
| `Result<bool, E>` for void ops | Meaningless success value; use `VoidResult<E>` |
| Missing IError implementation | Compile error on generic constraint |
| Catching exceptions incorrectly | Losing context, misleading error types |
| Result where Maybe fits | Treating normal absence as an error |
| Generic StatusCode on custom errors | Wrong HTTP status on the wire |
| Separate awaits in pipeline | Verbose; use `BindAsync`/`MapAsync` chain |
| Boxing Result structs | Heap allocation, negates zero-allocation design |
