---
title: "Architecture and Core Concepts"
description: "This guide explains **why** Pragmatic.Validation exists, how its pieces fit together, and how to choose the right approach for each validation scenario. Read th"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Validation/docs/concepts.md
sidebar:
  order: 1
---
This guide explains **why** Pragmatic.Validation exists, how its pieces fit together, and how to choose the right approach for each validation scenario. Read this before diving into the individual feature guides.

---

## The Problem

Validation in .NET applications tends to scatter across layers, styles, and libraries. Three patterns recur in most codebases, and each has serious downsides.

### Inline validation: buried in business logic

```csharp
public async Task<Result<Reservation, IError>> CreateReservation(
    CreateReservationRequest request, CancellationToken ct)
{
    // Validation is tangled with business logic
    if (string.IsNullOrWhiteSpace(request.GuestName))
        return ValidationError.For("GuestName", "validation.required");

    if (request.Email is null || !request.Email.Contains('@'))
        return ValidationError.For("Email", "validation.email");

    if (request.CheckIn < DateTimeOffset.UtcNow)
        return ValidationError.For("CheckIn", "validation.future_date");

    if (request.CheckOut <= request.CheckIn)
        return ValidationError.For("CheckOut", "validation.greaterthanproperty", ("other", "CheckIn"));

    if (request.NumberOfGuests is < 1 or > 20)
        return ValidationError.For("NumberOfGuests", "validation.range", ("min", 1), ("max", 20));

    // Only now: the actual business logic
    var roomType = await _roomTypes.GetByIdAsync(request.RoomTypeId, ct);
    if (roomType is null)
        return NotFoundError.For("RoomType", request.RoomTypeId);

    // ... create reservation
}
```

Fifteen lines of `if` checks before a single line of business logic. Every action that accepts input repeats this pattern. When two actions accept the same DTO, the validation is duplicated or forgotten in one of them.

### DataAnnotations: reflection-heavy and limited

```csharp
public class CreateReservationRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    public string GuestName { get; init; } = "";

    [System.ComponentModel.DataAnnotations.EmailAddress]
    public string Email { get; init; } = "";

    [System.ComponentModel.DataAnnotations.Range(1, 20)]
    public int NumberOfGuests { get; init; }
}

// Validation runs via reflection at runtime
var context = new ValidationContext(request);
var results = new List<ValidationResult>();
if (!Validator.TryValidateObject(request, context, results, true))
{
    // Convert results to your error format...
}
```

`DataAnnotations` uses reflection to discover attributes and invoke them on every validation call. Cross-property validation (e.g., "CheckOut must be after CheckIn") requires implementing `IValidatableObject`, which mixes validation logic back into the model. Async validation (database uniqueness checks) is not supported at all.

### FluentValidation: powerful but runtime-only

```csharp
public class CreateReservationValidator : AbstractValidator<CreateReservationRequest>
{
    public CreateReservationValidator()
    {
        RuleFor(x => x.GuestName).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.CheckIn).GreaterThan(DateTimeOffset.UtcNow);
        RuleFor(x => x.CheckOut).GreaterThan(x => x.CheckIn);
        RuleFor(x => x.NumberOfGuests).InclusiveBetween(1, 20);
    }
}
```

Expressive, but the rules are evaluated via expression trees and reflection at runtime. The validator class is separate from the model, so you must maintain two files in sync. It adds a NuGet dependency, and integrating async validation (database calls) into the rule chain requires careful handling of `MustAsync` and its performance implications.

**The fundamental issue**: in all three approaches, validation rules are either scattered, reflection-based, or disconnected from the model they validate. None integrates natively with a DomainAction pipeline, entity change tracking, or compile-time code generation.

---

## The Solution

Pragmatic.Validation inverts the model. You declare validation rules as attributes on the model itself, and the source generator produces the exact validation code at compile time -- no reflection, no runtime expression evaluation, no separate validator class to maintain.

The same reservation request:

