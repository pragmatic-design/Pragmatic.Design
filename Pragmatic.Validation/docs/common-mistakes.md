# Common Mistakes

These are the most common issues developers encounter when using Pragmatic.Validation. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Forgetting `partial` on the Validated Type

**Wrong:**

```csharp
public class RegisterGuestRequest
{
    [Required]
    [Email]
    public string Email { get; init; } = "";

    [Required]
    [NotWhiteSpace]
    public string Name { get; init; } = "";
}
```

**Compile result:** `PRAG0200` error -- "Type 'RegisterGuestRequest' has validation attributes but is not declared as partial".

**Right:**

```csharp
public partial class RegisterGuestRequest
{
    [Required]
    [Email]
    public string Email { get; init; } = "";

    [Required]
    [NotWhiteSpace]
    public string Name { get; init; } = "";
}
```

**Why:** The source generator emits the `ISyncValidator.Validate()` method into a partial class. Without `partial`, the compiler cannot merge the generated validation code with your type. This is the most common mistake because the code compiles fine without `partial` -- the attributes are just ordinary .NET attributes. The `PRAG0200` diagnostic is the only signal that validation code was not generated.

---

## 2. Putting Validation Logic Inside the Action Instead of Using Attributes

**Wrong:**

```csharp
[DomainAction]
public partial class CreateReservation : DomainAction<Reservation>
{
    public DateTimeOffset CheckIn { get; set; }
    public DateTimeOffset CheckOut { get; set; }
    public int NumberOfGuests { get; set; }

    public override async Task<Result<Reservation, IError>> Execute(CancellationToken ct)
    {
        // Manual validation buried in business logic
        if (CheckIn < DateTimeOffset.UtcNow)
            return ValidationError.For("CheckIn", "Must be in the future");

        if (CheckOut <= CheckIn)
            return ValidationError.For("CheckOut", "Must be after check-in");

        if (NumberOfGuests is < 1 or > 20)
            return ValidationError.For("NumberOfGuests", "Must be between 1 and 20");

        // ... actual business logic starts here
    }
}
```

**Runtime result:** Works, but validation is duplicated if another action or service accepts the same properties. The validation is invisible to the pipeline -- no `ValidationFilter` runs, no observability metrics, no consistent error format.

**Right:**

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
        // Validation has already passed
        // ... pure business logic
    }
}
```

**Why:** Declarative attributes are processed by the SG into generated `Validate()` code, and the action pipeline's `ValidationFilter`, registered by default, runs it before `Execute()` and short-circuits with a 422 response on failure. This separates validation from business logic, enables reuse across actions that share properties, and feeds into observability (metrics, traces, structured logs).

---

## 3. Adding [Validate] to Switch Validation On

**Wrong:**

```csharp
[DomainAction]
[Validate]  // "otherwise the attributes are not checked"
public partial class CreateGuest : DomainAction<Guest>
{
    [Required, Email]
    public string Email { get; set; } = "";
}
```

**Runtime result:** Nothing breaks, but the attribute is not doing what the comment says, and it
silently changes something else. Validation is **on by default**: the `ValidationFilter` is registered
with the actions pipeline and runs the generated sync `Validate()` for every action that has rules.
Async validators run too when the compilation declares a `[Validator]` for the action or for a nested
property. `[Validate]` replaces that inference with an explicit choice: sync and async, whether or not a
validator exists.

**Right:**

```csharp
[DomainAction]  // sync rules run; a [Validator] for CreateGuest would run too
public partial class CreateGuest : DomainAction<Guest>
{
    [Required, Email]
    public string Email { get; set; } = "";
}
```

**Why:** Reach for `[Validate]` only to override the default: `[Validate(AsyncOnly = true)]` to skip the
sync rules, for instance. To switch validation off, use `[NoValidation]`.

---

## 4. Async Validation Without [Validator] Attribute

**Wrong:**

```csharp
// No [Validator] attribute -- not auto-registered
public class CreateUserValidator : IAsyncValidator<CreateUserRequest>
{
    private readonly IReadRepository<User> _users;

    public CreateUserValidator(IReadRepository<User> users)
        => _users = users;

    public async Task<ValidationError> ValidateAsync(
        CreateUserRequest request, CancellationToken ct = default)
    {
        if (await _users.ExistsByEmailAsync(request.Email, ct))
            return ValidationError.For("Email", "validation.email.exists");

        return ValidationError.Valid;
    }
}
```

**Runtime result:** The validator class compiles, but it is not registered in DI. `CompositeValidator<CreateUserRequest>` receives an empty `IEnumerable<IAsyncValidator<CreateUserRequest>>` and only runs sync validation. The email uniqueness check never executes.

**Right:**

```csharp
[Validator]  // Auto-registers in DI as IAsyncValidator<CreateUserRequest>
public class CreateUserValidator : IAsyncValidator<CreateUserRequest>
{
    private readonly IReadRepository<User> _users;

