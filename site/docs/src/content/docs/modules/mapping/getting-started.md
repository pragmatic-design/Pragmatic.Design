---
title: "Getting Started with Pragmatic.Mapping"
description: "This guide walks you through creating your first entity-to-DTO mapping with Pragmatic.Mapping."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Mapping/docs/getting-started.md
sidebar:
  order: 2
---
This guide walks you through creating your first entity-to-DTO mapping with Pragmatic.Mapping.

## Prerequisites

- .NET 10.0+
- `Pragmatic.Mapping` package
- `Pragmatic.SourceGenerator` referenced as an analyzer

## Step 1: Define Your Entity

Entities are your domain objects. They can be classes, records, or structs.

```csharp
public class Guest
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string? Nationality { get; set; }
    public string PreferredLanguage { get; set; } = "";
}
```

## Step 2: Create a DTO with `[MapFrom<T>]`

Decorate a `partial` type with `[MapFrom<T>]`. The type **must** be `partial` (PRAG0300 error otherwise).

```csharp
using Pragmatic.Mapping.Attributes;

[MapFrom<Guest>]
[GenerateProjection]
public partial class GuestDto
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string Email { get; init; } = "";
    public string? Phone { get; init; }
    public string? Nationality { get; init; }
    public string PreferredLanguage { get; init; } = "";

    // Computed property -- excluded from mapping
    [MapIgnore]
    public string FullName => $"{FirstName} {LastName}";
}
```

Properties are matched by name (case-insensitive). Properties that exist on both the entity and DTO are mapped automatically.

## Step 3: Use the Generated Code

After building, the source generator creates several ways to map:

```csharp
// Option 1: Static factory method
var dto = GuestDto.FromEntity(guest);

// Option 2: Extension method on a single entity
var dto = guest.ToGuestDto();

// Option 3: Extension method on a collection
IEnumerable<GuestDto> dtos = guests.ToGuestDto();
List<GuestDto> dtoList = guestList.ToGuestDto();
GuestDto[] dtoArray = guestArray.ToGuestDto();

// Option 4: In-memory selector for LINQ
var dtos = guests.Select(GuestDto.Selector).ToList();

// Option 5: EF Core projection (requires [GenerateProjection])
var dtos = await db.Guests
    .Where(g => g.Email != null)
    .Select(GuestDto.Projection)
    .ToListAsync();
```

## What Gets Generated

For a `[MapFrom<Guest>]` DTO, the generator produces two files:

### 1. Main partial class (`GuestDto.Mapping.g.cs`)

```csharp
public partial class GuestDto
{
    public static GuestDto FromEntity(Guest entity) { ... }
    public static Func<Guest, GuestDto> Selector { get; } = FromEntity;
    public static Expression<Func<Guest, GuestDto>> Projection { get; } = ...;

    static partial void BeforeMapping(Guest source, ref GuestDto? result);
    static partial void CustomizeMapping(Guest source, ref GuestDtoMappingContext ctx);

    public struct GuestDtoMappingContext { ... }
}
```

### 2. Extension methods (`GuestDto.Extensions.g.cs`)

```csharp
public static class GuestDtoMappingExtensions
{
    public static GuestDto ToGuestDto(this Guest entity) => GuestDto.FromEntity(entity);
    public static IEnumerable<GuestDto> ToGuestDto(this IEnumerable<Guest> entities) => ...;
    public static List<GuestDto> ToGuestDto(this List<Guest> entities) => ...;
    public static GuestDto[] ToGuestDto(this Guest[] entities) => ...;
}
```

## Common Patterns

### Flattening Navigation Properties

When a DTO property name follows the pattern `{NavigationName}{PropertyName}`, the generator flattens automatically:

```csharp
// Entity
public class RoomType
{
    public Property Property { get; set; }  // Navigation
}

// DTO -- AddressCity auto-resolved? No, use [MapProperty] for navigation paths
[MapFrom<RoomType>]
public partial class RoomTypeSummaryDto
{
    [MapProperty("Property.Name")]
    public string PropertyName { get; init; } = "";
}
```

### Excluding Properties

Use `[MapIgnore]` for computed properties or properties populated by other means:

```csharp
[MapFrom<Property>]
public partial class PropertyDetailDto
{
    public string Name { get; init; } = "";
    public string City { get; init; } = "";
    public int StarRating { get; init; }

    [MapIgnore]
    public string DisplayLabel => $"{Name} ({City}, {StarRating})";

    [MapIgnore]
    public IReadOnlyDictionary<string, string>? Descriptions { get; init; }
}
```

### Format Strings

Use `[MapProperty(Format = "...")]` to format values via `.ToString(format)`:

```csharp
[MapFrom<Property>]
public partial class PropertyDetailDto
{
    [MapProperty("CreatedAt", Format = "yyyy-MM-dd")]
    public string CreatedDate { get; init; } = "";
}
```

**Note:** Format strings are not supported in projections (PRAG0321 info) because `.ToString(format)` cannot be translated to SQL.

### Bidirectional Mapping

A single DTO can have both `[MapFrom<T>]` and `[MapTo<T>]`:

```csharp
[MapFrom<User>]
[MapTo<User>]
public partial class UserDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
}

// Read path
var dto = UserDto.FromEntity(user);

// Write path
var entity = dto.ToEntity();
dto.ApplyTo(existingEntity);
```

### DTO-to-Entity (Write Path)

Use `[MapTo<T>]` for create operations:

```csharp
[MapTo<Guest>]
public partial record CreateGuestDto
{
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string Email { get; init; } = "";
}

// Creates a new Guest with mapped properties
Guest guest = dto.ToEntity();
```

ID properties are excluded from `ToEntity()` by default. Add `[MapProperty]` (with no arguments) to the ID property to force inclusion.

## Accessibility

The generated extension class matches the DTO's accessibility:

| DTO Accessibility | Extensions Class |
|-------------------|------------------|
| `public` | `public static` |
| `internal` | `internal static` |

## Supported Type Kinds

| DTO Type | `[MapFrom]` | `[MapTo]` | `[GenerateProjection]` |
|----------|-------------|-----------|------------------------|
| `class` | Yes | Yes | Yes |
| `record` | Yes | Yes | Yes |
| `struct` | Yes | Planned | Planned |
| `record struct` | Yes | Planned | Planned |

## Next Steps

- [Projections Guide](/modules/mapping/projections/) -- SQL-translatable Expression mappings for EF Core
- [Custom Converters Guide](/modules/mapping/custom-converters/) -- `IValueConverter<TSource, TTarget>` for complex types
- [Feature Matrix](/modules/mapping/feature-matrix/) -- Complete feature comparison table