```csharp
using Pragmatic.Validation.Attributes;

public partial class CreateReservationRequest
{
    [Required]
    [NotWhiteSpace]
    public string GuestName { get; init; } = "";

    [Required]
    [Email]
    public string Email { get; init; } = "";

    [FutureDate]
    public DateTimeOffset CheckIn { get; init; }

    [GreaterThanProperty(nameof(CheckIn))]
    public DateTimeOffset CheckOut { get; init; }

    [Positive]
    [Range(1, 20)]
    public int NumberOfGuests { get; init; } = 1;
}
```

The source generator reads this class at compile time and produces:
- An `ISyncValidator.Validate()` method on the class with inline `if` checks -- no attribute discovery, no reflection
- A change-aware `Validate(IReadOnlySet<string>?)` overload for entity types, with cross-property dependency tracking
- DI registration for async validators (`[Validator]` classes) and `CompositeValidator<T>` orchestration
- `IAsyncValidatorBindings<T>` for change-aware async filtering via `[AsyncValidate<T>]`
- Body DTO validation for endpoints -- the Validation SG predicts which DTOs the Endpoints SG will generate and adds `ISyncValidator` to them

The validation is on the model, visible in IntelliSense, verified by the compiler, and executed without overhead.

---

## How It Works: The Validation Pipeline

Every validation in Pragmatic flows through a deterministic pipeline. The source generator assembles the pipeline at compile time based on the attributes and interfaces you use.

```
Instance to validate
  |
  v
1. Sync Validation               ISyncValidator.Validate()
  |                               Generated from attributes: [Required], [Email], [Range], etc.
  |                               Fast, in-memory, zero I/O
  |
  v
2. FailFast Check                 If FailFast=true AND sync failed --> return errors
  |                               Skips expensive async validation
  |
  v
3. Async Validation               IAsyncValidator<T>.ValidateAsync()
  |                               Database lookups, external API calls
  |                               Filtered by IAsyncValidatorBindings<T> if available
  |
  v
4. Combine Errors                 Merge sync + async errors --> single ValidationError
  |
  v
ValidationError
  (IsSuccess or IsFailure with Issues)
```

The pipeline steps that apply depend on the type being validated. A DTO with only attribute validation skips straight to step 1 and returns. An entity with `[AsyncValidate<T>]` bindings gets the full pipeline with change-aware filtering in step 3. The SG only generates the infrastructure that is declared -- there is no overhead for unused features.

### Where validation runs in the stack

Validation integrates at two levels in the Pragmatic architecture:

| Level | What is validated | When | Trigger |
|-------|-------------------|------|---------|
| **Input** (Level 1) | Request DTOs, action properties, endpoint body DTOs | Before business logic runs | `[Validate]` on DomainAction, endpoint pipeline, manual `IValidator<T>` call |
| **Entity** (Level 2) | Entity invariants after mutation | After property setters run, before persistence | `MutationInvoker` calls `IValidator<T>.ValidateAsync(entity, modifiedProperties)` |

Level 1 validation ensures malformed input never reaches your business logic. Level 2 validation ensures the entity is consistent before it is persisted, even when multiple mutations modify different properties.

---

## Core Types

### ValidationError

A `readonly struct` that implements `IError` (HTTP 400) and has Result-like semantics. It is both the "error type" for the Result pattern and the container for validation issues.

```csharp
// Valid (empty) state -- the starting point
ValidationError error = ValidationError.Valid;
error.IsSuccess  // true
error.IsFailure  // false
error.Count      // 0

// Single issue -- property-first syntax
var error = ValidationError.For("Email", "validation.required");
error.IsFailure  // true
error.Count      // 1

// Fluent accumulation -- immutable, returns new instance each time
var error = ValidationError.Valid
    .WithFor("Email", "validation.required")
    .WithFor("Name", "validation.minlength", ("min", 2));

// Collection expression syntax
ValidationError errors = [
    new ValidationIssue("validation.required", "Email"),
    new ValidationIssue("validation.minlength", "Name", ("min", 3))
];

// Combine two errors
var combined = errorA.Combine(errorB);

// Nested paths (for collection element validation)
var error = ValidationError.Valid
    .WithNested("Items", 0, "ProductId", "validation.required");
// PropertyPath = "Items[0].ProductId"

// Pattern matching
error.Match(
    onValid: () => "All good",
    onInvalid: issues => $"Failed: {issues.Count} issues");

// Convert to Result for use in service methods
VoidResult<ValidationError> result = error.ToResult();

// Implicit conversion to IError for Result<T, IError>
Result<User, IError> result = ValidationError.For("Email", "validation.required");
```

