---
title: "Architecture and Core Concepts"
description: "This guide explains **why** Pragmatic.Patch exists, how `Optional<T>` solves the tri-state problem, and how the source generator turns a one-line declaration in"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Patch/docs/concepts.md
sidebar:
  order: 1
---
This guide explains **why** Pragmatic.Patch exists, how `Optional<T>` solves the tri-state problem, and how the source generator turns a one-line declaration into a complete PATCH DTO. Read this before diving into the individual feature guides.

---

## The Problem

HTTP PATCH operations need to distinguish three states for every field: the field was **not sent** (leave unchanged), **sent as null** (clear the value), or **sent with a value** (update it). Standard C# nullability cannot express all three.

### The null ambiguity

Consider a PATCH request to update a guest profile:

```json
{ "firstName": "Alice" }
```

This should update `FirstName` but leave `Email` unchanged. But with a normal DTO:

```csharp
public record UpdateGuest
{
    public string? FirstName { get; init; }  // "Alice"
    public string? Email { get; init; }      // null -- but does this mean "clear" or "not sent"?
}
```

When `Email` is `null`, there is no way to tell if the client explicitly sent `null` (meaning "clear my email") or simply omitted the field (meaning "don't touch it"). Both states collapse into the same `null` value.

### The value-type trap

The problem gets worse with non-nullable value types:

```csharp
public record UpdateProduct
{
    public decimal Price { get; init; }  // 0 -- was this sent or is it the default?
    public int Stock { get; init; }      // 0 -- same problem
}
```

An empty JSON body produces `Price = 0` and `Stock = 0`. A naive `ApplyTo` method would zero out every numeric field.

### Manual workarounds are fragile

Without a tri-state type, developers resort to workarounds:

```csharp
// Workaround 1: Separate "modified fields" set (manual tracking)
public record UpdateGuest
{
    public string? FirstName { get; init; }
    public HashSet<string> ModifiedFields { get; init; } = new();
}

// Workaround 2: Nullable wrappers everywhere
public record UpdateProduct
{
    public decimal? Price { get; init; }  // But now "null" means "not sent", not "clear"
}

// Workaround 3: JsonPatchDocument (loses type safety)
app.MapPatch("/products/{id}", (Guid id, JsonPatchDocument<Product> patch) => ...);
```

Each workaround introduces its own problems: manual tracking is error-prone, nullable wrappers flip the semantics, and `JsonPatchDocument` loses type safety and requires the caller to understand JSON Patch operations.

---

## The Solution

Pragmatic.Patch solves this with two pieces:

1. **`Optional<T>`** -- a readonly struct that carries three states: Undefined (not sent), Null (explicitly null), and Value (has a value)
2. **Source-generated patch DTOs** -- a single `[GeneratePatch<TEntity>]` attribute generates `Optional<T>` properties, `ApplyTo()`, `ModifiedProperties`, and a typed JSON converter

```csharp
// Declare the patch type
[GeneratePatch<Guest>]
public partial record UpdateGuestPatch;

// Use it in an endpoint
app.MapPatch("/api/guests/{id}", async (Guid id, UpdateGuestPatch patch, AppDbContext db) =>
{
    var guest = await db.Guests.FindAsync(id);
    if (guest is null) return Results.NotFound();

    patch.ApplyTo(guest);       // Only modifies fields that were sent
    await db.SaveChangesAsync();
    return Results.Ok(guest);
});
```

The generated JSON converter handles all three states automatically:

| JSON input | Result |
|------------|--------|
| `{ "firstName": "Alice" }` | `FirstName = Optional.Of("Alice")`, `Email = Optional.Undefined` |
| `{ "email": null }` | `Email = Optional.Null`, `FirstName = Optional.Undefined` |
| `{}` | All fields `Optional.Undefined` -- no changes applied |

---

## How It Works

### Step 1: You declare a partial record

```csharp
[GeneratePatch<Product>]
public partial record PatchProduct;
```

### Step 2: The source generator emits two files

**Patch type (`PatchProduct.Patch.g.cs`):**

```csharp
public partial record PatchProduct
{
    public Optional<string> Name { get; init; }
    public Optional<string?> Description { get; init; }
    public Optional<decimal> Price { get; init; }

    public void ApplyTo(Product entity)
    {
        if (Name.HasValue) entity.SetName(Name.Value!);
        if (Description.HasValue) entity.SetDescription(Description.Value);
        if (Price.HasValue) entity.SetPrice(Price.Value);
    }

    public IReadOnlySet<string> ModifiedProperties => GetModifiedProperties();
}
```

**JSON converter (`PatchProduct.JsonConverter.g.cs`):**

