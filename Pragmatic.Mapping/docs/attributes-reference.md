# Attributes Reference

The complete reference for every Pragmatic.Mapping attribute, the property-matching rules the
generator follows, the customization hooks it emits, the EF Core query helpers, the mutation helpers,
and the diagnostics. For the mental model and a guided first mapping, start with
[Concepts](concepts.md) and [Getting Started](getting-started.md).

## `[MapFrom<TSource>]`

Generates mapping from a source type (typically an entity) to the decorated DTO.

**Generated members:**
- `static TDto FromEntity(TSource entity)` -- static factory method
- `static Func<TSource, TDto> Selector` -- delegate for in-memory LINQ
- Extension methods: `entity.ToDto()`, `IEnumerable<T>.ToDto()`, `List<T>.ToDto()`, `T[].ToDto()`

**Usage:**

```csharp
[MapFrom<User>]
public partial class UserDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
}
```

**Requirements:** The type must be declared as `partial`. Supports `class`, `record`, `struct`, and `record struct`.

## `[MapTo<TTarget>]`

Generates mapping from the DTO to a target type (typically an entity).

**Generated members:**
- `TTarget ToEntity()` -- creates a new entity instance
- `void ApplyTo(TTarget entity)` -- updates an existing entity (partial update semantics for nullable properties)

**Usage:**

```csharp
[MapTo<User>]
public partial record CreateUserDto
{
    public string Email { get; init; } = "";
    public string FirstName { get; init; } = "";
}

// Creates a new entity
User user = dto.ToEntity();

// Updates an existing entity
dto.ApplyTo(existingUser);
```

**Note:** ID properties (`Id`, `{EntityName}Id`) are excluded from `ToEntity()` by default. Use `[MapProperty]` on the ID property to force inclusion.

## `[GenerateProjection]`

Generates `Expression<Func<TSource, TDto>> Projection` for EF Core `Select()` queries. Requires `[MapFrom<T>]` on the same type.

Projections translate to SQL what SQL can compute. A converter (`[MapConverter]`) or a format string over a column is computed on the client after the read — the projection is the query's last step — and equals `FromEntity`; through a navigation that may be null it is excluded (PRAG0320/PRAG0321). `CustomizeMapping()` runs in `FromEntity` only (PRAG0319).

**Usage:**

```csharp
[MapFrom<Property>]
[GenerateProjection]
public partial class PropertySummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string City { get; init; } = "";
    public int StarRating { get; init; }
}

// SQL-optimized query
var dtos = await db.Properties
    .Where(p => p.IsActive)
    .Select(PropertySummaryDto.Projection)
    .ToListAsync();
```

See [Projections](projections.md) for details.

## `[GenerateBodyOnlyVariant]`

Generates an additional `FromEntityBodyOnly()` method that maps only scalar properties, skipping collections, nested DTOs, and dictionaries. Useful for mutation scenarios.

```csharp
[MapFrom<Reservation>]
[GenerateBodyOnlyVariant]
public partial class ReservationSummaryDto
{
    public Guid Id { get; init; }
    public decimal TotalAmount { get; init; }       // Mapped in both
    public ReservationStatus Status { get; init; }   // Mapped in both

    [MapProperty("Guest.FirstName")]
    public string GuestFirstName { get; init; } = "";  // Skipped in BodyOnly
}
```

## `[MapProperty]`

Customizes how a single property is mapped.

**Capabilities:**

| Feature | Syntax | Example |
|---------|--------|---------|
| Explicit source path | `[MapProperty("Address.City")]` | Flatten navigation |
| Concatenation | `[MapProperty("FirstName", "LastName")]` | Multiple paths joined |
| Custom separator | `Separator = ", "` | `"Doe, John"` |
| Format string | `Format = "yyyy-MM-dd"` | Date formatting via `.ToString(format)` |
| Default value | `Default = "N/A"` | Fallback for nullable to non-nullable |
| Target path (MapTo) | `Target = "Customer.Name"` | Map to nested entity property |

**Examples from Showcase:**

```csharp
// Navigation flattening (Showcase: RoomTypeSummaryDto)
[MapProperty("Property.Name")]
public string PropertyName { get; init; } = "";

// Format string (Showcase: PropertyDetailDto)
[MapProperty("CreatedAt", Format = "yyyy-MM-dd")]
public string CreatedDate { get; init; } = "";

// Concatenation
[MapProperty(nameof(User.FirstName), nameof(User.LastName))]
public string FullName { get; init; } = "";

// Default value for nullable source
[MapProperty(nameof(User.MiddleName), Default = "N/A")]
public string MiddleName { get; init; } = "";
```

