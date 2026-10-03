# Architecture and Core Concepts

This guide explains **why** Pragmatic.Mapping exists, how its pieces fit together, and how to choose the right mapping approach for each situation. Read this before diving into the individual feature guides.

---

## The Problem

Every non-trivial application needs to convert between domain entities and DTOs. The .NET ecosystem offers two established approaches. Both have significant drawbacks.

### AutoMapper: magic at runtime

```csharp
// Startup.cs
services.AddAutoMapper(typeof(MappingProfile).Assembly);

// MappingProfile.cs
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Guest, GuestDto>();
        CreateMap<Guest, GuestSummaryDto>()
            .ForMember(d => d.FullName,
                opt => opt.MapFrom(s => s.FirstName + " " + s.LastName));
        CreateMap<Reservation, ReservationDto>()
            .ForMember(d => d.GuestName,
                opt => opt.MapFrom(s => s.Guest.FirstName + " " + s.Guest.LastName));
        // 50 more CreateMap calls...
    }
}

// Usage
var dto = _mapper.Map<GuestDto>(guest);
```

Problems:

1. **Runtime reflection.** AutoMapper discovers properties via reflection on every map call (cached, but still reflection-based initialization). This is incompatible with AOT compilation and adds startup cost.
2. **String-based configuration.** `ForMember` uses lambda expressions that are evaluated at runtime. Rename a property and the mapping silently breaks -- no compile-time error.
3. **Hidden failures.** A missing `CreateMap` or a misconfigured `ForMember` only surfaces at runtime, often in production.
4. **Debugging opacity.** When a mapping produces the wrong value, you step through AutoMapper internals. The actual mapping logic is buried in expression trees and reflection caches.
5. **Profile sprawl.** As the application grows, mapping profiles become large files that are hard to review and maintain.

### Manual mapping: correct but tedious

```csharp
public static class GuestMapper
{
    public static GuestDto ToDto(Guest entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return new GuestDto
        {
            Id = entity.Id,
            FirstName = entity.FirstName,
            LastName = entity.LastName,
            Email = entity.Email,
            Phone = entity.Phone ?? "",
            FullName = entity.FirstName + " " + entity.LastName,
        };
    }

    public static Guest ToEntity(GuestDto dto)
    {
        return new Guest
        {
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Email = dto.Email,
            Phone = string.IsNullOrEmpty(dto.Phone) ? null : dto.Phone,
        };
    }

    // And then the same for GuestSummaryDto, GuestDetailDto, CreateGuestDto...
    // And then for Reservation, Property, RoomType, Invoice, Payment...
}
```

Problems:

1. **Boilerplate multiplication.** Every entity/DTO pair needs hand-written mapping in both directions. A domain with 30 entities and 60 DTOs means hundreds of mapping methods.
2. **Drift.** Add a property to the entity, forget to add it to the mapper -- the new property silently gets `default`. No compiler warning.
3. **No projection support.** EF Core projections require `Expression<Func<TEntity, TDto>>`, which is tedious to write manually and easy to get wrong.
4. **Inconsistent patterns.** Different developers write mappers differently. Some use static methods, some use extension methods, some use constructors. No enforced convention.

**The fundamental issue**: both approaches force a tradeoff between safety (manual) and convenience (AutoMapper). Neither gives you both.

---

## The Solution

Pragmatic.Mapping eliminates the tradeoff. You declare the mapping relationship with an attribute on your DTO, and the source generator produces the mapping code at compile time. The generated code is:

- **Visible** -- you can read it in `obj/`, set breakpoints, step through it
- **Type-safe** -- property mismatches are caught at build time with diagnostics
- **Zero-reflection** -- no runtime discovery, no startup cost, AOT-compatible
- **Minimal-dependency** -- no DI registration, just static entry points; the only runtime dependency is the small Pragmatic.Ensure guard library

The same Guest mapping:

```csharp
[MapFrom<Guest>]
[GenerateProjection]
public partial class GuestDto
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string Email { get; init; } = "";
    public string? Phone { get; init; }

    [MapIgnore]
    public string FullName => $"{FirstName} {LastName}";
}
```

The source generator reads this at compile time and produces:

- `GuestDto.FromEntity(Guest entity)` -- static factory
- `GuestDto.Selector` -- `Func<Guest, GuestDto>` delegate for in-memory LINQ
- `GuestDto.Projection` -- `Expression<Func<Guest, GuestDto>>` for EF Core SQL projection
- `guest.ToGuestDto()` -- extension method on a single entity
- `guests.ToGuestDto()` -- extension methods on `IEnumerable<Guest>`, `List<Guest>`, `Guest[]`

No mapper class. No profile. No DI registration. No reflection. The generated code is a plain C# partial class with static methods.

---

## How It Works

### Compile-time analysis

When you build the project, the Pragmatic source generator:

1. Finds every `partial` type with `[MapFrom<T>]` or `[MapTo<T>]`
2. Resolves the source/target type's properties via Roslyn symbols (no reflection)
3. Matches DTO properties to source properties using the resolution strategy (see below)
4. Detects type mismatches and applies automatic conversions or reports diagnostics
5. Emits a partial class with the mapping methods and an extensions class

### What you write vs. what gets generated

```
You write:                              SG generates:

[MapFrom<Guest>]                        GuestDto.Mapping.g.cs
[GenerateProjection]                    ├── static FromEntity(Guest entity)
public partial class GuestDto           ├── static Func<Guest, GuestDto> Selector
{                                       ├── static Expression<...> Projection
    public Guid Id { get; init; }       ├── static partial void BeforeMapping(...)
    public string FirstName ...         ├── static partial void CustomizeMapping(...)
    public string LastName ...          └── struct GuestDtoMappingContext { ... }
    public string Email ...
    [MapIgnore]                         GuestDto.Extensions.g.cs
    public string FullName => ...       ├── static ToGuestDto(this Guest)
}                                       ├── static ToGuestDto(this IEnumerable<Guest>)
                                        ├── static ToGuestDto(this List<Guest>)
                                        └── static ToGuestDto(this Guest[])
```

All generated files live under `obj/Debug/net10.0/generated/` and are visible in the IDE under **Dependencies > Analyzers > Pragmatic.SourceGenerator**.

---

## MapFrom vs. MapTo

The two core attributes serve opposite directions.

### `[MapFrom<TSource>]` -- Entity to DTO (read path)

Use when you want to create DTOs from entities. This is the most common direction.

```csharp
[MapFrom<Guest>]
public partial class GuestDto
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = "";
    public string Email { get; init; } = "";
}

// Usage:
var dto = GuestDto.FromEntity(guest);
var dto = guest.ToGuestDto();
var dtos = guests.Select(GuestDto.Selector);
```

**Generated members:**

| Member | Type | Purpose |
|--------|------|---------|
| `FromEntity(TSource)` | Static method | Creates a DTO from an entity |
| `Selector` | `Func<TSource, TDto>` | Compiled delegate for in-memory LINQ |
| `Projection` | `Expression<Func<TSource, TDto>>` | Only with `[GenerateProjection]` |
| `RequiredNavigations` | `IReadOnlyList<string>` | Navigation properties needed for FromEntity |
| Extension methods | Static class | `ToXxxDto()` on single and collection types |

### `[MapTo<TTarget>]` -- DTO to Entity (write path)

Use when you want to create entities from DTOs, typically for create operations.

```csharp
[MapTo<Guest>]
public partial record CreateGuestDto
{
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string Email { get; init; } = "";
}

// Usage:
Guest entity = dto.ToEntity();
```

**Generated members:**

| Member | Type | Purpose |
|--------|------|---------|
| `ToEntity()` | Instance method | Creates a new entity from the DTO |

**ID exclusion:** `Id` and `{EntityName}Id` properties are excluded from `ToEntity()` by default (PRAG0324 info). The entity typically assigns its own ID. To force inclusion, add `[MapProperty]` (with no arguments) to the ID property.

### Bidirectional mapping

A single DTO can have both attributes:

```csharp
[MapFrom<User>]
[MapTo<User>]
public partial class UserDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
}

// Read path:
var dto = UserDto.FromEntity(user);

// Write path:
var entity = dto.ToEntity();
```

Both sets of methods are generated on the same partial class.

---

## Property Matching Rules

The generator resolves DTO properties to source properties using a priority-ordered strategy. The first match wins.

| Priority | Strategy | Example |
|----------|----------|---------|
| 1 | `[MapProperty]` explicit path | `[MapProperty("Address.City")]` on `City` |
| 2 | `[MapConverter<T>]` custom converter | Custom type conversion |
| 3 | Direct name match (case-insensitive + snake_case) | `Name` matches `Name`, `name`, `NAME`; `first_name` matches `FirstName` |
| 4 | Flattening convention | `AddressCity` matches `Address.City` |
| 5 | Concatenation convention | `FullName` matches `FirstName` + `" "` + `LastName` |

