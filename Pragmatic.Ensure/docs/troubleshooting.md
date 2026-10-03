# Troubleshooting

Practical problem/solution guide for Pragmatic.Ensure. Each section covers a common issue, the likely causes, and the fix.

---

## Guard Throws but Parameter Name Is Wrong

The exception message shows the wrong parameter name (e.g., `(Parameter 'value')` instead of the actual variable name).

### Checklist

1. **Are you passing a literal or intermediate variable?** `[CallerArgumentExpression]` captures the exact expression at the call site. If you extract the value into a local variable first, the local name is captured:

   ```csharp
   var val = GetEmail();
   Ensure.ThrowIfNullOrWhiteSpace(val);
   // Message: (Parameter 'val'), not the original source
   ```

   Pass the expression directly for the best parameter name:

   ```csharp
   Ensure.ThrowIfNullOrWhiteSpace(GetEmail());
   // Message: (Parameter 'GetEmail()')
   ```

   Or use a descriptive local:

   ```csharp
   var customerEmail = GetEmail();
   Ensure.ThrowIfNullOrWhiteSpace(customerEmail);
   // Message: (Parameter 'customerEmail')
   ```

2. **Are you passing an explicit `paramName`?** The `paramName` parameter is optional. If you pass it explicitly, it overrides the auto-captured expression:

   ```csharp
   Ensure.ThrowIfNull(value, "customName");
   // Message: (Parameter 'customName')
   ```

3. **Are you on an older .NET version?** `[CallerArgumentExpression]` requires C# 10 / .NET 6 or later at the compiler level. If your toolchain is older, the parameter defaults to `null` and the exception message will not include a parameter name.

---

## Null Value Passes Through Format Validation

You call `ThrowIfNotEmail(value)` with a null value, but no exception is thrown.

### Cause

Format validation methods are **null-safe by design**. They return without throwing when the value is null. This is intentional for optional fields.

### Null-safe methods (return silently on null)

- `ThrowIfNotEmail`
- `ThrowIfNotUrl`
- `ThrowIfNotPhone`
- `ThrowIfNotCreditCard`
- `ThrowIfNotMatch`

> `ThrowIfLengthOutOfRange` (and `ThrowIfLongerThan`/`ThrowIfShorterThan`/`ThrowIfContains`/
> `ThrowIfStartsWith`/`ThrowIfEndsWith`) are **not** null-safe: they throw `ArgumentNullException`
> on null. Only the `ThrowIfNot*` format guards silently pass a null.

### Fix

If the field is required, validate presence first:

```csharp
Ensure.ThrowIfNullOrWhiteSpace(email);  // Throws on null
Ensure.ThrowIfNotEmail(email);          // Now email is guaranteed non-null
```

If the field is optional, the null-safe behavior is correct -- null means "no value provided," which is valid.

---

## Check.* Method Returns Success When Value Is Null

`Check.Match(nullValue, pattern, error)` returns success instead of the expected error.

### Cause

The checks split in two on `null`:

- `Check.Email`, `Check.Url` and `Check.Phone` **reject** `null`: they delegate to `Ensure.IsEmail`/`IsUrl`/`IsPhone`, which return `false` for it.
- `Check.Match`, `Check.LengthInRange`, `Check.DoesNotContain`, `Check.DoesNotStartWith` and `Check.DoesNotEndWith` **pass** `null` on purpose: they test the shape of a value that is there, and leave "is it required?" to a separate check.

### Fix

If the value is required, check that first:

```csharp
var result = Check.NotNull(value, ValidationError.For("ZipCode", "validation.required"))
    .Then(() => Check.Match(value, @"^\d{5}$", ValidationError.For("ZipCode", "validation.zipcode")));
```

---

## Collection Guard Throws ArgumentNullException Instead of ArgumentException

You pass an empty collection to `ThrowIfNullOrEmpty`, expecting `ArgumentException`, but get `ArgumentNullException`.

### Cause

The collection is actually `null`, not empty. All `ThrowIf*` collection methods check null **first** and throw `ArgumentNullException` before checking emptiness. An empty collection produces `ArgumentException`.

### Fix

If you need to distinguish between null and empty in error handling, check them separately:

```csharp
Ensure.ThrowIfNull(items);         // ArgumentNullException if null
// At this point, items is guaranteed non-null
if (items.Count == 0)
{
    // Handle empty case differently if needed
}
```

In most cases, `ThrowIfNullOrEmpty` is the right single call -- null and empty are both invalid for a required collection.

---

## IEnumerable Collection Check Enumerates the Sequence

After calling `Ensure.ThrowIfNullOrEmpty(query)` where `query` is an `IEnumerable<T>` from a LINQ chain, the sequence behaves unexpectedly (e.g., database query runs twice).

### Cause

The `IEnumerable<T>` overload of `ThrowIfNullOrEmpty` calls `.Any()`, which enumerates the first element. If the enumerable is a deferred LINQ query (e.g., an EF Core `IQueryable`), this triggers execution.

### Fix

Materialize the collection first, or use a more specific overload:

```csharp
// Materialize first: a materialized collection reports its count in O(1)
var items = query.ToList();
Ensure.ThrowIfNullOrEmpty(items); // O(1) count via TryGetNonEnumeratedCount, no enumeration
```

The single `IEnumerable<T>` overload uses `TryGetNonEnumeratedCount`, which returns an O(1) count for
any `ICollection<T>`/`IReadOnlyCollection<T>`/array (including `List<T>`); only a genuinely lazy
sequence triggers a single `MoveNext()`. Materializing the query first avoids executing it here.

---

## Numeric Guard Does Not Accept My Custom Type

`Ensure.ThrowIfNegative(myMoney)` does not compile because `Money` does not implement `INumber<T>`.

### Cause

`ThrowIfNegative`, `ThrowIfNegativeOrZero`, `ThrowIfZero`, and `ThrowIfPositive` require `INumber<T>`. `ThrowIfOutOfRange`, `ThrowIfGreaterThan`, `ThrowIfLessThan`, `ThrowIfBelowMin`, and `ThrowIfAboveMax` require `IComparable<T>`.

### Fix

If your type implements `IComparable<T>` but not `INumber<T>`, use the range methods:

```csharp
// Money implements IComparable<Money>
Ensure.ThrowIfLessThan(amount, Money.Zero); // Works with IComparable<T>
```

If your type implements neither, extract the underlying value:

```csharp
Ensure.ThrowIfNegative(amount.Value); // Use the decimal/int inside
```

---

## Regex Validation Times Out

`ThrowIfNotMatch` throws or `IsMatch` returns `false` unexpectedly on a long or adversarial input.

### Cause

All regex-based validations have a 250ms timeout to prevent ReDoS attacks. If the input triggers catastrophic backtracking, the regex engine times out.

### Behavior on timeout

| Pattern | Behavior |
|---------|----------|
| `ThrowIfNotMatch` | Throws `ArgumentException` (value treated as invalid) |
| `IsMatch` | Returns `false` |
| `Check.Match` | Returns failure |

### Fix

1. **Simplify the regex.** Avoid nested quantifiers (`(a+)+`), alternation with overlap (`(a|a+)`), and other ReDoS-prone patterns.
2. **Consider using `Is*` methods.** If the value might legitimately be long (e.g., user-provided text), use `IsMatch` and handle `false` gracefully instead of relying on `ThrowIfNotMatch`.
3. **Validate input length first.** Limit the input size before running the regex:

   ```csharp
   Ensure.ThrowIfLongerThan(value, 500);
   Ensure.ThrowIfNotMatch(value, complexPattern);
   ```

---

## VoidResult Chain Stops at First Failure

You chain multiple `Check.*` calls with `.Then(() => …)`, but only the first error is returned even though multiple fields are invalid.

### Cause

`.Then()` short-circuits: when the first check fails, subsequent checks are not executed. This is by design -- `Then` is sequencing, not parallel validation.

### Fix

If you need to collect **all** validation errors at once, use `Pragmatic.Validation` with `ISyncValidator<T>` or `IAsyncValidator<T>` instead of `Check.*` chains. The Validation module is designed for collecting multiple field errors.

For sequential validation where you want the first error:

```csharp
// First failure stops the chain; this is correct behavior for Check
var result = Check.NotNullOrWhiteSpace(dto.Name, nameError)
    .Then(() => Check.Email(dto.Email, emailError))
    .Then(() => Check.InRange(dto.Age, 18, 120, ageError));
```

---

## Diagnostics Reference

Pragmatic.Ensure has a reserved diagnostic range of `PRAG0100-PRAG0199`. Currently, there are no compile-time diagnostics emitted by the source generator for the Ensure module -- all validation is purely runtime.

The runtime guard methods produce standard .NET exceptions:

| Exception | Thrown by |
|-----------|----------|
| `ArgumentNullException` | `ThrowIfNull`, collection methods when collection is null |
| `ArgumentException` | `ThrowIfNullOrEmpty` (string), `ThrowIfNullOrWhiteSpace`, `ThrowIfNotEmail`, `ThrowIfNotUrl`, `ThrowIfNotPhone`, `ThrowIfNotCreditCard`, `ThrowIfNotMatch`, `ThrowIfEmpty` (Guid), `ThrowIfContainsNull`, `ThrowIfEmpty` (collection), `ThrowIfTrue`, `ThrowIfFalse` |
| `ArgumentOutOfRangeException` | `ThrowIfNegative`, `ThrowIfNegativeOrZero`, `ThrowIfZero`, `ThrowIfPositive`, `ThrowIfOutOfRange`, `ThrowIfGreaterThan`, `ThrowIfLessThan`, `ThrowIfBelowMin`, `ThrowIfAboveMax`, `ThrowIfDefault`, `ThrowIfInPast`, `ThrowIfInFuture`, `ThrowIfNotDefined`, `ThrowIfCountGreaterThan`, `ThrowIfCountLessThan`, `ThrowIfCountOutOfRange` |

---

## FAQ

### Do I need both Pragmatic.Ensure and Pragmatic.Ensure.Result?

No. Install only what you need:

- `Pragmatic.Ensure` -- `ThrowIf*` and `Is*` methods, zero dependencies
- `Pragmatic.Ensure.Result` -- `Check.*` methods, depends on `Pragmatic.Result`

If you only use guard clauses and boolean checks, the core package is sufficient.

### Can I use Ensure in a netstandard2.0 library?

No. Pragmatic.Ensure targets .NET 10. The numeric methods use `INumber<T>` (introduced in .NET 7), and `[CallerArgumentExpression]` requires C# 10. If you need to target older frameworks, you cannot use this package.

### Why do Is* methods not throw?

By design. The three patterns have distinct failure semantics: `ThrowIf*` throws, `Is*` returns `bool`, `Check.*` returns `VoidResult<TError>`. If you want to throw on validation failure, use `ThrowIf*`. If you want a boolean for branching, use `Is*`.

### Why does ThrowIfNotEmail accept null without throwing?

Format validation methods are null-safe for optional fields. If the field is required, add `ThrowIfNullOrWhiteSpace` before the format check. See [Common Mistakes #6](common-mistakes.md) for the full explanation.

### Can I extend Ensure with custom methods?

`Ensure` is a `static class` and cannot be extended via inheritance. Create your own static class with domain-specific guards:

```csharp
public static class DomainEnsure
{
    public static void ThrowIfInvalidCurrency(
        string code,
        [CallerArgumentExpression(nameof(code))] string? paramName = null)
    {
        Ensure.ThrowIfNullOrWhiteSpace(code);
        if (code.Length != 3)
            throw new ArgumentException("Currency code must be 3 characters.", paramName);
    }
}
```

### What is the performance cost of guard clauses?

On the happy path, zero allocations and a single branch instruction (the method is inlined by the JIT). See the [Performance Characteristics](concepts.md) table in the Concepts guide.

---

## Getting Help

- **GitHub Issues**: [github.com/pragmatic-design/Pragmatic.Design/issues](https://github.com/pragmatic-design/Pragmatic.Design/issues)
- **API Reference**: See [api-reference.md](api-reference.md) for complete method signatures
- **Best Practices**: See [best-practices.md](best-practices.md) for guard placement rules
- **Common Mistakes**: See [common-mistakes.md](common-mistakes.md) for Wrong/Right/Why patterns