## `[MapIgnore]`

Excludes a property from mapping. Use for computed properties or properties populated elsewhere.

```csharp
[MapFrom<Guest>]
public partial class GuestDto
{
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";

    [MapIgnore]
    public string FullName => $"{FirstName} {LastName}";
}
```

## `[MapConverter<TConverter>]`

Specifies a custom `IValueConverter<TSource, TTarget>` for a property. The converter must have a parameterless constructor.

**In projections** the converter runs on the client after the read; through a navigation that may be null the property is excluded (PRAG0320 warning).

```csharp
// 1. Implement IValueConverter
public sealed class MoneyToStringConverter : IValueConverter<decimal, string>
{
    public string Convert(decimal source) => source.ToString("N2");
    public decimal ConvertBack(string target) =>
        decimal.TryParse(target, out var result) ? result : 0m;
}

// 2. Use on a property (Showcase: InvoiceSummaryDto)
[MapFrom<Invoice>]
public partial class InvoiceSummaryDto
{
    [MapProperty(nameof(Invoice.TotalAmount))]
    [MapConverter<MoneyToStringConverter>]
    public string TotalFormatted { get; init; } = "";
}
```

See [Custom Converters](custom-converters.md) for the full guide.

## `[MapConstructor]`

Forces the generator to use a specific constructor when creating entity instances in `[MapTo]` scenarios. When omitted, the generator uses a best-match algorithm.

```csharp
public class User
{
    public User() { }

    [MapConstructor]
    public User(int id, string email) { Id = id; Email = email; }
}
```

## `[MapCondition]`

Maps a property only when a predicate returns `true`; otherwise the property keeps its type default. The predicate is a `static bool` method on the DTO that takes the source entity (validated at compile time — **PRAG0329** if missing or the wrong shape).

```csharp
[MapFrom<Property>]
public partial class PropertyDetailDto
{
    [MapCondition(nameof(ShouldExposeOperational))]
    public string? OperationalNotes { get; init; }

    private static bool ShouldExposeOperational(Property p) => p.IsPublished;
}
// Generated: OperationalNotes = ShouldExposeOperational(entity) ? entity.OperationalNotes : default
```

**Projection:** a predicate with an **expression body** gates the projection too. Its parameter becomes the
row and its types are qualified, so `ShouldExposeOperational` above is written
`OperationalNotes = (entity.IsPublished) ? entity.OperationalNotes : default!` and the database (or the
final projection) evaluates it. A predicate with a **block body** cannot be inlined: the projection maps the
property unconditionally, and **PRAG0332** (info) says so.

## `[MapDerived<TDerivedSource, TDerivedDto>]`

Polymorphic mapping. Placed on the base DTO, it makes `FromEntity` type-switch on the runtime entity type and return the matching derived DTO. `AllowMultiple` — declare one per subtype (most-derived first).

```csharp
[MapFrom<Animal>]
[MapDerived<Dog, DogDto>]
[MapDerived<Cat, CatDto>]
public partial class AnimalDto { public string Name { get; init; } = ""; }

[MapFrom<Dog>]
public partial class DogDto : AnimalDto { public string Breed { get; init; } = ""; }

// AnimalDto.FromEntity(someDog) returns a DogDto instance.
```

The contract is validated at compile time (**PRAG0330**): `TDerivedSource` must derive the `[MapFrom]` source and `TDerivedDto` must derive the base DTO. Dispatch is **runtime-only** — EF projections keep the base shape (**PRAG0331** info); query derived DTOs explicitly.

## Class-level `[MapConverter<TConverter>]`

Placed on the DTO type (not a property), a converter applies to **every** convention-mapped scalar whose source→target types match the converter's `IValueConverter<TSource, TTarget>` signature. A property-level `[MapConverter]` always wins over a class-level one.

```csharp
[MapFrom<Order>]
[MapConverter<MoneyToStringConverter>]  // decimal → string, applied to all matching props
public partial class OrderDto
{
    public string Total { get; init; } = "";     // decimal Total on the entity → converted
    public string Shipping { get; init; } = "";   // decimal Shipping → converted
}
```

Converter instances are cached in a `static readonly` field (one per converter type, no per-call allocation).

## Property Matching Rules

The generator resolves DTO properties to source properties in this order:

| Priority | Strategy | Example |
|----------|----------|---------|
| 1 | `[MapProperty]` explicit path | `[MapProperty("Address.City")]` on `City` |
| 2 | `[MapConverter<T>]` | Custom type conversion |
| 3 | Direct name match (case-insensitive + snake_case) | `Name` maps to `Name`/`name`; `first_name` maps to `FirstName` |
| 4 | Flattening convention | `AddressCity` maps to `Address.City` |
| 5 | Concatenation convention | `FullName` maps to `FirstName` + `" "` + `LastName` |

The snake_case fallback (priority 3) helps when the source is an external API model with `snake_case` fields. Unmatched properties generate a PRAG0303 warning and receive `default`.

### Auto-Default for Nullable to Non-Nullable

When the source is nullable and the target is not, the generator applies sensible defaults: `string?` to `string` uses `?? ""`, numeric/bool/date/Guid types use `.GetValueOrDefault()`, enums use `.GetValueOrDefault()`. For explicit control, use `[MapProperty(Default = "value")]`.

### Type Conversions

The generator detects type mismatches and applies conversions automatically: numeric/enum/Guid/bool/`DateTime`/`DateTimeOffset` to/from `string` (via `.ToString()` / `.Parse()`, always with `InvariantCulture`), `DateTime` to/from `DateOnly` and `TimeOnly`, enum→enum across different enum types (by member name, validated — PRAG0328), and implicit numeric widening (`int`→`long`, etc.). Numeric **narrowing** is not implicit and reports PRAG0304. See [Feature Matrix](feature-matrix.md) for the full table.

### Collections and Nested DTOs

Collections (`List<T>`, `T[]`, `IEnumerable<T>`, `HashSet<T>`, `Dictionary<K,V>`) are mapped element-by-element. Cross-collection type conversions (`List<T>` to `T[]` and vice versa) are supported.

When a DTO property is itself a DTO with `[MapFrom<T>]`, the generator calls `FromEntity()` recursively. Circular references are detected at compile time (PRAG0313 info) and handled with instance tracking.

## Customization Hooks

The generator emits two `partial` methods that you can implement:

```csharp
[MapFrom<User>]
public partial class UserDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";

    // Called BEFORE mapping. Return a result to short-circuit.
    static partial void BeforeMapping(User source, ref UserDto? result)
    {
        if (source.IsDeleted)
        {
            result = new UserDto { Name = "[Deleted]" };
            // Mapping stops here
        }
    }

    // Called AFTER mapping. Modify the context to adjust values.
    static partial void CustomizeMapping(User source, ref UserDtoMappingContext ctx)
    {
        ctx.Name = ctx.Name.ToUpperInvariant();
    }
}
```

The generator creates a `{TypeName}MappingContext` struct with mutable fields for each mapped property. `CustomizeMapping` receives this struct by ref, allowing you to modify any mapped value before the final DTO is constructed.

**Note:** `CustomizeMapping` is ignored in `[GenerateProjection]` (PRAG0319 warning) because Expression Trees cannot contain arbitrary C# logic.

### Write-side hooks (`[MapTo]`)

Symmetric hooks run on the DTO → entity path. All are optional `partial` methods:

```csharp
[MapTo<Order>]
public partial record UpdateOrderDto
{
    public decimal Total { get; init; }

    // Short-circuit ToEntity: return a fully-built entity to skip generated construction.
    static partial void BeforeToEntity(ref Order? result);

    // Runs after ToEntity() builds a NEW entity.
    partial void CustomizeToEntity(Order entity) => entity.RecalculateTotals();

    // Runs after ApplyTo() updates an EXISTING entity.
    partial void CustomizeApplyTo(Order entity) => entity.Touch();
}
```

`BeforeToEntity` is `static` (no DTO instance yet); `CustomizeToEntity`/`CustomizeApplyTo` are instance methods with access to `this`.

## EF Core query helpers (`Pragmatic.Mapping.EFCore`)

The `Pragmatic.Mapping.EFCore` package provides extension methods for `IQueryable<T>` that work with generated projections:

| Method | Description |
|--------|-------------|
| `SelectDto(projection)` | Projects query using expression |
| `ToListDtoAsync(projection)` | Async projection to list |
| `FirstOrDefaultDtoAsync(projection)` | Async single item projection |
| `SingleOrDefaultDtoAsync(projection)` | Async single-or-default projection |
| `ToArrayDtoAsync(projection)` | Async projection to array |
| `ToPagedDtoAsync(projection, page, size)` | Paginated projection with metadata |
| `ToSliceDtoAsync(projection, offset, limit)` | Offset/limit projection |

