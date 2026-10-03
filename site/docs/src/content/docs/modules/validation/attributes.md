---
title: "Validation Attributes Reference"
description: "Complete reference for all validation attributes in `Pragmatic.Validation.Attributes`."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Validation/docs/attributes.md
sidebar:
  order: 3
---
Complete reference for all validation attributes in `Pragmatic.Validation.Attributes`.

All attributes are in the namespace `Pragmatic.Validation.Attributes`. They target `AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter` unless noted otherwise.

## General Rules

1. **Null values pass** for all attributes except `[Required]`. Use `[Required]` to enforce non-null, then combine with other attributes for format/range checks.
2. **Types must be `partial`** for the source generator to add the `Validate()` method (diagnostic `PRAG0200`).
3. **Custom message keys**: every attribute has a `MessageKey` property to override the default localization key: `[Email(MessageKey = "custom.invalid_email")]`. With translations, name the key through its generated constant instead of repeating it: `[Email(MessageKey = TKeys.Validation.InvalidEmail)]`. The generator reads the key the constant holds, although it writes the constant itself. A `MessageKey` it cannot read is `PRAG0222`, not a silent fallback to the default key.
4. **Multiple attributes**: you can stack multiple attributes on the same property. They are all checked in order.

---

## Presence Attributes

### [Required]

Validates that a value is not null. For strings, also validates not empty (unless `AllowEmptyStrings = true`).

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AllowEmptyStrings` | `bool` | `false` | When `true`, empty strings are accepted. |
| `MessageKey` | `string?` | `null` | Custom localization key. |

**Default message key**: `validation.required`

**Behavior by type**:
- `string`: null or empty string fails (unless `AllowEmptyStrings = true`).
- Reference types: null fails.
- Value types: always passes (they cannot be null).
- `required` modifier: The SG treats C# `required` properties as having an implicit `[Required]` attribute.

```csharp
[Required]
public string Email { get; init; } = "";

[Required(AllowEmptyStrings = true)]
public string Name { get; init; } = "";

[Required(MessageKey = "custom.email_required")]
public string Email { get; init; } = "";
```

### [NotEmpty]

Validates that a string has length > 0, or a collection has count > 0.

**Default message key**: `validation.notempty`

Works with: `string`, `Array`, `ICollection`, `IEnumerable`.

```csharp
[Required]
[NotEmpty]
public List<OrderItem> Items { get; init; } = [];
```

### [NotWhiteSpace]

Validates that a non-null string is not empty or whitespace only (spaces, tabs, newlines) -- rejects `"   "`.

**Default message key**: `validation.notwhitespace`

Following the general rule, **`null` passes** (only `[Required]` rejects null) -- combine `[Required]` with
`[NotWhiteSpace]` to reject both. Non-string values always pass.

```csharp
[NotWhiteSpace]
public string Content { get; init; } = "";
```

---

## String Length Attributes

### [MinLength(length)]

Validates minimum character count for strings, or minimum element count for collections/arrays.

| Constructor | Parameters |
|-------------|-----------|
| `MinLength(int length)` | Minimum length/count (must be >= 0). |

**Default message key**: `validation.minlength`

```csharp
[MinLength(2)]
public string Name { get; init; } = "";

[MinLength(1)]
public List<string> Tags { get; init; } = [];
```

### [MaxLength(length)]

Validates maximum character count for strings, or maximum element count for collections/arrays.

| Constructor | Parameters |
|-------------|-----------|
| `MaxLength(int length)` | Maximum length/count (must be >= 0). |

**Default message key**: `validation.maxlength`

```csharp
[MaxLength(100)]
public string Name { get; init; } = "";