Key properties:
- `IsSuccess` / `IsFailure` -- Result-like boolean accessors
- `Issues` -- `IReadOnlyList<ValidationIssue>` containing all validation problems
- `Count` -- Number of issues
- `StatusCode` -- Always 400 (Bad Request)
- `Code` -- Always `"VALIDATION_ERROR"`

### ValidationIssue

A `readonly record struct` representing a single validation failure:

```csharp
public readonly record struct ValidationIssue
{
    public string MessageKey { get; }        // "validation.required"
    public string? PropertyPath { get; }     // "Email", "Items[0].Name", null for object-level
    public IReadOnlyDictionary<string, object>? Parameters { get; } // {"min": 3}
}

// Factory methods
var issue = ValidationIssue.For("Email", "validation.required");
var nested = ValidationIssue.ForNested("Items", 0, "ProductId", "validation.required");
var objectLevel = ValidationIssue.ForObject("validation.date_range_invalid");
```

Message keys follow the `validation.{attribute}` convention. On the wire the `errors` extension carries
the keys — what a client matches on, the same in every language — and, when the host localizes
ProblemDetails (`i18n.LocalizeProblemDetails()`), a `messages` extension beside it carries their words
in the caller's language, one for one. Parameters enable interpolation: `"Name must be at least {min} characters"`.

### Interfaces

| Interface | Implemented by | Purpose |
|-----------|---------------|---------|
| `ISyncValidator` | SG-generated partial class | Has `Validate()` and `Validate(IReadOnlySet<string>?)`. Fast, in-memory checks from attributes. |
| `IAsyncValidator<T>` | Your `[Validator]` class | Async validation requiring I/O (database, external services). |
| `IValidator<T>` | `CompositeValidator<T>` (SG-registered) | Combined sync + async. **Inject this in services.** |
| `IAsyncValidatorBindings<T>` | SG-generated from `[AsyncValidate<T>]` | Maps async validators to trigger properties for change-aware filtering. |

### CompositeValidator\<T>

The runtime orchestrator that combines sync and async validation:

1. Runs `ISyncValidator.Validate()` first (if the instance implements it).
2. If `FailFast` is enabled and sync failed, returns immediately.
3. Runs all `IAsyncValidator<T>` instances, filtered by `IAsyncValidatorBindings<T>` when available.
4. Combines all errors and returns a single `ValidationError`.

`CompositeValidator<T>` is auto-registered by the SG as `IValidator<T>` when a `[Validator]` class exists for the type. It emits structured logging via `[LoggerMessage]` and OpenTelemetry metrics and traces.

### ValidationOptions

```csharp
builder.Services.AddPragmaticValidation(options =>
{
    // Stop at the first error (default: false -- accumulate all errors)
    options.FailFast = true;

    // Note: IncludePropertyPath is obsolete and has no effect — nested paths
    // (e.g. "Items[0].ProductId") are always included.
});
```

---

## Validation Attributes

Attributes are the primary way to declare validation rules. They live in `Pragmatic.Validation.Attributes` and are processed by the SG at compile time.

### Design principles

1. **Null passes** -- All attributes except `[Required]` return valid for null values. Use `[Required]` to enforce non-null, then combine with format/range attributes.
2. **Composable** -- Stack multiple attributes on a property. The SG generates checks in declaration order.
3. **Localizable** -- Every attribute has a `DefaultMessageKey` (e.g., `validation.required`) and an optional `MessageKey` property to override it.
4. **Zero-allocation** -- Generated code uses `if` statements and direct comparisons, not attribute instances.