Beyond these, the attribute surface also covers conditional mapping (`[MapCondition]`), polymorphic mapping (`[MapDerived<,>]`), and class-level converters; see the [Attributes Reference](attributes-reference.md).

### Direct name match

The simplest case. A DTO property matches a source property with the same name (case-insensitive comparison):

```csharp
// Entity: public string Email { get; set; }
// DTO:    public string Email { get; init; }  --> direct match
```

### Flattening convention

When a DTO property name follows the pattern `{NavigationName}{PropertyName}`, the generator resolves it as a navigation path:

```csharp
// Entity:
public class Reservation
{
    public Guest Guest { get; set; }   // Navigation property
}
public class Guest
{
    public string FirstName { get; set; }
}

// DTO:
[MapFrom<Reservation>]
public partial class ReservationDto
{
    public string GuestFirstName { get; init; } = "";
    // Resolved as: entity.Guest?.FirstName ?? ""
}
```

For deeper navigations or when the convention is ambiguous, use `[MapProperty]`:

```csharp
[MapProperty("Guest.FirstName")]
public string GuestFirstName { get; init; } = "";
```

### Concatenation convention

When two source properties can be combined to match a DTO property name:

```csharp
// Entity has FirstName and LastName
// DTO:
public string FullName { get; init; } = "";
// Resolved as: entity.FirstName + " " + entity.LastName
```

The default separator is a space. For custom separators, use `[MapProperty]`:

```csharp
[MapProperty("LastName", "FirstName", Separator = ", ")]
public string NameReversed { get; init; } = "";
// Result: "Doe, John"
```

### Unmatched properties

Properties with no match receive `default` and generate a PRAG0303 warning. This is intentional -- it alerts you to potential mismatches without breaking the build.

---

## Automatic Type Conversions

The generator detects type mismatches between source and target and applies conversions automatically.

### Supported automatic conversions

| Source | Target | Generated Code |
|--------|--------|----------------|
| `int`, `decimal`, `bool`, `Guid`, `DateTime`, `DateTimeOffset` | `string` | `.ToString(InvariantCulture)` |
| `string` | `int`, `decimal`, `bool`, `Guid`, `DateTime`, `DateTimeOffset` | `T.Parse(value, InvariantCulture)` |
| `enum` | `string` | `.ToString()` |
| `string` | `enum` | `Enum.Parse<T>(value)` (null source → default) |
| `enum` | different `enum` | by-member-name switch (validated by PRAG0328) |
| `DateTime` | `DateOnly` | `DateOnly.FromDateTime(value)` |
| `DateOnly` | `DateTime` | `value.ToDateTime(TimeOnly.MinValue)` |
| `DateTime` | `TimeOnly` | `TimeOnly.FromDateTime(value)` |
| `string` | `DateOnly`, `TimeOnly` | `DateOnly.Parse(value)`, `TimeOnly.Parse(value)` |
| `int` | `long`, `decimal` | Implicit widening (no code needed) |

All string parsing/formatting uses `InvariantCulture` (stable across locales). Numeric **narrowing** (e.g. `long` → `int`) is not implicit and reports PRAG0304 rather than silently truncating.

### Nullable to non-nullable auto-defaults

When the source is nullable and the target is not, the generator applies sensible defaults:

| Source | Target | Generated Code |
|--------|--------|----------------|
| `string?` | `string` | `entity.Value ?? ""` |
| `int?` | `int` | `entity.Value.GetValueOrDefault()` |
| `decimal?` | `decimal` | `entity.Value.GetValueOrDefault()` |
| `bool?` | `bool` | `entity.Value.GetValueOrDefault()` |
| `Guid?` | `Guid` | `entity.Value.GetValueOrDefault()` |
| `DateTime?` | `DateTime` | `entity.Value.GetValueOrDefault()` |
| `enum?` | `enum` | `entity.Value.GetValueOrDefault()` |

For explicit control, use `[MapProperty(Default = "value")]`.

### Format strings

Use `[MapProperty(Format = "...")]` to format values via `.ToString(format)`:

```csharp
[MapProperty("CreatedAt", Format = "yyyy-MM-dd")]
public string CreatedDate { get; init; } = "";
// Generated: entity.CreatedAt.ToString("yyyy-MM-dd")
```

In a projection the format is computed on the client after the read: SQL cannot run `.ToString(format)`, so the projection reads the column and formats it in the last step of the query. Through a navigation that may be null it cannot, and the member is left out (PRAG0321 info).

---

## Custom Converters

When automatic conversions are insufficient, implement `IValueConverter<TSource, TTarget>`:

```csharp
using Pragmatic.Mapping.Converters;

public sealed class MoneyToStringConverter : IValueConverter<decimal, string>
{
    public string Convert(decimal source) => source.ToString("N2");
    public decimal ConvertBack(string target) =>
        decimal.TryParse(target, out var result) ? result : 0m;
}
```

Apply it to a property with `[MapConverter<T>]`:

```csharp
[MapFrom<Invoice>]
public partial class InvoiceSummaryDto
{
    [MapProperty(nameof(Invoice.TotalAmount))]
    [MapConverter<MoneyToStringConverter>]
    public string TotalFormatted { get; init; } = "";
}
```

**Requirements:**
- Parameterless constructor (the generator calls `new TConverter()`)
- Stateless (a new instance is created per mapping call)
- Implements `IValueConverter<TSource, TTarget>`

**Bidirectional:** `Convert` is used in `[MapFrom]` (entity to DTO). `ConvertBack` is used in `[MapTo]` (DTO to entity).

**In a projection:** the converter runs on the client after the read, on the column the projection selected, the same `Convert` `FromEntity` calls. Through a navigation that may be null it cannot, and the member is left out with `default` (PRAG0320 warning).

See [Custom Converters Guide](custom-converters.md) for the full reference.

---

## Projection (IQueryable)

Adding `[GenerateProjection]` alongside `[MapFrom<T>]` generates a static `Projection` property of type `Expression<Func<TSource, TDto>>`. EF Core translates this expression tree directly to a SQL `SELECT` clause.

```csharp
[MapFrom<Property>]
[GenerateProjection]
public partial class PropertySummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public int StarRating { get; init; }
}

// Usage:
var dtos = await db.Properties
    .Where(p => p.IsActive)
    .Select(PropertySummaryDto.Projection)
    .ToListAsync();
```

The generated SQL selects only the three columns your DTO needs. No `SELECT *`, no loading the full entity into memory.

### Projection vs. FromEntity

| Feature | `FromEntity()` | `Selector` | `Projection` |
|---------|----------------|------------|--------------|
| SQL translation | No | No | Yes |
| Custom converters | Yes | Yes | No |
| Format strings | Yes | Yes | No |
| CustomizeMapping | Yes | Yes | No |
| BeforeMapping | Yes | Yes | No |
| Nested DTOs | Recursive call | Recursive call | Inlined expression |
| Navigation loading | Manual `Include()` | Manual `Include()` | Automatic SQL JOIN |
| Circular references | Tracked | Tracked | Not supported |

**Rule of thumb:** Use `Projection` when querying from EF Core. Fall back to `FromEntity()` when you need converters, format strings, hooks, or are working with in-memory objects.

### Nested projections

When a DTO has a nested DTO property, the generator inlines the nested DTO's projection into the parent expression:

```csharp
[MapFrom<Order>]
[GenerateProjection]
public partial class OrderDto
{
    public Guid Id { get; init; }
    public List<OrderLineDto> Lines { get; init; } = [];
}

[MapFrom<OrderLine>]
public partial class OrderLineDto
{
    public string ProductName { get; init; } = "";
    public int Quantity { get; init; }
}

// Generated projection inlines OrderLineDto:
// entity => new OrderDto
// {
//     Id = entity.Id,
//     Lines = entity.Lines.Select(x => new OrderLineDto
//     {
//         ProductName = x.ProductName,
//         Quantity = x.Quantity,
//     }).ToList(),
// }
```

EF Core translates this to SQL JOINs automatically. No `Include()` calls needed.

### Auto-defaults in projections

Nullable-to-non-nullable mappings use `??` coalescing in projections (instead of `GetValueOrDefault()`), which EF Core translates to SQL `COALESCE`:

| Source | Target | Generated Expression |
|--------|--------|---------------------|
| `string?` | `string` | `entity.Name ?? ""` |
| `int?` | `int` | `entity.Count ?? 0` |
| `Guid?` | `Guid` | `entity.Id ?? Guid.Empty` |

