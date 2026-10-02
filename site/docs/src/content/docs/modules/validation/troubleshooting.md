---
title: "Troubleshooting"
description: "Practical problem/solution guide for Pragmatic.Validation. Each section covers a common issue, the likely causes, and the fix."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Validation/docs/troubleshooting.md
sidebar:
  order: 6
---
Practical problem/solution guide for Pragmatic.Validation. Each section covers a common issue, the likely causes, and the fix.

---

## Validation Not Running (Invalid Input Reaches Execute)

Your type has validation attributes, but invalid input is not rejected.

### Checklist

1. **Is the type `partial`?** The SG cannot generate `Validate()` without `partial`. You will see diagnostic PRAG0200 if this is missing.

2. **Is `Pragmatic.Validation` referenced in your project?** The Validation SG only runs when the package is present.

3. **Is the SG analyzer referenced correctly?** In your `.csproj`, the Pragmatic.SourceGenerator must be referenced with `OutputItemType="Analyzer"`:

   ```xml
   <ProjectReference Include="..\Pragmatic.SourceGenerator\Pragmatic.SourceGenerator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
   ```

4. **For DomainAction endpoints: is `[Validate]` present?** Validation attributes alone cause the SG to generate `Validate()`, but the `ValidationFilter` only runs when `[Validate]` is on the action class.

   ```csharp
   [DomainAction]
   [Validate]  // Required to activate ValidationFilter
   public partial class CreateGuest : DomainAction<Guest> { /* ... */ }
   ```

5. **For manual service calls: are you calling `IValidator<T>.ValidateAsync()`?** Outside the action pipeline, you must call validation explicitly:

   ```csharp
   var validation = await validator.ValidateAsync(request, ct);
   if (validation.IsFailure)
       return validation;
   ```

6. **Is `AddPragmaticValidation()` called in your startup?** Without it, `ValidationOptions` are not registered.

---

## Async Validator Not Running

The sync validation (attributes) works, but your `IAsyncValidator<T>` is never called.

### Checklist

1. **Does the validator class have `[Validator]`?** Without it, the SG does not generate DI registration. Add `[Validator]` or register manually:

   ```csharp
   services.AddValidatorWithComposite<CreateUserValidator, CreateUserRequest>();
   ```

2. **Does the validator implement `IAsyncValidator<T>`?** The `[Validator]` attribute requires the class to implement `IAsyncValidator<T>`. Diagnostic PRAG0201 fires if this is missing.

3. **Is `AddGeneratedValidators()` called?** The SG generates a `ValidatorRegistrationExtensions.AddGeneratedValidators()` method. Call it in your `IStartupStep`:

   ```csharp
   services.AddGeneratedValidators();
   ```

4. **For change-aware entity validation: is the property bound?** If you use `[AsyncValidate<T>]` and the modified property is not in the binding, the validator is skipped. Check that:
   - Property-level `[AsyncValidate<T>]` is on the correct property.
   - Entity-level `[AsyncValidate<T>]` is on the class (fires on any modification).
   - In create mode (`modifiedProperties = null`), all bound validators fire.

5. **Is FailFast enabled and sync validation failing?** When `ValidationOptions.FailFast = true`, sync failure short-circuits the pipeline before async validators run. Check the sync errors first.

---

## Cross-Property Validation Failing at Compile Time

The build fails with PRAG0203 or PRAG0209 when using comparison attributes.

### PRAG0203: "Property not found"

The referenced property does not exist on the same type:

```csharp
// WRONG: "Confirm" does not exist
[EqualTo(nameof(Confirm))]
public string ConfirmPassword { get; init; } = "";
```

**Fix:** Use `nameof()` on the correct property name. Verify the referenced property is a public property on the same type.

### PRAG0209: "Incompatible types"

The compared properties have different types:

```csharp
[GreaterThanProperty(nameof(MinValue))]
public decimal MaxValue { get; init; }  // decimal

public int MinValue { get; init; }  // int -- different type
```

**Fix:** Ensure both properties are the same type, or at least types that implement `IComparable` compatibly. The SG warns when the fully-qualified type names differ.

---

## Collection Element Validation Not Working

`[ValidateElements]` is present but element errors are not reported.

### Checklist

1. **Is the property a collection type?** `[ValidateElements]` only works on `List<T>`, `T[]`, `IEnumerable<T>`, `IReadOnlyList<T>`, etc. Diagnostic PRAG0204 warns if applied to a non-collection.

2. **Is the element type `partial` with validation attributes?** The element type must implement `ISyncValidator`. Without `partial` or without attributes, the SG reports PRAG0205.

   ```csharp
   // Element type must be partial with attributes
   public partial class OrderItemRequest
   {
       [Required]
       public Guid ProductId { get; init; }

       [Range(1, 100)]
       public int Quantity { get; init; }
   }
   ```

3. **Is `[ValidateElements]` combined with `[Required]` and `[NotEmpty]`?** `[ValidateElements]` only validates existing elements. It does not check for null or empty collections. Combine with `[Required]` (non-null) and `[NotEmpty]` or `[MinCount(1)]` (at least one element).

---

## Generated Code Not Compiling

