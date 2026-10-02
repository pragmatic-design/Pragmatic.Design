# Getting Started with Pragmatic.Validation

This guide walks you through adding validation to a Pragmatic application, from a simple DTO to a full DomainAction with async database checks.

## Prerequisites

- .NET 10.0+
- `Pragmatic.Validation` NuGet package
- `Pragmatic.SourceGenerator` referenced as an analyzer

```xml
<!-- In your .csproj -->
<PackageReference Include="Pragmatic.Validation" />
<ProjectReference Include="..\Pragmatic.SourceGenerator\...\Pragmatic.SourceGenerator.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

---

## Step 1: Validate a Request DTO

Create a `partial` class (or record) and decorate properties with validation attributes:

```csharp
using Pragmatic.Validation.Attributes;

public partial class RegisterGuestRequest
{
    [Required]
    [NotWhiteSpace]
    public string FirstName { get; init; } = "";

    [Required]
    [NotWhiteSpace]
    public string LastName { get; init; } = "";

    [Required]
    [Email]
    public string Email { get; init; } = "";

    [Phone]
    public string? Phone { get; init; }
}
```

The source generator detects the validation attributes and generates an `ISyncValidator.Validate()` method on `RegisterGuestRequest`. The type must be `partial` -- if it is not, you will get diagnostic `PRAG0200`.

### Call Validate() directly

```csharp
var request = new RegisterGuestRequest
{
    FirstName = "",
    LastName = "Doe",
    Email = "not-an-email"
};

var result = request.Validate();

if (result.IsFailure)
{
    foreach (var issue in result.Issues)
        Console.WriteLine($"{issue.PropertyPath}: {issue.MessageKey}");
    // Output:
    // FirstName: validation.required
    // FirstName: validation.notwhitespace
    // Email: validation.email
}
```

Key points:
- `result.IsSuccess` is `true` when there are no issues.
- `result.IsFailure` is `true` when there is at least one issue.
- `result.Issues` is an `IReadOnlyList<ValidationIssue>` with `MessageKey`, `PropertyPath`, and optional `Parameters`.
- Null values pass for all attributes except `[Required]`. Combine `[Required]` with other attributes to check both presence and format.

---

## Step 2: Add Cross-Property Validation

Use comparison attributes to validate relationships between properties:

```csharp
using Pragmatic.Validation.Attributes;

public partial class CreateReservationRequest
{
    public Guid GuestId { get; init; }
    public Guid PropertyId { get; init; }
    public Guid RoomTypeId { get; init; }

    [FutureDate]
    public DateTimeOffset CheckIn { get; init; }

    [GreaterThanProperty(nameof(CheckIn))]
    public DateTimeOffset CheckOut { get; init; }

    [Positive]
    [Range(1, 20)]
    public int NumberOfGuests { get; init; } = 1;
}
```

This validates:
- `CheckIn` must be in the future.
- `CheckOut` must be after `CheckIn`.
- `NumberOfGuests` must be between 1 and 20 (inclusive) and positive.

The SG tracks cross-property dependencies. On entities, modifying `CheckIn` also triggers re-validation of `CheckOut`.

---

## Step 3: Add an Async Validator

For checks that require database lookups or external services, implement `IAsyncValidator<T>`:

```csharp
using Pragmatic.Validation;
using Pragmatic.Validation.Attributes;
using Pragmatic.Validation.Types;

[Validator]
public class CreateReservationValidator : IAsyncValidator<CreateReservationRequest>
{
    private readonly IReadRepository<RoomType> _roomTypes;
    private readonly IReadRepository<Reservation> _reservations;

    public CreateReservationValidator(
        IReadRepository<RoomType> roomTypes,
        IReadRepository<Reservation> reservations)
    {
        _roomTypes = roomTypes;
        _reservations = reservations;
    }

