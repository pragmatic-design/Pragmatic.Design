---
title: "API Reference"
description: "Complete reference for `Pragmatic.Ensure` (`ThrowIf*` / `Is*`) and `Pragmatic.Ensure.Result` (`Check.*`)."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Ensure/docs/api-reference.md
sidebar:
  order: 4
---
Complete reference for `Pragmatic.Ensure` (`ThrowIf*` / `Is*`) and `Pragmatic.Ensure.Result` (`Check.*`).

All `ThrowIf*` methods capture the argument expression automatically via `[CallerArgumentExpression]`,
so the `paramName` parameter (always the last, optional) is filled in by the compiler: you never pass it.

---

## Null Checks (`ThrowIf*`)

| Method | Signature | Returns / Throws |
|--------|-----------|------------------|
| `ThrowIfNull<T>` | `T? value` where `T : class` | Returns the non-null value / `ArgumentNullException` |
| `ThrowIfNull<T>` | `T? value` where `T : struct` | Returns the unwrapped value / `ArgumentNullException` |
| `ThrowIfNull<T1,T2>` | `T1? v1, T2? v2` | Returns `(T1, T2)` / `ArgumentNullException` |
| `ThrowIfAnyNull` | `object? v1, v2 [, v3, v4, v5]` | `void` / `ArgumentNullException` (boxes value types) |

```csharp
_repository = Ensure.ThrowIfNull(repository);          // fluent assign, reference type
int count   = Ensure.ThrowIfNull(nullableCount);       // unwrap nullable value type
var (repo, logger) = Ensure.ThrowIfNull(repo, logger); // tuple overload, both validated
Ensure.ThrowIfAnyNull(a, b, c);                        // void; prefer the generic overloads for null-flow analysis
```

> The `ThrowIfAnyNull` overloads take `object?`, which boxes value types and loses `[NotNull]` flow
> analysis. For null-state narrowing and fluent assignment, prefer individual `ThrowIfNull` calls.

## String Checks (`ThrowIf*`)

### Presence (throw on null)

| Method | Throws on |
|--------|-----------|
| `ThrowIfNullOrEmpty` | `null`, `""`; returns the validated string |
| `ThrowIfNullOrWhiteSpace` | `null`, `""`, `"   "`; returns the validated string |

### Length (throw `ArgumentNullException` on null)

```csharp
Ensure.ThrowIfLongerThan(name, 100);
Ensure.ThrowIfShorterThan(name, 2);
Ensure.ThrowIfLengthOutOfRange(name, 2, 100);
```

All three call `ThrowIfNull` internally: a null value throws `ArgumentNullException`. (The `Is*`
counterpart `IsLengthInRange` is null-safe; the guard is not.)

### Content (throw `ArgumentNullException` on null, `Ordinal` comparison)

```csharp
Ensure.ThrowIfContains(value, "substring");
Ensure.ThrowIfStartsWith(value, "prefix");
Ensure.ThrowIfEndsWith(value, "suffix");
```

### Format (null-safe: a null value passes)

```csharp
Ensure.ThrowIfNotEmail(email);                 // System.Net.Mail.MailAddress parsing
Ensure.ThrowIfNotUrl(url);                      // http/https absolute URI
Ensure.ThrowIfNotUrl(url, requireHttps: true);  // https only
Ensure.ThrowIfNotPhone(phone);                  // E.164-compatible (7-15 digits)
Ensure.ThrowIfNotCreditCard(card);              // Luhn / ISO-IEC 7812
Ensure.ThrowIfNotMatch(value, @"^\d{5}$");      // custom regex (250ms ReDoS timeout)
```

Null-safe means a `null` value passes without throwing (for optional fields). If the field is
required, call `ThrowIfNullOrWhiteSpace` first. On a regex timeout, `ThrowIfNotMatch` throws
`ArgumentException`.

## Numeric Checks (`ThrowIf*`)

Sign checks require `INumber<T>`; range/comparison checks require `IComparable<T>`. Both work with
`int`, `long`, `decimal`, `double`, `float`, and any conforming type.

```csharp
Ensure.ThrowIfNegative(value);           // value < 0
Ensure.ThrowIfNegativeOrZero(value);     // value <= 0
Ensure.ThrowIfZero(value);               // value == 0
Ensure.ThrowIfPositiveOrZero(value);     // value >= 0 (INumber.IsPositive: zero counts as positive)
Ensure.ThrowIfOutOfRange(value, 0, 100); // value outside [0, 100]
Ensure.ThrowIfGreaterThan(value, max);   // value > max
Ensure.ThrowIfLessThan(value, min);      // value < min
```