### Projection limitations

What SQL cannot compute is either computed on the client after the read (the projection is the query's
last step, where EF Core evaluates what it cannot translate) or left out with a compile-time diagnostic:

| Feature | Diagnostic | Behavior in Projection |
|---------|------------|----------------------|
| `[MapConverter<T>]` over the row | -- | Computed on the client after the read, as `FromEntity` computes it |
| `[MapProperty(Format = "...")]` over the row | -- | Formatted on the client after the read |
| either, through a navigation that may be null | PRAG0320 (Warning) / PRAG0321 (Info) | Property excluded, gets `default` |
| `CustomizeMapping()` | PRAG0319 (Warning) | Hook ignored |
| Circular references | -- | Not supported |

See [Projections Guide](projections.md) for the full reference.

---

## The Include Problem

`FromEntity()` and `Selector` operate on in-memory objects. If you load an entity from EF Core without `Include()`, navigation properties are null.

```csharp
// Navigation not loaded -- GuestFirstName will be "" or null
var reservation = await db.Reservations.FindAsync(id);
var dto = ReservationSummaryDto.FromEntity(reservation);  // Guest is null!
```

Two solutions:

### 1. Explicit Include

```csharp
var reservation = await db.Reservations
    .Include(r => r.Guest)
    .Include(r => r.Property)
    .FirstOrDefaultAsync(r => r.PersistenceId == id);
var dto = ReservationSummaryDto.FromEntity(reservation);
```

The generator tracks which navigations a DTO needs and exposes them:

```csharp
ReservationSummaryDto.RequiredNavigations  // ["Guest", "Property"]
```

### 2. Use Projection (recommended)

```csharp
var dto = await db.Reservations
    .Where(r => r.PersistenceId == id)
    .Select(ReservationSummaryDto.Projection)
    .FirstOrDefaultAsync();
```

Projections resolve navigations as SQL JOINs. No `Include()` needed. This is the recommended approach for queries.

---

## Nested Mapping

### Nested DTOs in MapFrom

When a DTO property is itself a DTO with `[MapFrom<T>]`, the generator calls `FromEntity()` recursively:

```csharp
[MapFrom<Reservation>]
public partial class ReservationDto
{
    public Guid Id { get; init; }
    public GuestDto Guest { get; init; }       // Calls GuestDto.FromEntity()
    public List<LineDto> Lines { get; init; }   // Calls LineDto.FromEntity() per item
}
```

If the nested DTO type does not have `[MapFrom<T>]`, the generator reports PRAG0309.

### Circular references

The generator detects circular references at compile time (PRAG0313 info) and generates instance-tracking code to prevent infinite recursion:

```csharp
// If User has List<Order> and Order has User:
// Generated code uses a HashSet to track visited instances
```

Circular references are not supported in projections.

### Collections of nested DTOs

Collections (`List<T>`, `T[]`, `IEnumerable<T>`, etc.) of nested DTOs are mapped element-by-element. Cross-collection type conversions are supported:

| Source Collection | Target Collection | Supported |
|-------------------|-------------------|-----------|
| `List<T>` | `T[]` | Yes |
| `T[]` | `List<T>` | Yes |
| `IEnumerable<T>` | `List<T>` | Yes |
| `List<T>` | `HashSet<T>` | Yes |
| `T[]` | `IReadOnlyList<T>` | Yes |

---

## Customization Hooks

The generator emits two `partial` methods that you can optionally implement for custom logic:

### BeforeMapping

Called before the mapping executes. Return a result to short-circuit:

```csharp
[MapFrom<User>]
public partial class UserDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";

    static partial void BeforeMapping(User source, ref UserDto? result)
    {
        if (source.IsDeleted)
        {
            result = new UserDto { Name = "[Deleted]" };
            // Mapping stops here -- FromEntity returns this result
        }
    }
}
```

### CustomizeMapping

Called after property mapping but before the DTO is constructed. Receives a mutable context struct:

```csharp
[MapFrom<User>]
public partial class UserDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";

    static partial void CustomizeMapping(User source, ref UserDtoMappingContext ctx)
    {
        ctx.Name = ctx.Name.ToUpperInvariant();
    }
}
```

The generator creates a `{TypeName}MappingContext` struct with mutable fields for each mapped property. You modify the struct by ref, and the final DTO is constructed from the modified values.

