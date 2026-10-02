---
title: "Projections Guide"
description: "This guide covers `[GenerateProjection]` -- generating `Expression<Func<TEntity, TDto>>` for SQL-translatable EF Core queries."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Mapping/docs/projections.md
sidebar:
  order: 6
---
This guide covers `[GenerateProjection]` -- generating `Expression<Func<TEntity, TDto>>` for SQL-translatable EF Core queries.

## Overview

When you add `[GenerateProjection]` to a DTO that has `[MapFrom<T>]`, the source generator emits a static `Projection` property. EF Core translates this expression tree directly to a SQL `SELECT` clause, fetching only the columns your DTO needs.

```csharp
[MapFrom<Property>]
[GenerateProjection]
public partial class PropertySummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string City { get; init; } = "";
    public int StarRating { get; init; }
    public bool IsActive { get; init; }
}
```

The generator creates:

```csharp
public static Expression<Func<Property, PropertySummaryDto>> Projection { get; } =
    entity => new PropertySummaryDto
    {
        Id = entity.Id,
        Name = entity.Name,
        City = entity.City,
        StarRating = entity.StarRating,
        IsActive = entity.IsActive,
    };
```

EF Core translates this to:

```sql
SELECT p."Id", p."Name", p."City", p."StarRating", p."IsActive"
FROM "Properties" p
WHERE ...
```

## When to Use Projection vs FromEntity

| Feature | `FromEntity()` | `Selector` | `Projection` |
|---------|----------------|------------|--------------|
| SQL translation | No | No | Yes |
| Custom converters | Yes | Yes | No |
| Format strings | Yes | Yes | No |
| CustomizeMapping | Yes | Yes | No |
| BeforeMapping | Yes | Yes | No |
| Nested DTOs | Yes (recursive) | Yes (recursive) | Yes (inlined) |
| Collections of DTOs | Yes | Yes | Yes (inlined) |
| Circular references | Yes (tracked) | Yes (tracked) | Not supported |
| Navigation loading | Manual Include | Manual Include | Automatic (SQL JOIN) |

**Rule of thumb:** Use `Projection` whenever you are querying from EF Core. Fall back to `FromEntity()` only when you need hooks (`CustomizeMapping`), or are working with in-memory objects. Converters and format strings over a column are computed by the projection too, after the read.

## Usage

### Basic Query

```csharp
var dtos = await db.Properties
    .Where(p => p.IsActive)
    .Select(PropertySummaryDto.Projection)
    .ToListAsync();
```

### With Pragmatic.Mapping.EFCore Extensions

The `Pragmatic.Mapping.EFCore` package provides convenience methods with built-in OpenTelemetry tracing:

```csharp
using Pragmatic.Mapping.EFCore.Extensions;

// List
var dtos = await db.Properties
    .Where(p => p.IsActive)
    .ToListDtoAsync(PropertySummaryDto.Projection);

// Single item
var dto = await db.Properties
    .Where(p => p.Id == id)
    .FirstOrDefaultDtoAsync(PropertySummaryDto.Projection);

// Paginated
var page = await db.Properties
    .Where(p => p.IsActive)
    .OrderBy(p => p.Name)
    .ToPagedDtoAsync(PropertySummaryDto.Projection, pageNumber: 1, pageSize: 20);

// page.Items          -- IReadOnlyList<PropertySummaryDto>
// page.TotalCount     -- Total matching rows
// page.TotalPages     -- Computed from TotalCount / PageSize
// page.HasNextPage    -- Boolean
// page.HasPreviousPage

// Offset/Limit (cursor-based)
var slice = await db.Properties
    .OrderBy(p => p.Name)
    .ToSliceDtoAsync(PropertySummaryDto.Projection, offset: 40, limit: 20);
```

> **Always `OrderBy` before paginating.** `ToPagedDtoAsync`/`ToSliceDtoAsync` apply `Skip`/`Take`; over an
> unordered query the database may return rows in a different order across the count and data round-trips,
> so a row can be duplicated or skipped between pages. Order on a stable, unique key.

### Single or Array

```csharp
var dto = await db.Properties
    .Where(p => p.Code == "PROP-001")
    .SingleOrDefaultDtoAsync(PropertySummaryDto.Projection);

var dtos = await db.Properties
    .Where(p => p.City == "Vienna")
    .ToArrayDtoAsync(PropertySummaryDto.Projection);
```

## Nested DTO Projections

When a DTO has a nested DTO property, the generator inlines the nested projection instead of calling `FromEntity()`. This keeps the entire expression SQL-translatable.

