---
title: "Common Mistakes"
description: "These are the most common issues developers encounter when using Pragmatic.Mapping. Each section shows the wrong approach, the correct approach, and explains wh"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Mapping/docs/common-mistakes.md
sidebar:
  order: 7
---
These are the most common issues developers encounter when using Pragmatic.Mapping. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Forgetting `partial` on the DTO Class

**Wrong:**

```csharp
[MapFrom<Guest>]
public class GuestDto
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
}
```

**Compile result:** `PRAG0300` error -- "Type 'GuestDto' must be declared as partial to use [MapFrom]".

**Right:**

```csharp
[MapFrom<Guest>]
public partial class GuestDto
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
}
```

**Why:** The source generator emits `FromEntity()`, `Selector`, hooks, and the `MappingContext` struct into a partial class. Without `partial`, the compiler cannot merge the generated code with your class. This applies to `class`, `record`, `struct`, and `record struct`.

---

## 2. Property Name Mismatch Without Explicit Mapping

**Wrong:**

```csharp
public class Guest
{
    public string FirstName { get; set; } = "";
}

[MapFrom<Guest>]
public partial class GuestDto
{
    public string Name { get; init; } = "";  // Does not match FirstName!
}
```

**Compile result:** `PRAG0303` warning -- "Property 'Name' has no matching source property on 'Guest'". The DTO compiles, but `Name` receives `default("")` at runtime.

**Right:**

```csharp
[MapFrom<Guest>]
public partial class GuestDto
{
    // Option 1: Match the name exactly
    public string FirstName { get; init; } = "";

    // Option 2: Use [MapProperty] for explicit mapping
    [MapProperty(nameof(Guest.FirstName))]
    public string Name { get; init; } = "";
}
```

**Why:** Property matching is case-insensitive, but the names must match. `Name` does not match `FirstName`. The generator emits PRAG0303 as a warning (not error) so you can build and iterate, but the property silently gets `default`. Always check build warnings.

---

## 3. Using Both MapFrom and MapTo When You Only Need One

**Wrong:**

```csharp
// Using [MapTo] for a read-only DTO
[MapFrom<Guest>]
[MapTo<Guest>]
public partial class GuestSummaryDto
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = "";
    public string Email { get; init; } = "";
}
```

**Result:** Works, but generates unnecessary `ToEntity()` that you never call. The generated code includes ID exclusion logic (PRAG0324 info) and potentially triggers PRAG0307 warnings for required properties on the entity that are not present on the DTO.

**Right:**

```csharp
// Read-only DTO: only [MapFrom]
[MapFrom<Guest>]
public partial class GuestSummaryDto
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = "";
    public string Email { get; init; } = "";
}

// Write DTO: only [MapTo]
[MapTo<Guest>]
public partial record CreateGuestDto
{
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string Email { get; init; } = "";
}
```

**Why:** Bidirectional mapping (`[MapFrom]` + `[MapTo]`) is valid but should be intentional. Use it when the same DTO genuinely serves both read and write paths. For most applications, separate read DTOs and write DTOs are cleaner and avoid unnecessary generated code and diagnostics.

---

## 4. Missing Custom Converter for Complex Types

**Wrong:**

```csharp
public class Invoice
{
    public Money TotalAmount { get; set; }  // Custom domain type
}

[MapFrom<Invoice>]
public partial class InvoiceDto
{
    public string TotalAmount { get; init; } = "";  // Money -> string ???
}
```

**Compile result:** `PRAG0304` error -- "Cannot map from 'Money' to 'string': incompatible types". The generator does not know how to convert `Money` to `string`.

**Right:**

```csharp
// 1. Implement a converter
public sealed class MoneyToStringConverter : IValueConverter<Money, string>
{
    public string Convert(Money source) => source.ToString("N2");
    public Money ConvertBack(string target) => Money.Parse(target);
}

// 2. Apply it
[MapFrom<Invoice>]
public partial class InvoiceDto
{
    [MapProperty(nameof(Invoice.TotalAmount))]
    [MapConverter<MoneyToStringConverter>]
    public string TotalAmount { get; init; } = "";
}
```

**Why:** The generator handles built-in type conversions automatically (int to string, DateTime to DateOnly, etc.), but it cannot guess how to convert custom domain types. Implement `IValueConverter<TSource, TTarget>` with a parameterless constructor, and apply `[MapConverter<T>]` to the property.

---

## 5. A Converter or a Format Through a Navigation That May Be Null

A converter (`[MapConverter<T>]`) or a format (`[MapProperty(Format = "...")]`) over a value read
straight off the row **is** carried by the projection: SQL cannot compute it, so the projection reads the
source column and computes the member on the client in the last step of the read: the executor's
top-level `Select`, where EF Core evaluates what it cannot translate. It equals what `FromEntity` gives.

**Wrong:**

