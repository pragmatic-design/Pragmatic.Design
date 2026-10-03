# Custom Validators

This guide covers how to build custom validation logic beyond the built-in attributes: custom sync attributes, async validators with database access, and change-aware async bindings.

---

## Custom Sync Validation Attributes

Extend `ValidationAttribute` to create reusable sync validation rules. The SG detects any `ValidationAttribute` subclass on properties and generates the corresponding `IsValid()` call in the `Validate()` method.

### Basic Custom Attribute

```csharp
using Pragmatic.Validation.Attributes;

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

Rules for `IsValid(object?)`:
- Return `true` for null values (leave null checks to `[Required]`).
- Return `true` for types that do not apply (e.g., non-string for a string-only attribute).
- The SG calls this method from the generated `Validate()` body.

### Custom Attribute with Parameters

```csharp
public sealed class ExactLengthAttribute : ValidationAttribute
{
    public ExactLengthAttribute(int length)
    {
        Ensure.Ensure.ThrowIfNegative(length);
        Length = length;
    }

    public int Length { get; }

    public override string DefaultMessageKey => "validation.exactlength";

    public override bool IsValid(object? value)
    {
        return value switch
        {
            null => true,
            string s => s.Length == Length,
            _ => true
        };
    }
}

// Usage:
[ExactLength(16)]
public string ApiKey { get; init; } = "";
```

### Cross-Property Custom Attribute

Override `RequiresInstance` and `IsValid(object?, object)` for attributes that need access to the parent object:

```csharp
public sealed class NotSameAsAttribute : ValidationAttribute
{
    public NotSameAsAttribute(string otherPropertyName)
    {
        Ensure.Ensure.ThrowIfNullOrEmpty(otherPropertyName);
        OtherPropertyName = otherPropertyName;
    }

    public string OtherPropertyName { get; }

    public override string DefaultMessageKey => "validation.notsame";
    public override bool RequiresInstance => true;

    public override bool IsValid(object? value) => true; // Requires instance

    public override bool IsValid(object? value, object instance)
    {
        if (value is null) return true;
        var otherValue = ((IPropertyValueProvider)instance).GetPropertyValue(OtherPropertyName);
        if (otherValue is null) return true;
        return !Equals(value, otherValue);
    }
}
```

When a type carries a custom attribute that overrides `IsValid(value, instance)`, the source generator
detects it and implements `IPropertyValueProvider` on the generated partial type. Reading a sibling
property is a `switch` over property names: **no reflection**. The cast to `IPropertyValueProvider`
always succeeds for an SG-generated (partial) type; for a non-partial type, implement the interface
yourself. (There is no `PropertyAccessorCache` type; that was a documentation error.)

### Custom Message Keys

Every attribute inherits `MessageKey` from `ValidationAttribute`:

```csharp
// The effective key is: MessageKey ?? DefaultMessageKey
[FiscalCode(MessageKey = "custom.it.fiscal_code_invalid")]
public string FiscalCode { get; init; } = "";
```

---

## Async Validators (IAsyncValidator\<T>)

For validation that requires I/O (database lookups, external API calls, file system access), implement `IAsyncValidator<T>`.

### Interface Definition

```csharp
public interface IAsyncValidator<in T>
{
    Task<ValidationError> ValidateAsync(T instance, CancellationToken ct = default);
}
```

### Implementing an Async Validator

```csharp
using Pragmatic.Validation;
using Pragmatic.Validation.Attributes;
using Pragmatic.Validation.Types;

[Validator]
public class CreateUserValidator : IAsyncValidator<CreateUserRequest>
{
    private readonly IReadRepository<User> _users;

    public CreateUserValidator(IReadRepository<User> users)
        => _users = users;

    public async Task<ValidationError> ValidateAsync(
        CreateUserRequest request,
        CancellationToken ct = default)
    {
        // Check email uniqueness in the database
        var exists = await _users.ExistsByEmailAsync(request.Email, ct)
            .ConfigureAwait(false);

        if (exists)
            return ValidationError.For("Email", "validation.email.exists");

        return ValidationError.Valid;
    }
}
```

### The [Validator] Attribute

The `[Validator]` attribute on a class tells the SG to:

1. **Register in DI**: The validator is registered as `IAsyncValidator<T>` with the specified lifetime.
2. **Generate CompositeValidator**: If the validated type (`T`) also has sync validation attributes, the SG registers `CompositeValidator<T>` as `IValidator<T>` to combine both.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Lifetime` | `ServiceLifetime` | `Scoped` | DI service lifetime for the validator. |

```csharp
// Default: Scoped
[Validator]
public class CreateUserValidator : IAsyncValidator<CreateUserRequest> { ... }

// Transient (no shared state)
[Validator(Lifetime = ServiceLifetime.Transient)]
public class StatelessValidator : IAsyncValidator<CreateUserRequest> { ... }

// Singleton (only for validators without scoped dependencies)
[Validator(Lifetime = ServiceLifetime.Singleton)]
public class ConfigValidator : IAsyncValidator<CreateUserRequest> { ... }
```

### Returning Errors