### Attribute categories

| Category | Attributes | Typical use |
|----------|-----------|-------------|
| **Presence** | `[Required]`, `[NotEmpty]`, `[NotWhiteSpace]` | Non-null, non-empty, non-whitespace |
| **String length** | `[MinLength]`, `[MaxLength]`, `[Length]` | Character count bounds |
| **Format** | `[Email]`, `[Phone]`, `[Url]`, `[CreditCard]`, `[Regex]`, `[Guid]` | String format validation |
| **Numeric** | `[Range]`, `[Positive]`, `[Negative]`, `[GreaterThan]`, `[LessThan]`, `[GreaterThanOrEqual]`, `[LessThanOrEqual]` | Number bounds |
| **Collection** | `[MinCount]`, `[MaxCount]`, `[Count]`, `[ValidateElements]` | Collection size and element validation |
| **Comparison** | `[EqualTo]`, `[NotEqualTo]`, `[GreaterThanProperty]`, `[LessThanProperty]` | Cross-property comparison |
| **Conditional** | `[RequiredIf]`, `[RequiredIfNot]` | Conditional presence based on another property |
| **Type-specific** | `[ValidEnum]`, `[OneOf]`, `[FutureDate]`, `[PastDate]` | Enum members, allowed values, date constraints |

For the full attribute reference with parameters, message keys, and examples, see [Attributes Reference](/modules/validation/attributes/).

### Custom sync attributes

Extend `ValidationAttribute` to create reusable sync validation:

```csharp
public sealed class FiscalCodeAttribute : ValidationAttribute
{
    public override string DefaultMessageKey => "validation.fiscalcode";

    public override bool IsValid(object? value)
    {
        if (value is not string s) return true; // null handled by [Required]
        return FiscalCodeValidator.IsValid(s);
    }
}
```

For cross-property custom attributes, override `RequiresInstance` and `IsValid(object?, object)`:

```csharp
public sealed class DateRangeAttribute : ValidationAttribute
{
    public string EndProperty { get; }

    public DateRangeAttribute(string endProperty) => EndProperty = endProperty;

    public override string DefaultMessageKey => "validation.daterange";
    public override bool RequiresInstance => true;

    public override bool IsValid(object? value, object instance)
    {
        if (value is not DateTime start) return true;
        // The generated validator implements IPropertyValueProvider — reflection-free sibling access.
        var end = ((IPropertyValueProvider)instance).GetPropertyValue(EndProperty);
        return end is DateTime endDate && start < endDate;
    }
}
```

When a type carries a custom attribute that overrides `IsValid(value, instance)`, the source generator
implements `IPropertyValueProvider` on the generated partial type, so the cast above always succeeds —
no reflection, no `PropertyAccessorCache`.

For the full custom validator guide, see [Custom Validators](/modules/validation/custom-validators/).

---

## Async Validation

Sync attributes cover format and presence checks. For validation that requires I/O -- database uniqueness, external API calls, file existence -- implement `IAsyncValidator<T>` and mark the class with `[Validator]`.

### Implementing an async validator

```csharp
[Validator]
public class CreateReservationValidator : IAsyncValidator<CreateReservationRequest>
{
    private readonly IReadRepository<RoomType> _roomTypes;

    public CreateReservationValidator(IReadRepository<RoomType> roomTypes)
        => _roomTypes = roomTypes;

    public async Task<ValidationError> ValidateAsync(
        CreateReservationRequest request,
        CancellationToken ct = default)
    {
        var roomType = await _roomTypes.GetByIdAsync(request.RoomTypeId, ct)
            .ConfigureAwait(false);

        if (roomType is null)
            return ValidationError.For("RoomTypeId", "validation.room_type.not_found");

        if (request.NumberOfGuests > roomType.MaxOccupancy)
            return ValidationError.For("NumberOfGuests",
                "validation.room_type.max_occupancy");

        return ValidationError.Valid;
    }
}
```