[MaxLength(10)]
public List<string> Tags { get; init; } = [];
```

### [Length(minimumLength, maximumLength)]

Combines `[MinLength]` and `[MaxLength]` into a single attribute. Validates that length/count is within the range (inclusive).

| Constructor | Parameters |
|-------------|-----------|
| `Length(int minimumLength, int maximumLength)` | Both must be >= 0, minimum <= maximum. |

| Property | Type | Description |
|----------|------|-------------|
| `MinimumLength` | `int` | Minimum length/count. |
| `MaximumLength` | `int` | Maximum length/count. |

**Default message key**: `validation.length`

```csharp
[Length(2, 100)]
public string Name { get; init; } = "";
```

---

## Format Attributes

### [Email]

Validates that a string is a valid email address format. Uses a practical regex pattern (not strict RFC 5322). Maximum 254 characters (per RFC). Compiled with a 250ms regex timeout (ReDoS protection).

Has a static `IsValidEmail(string?)` method used by the SG for inline validation.

**Default message key**: `validation.email`

```csharp
[Required]
[Email]
public string Email { get; init; } = "";
```

### [Phone]

Validates that a string is a valid phone number format. Accepts common formats including international (digits, spaces, hyphens, parentheses, optional leading `+`). Requires 7-15 digits.

Has a static `IsValidPhone(string?)` method.

**Default message key**: `validation.phone`

```csharp
[Phone]
public string? PhoneNumber { get; init; }
// Valid: "+1 234 567-8900", "(234) 567-8900", "+39 02 1234567"
```

### [Url]

Validates that a string is a valid URL.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AllowedSchemes` | `string[]?` | `["http", "https"]` | Allowed URI schemes. |
| `RequireAbsolute` | `bool` | `true` | Whether to require absolute URIs. |

Has a static `IsValidUrl(string?, string[]?, bool)` method.

**Default message key**: `validation.url`

```csharp
[Url]
public string? Website { get; init; }

[Url(AllowedSchemes = new[] { "http", "https", "ftp" })]
public string? FileUrl { get; init; }

[Url(RequireAbsolute = false)]
public string? RelativeLink { get; init; }
```

### [CreditCard]

Validates that a string is a valid credit card number using the Luhn algorithm (ISO/IEC 7812-1). Accepts 13-19 digits. Spaces and hyphens are stripped before validation.

Has a static `IsValidCreditCard(string?)` method.

**Default message key**: `validation.creditcard`

```csharp
[Required]
[CreditCard]
public string CardNumber { get; init; } = "";
```

### [Regex(pattern)]

Validates that a string matches a regular expression pattern. The regex is compiled with `RegexOptions.Compiled | RegexOptions.CultureInvariant` and a 250ms timeout.

Supports `AllowMultiple = true` -- you can apply multiple `[Regex]` attributes to the same property.

| Constructor | Parameters |
|-------------|-----------|
| `Regex(string pattern)` | The regex pattern (must not be null or empty). |

| Property | Type | Description |
|----------|------|-------------|
| `Pattern` | `string` | The regex pattern. |

**Default message key**: `validation.regex`

```csharp
[Regex(@"^[A-Z]{2}-\d{4}$")]
public string ProductCode { get; init; } = "";

[Regex(@"^\d{5}(-\d{4})?$", MessageKey = "validation.zipcode")]
public string ZipCode { get; init; } = "";
```

### [Guid]

Validates that a string is a valid GUID format. Accepts standard formats: `D` (with hyphens), `N` (digits only), `B` (braces), `P` (parentheses).

Has a static `IsValidGuid(string?)` method.

**Default message key**: `validation.guid`

```csharp
[Required]
[Guid]
public string ExternalId { get; init; } = "";
```

---

## Numeric Attributes

All numeric attributes support: `int`, `long`, `short`, `byte`, `sbyte`, `uint`, `ulong`, `ushort`, `float`, `double`, `decimal`.

### [Range(minimum, maximum)]

Validates that a numeric value is within a specified range (inclusive on both ends).

| Constructor | Parameters |
|-------------|-----------|
| `Range(int minimum, int maximum)` | Integer bounds. |
| `Range(double minimum, double maximum)` | Double bounds. |

| Property | Type | Description |
|----------|------|-------------|
| `Minimum` | `object` | Minimum value (inclusive). |
| `Maximum` | `object` | Maximum value (inclusive). |

