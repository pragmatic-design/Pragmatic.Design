---
title: "Architecture and Core Concepts"
description: "This guide explains **why** Pragmatic.Ensure exists, how its three patterns work together, and how to choose the right one for each situation. Read this before "
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Ensure/docs/concepts.md
sidebar:
  order: 1
---
This guide explains **why** Pragmatic.Ensure exists, how its three patterns work together, and how to choose the right one for each situation. Read this before diving into the API reference.

---

## The Problem

Every .NET codebase needs to validate inputs. Without a consistent approach, guard clauses drift into a patchwork of styles that are hard to read, hard to test, and easy to get wrong.

### Scattered null checks with inconsistent styles

```csharp
public class OrderService
{
    private readonly IOrderRepository _repository;
    private readonly ILogger<OrderService> _logger;

    public OrderService(IOrderRepository repository, ILogger<OrderService> logger)
    {
        // Developer A: manual throw
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

        // Developer B: forgot the guard entirely
        _logger = logger;
    }

    public async Task<Order> GetOrderAsync(Guid id)
    {
        // Developer C: guard-style method but wrong exception
        if (id == Guid.Empty) throw new InvalidOperationException("Id cannot be empty");

        var order = await _repository.GetByIdAsync(id);

        // Developer D: exception for a business case (user not found)
        if (order is null) throw new KeyNotFoundException($"Order {id} not found");

        return order;
    }
}
```

Four developers, four patterns. The code has at least three problems:

1. **Inconsistent exception types.** `InvalidOperationException` for a parameter check instead of `ArgumentException`. A production log full of mixed exception types makes triage harder.
2. **Missing guard.** `_logger` is never validated -- a `NullReferenceException` will surface later, far from the root cause.
3. **Exception for business logic.** "Order not found" is an expected outcome, not a programming error. Throwing an exception hides the error path from the type system and always produces 500 in an HTTP context.

### No distinction between programming errors and expected failures

The standard library provides `ArgumentNullException`, `ArgumentException`, and `ArgumentOutOfRangeException`, but deciding when to throw versus when to return an error object is left entirely to convention. The result is codebases where exceptions are used for everything -- bugs and business logic alike -- making it impossible to distinguish "this should never happen" from "the user entered an invalid email."

### Manual parameter names are fragile

```csharp
// Rename 'repository' to 'repo' and forget to update the string:
_repository = repository ?? throw new ArgumentNullException(nameof(repository));
// After rename:
_repo = repo ?? throw new ArgumentNullException(nameof(repository)); // Wrong name!
```

With `nameof`, this specific case is safe. But many teams still pass literal strings, and `nameof` adds visual noise to every guard clause.

---

## The Solution

Pragmatic.Ensure provides a single static class with three distinct patterns, each designed for a specific category of validation:

| Pattern | When to use | Returns | On failure |
|---------|-------------|---------|------------|
| `Ensure.ThrowIf*` | Programming errors (bugs) | The validated value (or void) | Throws `ArgumentException` |
| `Ensure.Is*` | Conditional logic | `bool` | Returns `false` |
| `Check.*` | Business validation (expected failures) | `VoidResult<TError>` | Returns typed error |

The same "validate an order" scenario, rewritten:

```csharp
public class OrderService(IOrderRepository repository, ILogger<OrderService> logger)
{
    // Guards validate and assign in one statement
    private readonly IOrderRepository _repository = Ensure.ThrowIfNull(repository);
    private readonly ILogger<OrderService> _logger = Ensure.ThrowIfNull(logger);

    public async Task<Result<Order, NotFoundError>> GetOrderAsync(Guid id)
    {
        // Programming error: empty GUID should never reach here
        Ensure.ThrowIfEmpty(id);

        var order = await _repository.GetByIdAsync(id);

        // Business case: order not found is expected, return typed error
        if (order is null)
            return NotFoundError.For("Order", id);

        return order;
    }
}
```

Every guard uses the same API. Parameter names are captured automatically by `[CallerArgumentExpression]`. Programming errors throw exceptions. Business failures return typed errors through the Result pattern.

---

## How It Works

### Zero-overhead design

All methods are marked `[MethodImpl(MethodImplOptions.AggressiveInlining)]`. On the happy path (validation passes), the JIT inlines the check into a single branch instruction with no allocation. The guard is effectively free.

```csharp
// What you write:
Ensure.ThrowIfNull(repository);

// What the JIT produces (approximately):
if (repository == null) throw new ArgumentNullException("repository");
```