```csharp
[MapFrom<Order>]
[GenerateProjection]
public partial class OrderDto
{
    public Guid Id { get; init; }
    public decimal Total { get; init; }
    public AddressDto? ShippingAddress { get; init; }
    public List<OrderLineDto> Lines { get; init; } = [];
}

[MapFrom<Address>]
public partial class AddressDto
{
    public string Street { get; init; } = "";
    public string City { get; init; } = "";
}

[MapFrom<OrderLine>]
public partial class OrderLineDto
{
    public string ProductName { get; init; } = "";
    public int Quantity { get; init; }
}
```

The generated projection inlines everything:

```csharp
public static Expression<Func<Order, OrderDto>> Projection { get; } =
    entity => new OrderDto
    {
        Id = entity.Id,
        Total = entity.Total,
        ShippingAddress = entity.ShippingAddress == null
            ? null
            : new AddressDto
            {
                Street = entity.ShippingAddress!.Street,
                City = entity.ShippingAddress!.City,
            },
        Lines = entity.Lines.Select(x => new OrderLineDto
        {
            ProductName = x.ProductName,
            Quantity = x.Quantity,
        }).ToList(),
    };
```

EF Core translates this to SQL JOINs automatically -- no `Include()` calls needed.

Deep nesting (3+ levels) is supported with recursive inlining. The generator uses unique variable names (`__e0`, `__e1`, etc.) to avoid lambda parameter conflicts.

What the nested initializer does with each member:

- **Nullability comes from the DTO.** A nested DTO property declared nullable (`AddressDto?`) keeps the
  `entity.X == null ? null : new …` check. One declared non-nullable is written `new …` with no check:
  the author says it is there, and for a required relation it is. In `FromEntity` a non-nullable nested
  DTO whose navigation is null throws an `InvalidOperationException` that names it, instead of assigning
  null to a non-nullable property.
- **A nullable column into a non-nullable member** gets the default the top level gives it
  (`entity.DecidedAt ?? default(DateTimeOffset)`).
- **A `LocalizedString` read as a `string`** is its `.Value`, evaluated in the final projection in the
  request's culture, as at the top level.
- A member the initializer cannot place is left at the DTO's default and reported (PRAG0326).

### A nested DTO over the same row

A nested DTO does not have to follow a navigation. When its `[MapFrom<T>]` is the same entity as the
parent's, and the source has no member of the property's name, it is built from the row itself — a group
of the row's own columns:

```csharp
[MapFrom<LeaveRequest>]
[GenerateProjection]
public partial class LeaveRequestDetailDto
{
    public Guid Id { get; init; }
    public EmployeeReferenceDto Employee { get; init; } = null!;

    [MapCondition(nameof(IsDecided))]
    public LeaveDecisionDetailDto? Decision { get; init; }       // null while pending

    private static bool IsDecided(LeaveRequest request) => request.Status != LeaveRequestStatus.Pending;
}

[MapFrom<LeaveRequest>]                                         // the same row
public partial class LeaveDecisionDetailDto
{
    public EmployeeReferenceDto DecidedBy { get; init; } = null!;
    public DateTimeOffset DecidedAt { get; init; }
    [MapProperty(nameof(LeaveRequest.DecisionNote))] public string? Note { get; init; }
}

// Generated projection:
// Decision = (entity.Status != LeaveRequestStatus.Pending)
//     ? new LeaveDecisionDetailDto { DecidedBy = new EmployeeReferenceDto { … }, DecidedAt = entity.DecidedAt ?? default(DateTimeOffset), Note = entity.DecisionNote }
//     : default!,
```

`FromEntity` maps it with `LeaveDecisionDetailDto.FromEntity(entity)`. A nested DTO over **another**
entity, named after no navigation, is still PRAG0303.

## Navigation Flattening in Projections

`[MapProperty]` with a navigation path works in projections. Expression Trees do not support the null-conditional operator (`?.`), so the generator rewrites a nullable navigation into an explicit null-check ternary — which EF Core translates to the correct `LEFT JOIN` null semantics in SQL.

```csharp
[MapFrom<RoomType>]
[GenerateProjection]
public partial class RoomTypeSummaryDto
{
    public string Name { get; init; } = "";

    [MapProperty("Property.Name")]
    public string PropertyName { get; init; } = "";   // Property navigation may be null
}
```

Generated projection:

```csharp
entity => new RoomTypeSummaryDto
{
    Name = entity.Name,
    // nullable navigation → null-check ternary, default folded into the null branch
    PropertyName = (entity.Property == null ? "" : entity.Property.Name),
}
```