**Default message key**: `validation.range`

```csharp
[Range(0, 100)]
public decimal Price { get; init; }

[Range(1, 1000)]
public int Quantity { get; init; }

[Range(0.0, 1.0)]
public double Percentage { get; init; }
```

### [Positive]

Validates that a numeric value is greater than zero. Zero is NOT considered positive.

**Default message key**: `validation.positive`

```csharp
[Required]
[Positive]
public decimal Amount { get; init; }
```

### [Negative]

Validates that a numeric value is less than zero. Zero is NOT considered negative. Unsigned types always pass (they cannot be negative).

**Default message key**: `validation.negative`

```csharp
[Negative]
public decimal Adjustment { get; init; }
```

### [GreaterThan(value)]

Validates that a numeric value is strictly greater than the specified value.

| Constructor | Parameters |
|-------------|-----------|
| `GreaterThan(int value)` | Integer bound. |
| `GreaterThan(double value)` | Double bound. |

**Default message key**: `validation.greaterthan`

```csharp
[GreaterThan(0)]
public decimal DiscountPercent { get; init; }
```

### [GreaterThanOrEqual(value)]

Validates that a numeric value is greater than or equal to the specified value.

| Constructor | Parameters |
|-------------|-----------|
| `GreaterThanOrEqual(int value)` | Integer bound. |
| `GreaterThanOrEqual(double value)` | Double bound. |

**Default message key**: `validation.greaterthanorequal`

```csharp
[GreaterThanOrEqual(1)]
public int Quantity { get; init; }
```

### [LessThan(value)]

Validates that a numeric value is strictly less than the specified value.

| Constructor | Parameters |
|-------------|-----------|
| `LessThan(int value)` | Integer bound. |
| `LessThan(double value)` | Double bound. |

**Default message key**: `validation.lessthan`

```csharp
[LessThan(150)]
public int Age { get; init; }
```

### [LessThanOrEqual(value)]

Validates that a numeric value is less than or equal to the specified value.

| Constructor | Parameters |
|-------------|-----------|
| `LessThanOrEqual(int value)` | Integer bound. |
| `LessThanOrEqual(double value)` | Double bound. |

**Default message key**: `validation.lessthanorequal`

```csharp
[LessThanOrEqual(100)]
public decimal DiscountPercent { get; init; }
```

---

## Collection Attributes

### [MinCount(count)]

Validates that a collection has at least the specified number of elements.

| Constructor | Parameters |
|-------------|-----------|
| `MinCount(int count)` | Minimum element count (must be >= 0). |

Works with: `Array`, `ICollection`, `IEnumerable`.

**Default message key**: `validation.mincount`

```csharp
[Required]
[MinCount(1)]
public List<OrderItem> Items { get; init; } = [];
```

### [MaxCount(count)]

Validates that a collection does not exceed the specified number of elements.

| Constructor | Parameters |
|-------------|-----------|
| `MaxCount(int count)` | Maximum element count (must be >= 0). |

**Default message key**: `validation.maxcount`

```csharp
[MaxCount(10)]
public List<string> Tags { get; init; } = [];
```

### [Count(minimumCount, maximumCount)]

Combines `[MinCount]` and `[MaxCount]` into a single attribute.

| Constructor | Parameters |
|-------------|-----------|
| `Count(int minimumCount, int maximumCount)` | Both must be >= 0, min <= max. |

| Property | Type | Description |
|----------|------|-------------|
| `MinimumCount` | `int` | Minimum element count. |
| `MaximumCount` | `int` | Maximum element count. |

**Default message key**: `validation.count`

```csharp
[Required]
[Count(2, 10)]
public List<Guid> TeamMembers { get; init; } = [];
```

### [ValidateElements]

Indicates that each element in a collection should be validated individually. The element type must implement `ISyncValidator` (it must be a `partial` type with validation attributes).