The build fails with PRAG02xx diagnostics from the source generator.

### Diagnostics Reference

| ID | Severity | Cause | Fix |
|----|----------|-------|-----|
| PRAG0200 | Error | Type with validation attributes is not `partial` | Add `partial` keyword to the type declaration |
| PRAG0201 | Error | `[Validator]` class does not implement `IValidator<T>` | Add `: IValidator<YourType>` to the class |
| PRAG0203 | Error | Referenced property in comparison attribute not found | Fix the property name in `[EqualTo]`, `[GreaterThanProperty]`, etc. Use `nameof()` |
| PRAG0204 | Warning | `[ValidateElements]` on a non-collection type | Move `[ValidateElements]` to a collection property (`List<T>`, `T[]`, etc.) |
| PRAG0205 | Error | Collection element type does not implement `ISyncValidator` | Make the element type `partial` and add validation attributes |
| PRAG0209 | Warning | Incompatible types in comparison attributes | Use the same type for both compared properties |

Check the **Error List** window in Visual Studio or the build output for diagnostic details and the affected source location.

---

## Validation Returns Empty Error (IsSuccess = true) Unexpectedly

The `Validate()` method returns `ValidationError.Valid` even though you expect failures.

### Possible Causes

1. **Null values passing all attributes.** All attributes except `[Required]` return `true` for null values. If a nullable property is `null`, only `[Required]` will catch it:

   ```csharp
   // WRONG: [Email] passes for null
   [Email]
   public string? Email { get; init; }

   // RIGHT: [Required] catches null, [Email] checks format
   [Required]
   [Email]
   public string? Email { get; init; }
   ```

2. **Change-aware validation with wrong modifiedProperties.** On entity types, `Validate(modifiedProperties)` only checks properties in the set. If the set is empty, nothing is validated. Verify the set contains the expected property names.

3. **Wrong instance being validated.** Ensure you call `Validate()` on the actual instance with the invalid data, not a default-constructed instance.

---

## ValidationError Not Converting to HTTP 400

The endpoint returns 500 instead of 400 when validation fails.

### Possible Causes

1. **Throwing instead of returning.** If your async validator throws an exception instead of returning `ValidationError`, the exception bypasses the pipeline:

   ```csharp
   // WRONG: throws
   if (invalid) throw new InvalidOperationException("bad data");

   // RIGHT: returns error
   if (invalid) return ValidationError.For("Field", "validation.error_key");
   ```

2. **IValidator<T> not registered.** If `CompositeValidator<T>` is not registered, DI resolution fails at runtime. Verify `AddGeneratedValidators()` is called.

3. **Missing Pragmatic.Validation reference in the host project.** The host project must reference `Pragmatic.Validation` for the validation pipeline to work.

---

## FAQ

### Can I use Pragmatic.Validation without the source generator?

Yes, but with reduced functionality. You can implement `IAsyncValidator<T>` manually and register it in DI. However, you lose the auto-generated `ISyncValidator.Validate()` from attributes, change-aware validation, body DTO validation, and auto-registration.

### How do I validate the same DTO with different rules in different contexts?

Use `IAsyncValidator<T>` for context-specific rules. The sync attributes provide baseline validation (format, presence), and the async validator can apply context-specific logic based on injected services.

### Can I run validation without DI (e.g., in unit tests)?

Yes. The generated `ISyncValidator.Validate()` method is instance-based and has no DI dependencies:

```csharp
var request = new CreateUserRequest { Email = "" };
var result = request.Validate();
result.IsFailure.Should().BeTrue();
```

For testing async validators, construct them with mock dependencies directly.

### How do I customize the error message for a built-in attribute?

Set the `MessageKey` property on any attribute:

```csharp
[Required(MessageKey = "custom.user.name_required")]
public string Name { get; init; } = "";
```

The key is resolved by `Pragmatic.Internationalization` at the serialization boundary.

### Does validation run on PATCH/partial update requests?

For entity mutations (update mode), the SG generates a change-aware `Validate(IReadOnlySet<string>? modifiedProperties)` that only validates properties in the modified set. Cross-property dependencies are tracked automatically -- modifying `CheckIn` also re-validates `CheckOut` if `[GreaterThanProperty(nameof(CheckIn))]` is on `CheckOut`.

### How do I see what validation code was generated?

Inspect the generated files under `obj/Debug/net10.0/generated/` in your project. In Visual Studio, expand **Dependencies > Analyzers > Pragmatic.SourceGenerator** in Solution Explorer. Look for `{TypeName}.Validator.g.cs` files.

---

## Getting Help

- **GitHub Issues**: [github.com/pragmatic-design/Pragmatic.Design/issues](https://github.com/pragmatic-design/Pragmatic.Design/issues)
- **Showcase Examples**: See the `Showcase` project for working validation implementations across DTOs, actions, and entities.
- **Attributes Reference**: See [attributes.md](/modules/validation/attributes/) for all validation attributes with parameters and examples.
- **Custom Validators**: See [custom-validators.md](/modules/validation/custom-validators/) for sync and async validator patterns.
- **Concepts**: See [concepts.md](/modules/validation/concepts/) for the architecture overview and pipeline design.