All numeric guards throw `ArgumentOutOfRangeException`.

> **Obsolete:** `ThrowIfPositive` (use `ThrowIfPositiveOrZero`, or `ThrowIfGreaterThan` with zero for
> strictly-positive), `ThrowIfBelowMin` (use `ThrowIfLessThan`), `ThrowIfAboveMax` (use `ThrowIfGreaterThan`).

## Collection Checks (`ThrowIf*`)

A single `IEnumerable<T>` overload serves every collection type (plus a more-specific `T[]` overload).
It uses `TryGetNonEnumeratedCount`, which is O(1) for `ICollection<T>`, `IReadOnlyCollection<T>`, and
arrays, so passing a concrete `List<T>`/`HashSet<T>` works and is never ambiguous.

| Method | Null behaviour | Empty behaviour |
|--------|----------------|-----------------|
| `ThrowIfEmpty` | Does **not** null-check (a null argument throws `NullReferenceException`) | `ArgumentException` |
| `ThrowIfNullOrEmpty` | `ArgumentNullException` | `ArgumentException` |
| `ThrowIfContainsDuplicate` | n/a | `ArgumentException` if a duplicate is found |
| `ThrowIfContainsNull` | `ArgumentNullException` | `ArgumentException` if any element is null |
| `ThrowIfCountGreaterThan(max)` | `ArgumentNullException` | `ArgumentOutOfRangeException` if `Count > max` |
| `ThrowIfCountLessThan(min)` | `ArgumentNullException` | `ArgumentOutOfRangeException` if `Count < min` |
| `ThrowIfCountOutOfRange(min, max)` | `ArgumentNullException` | `ArgumentOutOfRangeException` if outside `[min, max]` |

```csharp
Ensure.ThrowIfNullOrEmpty(items);        // null OR empty: the usual choice for a required collection
Ensure.ThrowIfEmpty(items);              // empty only (guard null separately)
Ensure.ThrowIfContainsNull(items);       // no null elements (reference types)
Ensure.ThrowIfCountOutOfRange(items, 1, 10);
```

> `ThrowIfEmpty` and `ThrowIfNullOrEmpty` are **not** the same method: `ThrowIfEmpty` does not check
> for null. Use `ThrowIfNullOrEmpty` when null is also invalid.

## Guid, Enum, DateTime (`ThrowIf*`)

```csharp
Ensure.ThrowIfEmpty(id);           // Guid.Empty → ArgumentException
Ensure.ThrowIfNotDefined(status);  // enum value not defined → ArgumentOutOfRangeException

Ensure.ThrowIfDefault(date);       // default(DateTime) / default(DateTimeOffset) → ArgumentOutOfRangeException
Ensure.ThrowIfInPast(date);        // earlier than UtcNow → ArgumentOutOfRangeException
Ensure.ThrowIfInFuture(date);      // later than UtcNow → ArgumentOutOfRangeException
```

`DateTime` and `DateTimeOffset` overloads exist for all three temporal guards. For `DateTime`, a
`Local` value is converted to UTC before comparison; `Unspecified` is treated as UTC.

## Equality & Default (`ThrowIf*`)

```csharp
Ensure.ThrowIfEqual(value, forbidden);   // ArgumentException if value == forbidden
Ensure.ThrowIfNotEqual(value, expected); // ArgumentException if value != expected
Ensure.ThrowIfDefault(structValue);      // ArgumentException if EqualityComparer<T>.Default.Equals(value, default)
```

## Boolean Conditions (`ThrowIf*`)

```csharp
Ensure.ThrowIfTrue(user.IsDeleted, "User is deleted");
Ensure.ThrowIfFalse(user.IsActive, "User is inactive");
```

Both throw `ArgumentException`; the message is optional (defaults to "Condition must be false/true.").

## Exception Types (`ThrowIf*`)

| Pattern | Exception |
|---------|-----------|
| `ThrowIfNull*`, collection guards on a null argument | `ArgumentNullException` |
| `ThrowIfNegative*`, `ThrowIfZero`, `ThrowIfPositiveOrZero`, `ThrowIf*Range`, `ThrowIfGreaterThan`, `ThrowIfLessThan`, `ThrowIfNotDefined`, `ThrowIfDefault`, `ThrowIfInPast/InFuture`, `ThrowIfCount*` | `ArgumentOutOfRangeException` |
| All other `ThrowIf*` (string/collection/Guid/equality/boolean) | `ArgumentException` |

---

## Boolean Checks (`Is*`)