### Automatic parameter names

Every `ThrowIf*` method has a `[CallerArgumentExpression]` parameter that the compiler fills in automatically:

```csharp
public static T ThrowIfNull<T>(
    [NotNull] T? value,
    [CallerArgumentExpression(nameof(value))] string? paramName = null)
    where T : class
```

This means:

```csharp
Ensure.ThrowIfNull(repository);
// Exception message: "Value is null. (Parameter 'repository')"
// The compiler fills in "repository"; no magic strings needed.
```

### Null-state analysis integration

The `ThrowIf*` methods use `[NotNull]` and the `Is*` methods use `[NotNullWhen(true)]` to integrate with C#'s nullable reference type analysis:

```csharp
string? name = GetName();

// Before the check, the compiler treats 'name' as nullable.
if (Ensure.IsNotNull(name))
{
    // After the check, the compiler knows 'name' is not null.
    // No warning on name.Length.
    Console.WriteLine(name.Length);
}
```

```csharp
// ThrowIfNull uses [NotNull]: after the call, the compiler
// treats the value as non-null for the rest of the scope.
Ensure.ThrowIfNull(name);
Console.WriteLine(name.Length); // No nullable warning.
```

---

## ThrowIf Pattern: Guard Clauses for Programming Errors

`ThrowIf*` methods validate **preconditions** -- conditions that must be true for the code to function correctly. A failure means the **caller** has a bug.

### Return value semantics

`ThrowIfNull` returns the validated non-null value, enabling fluent assignment:

```csharp
public class OrderService(IOrderRepository repository, ILogger<OrderService> logger)
{
    // Validate and assign in one statement
    private readonly IOrderRepository _repository = Ensure.ThrowIfNull(repository);
    private readonly ILogger<OrderService> _logger = Ensure.ThrowIfNull(logger);
}
```

All other `ThrowIf*` methods return `void`. They are fire-and-forget guards:

```csharp
public void SetDiscount(decimal percentage)
{
    Ensure.ThrowIfOutOfRange(percentage, 0m, 100m); // void: just validates
    _discount = percentage;
}
```

### Exception types

Each method throws the most specific exception for the validation it performs:

| Method pattern | Exception |
|----------------|-----------|
| `ThrowIfNull*` | `ArgumentNullException` |
| `ThrowIfNegative*`, `ThrowIfZero`, `ThrowIfPositiveOrZero`, `ThrowIfOutOfRange`, `ThrowIfGreaterThan`, `ThrowIfLessThan` | `ArgumentOutOfRangeException` |
| `ThrowIfNotDefined` (enum) | `ArgumentOutOfRangeException` |
| `ThrowIfDefault`, `ThrowIfInPast`, `ThrowIfInFuture` | `ArgumentOutOfRangeException` |
| `ThrowIfCountGreaterThan`, `ThrowIfCountLessThan`, `ThrowIfCountOutOfRange` | `ArgumentOutOfRangeException` |
| All other `ThrowIf*` | `ArgumentException` |

### Null-safety contracts for strings

String methods fall into two categories:

**Methods that throw on null** -- use these when a value is required:

| Method | On null |
|--------|---------|
| `ThrowIfNullOrEmpty` | Throws `ArgumentException` |
| `ThrowIfNullOrWhiteSpace` | Throws `ArgumentException` |
| `ThrowIfLongerThan` | Throws `ArgumentNullException` (via internal `ThrowIfNull`) |
| `ThrowIfShorterThan` | Throws `ArgumentNullException` (via internal `ThrowIfNull`) |
| `ThrowIfLengthOutOfRange` | Throws `ArgumentNullException` (via internal `ThrowIfNull`) |
| `ThrowIfContains` / `ThrowIfStartsWith` / `ThrowIfEndsWith` | Throws `ArgumentNullException` (via internal `ThrowIfNull`) |

**Null-safe methods** -- use these for optional fields where null is valid but, if a value is present, it must satisfy the constraint:

| Method | On null |
|--------|---------|
| `ThrowIfNotEmail` | Returns without throwing |
| `ThrowIfNotUrl` | Returns without throwing |
| `ThrowIfNotPhone` | Returns without throwing |
| `ThrowIfNotCreditCard` | Returns without throwing |
| `ThrowIfNotMatch` | Returns without throwing |