All methods include OpenTelemetry `ActivitySource` tracing with `Pragmatic.Mapping` activity names.

```csharp
// Paginated results
var page = await db.Properties
    .Where(p => p.IsActive)
    .OrderBy(p => p.Name)
    .ToPagedDtoAsync(PropertySummaryDto.Projection, pageNumber: 1, pageSize: 20);

page.Items          // IReadOnlyList<PropertySummaryDto>
page.TotalCount     // Total matching items
page.TotalPages     // Computed page count
page.HasNextPage    // Pagination flag
```

> **The Include problem:** `FromEntity()` operates on in-memory objects -- navigation properties not
> loaded via `.Include()` will be `null`, producing silent data loss. Prefer `Projection` for EF Core
> queries, or use explicit `.Include()`. See [Common Mistakes](common-mistakes.md).

## Mutation Helpers

`MutationHelpers` provides composable methods for applying DTO changes to entities:

- **`MapOneToOne`** -- Maps a 1:1 reference navigation. If the DTO value is null, the navigation is set to null. Otherwise updates in-place or creates new.
- **`MapOneToMany`** -- Synchronizes a collection navigation with three strategies:
  - `CollectionStrategy.Sync` -- Add new, update existing, remove missing (default)
  - `CollectionStrategy.AddOnly` -- Add new, update existing, never remove
  - `CollectionStrategy.Replace` -- Clear all and recreate from DTO

```csharp
MutationHelpers.MapOneToOne(
    dto.ShippingAddress,
    () => order.ShippingAddress,
    addr => order.ShippingAddress = addr,
    addrDto => addrDto.ToEntity(),
    (addrDto, addr) => addrDto.ApplyTo(addr));

MutationHelpers.MapOneToMany(
    dto.Lines, order.Lines,
    d => d.Id, e => e.Id,
    lineDto => lineDto.ToEntity(),
    (lineDto, line) => lineDto.ApplyTo(line),
    CollectionStrategy.Sync);
```

## Diagnostics

The generator emits 28 diagnostics (`PRAG0300`–`PRAG0332`; IDs 0301/0308/0311/0312/0318 are retired and not reused). See [Troubleshooting](troubleshooting.md#diagnostics-reference) for the fix for each.

| ID | Severity | Description |
|----|----------|-------------|
| PRAG0300 | Error | Type must be declared `partial` |
| PRAG0302 | Error | `[MapProperty]` source path / `Target` segment not found |
| PRAG0304 | Error | Incompatible types, no conversion path (incl. numeric narrowing) |
| PRAG0305 | Error | Converter must implement `IValueConverter<,>` |
| PRAG0306 | Error | Converter must have a public parameterless constructor |
| PRAG0307 | Warning | Required property not mapped in `[MapTo]` |
| PRAG0309 | Error | Nested type missing `[MapFrom]` |
| PRAG0310 | Error | `[GenerateProjection]` requires `[MapFrom]` |
| PRAG0313 | Info | Circular reference detected, using instance tracking |
| PRAG0314 | Error | Conflicting `[MapIgnore]` + `[MapProperty]` |
| PRAG0315 | Error | Nested DTO `[MapFrom<T>]` unrelated to the navigation type |
| PRAG0316 | Error | No suitable `[MapTo]` constructor |
| PRAG0317 | Error | Nullable → non-nullable (non-simple target) without `Default` |
| PRAG0319 | Warning | `CustomizeMapping` ignored in Projection |
| PRAG0320 | Warning | `[MapConverter]` through a nullable navigation, excluded from the Projection |
| PRAG0321 | Info | Format string through a nullable navigation, excluded from the Projection |
| PRAG0322 | Info | Complex dictionary value not supported |
| PRAG0323 | Warning | Ambiguous mapping (direct match + flattening) |
| PRAG0324 | Info | ID property excluded from `ToEntity()` |
| PRAG0325 | Hidden | Source property not mapped to the DTO (data dropped) |
| PRAG0326 | Warning | Nested projection drops a complex mapping |
| PRAG0327 | Warning | Nested projection truncated by `MaxDepth` |
| PRAG0328 | Error | Enum→enum: source member missing on the target enum |
| PRAG0329 | Error | `[MapCondition]` predicate missing or invalid |
| PRAG0330 | Error | Invalid `[MapDerived]` pair (inheritance contract) |
| PRAG0331 | Info | `[MapDerived]` ignored in Projection |
| PRAG0332 | Info | `[MapCondition]` ignored in Projection (a predicate with a block body) |