A typed `System.Text.Json` converter that:
- Tracks which JSON keys were present during deserialization
- Present key with value: `Optional<T>.Of(value)`
- Present key with `null`: `Optional<T>.Null`
- Absent key: `Optional<T>.Undefined` (default)
- Respects `JsonSerializerOptions.PropertyNamingPolicy` (camelCase-aware)

The converter is applied via `[JsonConverter]` attribute on the partial record, so no manual registration is needed.

### Step 3: ASP.NET Core model binding does the rest

When a PATCH request arrives, the System.Text.Json deserializer calls the generated converter, which sets each property to the correct tri-state value. `ApplyTo()` then applies only the fields where `HasValue` is `true`.

---

## Optional\<T\>

`Optional<T>` is the core type. It is a `readonly struct` with zero heap allocation.

### Three States

| State | `HasValue` | `IsUndefined` | `Value` | Meaning |
|-------|-----------|---------------|---------|---------|
| **Undefined** | `false` | `true` | throws `InvalidOperationException` | Field was not in the JSON payload |
| **Null** | `true` | `false` | `null` | Field was explicitly set to `null` |
| **Value** | `true` | `false` | the value | Field was set to a specific value |

### Factory Methods

```csharp
Optional<string>.Undefined       // Not sent -- same as default(Optional<T>)
Optional<string>.Null            // Explicitly null
Optional<string>.Of("Alice")    // Has value
Optional<string>.Of(null)       // Same as Null
```

### Implicit Conversion

```csharp
Optional<string> name = "Alice";  // Implicit from T to Optional<T>
```

### Instance Members

| Member | Returns | Description |
|--------|---------|-------------|
| `HasValue` | `bool` | `true` if the field was sent (including null) |
| `IsUndefined` | `bool` | `true` if the field was not sent at all |
| `Value` | `T?` | The value. Throws `InvalidOperationException` if undefined |
| `GetValueOrDefault(T?)` | `T?` | Value if present, otherwise the default |
| `IfPresent(Action<T?>)` | `void` | Executes the action only when `HasValue` |
| `Map<TResult>(Func<T?, TResult?>)` | `Optional<TResult>` | Transforms the value, propagates `Undefined` |

### Equality

`Optional<T>` implements `IEquatable<Optional<T>>` with value semantics:

- Two `Undefined` are equal
- Two values are equal when `EqualityComparer<T>.Default` says so
- `Undefined` is never equal to `Null`
- `Optional.Of(null)` equals `Optional.Null`

### ToString

```csharp
Optional<string>.Of("Alice").ToString()  // "Optional(Alice)"
Optional<string>.Null.ToString()          // "Optional(null)"
Optional<string>.Undefined.ToString()     // "Undefined"
```

---

## HasValue vs IsUndefined vs Checking for Null

These three checks cover different scenarios. Understanding the distinction is essential for correct PATCH handling.

### HasValue: "Was this field sent?"

```csharp
if (patch.Email.HasValue)
{
    // The client explicitly included "email" in the JSON body.
    // The value might be null (meaning "clear") or a string (meaning "update").
}
```

### IsUndefined: "Was this field omitted?"

```csharp
if (patch.Email.IsUndefined)
{
    // The client did not include "email" in the JSON body.
    // Do not touch the entity's Email property.
}
```

### Checking Value for null: "Did the client send null?"

```csharp
if (patch.Email.HasValue && patch.Email.Value is null)
{
    // The client explicitly sent { "email": null }
    // Clear the entity's Email property.
}
```

### Combined example

```csharp
if (patch.Email.IsUndefined)
    logger.LogDebug("Email not included in patch");
else if (patch.Email.Value is null)
    logger.LogInformation("Guest {Id} cleared their email", id);
else
    logger.LogInformation("Guest {Id} changed email to {Email}", id, patch.Email.Value);
```

---

## ApplyTo Pattern

The generated `ApplyTo(TEntity entity)` method applies only the fields where `HasValue` is `true`. It respects the entity's encapsulation by preferring `Set*` methods over public setters.

### Property Setter Resolution

For each property, the generator checks:

1. **`Set{PropertyName}()` method** -- preferred. Uses the entity's encapsulated setter (e.g., `entity.SetName(value)`).
2. **Public setter** -- fallback when no `Set*` method exists (e.g., `entity.Name = value`).

If a property has both a `Set*` method and a public setter, the `Set*` method takes priority. This integrates with Pragmatic.Persistence's generated entity setters.

### Excluded Properties

The generator automatically skips properties that should not be patchable:

| Category | Properties |
|----------|------------|
| **Identity** | `Id`, `PersistenceId` |
| **Auditing** (`IAuditable`) | `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy` |
| **Soft Delete** (`ISoftDelete`) | `IsDeleted`, `DeletedAt`, `DeletedBy` |
| **Concurrency** | `RowVersion` |
| **Navigation** | Collection properties (`ICollection<T>`, `IList<T>`, `IEnumerable<T>`) |
| **Entity references** | Reference types that have an `Id` or `PersistenceId` property |

These exclusions apply regardless of whether the entity explicitly implements the interfaces -- the property names alone trigger exclusion.

---

## JSON Serialization

### Generated Typed Converter

The source generator produces a typed `JsonConverter` per patch type. This converter is automatically applied via the `[JsonConverter]` attribute on the generated partial record.

The converter handles:
- `PropertyNamingPolicy` awareness: both PascalCase and camelCase property names work in the same request
- Tracking which keys were present during deserialization
- Correct mapping: present key with value to `Optional.Of()`, present key with null to `Optional.Null`, absent key to `Optional.Undefined`

### Non-Nullable Value Types

For non-nullable value types like `decimal`, sending `null` in JSON results in `Undefined` (not `Null`), because `null` is not a valid value for `decimal`. This prevents accidental zeroing:

```json
{ "price": null }     // Price stays Undefined (decimal cannot be null)
{ "price": 0 }        // Price = Optional.Of(0m) (explicit zero)
```

### OptionalConverter\<T\> (Fallback)

For scenarios where you use `Optional<T>` outside of a generated patch DTO (e.g., in a manually-written DTO), register a typed converter for each `T`:

```csharp
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new OptionalConverter<string>()));
```

There is no open-generic factory: it would need `MakeGenericType`. `OptionalConverter<T>` is annotated `[RequiresDynamicCode]`/`[RequiresUnreferencedCode]`; the SG-generated converters are the AOT-safe path.

---

## Integration with Pragmatic.Persistence

When using Pragmatic.Persistence, the source generator also generates a `PatchApplyTemplate` for mutation classes. This uses `SetProperties` tracking with `MarkSet()` instead of `Optional<T>`:

- **`SetProperties`** -- an `IReadOnlySet<string>` tracking which properties were explicitly marked
- **`MarkSet(propertyName)`** -- marks a property as explicitly set
- **`ApplyPatch(entity)`** -- applies only tracked properties, respecting entity setters and converters

This dual approach supports both:
- **HTTP PATCH endpoints** via `[GeneratePatch<T>]` with `Optional<T>` tri-state
- **Mutation partial updates** via `SetProperties` tracking in the persistence layer

### Change Tracking Integration

When `ApplyTo()` calls the entity's `Set*()` methods:
- `IChangeTracking.ModifiedProperties` is updated automatically
- Only modified properties are validated
- EF Core tracks only changed columns
- `EntityPropertyChanged<T>` events fire only for actual changes

---

## Diagnostics

| ID | Severity | Description |
|----|----------|-------------|
| **PRAG2200** | Error | `[GeneratePatch<T>]` must be applied to a `partial` class or record |
| **PRAG2201** | Error | Entity type could not be resolved from the attribute type argument |
| **PRAG2202** | Warning | Entity has no settable properties suitable for patching |

---

## Key Types

| Type | Namespace | Purpose |
|------|-----------|---------|
| `Optional<T>` | `Pragmatic.Patch` | Tri-state readonly struct |
| `GeneratePatchAttribute<TEntity>` | `Pragmatic.Patch.Attributes` | Triggers SG for patch DTO generation |
| `OptionalConverter<T>` | `Pragmatic.Patch.Serialization` | Fallback JSON converter for one `Optional<T>` |

---

## Design Principles

- **Zero reflection** -- all property mapping is resolved at compile time by the source generator
- **Zero allocation** -- `Optional<T>` is a `readonly struct`, no heap allocation
- **Type safety** -- the generated converter is typed per patch DTO, no `MakeGenericType` at runtime
- **Encapsulation** -- prefers `Set*` methods over public setters, respecting entity invariants
- **Composable** -- works standalone or integrated with Pragmatic.Persistence mutation pipeline

---

## See Also

- [Getting Started](/modules/patch/getting-started/) -- Step-by-step guide to adding PATCH support
- [Tri-State Semantics](/modules/patch/tri-state-semantics/) -- Detailed `Optional<T>` API and JSON behavior
- [Common Mistakes](/modules/patch/common-mistakes/) -- Pitfalls and how to avoid them
- [Troubleshooting](/modules/patch/troubleshooting/) -- Problem/solution guide