The `[Validator]` attribute tells the SG to:
1. Register `CreateReservationValidator` as `IAsyncValidator<CreateReservationRequest>` in DI (default lifetime: Scoped).
2. Register `CompositeValidator<CreateReservationRequest>` as `IValidator<CreateReservationRequest>`, combining sync attributes + async checks.

### Multiple validators per type

You can have multiple `[Validator]` classes for the same type. They are resolved as `IEnumerable<IAsyncValidator<T>>` and all run sequentially:

```csharp
[Validator]
public class EmailUniquenessValidator : IAsyncValidator<CreateUserRequest> { /* ... */ }

[Validator]
public class DomainBlocklistValidator : IAsyncValidator<CreateUserRequest> { /* ... */ }
// Both run. Errors from both are combined.
```

### Change-aware async validation with [AsyncValidate\<T>]

For entity validation, bind async validators to specific properties so they only fire when those properties change:

```csharp
public partial class User
{
    [AsyncValidate<EmailUniquenessValidator>]
    public string Email { get; private set; } = "";

    [AsyncValidate<UsernameAvailabilityValidator>]
    public string Username { get; private set; } = "";
}
```

- Modifying `Email` triggers `EmailUniquenessValidator` but not `UsernameAvailabilityValidator`.
- In create mode (`modifiedProperties = null`), all bound validators fire.
- Entity-level `[AsyncValidate<T>]` (on the class) fires on any property modification.

The SG generates `IAsyncValidatorBindings<User>` that `CompositeValidator<User>` uses to filter which validators to invoke.

---

## Integration with Other Modules

### Pragmatic.Actions

A `DomainAction` is validated by the pipeline's `ValidationFilter` before `Execute()` by default — no
attribute needed. `[Validate]` replaces the default with an explicit choice, `[NoValidation]` turns it off:

```csharp
[DomainAction]
public partial class CreateReservation : DomainAction<Reservation>
{
    [Required] [Email]
    public string Email { get; set; } = "";

    [Positive] [Range(1, 20)]
    public int NumberOfGuests { get; set; } = 1;

    public override async Task<Result<Reservation, IError>> Execute(CancellationToken ct)
    {
        // Validation has already passed by the time we get here
        // ...
    }
}
```

The `ValidationFilter` in the action pipeline:
1. Calls `ISyncValidator.Validate()` on the action instance (generated from attributes).
2. Calls `IAsyncValidator<T>.ValidateAsync()` when the compilation declares a `[Validator]` for the
   action or a nested property, or when `[Validate]` asks for async explicitly.
3. Returns the `ValidationError` as a 422 response if validation fails, short-circuiting `Execute()`.

### Pragmatic.Endpoints

Body DTOs generated by the Endpoints SG automatically get `ISyncValidator` validation. The Validation SG predicts which body DTOs the Endpoints SG will generate and adds sync validation attributes to them. When validation fails, the endpoint returns HTTP 422 with the `ValidationError` serialized as ProblemDetails.

### Pragmatic.Persistence (Entity Validation)

For `[Entity]` types, the SG generates a change-aware `Validate(IReadOnlySet<string>? modifiedProperties)` overload:

- When `modifiedProperties` is `null` (create mode), all properties are validated.
- When `modifiedProperties` is provided (update mode), only rules for modified properties run.
- Cross-property dependencies are tracked: if `CheckOut` has `[GreaterThanProperty(nameof(CheckIn))]`, modifying `CheckIn` also triggers re-validation of `CheckOut`.

The `MutationInvoker` calls `IValidator<TEntity>.ValidateAsync(entity, modifiedProperties)` after applying setters and before calling `SaveChangesAsync`.

### Pragmatic.Result

`ValidationError` implements `IError` with `StatusCode = 400`. You can return it directly from a `Result<T, IError>`:

```csharp
Result<User, IError> result = ValidationError.For("Email", "validation.required");
```

The implicit conversion from `ValidationError` to `IError` is built in.

---

## What Gets Generated

For each class with validation attributes, the source generator produces one or more files. The exact set depends on the type's characteristics.