**Projection limitation:** `CustomizeMapping` is ignored in `[GenerateProjection]` (PRAG0319 warning) because Expression Trees cannot contain arbitrary C# logic.

---

## BodyOnly Variant

Adding `[GenerateBodyOnlyVariant]` generates an additional `FromEntityBodyOnly()` method that maps only scalar properties, skipping collections, nested DTOs, and dictionaries:

```csharp
[MapFrom<Reservation>]
[GenerateBodyOnlyVariant]
public partial class ReservationDto
{
    public Guid Id { get; init; }
    public decimal TotalAmount { get; init; }       // Mapped in both
    public ReservationStatus Status { get; init; }   // Mapped in both
    public GuestDto Guest { get; init; }             // Skipped in BodyOnly
    public List<LineDto> Lines { get; init; } = [];  // Skipped in BodyOnly
}

// Full mapping:
var dto = ReservationDto.FromEntity(reservation);

// Scalar-only mapping (for mutation scenarios):
var dto = ReservationDto.FromEntityBodyOnly(reservation);
```

This is useful for mutation scenarios where you map simple properties separately from navigations and collections.

---

## Mutation Helpers

`MutationHelpers` provides composable methods for applying DTO changes to entities, handling the complexity of 1:1 and 1:N relationship updates:

### MapOneToOne

Maps a 1:1 reference navigation. If the DTO value is null, the navigation is set to null. Otherwise updates in-place or creates new:

```csharp
MutationHelpers.MapOneToOne(
    dto.ShippingAddress,
    () => order.ShippingAddress,
    addr => order.ShippingAddress = addr,
    addrDto => addrDto.ToEntity(),
    (addrDto, addr) => addrDto.ApplyTo(addr));
```

### MapOneToMany

Synchronizes a collection navigation with three strategies:

| Strategy | Behavior |
|----------|----------|
| `CollectionStrategy.Sync` (default) | Add new, update existing, remove missing |
| `CollectionStrategy.AddOnly` | Add new, update existing, never remove |
| `CollectionStrategy.Replace` | Clear all and recreate from DTO |

```csharp
MutationHelpers.MapOneToMany(
    dto.Lines, order.Lines,
    d => d.Id, e => e.Id,
    lineDto => lineDto.ToEntity(),
    (lineDto, line) => lineDto.ApplyTo(line),
    CollectionStrategy.Sync);
```

---

## What Gets Generated

For each type decorated with `[MapFrom<T>]` or `[MapTo<T>]`, the generator produces the following files:

| Generated File | Content | Condition |
|----------------|---------|-----------|
| `{Type}.Mapping.g.cs` | `FromEntity()`, `Selector`, `Projection`, hooks, `MappingContext` struct | `[MapFrom<T>]` present |
| `{Type}.Extensions.g.cs` | `ToXxxDto()` extension methods on single and collection types | `[MapFrom<T>]` present |
| `{Type}.Mapping.g.cs` | `ToEntity()` instance method | `[MapTo<T>]` present |

### Generated FromEntity example

For a `[MapFrom<Guest>]` DTO with three properties:

```csharp
// Generated: GuestDto.Mapping.g.cs
public partial class GuestDto
{
    public static GuestDto FromEntity(Guest entity)
    {
        Ensure.ThrowIfNull(entity);

        GuestDto? beforeResult = null;
        BeforeMapping(entity, ref beforeResult);
        if (beforeResult is not null) return beforeResult;

        var ctx = new GuestDtoMappingContext
        {
            Id = entity.Id,
            FirstName = entity.FirstName,
            Email = entity.Email,
        };

        CustomizeMapping(entity, ref ctx);

        return new GuestDto
        {
            Id = ctx.Id,
            FirstName = ctx.FirstName,
            Email = ctx.Email,
        };
    }

    public static Func<Guest, GuestDto> Selector { get; } = FromEntity;

    static partial void BeforeMapping(Guest source, ref GuestDto? result);
    static partial void CustomizeMapping(Guest source, ref GuestDtoMappingContext ctx);

    public struct GuestDtoMappingContext
    {
        public Guid Id;
        public string FirstName;
        public string Email;
    }
}
```

### Generated extensions example