If the target property is a **value type** (e.g. `int PropertyStarRating`), the default is folded into the null branch the same way — `(entity.Property == null ? 0 : entity.Property.StarRating)` — so the expression type-checks and still translates to SQL. This translates to a `LEFT JOIN` on the `Property` table.

## Auto-Defaults in Projections

When a nullable source maps to a non-nullable target, the generator uses null-coalescing (`??`) with EF Core-compatible defaults:

| Source | Target | Generated Expression |
|--------|--------|---------------------|
| `string?` | `string` | `entity.Name ?? ""` |
| `int?` | `int` | `entity.Count ?? 0` |
| `decimal?` | `decimal` | `entity.Amount ?? 0m` |
| `bool?` | `bool` | `entity.Flag ?? false` |
| `Guid?` | `Guid` | `entity.Id ?? Guid.Empty` |
| `DateTime?` | `DateTime` | `entity.Date ?? default(DateTime)` |
| `DateTimeOffset?` | `DateTimeOffset` | `entity.At ?? default(DateTimeOffset)` |
| `enum?` | `enum` | `entity.Status ?? default(MyEnum)` |

These expressions translate cleanly to SQL `COALESCE`.

## Required Navigations

The generator analyzes mapping paths and tracks which navigations the DTO needs. This metadata is available as a static property:

```csharp
// Generated on ReservationSummaryDto
public static IReadOnlyList<string> RequiredNavigations { get; } = ["Guest", "Property"];
```

This tells you exactly which `Include()` calls are needed when using `FromEntity()` instead of `Projection`:

```csharp
// When not using Projection, you need these Includes
var reservation = await db.Reservations
    .Include(r => r.Guest)
    .Include(r => r.Property)
    .FirstOrDefaultAsync(r => r.PersistenceId == id);

var dto = ReservationSummaryDto.FromEntity(reservation);
```

With `Projection`, these navigations are resolved as SQL JOINs automatically.

## Limitations

What SQL cannot compute is computed on the client after the read — the projection is the query's last
step, where EF Core evaluates what it cannot translate — or excluded with a compile-time diagnostic:

| Feature | In FromEntity | In Projection | Diagnostic |
|---------|---------------|---------------|------------|
| `[MapConverter<T>]` over a column | Yes | Yes — on the client after the read | -- |
| `[MapProperty(Format = "...")]` over a column | Yes | Yes — on the client after the read | -- |
| either, through a navigation that may be null | Yes | Excluded | PRAG0320 / PRAG0321 |
| `CustomizeMapping()` | Yes | Ignored | PRAG0319 |
| `BeforeMapping()` | Yes | Ignored | -- |
| `[MapCondition]` gate, predicate with an expression body | Yes | Yes — `(predicate) ? mapping : default!` | -- |
| `[MapCondition]` gate, predicate with a block body | Yes | Mapped **unconditionally** | PRAG0332 |
| `[MapDerived<,>]` dispatch | Yes (runtime type-switch) | Base shape only | PRAG0331 |
| Circular references | Tracked | Not supported | -- |
| Complex dictionary values | Yes | Not supported | PRAG0322 |
| Nested projection member with a converter, a format, an unresolved path, or a source that is neither a column nor a nested DTO | Yes | Those members keep the DTO's default | PRAG0326 |
| Nested projection deeper than `MaxDepth` | Yes | Truncated | PRAG0327 |

When a property is excluded from projection, it receives `default` in the SQL result. A member computed
after the read cannot be filtered or sorted on: the projection is the last step, and SQL never sees it. Structure your DTOs accordingly -- consider having a projection-safe DTO for queries and a richer DTO for single-entity display.

## Projection vs SelectDto vs Direct Select

All three approaches produce the same SQL:

```csharp
// 1. Direct Select (standard EF Core)
var dtos = await db.Properties.Select(PropertySummaryDto.Projection).ToListAsync();

// 2. SelectDto extension (Pragmatic.Mapping.EFCore, same thing + readability)
var dtos = await db.Properties.SelectDto(PropertySummaryDto.Projection).ToListAsync();

// 3. ToListDtoAsync (Pragmatic.Mapping.EFCore, convenience + tracing)
var dtos = await db.Properties.ToListDtoAsync(PropertySummaryDto.Projection);
```

The `ToListDtoAsync` and related methods add OpenTelemetry activity tracing (`Pragmatic.Mapping` activity source) with tags for source type, DTO type, and result count.
