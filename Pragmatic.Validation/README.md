# Pragmatic.Validation

Zero-allocation, source-generated validation for .NET 10.

## The Problem

Validation in .NET scatters across layers and styles. Inline `if` checks bury rules inside business
logic and get duplicated. `DataAnnotations` uses reflection on every call, can't do async, and handles
cross-property rules awkwardly. `FluentValidation` is expressive but runtime-only, and the validator
class lives apart from the model it validates. In all three, rules are scattered, reflection-based, or
disconnected from the model, and none integrates natively with a DomainAction pipeline or entity
change tracking.

## The Solution

Declare rules as attributes on the model. The generator produces the exact validation code at compile
time: no reflection, no runtime expression evaluation, no separate validator class.

```csharp
public partial class CreateReservationRequest
{
    [Required, NotWhiteSpace]
    public string GuestName { get; init; } = "";

    [Required, Email]
    public string Email { get; init; } = "";

    [FutureDate]
    public DateTimeOffset CheckIn { get; init; }

    [GreaterThanProperty(nameof(CheckIn))]
    public DateTimeOffset CheckOut { get; init; }

    [Positive, Range(1, 20)]
    public int NumberOfGuests { get; init; } = 1;
}
```

The generator emits `ISyncValidator.Validate()` with inline `if` statements. For database checks,
implement `IAsyncValidator<T>` with `[Validator]`: the generator wires up DI and a
`CompositeValidator<T>`. A `DomainAction`'s pipeline runs validation before `Execute()` by default;
`[Validate]` only changes that default and `[NoValidation]` switches it off.

## Two levels

- **Level 1 (input)**: validates input DTOs before any database access: sync attributes first, then
  async validators (uniqueness, external checks).
- **Level 2 (entity)**: validates entity invariants after a mutation is applied, before persistence;
  change-aware, so only modified properties are re-validated.

## Installation

```bash
dotnet add package Pragmatic.Validation
dotnet add package Pragmatic.SourceGenerator   # the analyzer that generates the validators
```

## Choosing the right attribute

All attributes live in `Pragmatic.Validation.Attributes`. Full details + semantics:
[Attribute Reference](docs/attributes.md).

| Category | Attributes | Use when |
|----------|-----------|----------|
| **Presence** | `[Required]`, `[NotEmpty]`, `[NotWhiteSpace]` | Value must exist / not be blank |
| **String** | `[MinLength]`, `[MaxLength]`, `[Length]`, `[Regex]`, `[Email]`, `[Url]`, `[Phone]`, `[CreditCard]`, `[Guid]` | String format or length |
| **Numeric** | `[Range]`, `[GreaterThan(OrEqual)]`, `[LessThan(OrEqual)]`, `[Positive]`, `[Negative]` | Constraining numbers |
| **Money** | `[PositiveMoney]`, `[NonNegativeMoney]`, `[SupportedCurrency]` | Amount sign / accepted currency |
| **Date** | `[FutureDate]`, `[PastDate]` | Date must be past/future |
| **Collection** | `[MinCount]`, `[MaxCount]`, `[Count]`, `[ValidateElements]` | Collection size / per-element |
| **Cross-property** | `[EqualTo]`, `[NotEqualTo]`, `[GreaterThan(OrEqual)Property]`, `[LessThan(OrEqual)Property]`, `[RequiredIf]`, `[RequiredIfNot]` | Compare / conditionally require |
| **Enum/Set** | `[ValidEnum]`, `[OneOf]` | Must be a defined enum member / allowed value |
| **Custom (sync)** | extend `ValidationAttribute` | Custom synchronous logic |
| **Async** | implement `IAsyncValidator<T>` + `[Validator]` | Database / external service checks |

## Quick Start

Decorate a `partial` class; the generator emits `Validate()`:

```csharp
using Pragmatic.Validation.Attributes;

public partial class RegisterGuestRequest
{
    [Required, NotWhiteSpace] public string FirstName { get; init; } = "";
    [Required, Email]         public string Email { get; init; } = "";
    [Range(18, 120)]          public int Age { get; init; }
}
```

```csharp
// Run it directly…
var result = request.Validate();

// …or let a DomainAction run L1+L2: it does by default, no attribute needed:
[DomainAction]
public partial class RegisterGuest : DomainAction<Guid> { /* ... */ }
```

Full walkthrough: [Getting Started](docs/getting-started.md).

## Status

**Stable** within 1.0.0-alpha: the attribute set, sync/async validators, and the L1/L2 pipeline are
settled. See the [roadmap](../docs/ROADMAP.md).

## Documentation

| Guide | What you'll learn |
|-------|-------------------|
| [Concepts](docs/concepts.md) | L1/L2 model, sync vs async, change-aware validation, decision guide |
| [Getting Started](docs/getting-started.md) | Your first validated DTO and the `[Validate]` pipeline |
| [Attribute Reference](docs/attributes.md) | Every built-in attribute, parameters, generated code, diagnostics |
| [Custom Validators](docs/custom-validators.md) | `IAsyncValidator<T>`, `[Validator]`, `CompositeValidator<T>`, custom `ValidationAttribute` |
| [Common Mistakes](docs/common-mistakes.md) | The most frequent validation pitfalls |
| [Troubleshooting](docs/troubleshooting.md) | Problem/solution guide with diagnostics |

## Requirements

- .NET 10.0+
- `Pragmatic.SourceGenerator` analyzer

## License

Part of the [Pragmatic.Design](../README.md) ecosystem. See [Licensing](../docs/LICENSING.md).
Pragmatic.Validation is **MIT-licensed**.