**Rule of thumb:** Only the format validation methods (`ThrowIfNot*`) are null-safe. Presence checks
(`ThrowIfNullOr*`) and every explicit-length/content check (`ThrowIfLongerThan`, `ThrowIfShorterThan`,
`ThrowIfLengthOutOfRange`, `ThrowIfContains`/`StartsWith`/`EndsWith`) throw `ArgumentNullException` on null.
The `Is*` counterpart `IsLengthInRange` is null-safe (returns `true`), but the guard is not.

### Null-safety for collections

All `ThrowIf*` collection methods throw `ArgumentNullException` when the collection itself is null. A null collection is always considered a programming error.

### Generic numeric constraints

Numeric methods use `INumber<T>` (for sign checks) or `IComparable<T>` (for range checks), so they work with any numeric type:

```csharp
Ensure.ThrowIfNegative(42);          // int
Ensure.ThrowIfNegative(3.14);        // double
Ensure.ThrowIfNegative(99.99m);      // decimal

Ensure.ThrowIfOutOfRange(age, 0, 150);        // int
Ensure.ThrowIfOutOfRange(price, 0m, 10_000m); // decimal
```

### DateTimeOffset support

DateTime and DateTimeOffset variants exist for temporal checks. For `DateTime`, local values are converted to UTC before comparison; `DateTimeKind.Unspecified` is treated as UTC.

```csharp
Ensure.ThrowIfDefault(startDate);
Ensure.ThrowIfInPast(expirationDate);
Ensure.ThrowIfInFuture(birthDate);

// DateTimeOffset variants
Ensure.ThrowIfDefault(createdAt);    // DateTimeOffset
Ensure.ThrowIfInPast(validUntil);    // DateTimeOffset
```

---

## Is Pattern: Boolean Checks for Conditional Logic

`Is*` methods return `bool` and never throw. They are designed for situations where invalid data is **expected** and the code should branch on it.

### Null-state analysis

Methods that check for null use `[NotNullWhen(true)]`, so the compiler narrows the type after a successful check:

```csharp
string? email = GetEmailFromInput();

if (Ensure.IsEmail(email))
{
    // Compiler knows email is not null here
    SendVerification(email);
}
```

### Null-safety semantics

`Is*` methods follow two conventions:

**Methods that return `false` for null** -- these validate presence:

| Method | On null |
|--------|---------|
| `IsNotNull` | `false` |
| `IsNotNullOrEmpty` | `false` |
| `IsNotNullOrWhiteSpace` | `false` |
| `IsEmail` | `false` |
| `IsPhone` | `false` |
| `IsUrl` | `false` |
| `IsCreditCard` | `false` |
| `IsNotEmpty` (Guid) | Not applicable (Guid is a struct) |
| `IsNotNullOrEmpty` (collection) | `false` |

**Methods that return `true` for null** -- these are null-safe for optional values:

| Method | On null |
|--------|---------|
| `IsLengthInRange` | `true` |
| `IsMatch` | `true` |

### Combining checks

Use standard boolean operators to compose multiple checks:

```csharp
if (Ensure.IsNotNullOrEmpty(name) && Ensure.IsLengthInRange(name, 2, 50))
{
    ProcessName(name);
}

// Filter invalid items without throwing
var validItems = items.Where(i =>
    Ensure.IsNotNullOrWhiteSpace(i.Name) &&
    Ensure.IsPositive(i.Price));
```

---

## Check Pattern: Domain Validation with Typed Errors

The `Check.*` methods (from the `Pragmatic.Ensure.Result` package) bridge guard-style validation with the Result pattern. They return `VoidResult<TError>` -- a value that is either success or a typed error.

### Why a separate package?

`Check.*` depends on `Pragmatic.Result` (for `VoidResult<TError>` and `IError`). The core `Pragmatic.Ensure` package has zero dependencies. If you do not need the Result integration, you do not pay for it.

### Basic usage

```csharp
using Pragmatic.Ensure.Result;

// Single check
var result = Check.NotNull(user, NotFoundError.For("User", userId));

if (result.IsFailure)
    return result.Error;
```

### Composable chains with Then

Chain multiple checks using `.Then(() => …)`. The first failure short-circuits the chain. Every link
returns `VoidResult<TError>` with the **same** `TError`, so a chain uses one error type:

```csharp
var result = Check.NotNullOrWhiteSpace(dto.Name, ValidationError.For("Name", "validation.required"))
    .Then(() => Check.Email(dto.Email, ValidationError.For("Email", "validation.email")))
    .Then(() => Check.InRange(dto.Age, 18, 120, ValidationError.For("Age", "validation.range", ("min", 18), ("max", 120))));

if (result.IsFailure)
    return result.Error;
```