`Is*` methods return `bool` and never throw (a regex timeout is reported as a non-match, not an exception).
Null-narrowing (`[NotNullWhen(true)]`) is applied where a `true` result guarantees non-null.

```csharp
// Strings
Ensure.IsNotNullOrEmpty(value)      Ensure.IsNotNullOrWhiteSpace(value)
Ensure.IsLengthInRange(value, min, max)   // null-safe: null returns true
Ensure.IsEmail(value)  Ensure.IsUrl(value, requireHttps: false)  Ensure.IsPhone(value)
Ensure.IsCreditCard(value)  Ensure.IsMatch(value, pattern)        // IsMatch: null returns true
Ensure.DoesNotContain(value, "x")  Ensure.DoesNotStartWith(value, "x")  Ensure.DoesNotEndWith(value, "x")

// Numeric
Ensure.IsPositive(value)  Ensure.IsNegative(value)  Ensure.IsZero(value)  Ensure.IsNotZero(value)
Ensure.IsNotNegative(value)  Ensure.IsInRange(value, min, max)
Ensure.IsAtLeastMin(value, min)  Ensure.IsAtMostMax(value, max)

// Null / Guid / enum
Ensure.IsNotNull(value)  Ensure.IsNotEmpty(guid)  Ensure.IsDefined(enumValue)

// Collections (single IEnumerable<T> overload + array)
Ensure.IsNotNullOrEmpty(items)   Ensure.HasNoDuplicates(items)   // HasNoDuplicates: null returns true

// DateTime / DateTimeOffset
Ensure.IsPast(date)  Ensure.IsFuture(date)  Ensure.IsNotDefault(date)

// Equality (T : IEquatable<T>?)
Ensure.AreEqual(a, b)  Ensure.AreNotEqual(a, b)
```

Two `Is*` string methods return **true** for null (null-safe for optional values): `IsLengthInRange`
and `IsMatch`. All other null-aware `Is*` methods return **false** for null.

---

## Check Methods (`Pragmatic.Ensure.Result`)

`Check.*` returns `VoidResult<TError>` (`where TError : IError`) instead of throwing, for domain
validation where failure is expected. Every method has two overloads: one taking the error directly,
one taking a `Func<TError>` factory (invoked only on failure, for lazy error construction).

```bash
dotnet add package Pragmatic.Ensure.Result
```

```csharp
using Pragmatic.Ensure.Result;

// Null
Check.NotNull(value, error)                 // T : class  or  T? where T : struct

// String
Check.NotNullOrEmpty(value, error)          Check.NotNullOrWhiteSpace(value, error)
Check.LengthInRange(value, min, max, error) // null-safe: null → success
Check.Email(value, error)                   Check.Url(value, error, requireHttps: false)
Check.Phone(value, error)                   Check.CreditCard(value, error)
Check.Match(value, pattern, error)          // null-safe: null → success
Check.DoesNotContain(value, "x", error)     Check.DoesNotStartWith(value, "x", error)
Check.DoesNotEndWith(value, "x", error)

// Numeric   (Positive/NotNegative/NotZero: INumber<T>; InRange/AtLeast/AtMost: IComparable<T>)
Check.Positive(value, error)     Check.NotNegative(value, error)   Check.NotZero(value, error)
Check.InRange(value, min, max, error)  Check.AtLeast(value, min, error)  Check.AtMost(value, max, error)

// Collections (single IEnumerable<T> overload + array)
Check.NotNullOrEmpty(value, error)   Check.NoDuplicates(value, error)   Check.ContainsNoNull(value, error)
Check.CountNotGreaterThan(value, max, error)   Check.CountNotLessThan(value, min, error)

// Guid / enum / boolean
Check.NotEmpty(guid, error)   Check.Defined(enumValue, error)
Check.That(condition, error)  Check.Not(condition, error)   // Not = failure when condition is true

// DateTime / DateTimeOffset
Check.InPast(value, error)   Check.InFuture(value, error)   Check.NotDefault(value, error)

// Equality (T : IEquatable<T>?)
Check.Equal(a, b, error)   Check.NotEqual(a, b, error)
```

### Composable chain

```csharp
var result = Check.NotNullOrWhiteSpace(dto.Email, ValidationError.For("Email", "validation.required"))
    .Then(() => Check.Email(dto.Email, ValidationError.For("Email", "validation.email")));

if (result.IsFailure)
    return result.Error;
```

`Then` short-circuits at the first failure, and every link shares one `TError`: a `NotFoundError`
check and a `ValidationError` check do not chain. To collect *all* field errors at once, use
`Pragmatic.Validation` instead of a `Check.*` chain.