| Generated File | Content | Condition |
|----------------|---------|-----------|
| `{Type}.Validator.g.cs` | `ISyncValidator` implementation with `Validate()` method | Type has validation attributes and is `partial` |
| `{Type}AsyncValidatorBindings.g.cs` | `IAsyncValidatorBindings<T>` implementation | Type has `[AsyncValidate<T>]` attributes |
| `_Infra.Validation.ValidatorRegistration.g.cs` | `AddGeneratedValidators()` extension method with DI registrations | At least one `[Validator]` class or `[AsyncValidate<T>]` binding exists |
| `_Metadata.Validation.g.cs` | `[assembly: PragmaticMetadata]` for cross-assembly discovery | `Pragmatic.Composition` is referenced |
| `{BodyDtoType}.Validator.g.cs` | `ISyncValidator` for endpoint body DTOs | Endpoint has validation attributes and `Pragmatic.Validation` is referenced |

### Example: what gets generated for a DTO

Given this input:

```csharp
public partial class RegisterGuestRequest
{
    [Required] [NotWhiteSpace]
    public string FirstName { get; init; } = "";

    [Required] [Email]
    public string Email { get; init; } = "";
}
```

The SG generates `RegisterGuestRequest.Validator.g.cs`:

```csharp
public partial class RegisterGuestRequest : ISyncValidator
{
    public ValidationError Validate()
    {
        var error = ValidationError.Valid;

        // Validate FirstName
        if (string.IsNullOrEmpty(FirstName))
            error = error.WithFor("FirstName", "validation.required");
        else
        {
            if (string.IsNullOrWhiteSpace(FirstName))
                error = error.WithFor("FirstName", "validation.notwhitespace");
        }

        // Validate Email
        if (string.IsNullOrEmpty(Email))
            error = error.WithFor("Email", "validation.required");
        else
        {
            if (!EmailRegex.IsMatch(Email))
                error = error.WithFor("Email", "validation.email");
        }

        return error;
    }
}
```

All generated files live under `obj/Debug/net10.0/generated/` and are fully visible in the IDE. You can set breakpoints in generated validation code.

### DI registration generation

When a `[Validator]` class exists, the SG generates a `ValidatorRegistrationExtensions` class:

```csharp
public static partial class ValidatorRegistrationExtensions
{
    public static IServiceCollection AddGeneratedValidators(this IServiceCollection services)
    {
        // Register async validator + CompositeValidator
        services.AddValidatorWithComposite<CreateReservationValidator, CreateReservationRequest>(
            ServiceLifetime.Scoped);

        // Register async validator bindings
        services.AddAsyncValidatorBindings<UserAsyncValidatorBindings, User>();

        return services;
    }
}
```

Call `services.AddGeneratedValidators()` in your `IStartupStep` to activate the registrations.

---

## Observability

`CompositeValidator<T>` emits OpenTelemetry metrics and traces:

| Instrument | Name | Type | Description |
|------------|------|------|-------------|
| Histogram | `pragmatic.validation.duration` | ms | Duration of validation execution |
| Counter | `pragmatic.validation.executions` | count | Total validation executions |
| Counter | `pragmatic.validation.failures` | count | Total validation failures |

Activity source: `Pragmatic.Validation` with activities named `Validate.{TypeName}`.

Tags on activities:
- `pragmatic.validation.type` -- The type name being validated
- `pragmatic.validation.issue_count` -- Number of issues (on failure)

Structured logging via `[LoggerMessage]` covers: validation start, sync pass/fail, async validator execution/skip/fail, completion.

---

## See Also

- [Getting Started](/modules/validation/getting-started/) -- Install and validate your first DTO in 5 minutes
- [Attributes Reference](/modules/validation/attributes/) -- Complete reference for all 30+ validation attributes
- [Custom Validators](/modules/validation/custom-validators/) -- Custom sync attributes, async validators, async bindings
- [Common Mistakes](/modules/validation/common-mistakes/) -- The 10 most frequent errors and how to fix them
- [Troubleshooting](/modules/validation/troubleshooting/) -- Problem/solution guide with diagnostic reference
