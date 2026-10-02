---
title: "Common Mistakes"
description: "These are the most common issues developers encounter when using Pragmatic.Patch. Each section shows the wrong approach, the correct approach, and explains why."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Patch/docs/common-mistakes.md
sidebar:
  order: 4
---
These are the most common issues developers encounter when using Pragmatic.Patch. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Forgetting `partial` on the Patch Record

**Wrong:**

```csharp
[GeneratePatch<Guest>]
public record UpdateGuestPatch;
```

**Compile result:** `PRAG2200` error -- "[GeneratePatch] must be applied to a partial class or record".

**Right:**

```csharp
[GeneratePatch<Guest>]
public partial record UpdateGuestPatch;
```

**Why:** The source generator emits `Optional<T>` properties, `ApplyTo()`, `ModifiedProperties`, and a JSON converter into a partial class. Without `partial`, the compiler cannot merge the generated code with your declaration.

---

## 2. Confusing Null and Undefined

**Wrong:**

```csharp
// Treating null the same as "not sent"
if (patch.Email.Value is null)
{
    // Skip -- assume the client didn't send it
}
```

**Right:**

```csharp
// Check IsUndefined for "not sent"
if (patch.Email.IsUndefined)
{
    // Client did not include "email" in the JSON body -- skip
}

// Check HasValue + null for "clear the value"
if (patch.Email.HasValue && patch.Email.Value is null)
{
    // Client explicitly sent { "email": null } -- clear the value
}
```

**Why:** `Optional<T>` distinguishes three states. `null` means the client explicitly sent `null` (meaning "clear this field"). `IsUndefined` means the field was absent from the JSON body (meaning "don't touch this field"). Treating them the same defeats the purpose of tri-state semantics.

---

## 3. Accessing Value Without Checking HasValue

**Wrong:**

```csharp
var email = patch.Email.Value; // Throws if undefined!
logger.LogInformation("New email: {Email}", email);
```

**Right:**

```csharp
if (patch.Email.HasValue)
{
    logger.LogInformation("New email: {Email}", patch.Email.Value);
}

// Or use GetValueOrDefault
var email = patch.Email.GetValueOrDefault("unknown");

// Or use IfPresent
patch.Email.IfPresent(email =>
    logger.LogInformation("New email: {Email}", email));
```

**Why:** Accessing `.Value` on an undefined `Optional<T>` throws `InvalidOperationException`. Always check `HasValue` first, or use `GetValueOrDefault()` or `IfPresent()` for safe access.

---

## 4. Using Optional\<T\> Without Registering the JSON Converter

**Wrong:**

```csharp
// Manual DTO with Optional<T> -- no JSON converter registered
public class ManualPatchDto
{
    public Optional<string> Name { get; init; }   // Always Undefined after deserialization!
    public Optional<decimal> Price { get; init; }  // Always Undefined!
}
```

**Right (Option A): Use the source generator:**

```csharp
[GeneratePatch<Product>]
public partial record PatchProduct; // SG generates the converter automatically
```

**Right (Option B): Register a typed converter for each `Optional<T>` you use:**

```csharp
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new OptionalConverter<string>());
    options.SerializerOptions.Converters.Add(new OptionalConverter<decimal>());
});
```

**Why:** Without a JSON converter, `System.Text.Json` does not know how to deserialize `Optional<T>`. The struct has no public constructor that matches the JSON shape, so all properties remain `default` (Undefined). The SG-generated patch types include a `[JsonConverter]` attribute automatically. If you use `Optional<T>` in a manually-written DTO, register an `OptionalConverter<T>` (`Pragmatic.Patch.Serialization`) for each `T`. There is no open-generic factory: it would need `MakeGenericType`. `OptionalConverter<T>` is itself annotated `[RequiresDynamicCode]`, so under Native AOT use a generated patch type.

---

## 5. Expecting Null to Work for Non-Nullable Value Types

**Wrong:**

```csharp
// Expecting this to set Price to Optional.Null
// JSON: { "price": null }
if (patch.Price.HasValue && patch.Price.Value is null)
{
    // This branch never executes -- Price is Optional<decimal>, not Optional<decimal?>
}
```

**Right:**

```csharp
// For non-nullable value types, null in JSON means Undefined (not sent)
// JSON: { "price": null } => Price = Optional<decimal>.Undefined
// JSON: { "price": 0 }    => Price = Optional.Of(0m) (explicit zero)

// If you need nullable semantics, use a nullable entity property:
public class Product
{
    public decimal? DiscountRate { get; set; }  // Nullable -- Optional<decimal?> supports null
}
```

**Why:** `decimal` cannot be `null`. When the JSON contains `"price": null`, the converter cannot create an `Optional<decimal>.Null` because `decimal` has no null representation. Instead, it treats this as `Undefined`. If you need to allow null values, make the entity property nullable (`decimal?`), which generates `Optional<decimal?>` in the patch DTO.

---

## 6. Manually Applying Properties Instead of Using ApplyTo

**Wrong:**