```csharp
[MapFrom<Reservation>]
[GenerateProjection]
public partial class ReservationDto
{
    [MapProperty("Guest.CreatedAt", Format = "yyyy-MM-dd")]   // Guest may be null
    public string GuestSince { get; init; } = "";
}
```

**Compile result:** `PRAG0321` info (a format) or `PRAG0320` warning (a converter). The member is left
out of the projection and keeps its initialiser. The client step would dereference a navigation that may
be null, and an expression tree cannot say `?.`.

**Right:** map the navigation's own DTO (`public GuestDto? Guest { get; init; }`, with the format on
`GuestDto`), or read the raw value (`public DateTime? GuestCreatedAt`) and format it where it is shown.

**Also:** the projection is meant to be the **last** step. `query.Select(Dto.Projection).Where(d =>
d.IssuedDate == "…")` asks SQL for a member only the client computes, and EF Core refuses it at run time.
Filter on the entity, before the projection.

---

## 6. Missing [MapFrom] on Nested DTO Types

**Wrong:**

```csharp
[MapFrom<Order>]
public partial class OrderDto
{
    public Guid Id { get; init; }
    public AddressDto ShippingAddress { get; init; }  // AddressDto has no [MapFrom]!
}

public class AddressDto  // Missing [MapFrom<Address>] and partial
{
    public string Street { get; init; } = "";
    public string City { get; init; } = "";
}
```

**Compile result:** `PRAG0309` error -- "Nested type 'AddressDto' must have [MapFrom<Address>] for mapping".

**Right:**

```csharp
[MapFrom<Order>]
public partial class OrderDto
{
    public Guid Id { get; init; }
    public AddressDto ShippingAddress { get; init; }
}

[MapFrom<Address>]
public partial class AddressDto
{
    public string Street { get; init; } = "";
    public string City { get; init; } = "";
}
```

**Why:** When the generator encounters a DTO-typed property, it needs to call `FromEntity()` on that nested DTO. The nested type must have `[MapFrom<T>]` so the generator knows the source type and can produce the recursive mapping. If you do not want the property mapped, use `[MapIgnore]`.

---

## 7. `CustomizeMapping()` in a Projection DTO