This is NOT a `ValidationAttribute` -- it is a standalone `Attribute`. The SG generates a loop that calls `Validate()` on each element. Error paths include the index: `Items[0].ProductId`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `StopOnFirstError` | `bool` | `false` | Stop at the first invalid element. |

**Diagnostics**:
- `PRAG0204` (Warning): Applied to a non-collection type.
- `PRAG0205` (Error): Element type does not implement `ISyncValidator`.

```csharp
public partial class OrderItemRequest
{
    [Required]
    public Guid ProductId { get; init; }

    [Range(1, 100)]
    public int Quantity { get; init; }
}

public partial class PlaceOrderRequest
{
    [Required]
    [NotEmpty]
    [ValidateElements]
    public List<OrderItemRequest> Items { get; init; } = [];
}
// Error path: "Items[0].ProductId", "Items[2].Quantity"
```

---

## Comparison Attributes (Cross-Property)

These attributes compare the decorated property against another property on the same type. They set `RequiresInstance = true` and use the `IsValid(object?, object)` overload.

The source generator emits direct, compile-time property access for the built-in comparison attributes
(no reflection). Custom cross-property attributes read the sibling value through the generated
`IPropertyValueProvider.GetPropertyValue(name)` (a `switch` over property names, also reflection-free).

**Diagnostic `PRAG0203`**: Emitted as an error when the referenced property does not exist on the type.
**Diagnostic `PRAG0209`**: Emitted as a warning when the two properties have incompatible types.

### [EqualTo(otherPropertyName)]

Validates that the property value equals another property's value. Uses `object.Equals()`. Both null values are considered equal.

**Default message key**: `validation.equalto`

```csharp
public partial class ChangePasswordRequest
{
    [Required]
    public string NewPassword { get; init; } = "";

    [Required]
    [EqualTo(nameof(NewPassword))]
    public string ConfirmPassword { get; init; } = "";
}
```

### [NotEqualTo(otherPropertyName)]

Validates that the property value does NOT equal another property's value. If either value is null, the comparison passes.

**Default message key**: `validation.notequalto`

```csharp
public partial class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; init; } = "";

    [Required]
    [NotEqualTo(nameof(CurrentPassword))]
    public string NewPassword { get; init; } = "";
}
```

### [GreaterThanProperty(otherPropertyName)]

Validates that the property value is greater than another property's value. Both values must implement `IComparable`. Works with `DateTime`, `DateTimeOffset`, and numeric types.

**Default message key**: `validation.greaterthanproperty`

```csharp
public partial class DateRangeRequest
{
    [Required]
    public DateTime StartDate { get; init; }

    [Required]
    [GreaterThanProperty(nameof(StartDate))]
    public DateTime EndDate { get; init; }
}
```

### [LessThanProperty(otherPropertyName)]

Validates that the property value is less than another property's value.

**Default message key**: `validation.lessthanproperty`

```csharp
public partial class PriceRangeRequest
{
    [Required]
    [LessThanProperty(nameof(MaxPrice))]
    public decimal MinPrice { get; init; }

    [Required]
    public decimal MaxPrice { get; init; }
}
```

### [GreaterThanOrEqualProperty(otherPropertyName)] / [LessThanOrEqualProperty(otherPropertyName)]

The inclusive forms: the equal value passes. A period that ends on the day it starts is a period, and
`[GreaterThanProperty]` refuses it.

**Default message keys**: `validation.greaterthanorequalproperty`, `validation.lessthanorequalproperty`

```csharp
public partial class SubmitLeaveRequest
{
    public required DateOnly From { get; init; }

    [GreaterThanOrEqualProperty(nameof(From))]
    public required DateOnly To { get; init; }
}
```

---

## Conditional Attributes

### [RequiredIf(otherPropertyName, expectedValue)]

Makes a property conditionally required when another property equals the expected value.

Supports `AllowMultiple = true` -- multiple `[RequiredIf]` attributes on the same property.