```csharp
// Manually checking and applying every field
if (patch.FirstName.HasValue) guest.SetFirstName(patch.FirstName.Value!);
if (patch.LastName.HasValue) guest.SetLastName(patch.LastName.Value!);
if (patch.Email.HasValue) guest.SetEmail(patch.Email.Value);
if (patch.Phone.HasValue) guest.SetPhone(patch.Phone.Value);
if (patch.LoyaltyPoints.HasValue) guest.SetLoyaltyPoints(patch.LoyaltyPoints.Value);
```

**Right:**

```csharp
patch.ApplyTo(guest);
```

**Why:** The generated `ApplyTo()` method does exactly the same thing, but it stays in sync with the entity automatically. When you add a new property to the entity, the SG regenerates `ApplyTo()` to include it. Manual application requires updating the code every time the entity changes -- and forgetting to add a new property is a silent bug.

Use manual application only when you need conditional logic per field (e.g., validation before apply):

```csharp
if (patch.Email.HasValue && patch.Email.Value is not null)
{
    if (!IsValidEmail(patch.Email.Value))
        return ValidationError.For("Email", "validation.email");
}
patch.ApplyTo(guest);
```

---

## 7. Applying a Patch to the Wrong Entity Type

**Wrong:**

```csharp
[GeneratePatch<Guest>]
public partial record UpdateGuestPatch;

// Trying to apply to a different entity
var product = await db.Products.FindAsync(id);
patch.ApplyTo(product); // Compile error -- wrong type
```

**Right:**

```csharp
var guest = await db.Guests.FindAsync(id);
patch.ApplyTo(guest); // Correct -- Guest matches [GeneratePatch<Guest>]
```

**Why:** The generated `ApplyTo()` method is strongly typed to the entity specified in `[GeneratePatch<TEntity>]`. This is by design -- it prevents applying a guest patch to a product. If you need to patch multiple entity types, create separate patch DTOs for each.

---

## 8. Expecting Excluded Properties in the Patch

**Wrong:**

```csharp
public class Guest
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }  // Auditing property
    public bool IsDeleted { get; set; }       // Soft delete property
}

[GeneratePatch<Guest>]
public partial record UpdateGuestPatch;

// Expecting these to exist -- they don't
patch.Id           // Compile error -- Id is excluded
patch.CreatedAt    // Compile error -- auditing properties are excluded
patch.IsDeleted    // Compile error -- soft delete properties are excluded
```

**Right:**

```csharp
// Only Name is generated (the only patchable property)
if (patch.Name.HasValue)
    logger.LogInformation("Name changed to {Name}", patch.Name.Value);
```

**Why:** The generator automatically excludes properties that should never be modified through a PATCH request: identity (`Id`, `PersistenceId`), auditing (`CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`), soft delete (`IsDeleted`, `DeletedAt`, `DeletedBy`), concurrency (`RowVersion`), collections, and entity references. These are managed by the framework, not by API consumers.

---

## 9. Not Checking ModifiedProperties for Audit or Conditional Logic

**Wrong:**

```csharp
// Logging all fields regardless of whether they changed
patch.ApplyTo(guest);
logger.LogInformation("Guest updated: Name={Name}, Email={Email}",
    guest.FirstName, guest.Email);
```

**Right:**

```csharp
// Log only what actually changed
var modified = patch.ModifiedProperties;
patch.ApplyTo(guest);

foreach (var prop in modified)
{
    logger.LogInformation("Guest {Id}: {Property} was modified", guest.Id, prop);
}
```

**Why:** `ModifiedProperties` returns an `IReadOnlySet<string>` of property names that were present in the JSON input. This is valuable for audit logging, conditional business logic (e.g., "send a notification only if the email changed"), and optimized persistence (only update changed columns).

---

## 10. Using JsonPatchDocument Instead of GeneratePatch

**Wrong:**

```csharp
// Loses type safety, requires the client to know JSON Patch operations
app.MapPatch("/products/{id}", (Guid id, JsonPatchDocument<Product> patch, AppDbContext db) =>
{
    var product = db.Products.Find(id);
    patch.ApplyTo(product); // No compile-time validation of operation paths
    db.SaveChanges();
    return Results.Ok(product);
});
```

**Right:**

```csharp
[GeneratePatch<Product>]
public partial record PatchProduct;

app.MapPatch("/products/{id}", async (Guid id, PatchProduct patch, AppDbContext db) =>
{
    var product = await db.Products.FindAsync(id);
    if (product is null) return Results.NotFound();

    patch.ApplyTo(product);
    await db.SaveChangesAsync();
    return Results.Ok(product);
});
```

**Why:** `JsonPatchDocument<T>` requires the client to send JSON Patch operations (`op`, `path`, `value`), which is more complex for API consumers. It also bypasses entity encapsulation -- operations can target any property path, including those that should not be modified. `[GeneratePatch<T>]` generates a type-safe DTO with excluded properties, encapsulated setters, and a simple JSON body that any client can produce.