`ValidationError.For(propertyPath, messageKey, parameters)` carries a message **key**, resolved by the
localization pipeline, not a sentence.

### Lazy error construction

Every `Check.*` method has two overloads: one accepting the error directly, and one accepting a `Func<TError>` factory. The factory is only invoked when validation fails:

```csharp
// Eager: error is always constructed (even if check passes)
var result = Check.NotNull(user, NotFoundError.For("User", userId));

// Lazy: error is constructed only on failure
var result = Check.NotNull(user, () => NotFoundError.For("User", userId));
```

When to use each:

| Scenario | Use |
|----------|-----|
| Error is a simple struct or singleton | Eager -- no allocation benefit |
| Error construction involves string interpolation | Lazy -- avoids string formatting |
| Error requires database/API lookup | Lazy -- avoids unnecessary I/O |
| Error constructor is expensive | Lazy -- defers cost to failure path |
| Hot path with high success rate | Lazy -- zero alloc on success |

### Error type constraint

All `Check.*` methods constrain the error type to `where TError : IError`. This ensures that Check methods produce errors compatible with the `Result<T, TError>` pipeline used throughout the Pragmatic ecosystem.

---

## When to Use Ensure vs Result vs Pragmatic.Validation

Three modules can validate data. They serve different purposes:

### Decision Table

| Question | Answer | Use |
|----------|--------|-----|
| Is the caller violating a contract (bug)? | Yes | `Ensure.ThrowIf*` |
| Do you need a quick true/false for branching? | Yes | `Ensure.Is*` |
| Is this domain/business validation with typed errors? | Yes | `Check.*` |
| Is this input validation on an endpoint/action with attributes? | Yes | `Pragmatic.Validation` |

### Ensure.ThrowIf* vs Check.*

| Aspect | `ThrowIf*` | `Check.*` |
|--------|-----------|-----------|
| Failure meaning | Bug in the caller | Expected invalid data |
| Failure behavior | Throws exception | Returns `VoidResult<TError>` |
| Where to use | Constructor guards, public API boundaries | Service methods, domain logic |
| Error detail | Exception message (for developers) | Typed `IError` (for callers/users) |
| Composability | Not composable | `.Then(() => …)` chains |
| Package | `Pragmatic.Ensure` | `Pragmatic.Ensure.Result` |

### Check.* vs Pragmatic.Validation

| Aspect | `Check.*` | `Pragmatic.Validation` |
|--------|-----------|----------------------|
| Scope | Inline, ad-hoc checks | Declarative, attribute-driven |
| Integration | Manual -- call in your method | Automatic -- SG generates validators, pipeline runs before handler |
| Best for | Service-layer validation, complex conditional rules | DTO/input validation with `[Required]`, `[Email]`, `[MinLength]` |
| Error type | Any `IError` | `ValidationError` (standard) |

In practice, many applications use all three:

```csharp
// Endpoint layer: Pragmatic.Validation runs automatically on the DTO
// (attribute-driven, before HandleAsync)

// Service layer: Check.* for business rules
public VoidResult<ValidationError> ValidateTransfer(Account from, Account to, decimal amount)
{
    return Check.NotNull(from, ValidationError.For("From", "validation.required"))
        .Then(() => Check.NotNull(to, ValidationError.For("To", "validation.required")))
        .Then(() => Check.That(from.Id != to.Id, ValidationError.For("To", "transfer.same_account")));
}

// Infrastructure layer: Ensure.ThrowIf* for internal contracts
public class TransferService(IAccountRepository accounts)
{
    private readonly IAccountRepository _accounts = Ensure.ThrowIfNull(accounts);
}
```

---

## Collection Method Overloads

`ThrowIfEmpty`, `ThrowIfNullOrEmpty`, `IsNotNullOrEmpty`, and `Check.NotNullOrEmpty` each expose a
single `IEnumerable<T>` overload (plus a more-specific `T[]` overload). The `IEnumerable<T>` overload
uses `Enumerable.TryGetNonEnumeratedCount`, which returns an O(1) count for any `ICollection<T>`,
`IReadOnlyCollection<T>`, or array, and falls back to a single `MoveNext()` for lazy sequences:

| Argument | How emptiness is determined |
|----------|-----------------------------|
| `List<T>`, `HashSet<T>`, `Collection<T>`, `IReadOnlyCollection<T>`, `ICollection<T>` | O(1) `Count` via `TryGetNonEnumeratedCount` |
| `T[]` | O(1) `Length` (dedicated overload) |
| Lazy `IEnumerable<T>` (LINQ query) | Single `MoveNext()`, no full enumeration |