```csharp
// Generated: GuestDto.Extensions.g.cs
public static class GuestDtoMappingExtensions
{
    public static GuestDto ToGuestDto(this Guest entity)
        => GuestDto.FromEntity(entity);

    public static IEnumerable<GuestDto> ToGuestDto(this IEnumerable<Guest> entities)
        => entities.Select(GuestDto.FromEntity);

    public static List<GuestDto> ToGuestDto(this List<Guest> entities)
        => entities.ConvertAll(GuestDto.FromEntity);

    public static GuestDto[] ToGuestDto(this Guest[] entities)
        => Array.ConvertAll(entities, GuestDto.FromEntity);
}
```

The extensions class matches the DTO's accessibility: `public` DTO generates `public static` extensions, `internal` DTO generates `internal static` extensions.

---

## EF Core Integration (Pragmatic.Mapping.EFCore)

The optional `Pragmatic.Mapping.EFCore` package provides convenience extension methods for `IQueryable<T>` with built-in OpenTelemetry tracing:

| Method | Description |
|--------|-------------|
| `SelectDto(projection)` | Projects query using expression |
| `ToListDtoAsync(projection)` | Async projection to list |
| `FirstOrDefaultDtoAsync(projection)` | Async single item projection |
| `SingleOrDefaultDtoAsync(projection)` | Async single-or-default projection |
| `ToArrayDtoAsync(projection)` | Async projection to array |
| `ToPagedDtoAsync(projection, page, size)` | Paginated projection with metadata |
| `ToSliceDtoAsync(projection, offset, limit)` | Offset/limit projection |

```csharp
using Pragmatic.Mapping.EFCore.Extensions;

// Paginated query with OpenTelemetry tracing
var page = await db.Properties
    .Where(p => p.IsActive)
    .OrderBy(p => p.Name)
    .ToPagedDtoAsync(PropertySummaryDto.Projection, pageNumber: 1, pageSize: 20);

page.Items          // IReadOnlyList<PropertySummaryDto>
page.TotalCount     // Total matching rows
page.TotalPages     // Computed from TotalCount / PageSize
page.HasNextPage    // Pagination flag
```

All methods include `Pragmatic.Mapping` activity source tracing with tags for source type, DTO type, and result count.

---

## Ecosystem Integration

Pragmatic.Mapping integrates with other Pragmatic modules through generated code. Each integration is opt-in.

### Pragmatic.Persistence

Mutations use `[MapFrom<T>]` DTOs as input. Repository query results are mapped to DTOs via `FromEntity()` or `Projection`. The `RequiredNavigations` metadata tells you which `Include()` calls are needed.

### Pragmatic.Endpoints

Endpoint response DTOs use `[MapFrom<T>]` for entity-to-response mapping. Query endpoints (`Query<TEntity, TResult>`) use the generated `Projection` to project entities at the IQueryable level.

### Pragmatic.Actions

Action input/output DTOs with generated mapping. Domain actions that return entity-derived results use `FromEntity()` in their `Execute` method.

### Pragmatic.Caching

Cached queries project with `[GenerateProjection]` before caching, ensuring only the needed data is stored.

---

## Decision Guide

| Scenario | Approach |
|----------|----------|
| EF Core query to API response | `[GenerateProjection]` + `.Select(Dto.Projection)` |
| In-memory collection transform | `.Select(Dto.Selector)` or `.ToXxxDto()` |
| Single entity with custom logic | `FromEntity()` + `CustomizeMapping()` |
| Write path (create entity from DTO) | `[MapTo<T>]` + `dto.ToEntity()` |
| Scalar-only mapping for mutations | `[GenerateBodyOnlyVariant]` + `FromEntityBodyOnly()` |
| Navigation 1:1 update | `MutationHelpers.MapOneToOne()` |
| Collection sync (add/update/remove) | `MutationHelpers.MapOneToMany()` |
| Complex type transformation | `[MapConverter<T>]` with `IValueConverter<,>` |
| Hot path, value types | `record struct` DTO |

---

## See Also

- [Getting Started](getting-started.md) -- Your first mapping from entity to DTO
- [Projections Guide](projections.md) -- SQL-translatable Expression mappings for EF Core
- [Custom Converters Guide](custom-converters.md) -- `IValueConverter<TSource, TTarget>` for complex types
- [Feature Matrix](feature-matrix.md) -- Complete feature comparison table
- [Common Mistakes](common-mistakes.md) -- Avoid the most frequent pitfalls
- [Troubleshooting](troubleshooting.md) -- Problem/solution guide with diagnostics reference