    public async Task<ValidationError> ValidateAsync(
        CreateReservationRequest request,
        CancellationToken ct = default)
    {
        // Check room type exists
        var roomType = await _roomTypes.GetByIdAsync(request.RoomTypeId, ct)
            .ConfigureAwait(false);

        if (roomType is null)
            return ValidationError.For("RoomTypeId", "validation.room_type.not_found");

        // Check occupancy
        if (request.NumberOfGuests > roomType.MaxOccupancy)
            return ValidationError.For("NumberOfGuests",
                "validation.room_type.max_occupancy");

        return ValidationError.Valid;
    }
}
```

The `[Validator]` attribute tells the SG to:
1. Register `CreateReservationValidator` as `IAsyncValidator<CreateReservationRequest>` in DI.
2. Register `CompositeValidator<CreateReservationRequest>` as `IValidator<CreateReservationRequest>`.

The `CompositeValidator` runs sync validation first (attribute checks), then async validation (database checks).

---

## Step 4: Register Validation Services

In your `IStartupStep` or `Program.cs`:

```csharp
builder.Services.AddPragmaticValidation();
```

With options:

```csharp
builder.Services.AddPragmaticValidation(options =>
{
    options.FailFast = true; // Stop at first error (default: false)
});
```

The SG generates additional registration extension methods for discovered validators. Call them in your startup:

```csharp
// Generated by SG:
services.AddGeneratedValidators();
```

---

## Step 5: Use in a DomainAction

A DomainAction is validated automatically by the pipeline's `ValidationFilter` — no attribute needed:

```csharp
[DomainAction]
public partial class CreateReservation : DomainAction<Reservation>
{
    [FutureDate]
    public DateTimeOffset CheckIn { get; set; }

    [GreaterThanProperty(nameof(CheckIn))]
    public DateTimeOffset CheckOut { get; set; }

    [Positive]
    [Range(1, 20)]
    public int NumberOfGuests { get; set; } = 1;

    public override async Task<Result<Reservation, IError>> Execute(CancellationToken ct)
    {
        // Validation has already passed by the time we get here
        // ...
    }
}
```

The `ValidationFilter` in the action pipeline:
1. Calls `ISyncValidator.Validate()` on the action (if it has validation attributes).
2. Calls `IAsyncValidator<T>.ValidateAsync()` when a `[Validator]` for the action (or a nested property)
   is declared in the compilation.
3. Returns the `ValidationError` as a 422 response if validation fails.

`[Validate]` overrides that default explicitly (for instance `[Validate(AsyncOnly = true)]`), and
`[NoValidation]` switches it off.

---

## Step 6: Inject IValidator\<T> in Services

For manual validation in services (outside the action pipeline):

```csharp
public class ReservationService(IValidator<CreateReservationRequest> validator)
{
    public async Task<Result<Reservation, IError>> CreateAsync(
        CreateReservationRequest request,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (validation.IsFailure)
            return validation; // ValidationError implements IError

        // ... create reservation
    }
}
```

`IValidator<T>` runs both sync (attributes) and async (database) validation in the correct order.

---

## Step 7: Write Tests

### Unit Tests (Sync Validation)

```csharp
using Pragmatic.Testing.Assertions;

public class RegisterGuestValidationTests
{
    [Fact]
    public void Validate_WithValidRequest_ReturnsSuccess()
    {
        var request = new RegisterGuestRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com"
        };

        var result = request.Validate();

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithEmptyEmail_ReturnsFailure()
    {
        var request = new RegisterGuestRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = ""
        };

        var result = request.Validate();

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithMultipleErrors_ReportsAll()
    {
        var request = new RegisterGuestRequest
        {
            FirstName = "",
            LastName = "",
            Email = ""
        };

        var result = request.Validate();

        result.IsFailure.Should().BeTrue();
        result.Count.Should().BeGreaterThan(1);
    }
}
```

### Integration Tests (HTTP Endpoints)

```csharp
[Fact]
public async Task CreateGuest_InvalidEmail_Returns400()
{
    var body = new
    {
        firstName = "Val",
        lastName = "Test",
        email = "not-an-email"
    };

    var response = await PostAsync("/api/v1/guests", body);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
}
```

---

## Common Patterns

### Accumulating Multiple Errors

`ValidationError` is immutable. Use `WithFor()` to accumulate errors:

```csharp
var error = ValidationError.Valid
    .WithFor("Email", "validation.required")
    .WithFor("Name", "validation.minlength", ("min", 2));

// Or combine two ValidationError instances
var combined = errorA.Combine(errorB);
```

### Collection Validation

Validate each element in a collection by marking the element type as `partial` with attributes, then using `[ValidateElements]`:

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
```

Errors include the index: `Items[0].ProductId`, `Items[1].Quantity`.

### Conditional Validation

Use `[RequiredIf]` to make fields conditionally required:

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

---

## Next Steps

- [Attributes Reference](attributes.md) -- Complete list of all 30+ validation attributes.
- [Custom Validators](custom-validators.md) -- Build custom sync and async validators.