    public CreateUserValidator(IReadRepository<User> users)
        => _users = users;

    public async Task<ValidationError> ValidateAsync(
        CreateUserRequest request, CancellationToken ct = default)
    {
        if (await _users.ExistsByEmailAsync(request.Email, ct))
            return ValidationError.For("Email", "validation.email.exists");

        return ValidationError.Valid;
    }
}
```

**Why:** The `[Validator]` attribute tells the SG to generate DI registration for the validator. Without it, you must register manually via `services.AddValidatorWithComposite<CreateUserValidator, CreateUserRequest>()`. If you forget both `[Validator]` and manual registration, the async validator silently does not run.

---

## 5. [Validator] Class Not Implementing IAsyncValidator\<T>

**Wrong:**

```csharp
[Validator]
public class CreateUserValidator  // Missing IAsyncValidator<T>!
{
    public async Task<ValidationError> ValidateAsync(
        CreateUserRequest request, CancellationToken ct = default)
    {
        // ...
    }
}
```

**Compile result:** `PRAG0201` error -- "Class 'CreateUserValidator' has [Validator] attribute but does not implement IAsyncValidator<T>".

**Right:**

```csharp
[Validator]
public class CreateUserValidator : IAsyncValidator<CreateUserRequest>
{
    public async Task<ValidationError> ValidateAsync(
        CreateUserRequest request, CancellationToken ct = default)
    {
        // ...
    }
}
```

**Why:** The SG uses the `IAsyncValidator<T>` interface to determine the validated type (`T`). Without it, the SG cannot generate the correct DI registration. The method signature alone is not enough -- the interface provides the generic type parameter that connects the validator to its target type.

---

## 6. Throwing Exceptions Instead of Returning ValidationError

**Wrong:**

```csharp
[Validator]
public class CreateReservationValidator : IAsyncValidator<CreateReservationRequest>
{
    private readonly IReadRepository<RoomType> _roomTypes;

    public CreateReservationValidator(IReadRepository<RoomType> roomTypes)
        => _roomTypes = roomTypes;

    public async Task<ValidationError> ValidateAsync(
        CreateReservationRequest request, CancellationToken ct = default)
    {
        var roomType = await _roomTypes.GetByIdAsync(request.RoomTypeId, ct);

        if (roomType is null)
            throw new InvalidOperationException($"Room type {request.RoomTypeId} not found");

        if (request.NumberOfGuests > roomType.MaxOccupancy)
            throw new ArgumentException("Too many guests for this room type");

        return ValidationError.Valid;
    }
}
```

**Runtime result:** The exception propagates out of `CompositeValidator<T>`, bypasses the validation pipeline, and produces a 500 Internal Server Error. The observability metrics record an unhandled exception instead of a validation failure.

**Right:**

```csharp
[Validator]
public class CreateReservationValidator : IAsyncValidator<CreateReservationRequest>
{
    private readonly IReadRepository<RoomType> _roomTypes;

    public CreateReservationValidator(IReadRepository<RoomType> roomTypes)
        => _roomTypes = roomTypes;

    public async Task<ValidationError> ValidateAsync(
        CreateReservationRequest request, CancellationToken ct = default)
    {
        var roomType = await _roomTypes.GetByIdAsync(request.RoomTypeId, ct)
            .ConfigureAwait(false);

        if (roomType is null)
            return ValidationError.For("RoomTypeId", "validation.room_type.not_found");

        if (request.NumberOfGuests > roomType.MaxOccupancy)
            return ValidationError.For("NumberOfGuests", "validation.room_type.max_occupancy");

        return ValidationError.Valid;
    }
}
```

**Why:** Validation errors are expected outcomes, not exceptional conditions. The `ValidateAsync` method returns `ValidationError` -- use it. Return `ValidationError.For(...)` for failures and `ValidationError.Valid` for success. Exceptions bypass the entire validation pipeline, lose field-level error information, and always produce 500 instead of the correct 400.

---

## 7. Performing I/O in a Custom ValidationAttribute

**Wrong:**

```csharp
public sealed class UniqueEmailAttribute : ValidationAttribute
{
    public override string DefaultMessageKey => "validation.email.unique";

    public override bool IsValid(object? value)
    {
        if (value is not string email) return true;

        // BAD: blocking I/O in a sync validation attribute
        using var scope = ServiceLocator.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var exists = repo.ExistsByEmailAsync(email).GetAwaiter().GetResult();
        return !exists;
    }
}
```

**Runtime result:** The blocking `.GetAwaiter().GetResult()` can cause thread pool starvation and deadlocks. The Service Locator anti-pattern makes the code untestable. The validation attribute has no access to DI by design.

**Right:**

```csharp
// Sync attribute: format check only
// [Required] [Email] on the property

// Async validator: database check via DI
[Validator]
public class UniqueEmailValidator : IAsyncValidator<CreateUserRequest>
{
    private readonly IReadRepository<User> _users;