| Constructor | Parameters |
|-------------|-----------|
| `RequiredIf(string otherPropertyName, object? expectedValue)` | Property name and trigger value. |

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `OtherPropertyName` | `string` | -- | Property to check. |
| `ExpectedValue` | `object?` | -- | Value that triggers the requirement. |
| `AllowEmptyStrings` | `bool` | `false` | Whether empty strings are valid when required. |

**Default message key**: `validation.requiredif`

```csharp
public partial class PaymentRequest
{
    [Required]
    public PaymentMethod Method { get; init; }

    [RequiredIf(nameof(Method), PaymentMethod.CreditCard)]
    public string? CardNumber { get; init; }

    [RequiredIf(nameof(Method), PaymentMethod.BankTransfer)]
    public string? BankAccount { get; init; }
}
```

### [RequiredIfNot(otherPropertyName, excludedValue)]

Makes a property conditionally required when another property does NOT equal the specified value.

Supports `AllowMultiple = true`.

| Constructor | Parameters |
|-------------|-----------|
| `RequiredIfNot(string otherPropertyName, object? excludedValue)` | Property name and excluded value. |

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `OtherPropertyName` | `string` | -- | Property to check. |
| `ExcludedValue` | `object?` | -- | Value that, when NOT present, triggers the requirement. |
| `AllowEmptyStrings` | `bool` | `false` | Whether empty strings are valid when required. |

**Default message key**: `validation.requiredifnot`

```csharp
public partial class ContactRequest
{
    public string? Email { get; init; }

    // Phone is required when Email is NOT null (i.e., user provided email)
    [RequiredIfNot(nameof(Email), null)]
    public string? Phone { get; init; }
}

// Or: require Phone when ContactMethod is NOT Email
public partial class ContactRequest2
{
    [Required]
    public ContactMethod Method { get; init; }

    [RequiredIfNot(nameof(Method), ContactMethod.Email)]
    public string? Phone { get; init; }
}
```

---

## Extended / Type-Specific Attributes

### [ValidEnum]

Validates that a value is a defined member of an enum type. Prevents invalid values assigned through casting (e.g., `(Status)999`).

Has a static generic `IsValidEnum<TEnum>(TEnum value)` method.

**Default message key**: `validation.enum`

```csharp
[Required]
[ValidEnum]
public OrderStatus Status { get; init; }
```

### [OneOf(allowedValues)]

Validates that a value is one of a fixed set of allowed values. Uses `object.Equals()` for comparison.

| Constructor | Parameters |
|-------------|-----------|
| `OneOf(params object[] allowedValues)` | The set of allowed values. |

| Property | Type | Description |
|----------|------|-------------|
| `AllowedValues` | `object[]` | The allowed values. |

**Default message key**: `validation.oneof`

```csharp
[Required]
[OneOf("draft", "published", "archived")]
public string Status { get; init; } = "";
```

### [FutureDate]

Validates that a `DateTime` or `DateTimeOffset` is in the future. Compares against `DateTime.UtcNow` / `DateTimeOffset.UtcNow`.

**Default message key**: `validation.future_date`

Note: Uses `DateTime.UtcNow` directly because `ValidationAttribute.IsValid()` does not support dependency injection. The generated code has the same limitation.

```csharp
[Required]
[FutureDate]
public DateTimeOffset StartDate { get; init; }
```

### [PastDate]

Validates that a `DateTime` or `DateTimeOffset` is in the past.

**Default message key**: `validation.past_date`

Same `DateTime.UtcNow` limitation as `[FutureDate]`.

```csharp
[Required]
[PastDate]
public DateTime BirthDate { get; init; }
```

---

## Special Attributes

### [Validator]

Marks a class as an async validator for DI auto-registration. Targets `AttributeTargets.Class` only.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Lifetime` | `ServiceLifetime` | `Scoped` | DI service lifetime. |

The class must implement `IAsyncValidator<T>`. If it does not, diagnostic `PRAG0201` is emitted.

```csharp
[Validator]
public class CreateUserValidator : IAsyncValidator<CreateUserRequest> { ... }