`CustomizeMapping()` runs inside `FromEntity` only: it is a C# method body over the whole entity, and the
projection is an expression over the columns (`PRAG0319`). A member it sets keeps what the projection
computed. Use a `[MapProperty]` with a `Format` or a `[MapConverter<T>]` for a value derived from one
column (both are carried by the projection, see #5), and keep `CustomizeMapping()` for the in-memory
mapping.

---

## 8. Wrong Namespace or Type Name for Source Type

**Wrong:**

```csharp
using Pragmatic.Mapping.Attributes;

[MapFrom<Guest>]  // Which Guest? Using directive might resolve to wrong type
public partial class GuestDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
}
```

**Possible result:** If multiple types named `Guest` exist in different namespaces, the generator maps from whichever the compiler resolves. If the resolved type does not have the expected properties, you get PRAG0303 warnings for every property.

**Right:**

```csharp
using Pragmatic.Mapping.Attributes;
using Showcase.Booking.Entities;  // Be explicit about the namespace

[MapFrom<Guest>]
public partial class GuestDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
}

// Or use fully qualified name:
[MapFrom<Showcase.Booking.Entities.Guest>]
public partial class GuestDto { /* ... */ }
```

**Why:** The generic type parameter in `[MapFrom<T>]` is resolved by the C# compiler using standard name resolution rules. If you have multiple types with the same name (common in large solutions), the wrong one may be selected. Use explicit `using` directives or fully qualified names to avoid ambiguity.

---

## 9. Conflicting [MapIgnore] and [MapProperty] on the Same Property

**Wrong:**

```csharp
[MapFrom<Guest>]
public partial class GuestDto
{
    [MapIgnore]
    [MapProperty(nameof(Guest.Email))]
    public string Email { get; init; } = "";
}
```

**Compile result:** `PRAG0314` error -- "Property 'Email' has conflicting attributes [MapIgnore] and [MapProperty]".

**Right:**

```csharp
[MapFrom<Guest>]
public partial class GuestDto
{
    // Either ignore it:
    [MapIgnore]
    public string Email { get; init; } = "";

    // Or map it explicitly:
    [MapProperty(nameof(Guest.Email))]
    public string Email { get; init; } = "";
}
```

**Why:** `[MapIgnore]` says "do not map this property." `[MapProperty]` says "map this property from a specific source." These are contradictory instructions. The generator reports an error rather than guessing which one you intended. Remove one of the two attributes.

---

## 10. Manual Mapping When the Source Generator Handles It

**Wrong:**

```csharp
public static class GuestMapper
{
    public static GuestDto ToDto(Guest entity)
    {
        return new GuestDto
        {
            Id = entity.Id,
            FirstName = entity.FirstName,
            LastName = entity.LastName,
            Email = entity.Email,
            Phone = entity.Phone ?? "",
        };
    }
}

// Usage:
var dto = GuestMapper.ToDto(guest);
```

**Result:** Works, but you are maintaining mapping code that the generator produces automatically. When you add a property to `Guest` and `GuestDto`, you must also update `GuestMapper` -- a third place to change.

**Right:**

```csharp
[MapFrom<Guest>]
public partial class GuestDto
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string Email { get; init; } = "";
    public string? Phone { get; init; }
}

// Usage:
var dto = GuestDto.FromEntity(guest);
var dto = guest.ToGuestDto();
```

**Why:** The generator keeps the mapping in sync with both the entity and DTO. Add a property to both types and the mapping updates automatically at the next build. The generated code handles null checks, nullable-to-non-nullable defaults, navigation flattening, and collection mapping. Manual mapping should only be used for scenarios the generator cannot handle.

---

## 11. Forgetting to Add [GenerateProjection] for EF Core Queries

**Wrong:**

```csharp
[MapFrom<Property>]
public partial class PropertySummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
}

// Using Selector instead of Projection:
var dtos = await db.Properties
    .Select(PropertySummaryDto.Selector)  // This is a Func, not an Expression!
    .ToListAsync();
```

**Runtime result:** EF Core cannot translate a `Func<T, R>` to SQL. It loads all properties from the database into memory and then applies the mapping in-memory. For large tables, this is a performance disaster.

**Right:**

```csharp
[MapFrom<Property>]
[GenerateProjection]  // Generates Expression<Func<Property, PropertySummaryDto>>
public partial class PropertySummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
}

// Now using Projection:
var dtos = await db.Properties
    .Select(PropertySummaryDto.Projection)  // Expression -- translates to SQL
    .ToListAsync();
```

**Why:** `Selector` is a compiled `Func<TSource, TDto>` delegate for in-memory LINQ. `Projection` is an `Expression<Func<TSource, TDto>>` that EF Core can inspect and translate to SQL. Without `[GenerateProjection]`, the `Projection` property is not generated. If you use `Selector` in an EF Core query, EF Core evaluates it client-side, defeating the purpose of projection.

---

## 12. Using [MapProperty(Default)] When Auto-Default Handles It

**Wrong:**

```csharp
[MapFrom<Guest>]
public partial class GuestDto
{
    [MapProperty(nameof(Guest.Phone), Default = "")]
    public string Phone { get; init; } = "";

    [MapProperty(nameof(Guest.Age), Default = "0")]
    public int Age { get; init; }

    [MapProperty(nameof(Guest.Score), Default = "0")]
    public decimal Score { get; init; }
}
```

**Result:** Works, but the explicit `Default` is unnecessary. The generator already handles nullable-to-non-nullable conversions with sensible defaults: `string?` to `string` uses `?? ""`, `int?` to `int` uses `.GetValueOrDefault()`, etc.

**Right:**

```csharp
[MapFrom<Guest>]
public partial class GuestDto
{
    // Auto-default handles these automatically:
    public string Phone { get; init; } = "";     // string? -> string: ?? ""
    public int Age { get; init; }                 // int? -> int: .GetValueOrDefault()
    public decimal Score { get; init; }           // decimal? -> decimal: .GetValueOrDefault()

    // Use Default only for non-standard fallbacks:
    [MapProperty(nameof(Guest.Nationality), Default = "Unknown")]
    public string Nationality { get; init; } = "";  // "Unknown" instead of ""
}
```

**Why:** The generator's auto-default system handles the common nullable-to-non-nullable patterns. Reserve `[MapProperty(Default = "...")]` for cases where you need a specific fallback value different from the type's natural default.

---

## Quick Reference

| Mistake | Diagnostic / Symptom |
|---------|---------------------|
| Missing `partial` | `PRAG0300` compile error |
| Property name mismatch | `PRAG0303` warning, property gets `default` |
| Unnecessary bidirectional mapping | Extra generated code, PRAG0307/PRAG0324 noise |
| Missing converter for complex type | `PRAG0304` compile error |
| Converter through a nullable navigation, in a projection | `PRAG0320` warning, property gets `default` |
| Nested DTO without `[MapFrom]` | `PRAG0309` compile error |
| Format string through a nullable navigation, in a projection | `PRAG0321` info, property excluded |
| Wrong source type resolved | `PRAG0303` warnings on all properties |
| Conflicting `[MapIgnore]` + `[MapProperty]` | `PRAG0314` compile error |
| Manual mapping instead of SG | Works but triple maintenance burden |
| Missing `[GenerateProjection]` | No `Projection` property, client-side evaluation |
| Redundant `[MapProperty(Default)]` | Harmless but unnecessary code |