    public UniqueEmailValidator(IReadRepository<User> users)
        => _users = users;

    public async Task<ValidationError> ValidateAsync(
        CreateUserRequest request, CancellationToken ct = default)
    {
        if (await _users.ExistsByEmailAsync(request.Email, ct).ConfigureAwait(false))
            return ValidationError.For("Email", "validation.email.unique");

        return ValidationError.Valid;
    }
}
```

**Why:** The validation pipeline separates sync and async concerns by design. `ValidationAttribute.IsValid()` is a synchronous method called from the generated `Validate()` code. It has no access to DI, no CancellationToken, and no async context. Database lookups, external API calls, and any I/O must go in `IAsyncValidator<T>`, which receives dependencies via constructor injection and runs in the async phase of the pipeline.

---

## 8. [ValidateElements] on a Non-Collection Property

**Wrong:**

```csharp
public partial class OrderRequest
{
    [ValidateElements]
    public string Name { get; init; } = "";  // string, not a collection!
}
```

**Compile result:** `PRAG0204` warning -- "Property 'Name' has [ValidateElements] but is not a collection type". The attribute is silently ignored.

**Right:**

```csharp
public partial class OrderRequest
{
    [Required]
    [NotEmpty]
    [ValidateElements]
    public List<OrderItemRequest> Items { get; init; } = [];
}

public partial class OrderItemRequest
{
    [Required]
    public Guid ProductId { get; init; }

    [Range(1, 100)]
    public int Quantity { get; init; }
}
```

**Why:** `[ValidateElements]` tells the SG to iterate over collection elements and call `ISyncValidator.Validate()` on each. On a non-collection type, there are no elements to iterate. The element type must also be `partial` with validation attributes (implementing `ISyncValidator`), otherwise `PRAG0205` fires.

---

## 9. Collection Element Type Missing ISyncValidator

**Wrong:**

```csharp
public partial class PlaceOrderRequest
{
    [Required]
    [NotEmpty]
    [ValidateElements]
    public List<OrderItemRequest> Items { get; init; } = [];
}

// Not partial, no validation attributes
public class OrderItemRequest
{
    public Guid ProductId { get; init; }
    public int Quantity { get; init; }
}
```

**Compile result:** `PRAG0205` error -- "Element type 'OrderItemRequest' of property 'Items' does not implement ISyncValidator".

**Right:**

```csharp
public partial class PlaceOrderRequest
{
    [Required]
    [NotEmpty]
    [ValidateElements]
    public List<OrderItemRequest> Items { get; init; } = [];
}

public partial class OrderItemRequest  // partial + attributes
{
    [Required]
    public Guid ProductId { get; init; }

    [Range(1, 100)]
    public int Quantity { get; init; }
}
```

**Why:** `[ValidateElements]` delegates to each element's `Validate()` method. The element type must implement `ISyncValidator`, which the SG generates when the type is `partial` and has validation attributes. Without `partial` or without attributes, no `Validate()` is generated and the SG reports an error.

---

## 10. Referencing a Non-Existent Property in Comparison Attributes

**Wrong:**

```csharp
public partial class ChangePasswordRequest
{
    [Required]
    public string NewPassword { get; init; } = "";

    [EqualTo(nameof(ConfirmPassword))]  // Typo: should be "ConfirmPassword" but there is no such property
    public string Confirmation { get; init; } = "";
}
```

**Compile result:** `PRAG0203` error -- "Property 'ConfirmPassword' referenced by [EqualTo] on 'Confirmation' was not found".

**Right:**

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

**Why:** Comparison attributes (`[EqualTo]`, `[NotEqualTo]`, `[GreaterThanProperty]`, `[LessThanProperty]`) reference another property by name. The SG validates that the referenced property exists on the same type at compile time. Use `nameof()` to get compile-time safety on the property name itself.

Additionally, `PRAG0209` fires as a warning when the compared properties have incompatible types (e.g., comparing a `string` with an `int`).

---

## Quick Reference

| Mistake | Diagnostic / Symptom |
|---------|---------------------|
| Missing `partial` | `PRAG0200` compile error |
| Validation in action body | Works but duplicated, no pipeline, no observability |
| Missing `[Validate]` on action | Attributes exist but `ValidationFilter` does not run |
| Missing `[Validator]` on async validator | Validator not registered, silently skipped |
| `[Validator]` without `IAsyncValidator<T>` | `PRAG0201` compile error |
| Throwing exceptions in validator | 500 instead of 400, bypasses pipeline |
| I/O in sync `ValidationAttribute` | Thread starvation, deadlocks, no DI |
| `[ValidateElements]` on non-collection | `PRAG0204` warning, silently ignored |
| Element type missing `ISyncValidator` | `PRAG0205` compile error |
| Non-existent property in comparison | `PRAG0203` compile error |