[Validator(Lifetime = ServiceLifetime.Transient)]
public class ExpensiveValidator : IAsyncValidator<ComplexRequest> { ... }
```

### [AsyncValidate\<TValidator>]

Binds an async validator to a property or entity for change-aware invocation. Targets `AttributeTargets.Property | AttributeTargets.Class`. Supports `AllowMultiple = true`.

When applied to a **property**: the validator fires only when that property is modified.
When applied to a **class**: the validator fires on any entity modification.

The SG generates `IAsyncValidatorBindings<T>` from these bindings.

```csharp
// Property-level binding
public partial class User
{
    [AsyncValidate<EmailUniquenessValidator>]
    public string Email { get; private set; }
}

// Entity-level binding
[AsyncValidate<UserConsistencyValidator>]
public partial class User { ... }
```

---

## Summary Table

| Category | Attribute | Parameters | Default Message Key |
|----------|-----------|------------|---------------------|
| **Presence** | `[Required]` | `AllowEmptyStrings` | `validation.required` |
| | `[NotEmpty]` | -- | `validation.notempty` |
| | `[NotWhiteSpace]` | -- | `validation.notwhitespace` |
| **String** | `[MinLength(n)]` | `int length` | `validation.minlength` |
| | `[MaxLength(n)]` | `int length` | `validation.maxlength` |
| | `[Length(min, max)]` | `int min, int max` | `validation.length` |
| **Format** | `[Email]` | -- | `validation.email` |
| | `[Phone]` | -- | `validation.phone` |
| | `[Url]` | `AllowedSchemes`, `RequireAbsolute` | `validation.url` |
| | `[CreditCard]` | -- | `validation.creditcard` |
| | `[Regex(pattern)]` | `string pattern` | `validation.regex` |
| | `[Guid]` | -- | `validation.guid` |
| **Numeric** | `[Range(min, max)]` | `int/double min, max` | `validation.range` |
| | `[Positive]` | -- | `validation.positive` |
| | `[Negative]` | -- | `validation.negative` |
| | `[GreaterThan(n)]` | `int/double value` | `validation.greaterthan` |
| | `[GreaterThanOrEqual(n)]` | `int/double value` | `validation.greaterthanorequal` |
| | `[LessThan(n)]` | `int/double value` | `validation.lessthan` |
| | `[LessThanOrEqual(n)]` | `int/double value` | `validation.lessthanorequal` |
| **Collection** | `[MinCount(n)]` | `int count` | `validation.mincount` |
| | `[MaxCount(n)]` | `int count` | `validation.maxcount` |
| | `[Count(min, max)]` | `int min, int max` | `validation.count` |
| | `[ValidateElements]` | `StopOnFirstError` | N/A |
| **Comparison** | `[EqualTo(prop)]` | `string otherPropertyName` | `validation.equalto` |
| | `[NotEqualTo(prop)]` | `string otherPropertyName` | `validation.notequalto` |
| | `[GreaterThanProperty(prop)]` | `string otherPropertyName` | `validation.greaterthanproperty` |
| | `[LessThanProperty(prop)]` | `string otherPropertyName` | `validation.lessthanproperty` |
| | `[GreaterThanOrEqualProperty(prop)]` | `string otherPropertyName` | `validation.greaterthanorequalproperty` |
| | `[LessThanOrEqualProperty(prop)]` | `string otherPropertyName` | `validation.lessthanorequalproperty` |
| **Conditional** | `[RequiredIf(prop, value)]` | `string prop, object? value` | `validation.requiredif` |
| | `[RequiredIfNot(prop, value)]` | `string prop, object? value` | `validation.requiredifnot` |
| **Extended** | `[ValidEnum]` | -- | `validation.enum` |
| | `[OneOf(values)]` | `params object[] values` | `validation.oneof` |
| | `[FutureDate]` | -- | `validation.future_date` |
| | `[PastDate]` | -- | `validation.past_date` |
| **DI** | `[Validator]` | `Lifetime` | N/A |
| **Binding** | `[AsyncValidate<T>]` | -- | N/A |