Use `ValidationError` factory methods to construct error results:

```csharp
public async Task<ValidationError> ValidateAsync(CreateOrderRequest req, CancellationToken ct)
{
    var error = ValidationError.Valid;

    // Single issue
    if (await IsInvalid1(req, ct))
        return ValidationError.For("ProductId", "validation.product.not_found");

    // Accumulate multiple issues
    if (await IsInvalid2(req, ct))
        error = error.WithFor("Quantity", "validation.stock.insufficient");

    if (await IsInvalid3(req, ct))
        error = error.WithFor("CouponCode", "validation.coupon.expired");

    return error;
}
```

### Multiple Async Validators for the Same Type

You can have multiple `[Validator]` classes for the same type. The DI container resolves them as `IEnumerable<IAsyncValidator<T>>`, and `CompositeValidator<T>` runs all of them:

```csharp
[Validator]
public class EmailUniquenessValidator : IAsyncValidator<CreateUserRequest>
{
    public async Task<ValidationError> ValidateAsync(CreateUserRequest req, CancellationToken ct)
    {
        // Check email uniqueness
    }
}

[Validator]
public class DomainBlocklistValidator : IAsyncValidator<CreateUserRequest>
{
    public async Task<ValidationError> ValidateAsync(CreateUserRequest req, CancellationToken ct)
    {
        // Check email domain against blocklist
    }
}
```

Both validators run sequentially. Errors from all validators are combined.

---

## Combined Validation: IValidator\<T>

`IValidator<T>` is the main interface for combined sync + async validation. It is implemented by `CompositeValidator<T>`, which is auto-registered by the SG.

### Interface Definition

```csharp
public interface IValidator<in T>
{
    Task<ValidationError> ValidateAsync(T instance, CancellationToken ct = default);

    Task<ValidationError> ValidateAsync(
        T instance,
        IReadOnlySet<string>? modifiedProperties,
        CancellationToken ct = default);
}
```

### Injecting IValidator\<T>

```csharp
public class UserService(IValidator<CreateUserRequest> validator)
{
    public async Task<Result<User, IError>> CreateAsync(
        CreateUserRequest request,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (validation.IsFailure)
            return validation; // ValidationError implements IError

        // ... create user
    }
}
```

### CompositeValidator\<T> Execution Order

1. **Sync validation** -- If the instance implements `ISyncValidator`, calls `Validate()` (or `Validate(modifiedProperties)` for change-aware).
2. **FailFast check** -- If `ValidationOptions.FailFast = true` and sync failed, returns immediately.
3. **Async validation** -- Runs all `IAsyncValidator<T>` instances. Filtered by `IAsyncValidatorBindings<T>` when available.
4. **Combine** -- Merges errors from both phases.

---

## Change-Aware Async Validation: [AsyncValidate\<T>]

For entity validation, you can bind async validators to specific properties so they only fire when those properties change. This avoids expensive database calls on unrelated updates.

### Property-Level Binding

```csharp
public partial class User
{
    [AsyncValidate<EmailUniquenessValidator>]
    public string Email { get; private set; } = "";

    [AsyncValidate<UsernameAvailabilityValidator>]
    public string Username { get; private set; } = "";
}
```

- Modifying `Email` triggers `EmailUniquenessValidator`.
- Modifying `Username` triggers `UsernameAvailabilityValidator`.
- Modifying `FirstName` triggers neither.

### Entity-Level Binding

```csharp
[AsyncValidate<ReservationConsistencyValidator>]
public partial class Reservation
{
    public DateTimeOffset CheckIn { get; private set; }
    public DateTimeOffset CheckOut { get; private set; }
}
```

`ReservationConsistencyValidator` fires on ANY property modification (it is not bound to a specific property).

### Create Mode

When `modifiedProperties` is `null` (create mode), all bound validators fire regardless of binding level. This ensures full validation on entity creation.

### What the SG Generates

For each entity type that has `[AsyncValidate<T>]` attributes, the SG generates a class implementing `IAsyncValidatorBindings<T>`:

```csharp
// Generated: IAsyncValidatorBindings<User>
internal sealed class UserAsyncValidatorBindings : IAsyncValidatorBindings<User>
{
    public bool ShouldInvoke(Type validatorType, IReadOnlySet<string>? modifiedProperties)
    {
        // Create mode: always invoke all
        if (modifiedProperties is null)
            return true;

        if (validatorType == typeof(EmailUniquenessValidator))
            return modifiedProperties.Contains("Email");

        if (validatorType == typeof(UsernameAvailabilityValidator))
            return modifiedProperties.Contains("Username");

        // Unknown validator: skip
        return false;
    }
}
```

This is auto-registered in DI as `IAsyncValidatorBindings<User>` and consumed by `CompositeValidator<User>`.

---

## Manual DI Registration

While the SG auto-registers validators marked with `[Validator]`, you can also register validators manually:

```csharp
// Register async validator + CompositeValidator as IValidator<T>
services.AddValidatorWithComposite<CreateUserValidator, CreateUserRequest>();

// Register async validator only (no CompositeValidator)
services.AddAsyncValidator<CreateUserValidator, CreateUserRequest>();

// Register a fully custom IValidator<T>
services.AddValidator<MyCustomValidator, MyType>();

// Register CompositeValidator for types with only sync validation (no async)
services.AddSyncOnlyValidator<CreateUserRequest>();

// Register async validator bindings manually
services.AddAsyncValidatorBindings<UserAsyncValidatorBindings, User>();
```

---

## Testing Custom Validators

### Unit Testing Sync Attributes

```csharp
public class FiscalCodeAttributeTests
{
    private readonly FiscalCodeAttribute _attribute = new();

    [Fact]
    public void IsValid_WithNull_ReturnsTrue()
    {
        _attribute.IsValid(null).Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithValidCode_ReturnsTrue()
    {
        _attribute.IsValid("RSSMRA85M01H501Z").Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithInvalidCode_ReturnsFalse()
    {
        _attribute.IsValid("INVALID").Should().BeFalse();
    }
}
```

### Unit Testing Async Validators

```csharp
public class CreateUserValidatorTests
{
    [Fact]
    public async Task ValidateAsync_WithExistingEmail_ReturnsFailure()
    {
        var repo = Substitute.For<IReadRepository<User>>();
        repo.ExistsByEmailAsync("existing@test.com", Arg.Any<CancellationToken>())
            .Returns(true);

        var validator = new CreateUserValidator(repo);
        var request = new CreateUserRequest { Email = "existing@test.com", Name = "Test" };

        var result = await validator.ValidateAsync(request);

        result.IsFailure.Should().BeTrue();
        result.Issues.Should().ContainSingle()
            .Which.PropertyPath.Should().Be("Email");
    }

    [Fact]
    public async Task ValidateAsync_WithNewEmail_ReturnsSuccess()
    {
        var repo = Substitute.For<IReadRepository<User>>();
        repo.ExistsByEmailAsync("new@test.com", Arg.Any<CancellationToken>())
            .Returns(false);

        var validator = new CreateUserValidator(repo);
        var request = new CreateUserRequest { Email = "new@test.com", Name = "Test" };

        var result = await validator.ValidateAsync(request);

        result.IsSuccess.Should().BeTrue();
    }
}
```

### Testing Generated Validation

Since the SG generates `Validate()` on your partial types, you can test them directly:

```csharp
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

        request.Validate().IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithInvalidEmail_ReturnsFailure()
    {
        var request = new RegisterGuestRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "not-an-email"
        };

        var result = request.Validate();
        result.IsFailure.Should().BeTrue();
    }
}
```

### Integration Testing via HTTP

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

## Patterns and Best Practices

### 1. Separate Sync and Async Concerns

Sync attributes handle format/presence checks. Async validators handle uniqueness/existence checks. Do not perform I/O in custom `ValidationAttribute` subclasses.

```csharp
// Sync: format checks via attributes
public partial class CreateUserRequest
{
    [Required] [Email]
    public string Email { get; init; } = "";
}

// Async: database checks via IAsyncValidator
[Validator]
public class CreateUserValidator : IAsyncValidator<CreateUserRequest>
{
    public async Task<ValidationError> ValidateAsync(CreateUserRequest req, CancellationToken ct)
    {
        // Database uniqueness check
    }
}
```

### 2. Return Early on Critical Failures

In async validators, return immediately for critical failures. Do not continue checking if a prerequisite is not met:

```csharp
public async Task<ValidationError> ValidateAsync(CreateOrderRequest req, CancellationToken ct)
{
    // Critical: product must exist
    var product = await _products.GetByIdAsync(req.ProductId, ct).ConfigureAwait(false);
    if (product is null)
        return ValidationError.For("ProductId", "validation.product.not_found");

    // Only check stock if product exists
    if (req.Quantity > product.Stock)
        return ValidationError.For("Quantity", "validation.stock.insufficient");

    return ValidationError.Valid;
}
```

### 3. Use FailFast for Performance

When you want to skip async validation if sync fails:

```csharp
builder.Services.AddPragmaticValidation(options =>
{
    options.FailFast = true;
});
```

This avoids expensive database calls when basic format checks already fail.

### 4. Bind Async Validators to Properties

For entity validation, always use `[AsyncValidate<T>]` to bind validators to trigger properties. This prevents running expensive checks on unrelated property updates:

```csharp
public partial class User
{
    [AsyncValidate<EmailUniquenessValidator>]
    public string Email { get; private set; } = "";
    // EmailUniquenessValidator only runs when Email changes
}
```

### 5. Accumulate All Errors

When multiple checks are independent, accumulate errors rather than returning on the first one:

```csharp
public async Task<ValidationError> ValidateAsync(ImportRequest req, CancellationToken ct)
{
    var error = ValidationError.Valid;

    if (!await _categories.ExistsAsync(req.CategoryId, ct))
        error = error.WithFor("CategoryId", "validation.category.not_found");

    if (!await _brands.ExistsAsync(req.BrandId, ct))
        error = error.WithFor("BrandId", "validation.brand.not_found");

    return error;
}
```