A single overload avoids the ambiguity that per-interface overloads cause: a concrete `List<T>` (which
implements both `ICollection<T>` and `IReadOnlyCollection<T>`) is never an ambiguous call.

---

## Format Validation Details

### Email

Uses `System.Net.Mail.MailAddress` for parsing. Rejects the `"Display Name <email>"` form -- only bare `local@domain.tld` is accepted. This is more robust than a regex but does not validate RFC 5322 fully (no IP literals, no quoted strings).

### Phone

Pre-compiled regex with 250ms ReDoS timeout. After format validation, verifies E.164-compatible digit count (7-15 digits). Accepts international formats: `+1 555 123 4567`, `(02) 1234-5678`, `+39.02.1234567`. Does not validate country codes or per-country number lengths.

### URL

Uses `Uri.TryCreate(value, UriKind.Absolute)`. Accepts `http://` and `https://` schemes. Set `requireHttps: true` to accept only `https://`. Rejects relative URIs and non-HTTP schemes (ftp, file, etc.).

### Credit Card

Luhn algorithm (ISO/IEC 7812). Strips spaces and hyphens before validation. Accepts 13-19 digit cards. Validates checksum only -- does not verify issuer or account existence.

### ReDoS Protection

All regex-based validations (`ThrowIfNotMatch`/`IsMatch`/`Check.Match` and the phone checks) use a
250ms match timeout. If a regex times out:
- `ThrowIf*` methods throw `ArgumentException`
- `Is*` methods return `false` (the never-throws contract holds even on timeout)
- `Check.*` methods return failure

Compiled patterns are cached (bounded to 64 entries) so repeated calls do not recompile.

---

## Performance Characteristics

| Scenario | Allocation | Cost |
|----------|-----------|------|
| `ThrowIfNull` -- happy path | Zero | One null check, inlined |
| `ThrowIfNull` -- failure | One `ArgumentNullException` | Exception construction + throw |
| `IsEmail` -- happy path | One `MailAddress` | MailAddress parsing |
| `IsEmail` -- failure | Zero (caught `FormatException`) | try/catch, no alloc on failure |
| `Check.NotNull` -- happy path | Zero | One null check + `VoidResult<T>.Success()` |
| `Check.NotNull` with lazy error -- happy path | Zero | Factory delegate is not invoked |
| `ThrowIfNotPhone` -- happy path | Zero | Compiled regex match |
| Numeric methods | Zero | Single comparison, inlined |

All `ThrowIf*` and `Is*` methods on the happy path are zero-allocation. The only allocation occurs when an exception is thrown or when an `Is*` method creates temporary objects for validation (e.g., `MailAddress` for email).

---

## Design Principles

1. **Exceptions for bugs, Results for business logic.** `ThrowIf*` signals a contract violation that should never happen in correct code. `Check.*` signals an expected failure that the caller should handle.

2. **No allocation on the happy path.** Guard clauses run on every call. They must be free when validation passes.

3. **Compiler-friendly.** `[NotNull]`, `[NotNullWhen]`, and `[CallerArgumentExpression]` integrate with the compiler's flow analysis and eliminate the need for magic strings.

4. **Self-documenting.** `Ensure.ThrowIfNegativeOrZero(amount)` reads as a specification. The method name is the documentation.

5. **Zero dependencies.** The core `Pragmatic.Ensure` package depends on nothing. It is a Layer 0 foundation library that every other module can reference.

---

## Two Packages

| Package | Contents | Dependencies |
|---------|----------|-------------|
| `Pragmatic.Ensure` | `Ensure.ThrowIf*`, `Ensure.Is*` | None |
| `Pragmatic.Ensure.Result` | `Check.*` | `Pragmatic.Result` |

Install only what you need. If you do not use the Result pattern, `Pragmatic.Ensure` alone covers all guard clause and boolean check needs.

---

## See Also

- [Getting Started](/modules/ensure/getting-started/) -- Install and write your first guard in 2 minutes
- [API Reference](/modules/ensure/api-reference/) -- Complete method documentation with signatures and examples
- [Best Practices](/modules/ensure/best-practices/) -- Rules and patterns for effective guard usage
- [Common Mistakes](/modules/ensure/common-mistakes/) -- Wrong/Right/Why for the most frequent errors
- [Troubleshooting](/modules/ensure/troubleshooting/) -- Problem/checklist format for debugging issues
