---
title: "Pragmatic.Mapping"
description: "High-performance, source-generated object-to-object mapping for .NET 10."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Mapping/README.md
sidebar:
  order: 0
  label: Overview
---
High-performance, source-generated object-to-object mapping for .NET 10.

## The Problem

Every application converts between entities and DTOs, and the two standard approaches each force a bad tradeoff.

**AutoMapper** relies on runtime reflection: it hides failures until production, is incompatible with AOT compilation, and makes debugging opaque. Rename a property and the mapping silently breaks — no compiler error, just wrong data at runtime.

**Manual mapping** is correct but tedious. Every entity/DTO pair needs hand-written code in *both* directions. Add a property to the entity, forget to update the mapper, and the new property silently gets `default`. With 30 entities and 60 DTOs, you maintain hundreds of mapping methods by hand.

You shouldn't have to choose between safety and convenience.

## The Solution

Pragmatic.Mapping eliminates the tradeoff. You declare the mapping relationship with an attribute, and the source generator writes the mapping code at compile time:

- **Visible** — generated code lives in `obj/`, fully debuggable and step-through-able. No magic.
- **Type-safe** — property mismatches are caught at build time with `PRAG03xx` diagnostics, not in production.
- **Zero-reflection** — no runtime discovery, no startup cost, AOT-compatible.
- **Minimal footprint** — no DI registration, static entry points; the only runtime dependency is the small `Pragmatic.Ensure` guard library.

Rename a property and the build fails with a precise message — instead of shipping silent data loss.

### How It Works

```
[MapFrom<User>]                     Pragmatic.SourceGenerator
public partial class UserDto  --->  generates partial class:
{                                     - FromEntity(User entity)
    public Guid Id { get; init; }     - Selector (Func<User, UserDto>)
    public string Name { get; init; } - Projection (Expression<>)
}                                     - Extension: user.ToUserDto()
                                      - Extension: users.ToUserDto()
```

1. You decorate a `partial` class/record/struct with `[MapFrom<T>]` or `[MapTo<T>]`.
2. The Pragmatic source generator analyzes the properties at compile time.
3. It emits a partial class with strongly-typed static methods plus a companion extensions class.
4. No DI registration needed — everything is static.

## When to use it

Use Pragmatic.Mapping whenever you map between entities and DTOs and want compile-time safety + AOT
without hand-writing mappers. Then pick the **path** that fits each case:

| Scenario | Use |
|----------|-----|
| EF Core query → API response | `[GenerateProjection]` + `Projection` (SQL-level, best performance) |
| In-memory collection transform | `.Select(Dto.Selector)` (compiled delegate) |
| Single entity with hooks/custom logic | `FromEntity()` + `CustomizeMapping()` |
| Write path (DTO → entity) | `[MapTo<T>]` + `ToEntity()` |
| Update an existing entity | `[MapTo<T>]` + `ApplyTo(entity, context)` (see [Updating an entity](#updating-an-entity)) |
| Hot path, value types | `record struct` DTO |
| Scalar-only mapping for mutations | `[GenerateBodyOnlyVariant]` + `FromEntityBodyOnly()` |

## Installation

```bash
dotnet add package Pragmatic.Mapping
dotnet add package Pragmatic.Mapping.EFCore   # optional: EF Core projection helpers
```

The mapping generator ships as a Roslyn analyzer inside the `Pragmatic.Mapping` package — referencing
the package is enough, no separate analyzer wiring. (Building inside this monorepo instead? See
[Monorepo Structure](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/howto/monorepo-structure.md).)

## Quick Start

Given an entity, declare a DTO with `[MapFrom<T>]`:

```csharp
using Pragmatic.Mapping.Attributes;

[MapFrom<Guest>]
[GenerateProjection]
public partial class GuestDto
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string? Phone { get; init; }

    [MapIgnore]
    public string FullName => $"{FirstName} {LastName}";
}
```

The generator produces a static factory, extension methods, an in-memory delegate, and an EF Core
projection — pick whichever the call site needs:

```csharp
var dto  = GuestDto.FromEntity(guest);          // static factory
var dto  = guest.ToGuestDto();                  // extension method
var dtos = guests.ToGuestDto();                 // IEnumerable / List / array overloads

var dtos = guests.Select(GuestDto.Selector);    // in-memory LINQ (compiled delegate)

var dtos = await db.Guests                       // EF Core → translates to a SQL SELECT
    .Where(g => g.Email != null)
    .Select(GuestDto.Projection)
    .ToListAsync();
```

No DI registration — everything is static. This example is from the Showcase
(`examples/showcase/src/Showcase.Booking/Guests/Dtos/GuestDto.cs`). Full first-mapping walkthrough:
[Getting Started](/modules/mapping/getting-started/).

## Know this one gotcha

`FromEntity()` maps **in-memory** objects: navigation properties you did not `.Include()` are `null`,
so the DTO silently gets nulls/empty collections with **no error or warning**.

```csharp
// WRONG — Guest and Property are null in the DTO (not Included)
var dto = ReservationSummaryDto.FromEntity(await db.Reservations.FindAsync(id));

// RIGHT — Projection translates to a SQL JOIN, no Include needed
var dto = await db.Reservations
    .Where(r => r.PersistenceId == id)
    .Select(ReservationSummaryDto.Projection)
    .FirstOrDefaultAsync();
```

**Prefer `Projection`** for EF Core queries, or `.Include()` explicitly before `FromEntity()`. The
generator exposes the required navigations as `Dto.RequiredNavigations`. Details:
[Common Mistakes](/modules/mapping/common-mistakes/).

## Feature highlights

Beyond name-convention mapping, projections and converters, the generator supports:

- **Conditional mapping** — `[MapCondition(nameof(Predicate))]` gates a property behind a static bool
  predicate on the DTO (validated at compile time, PRAG0329).
- **Polymorphic mapping** — `[MapDerived<Dog, DogDto>]` on the base DTO: `FromEntity` type-switches to
  the derived DTO (inheritance contract validated, PRAG0330; runtime-only, projections keep the base
  shape, PRAG0331).
- **Enum → enum across types** — mapped by member name with compile-time member validation
  (missing member = PRAG0328 error, not a runtime surprise).
- **Class-level converters** — `[MapConverter<MoneyToDecimalConverter>]` on the DTO applies to every
  convention-mapped property whose types match the converter signature. Converter instances are cached
  in `static readonly` fields.
- **Write-side hooks** — `BeforeToEntity(ref entity?)` (short-circuit), `CustomizeToEntity(entity)`,
  `CustomizeApplyTo(entity)` — symmetric to `BeforeMapping`/`CustomizeMapping`.
- **Null-safe write unflattening** — `[MapProperty(Target = "Customer.Address.City")]` initializes
  intermediate navigations (`??= new()`) instead of throwing NRE; invalid paths are PRAG0302 errors.
- **Collection fidelity** — `ToEntity` materializes what the *entity* declares (List/HashSet/array);
  `ImmutableArray<T>`/`ImmutableList<T>` DTO collections supported on runtime paths.
- **Naming conventions** — case-insensitive matching plus a snake_case fallback
  (`first_name` → `FirstName`); `FullName` maps by convention to `FirstName + " " + LastName`
  (built-in convenience — use multi-path `[MapProperty]` for any other concatenation).
- **Null substitution** — `[MapProperty(Default = ...)]` applies to any nullable source (fallback
  value), not just nullable→non-nullable.
- **Projection depth control** — `[GenerateProjection(MaxDepth = 8)]`; truncation warns (PRAG0327);
  flattening/concatenation inline into nested projections instead of being dropped.

### Updating an entity

Which update forms a DTO gets depends on whether it writes through a navigation, because that is the
only case where the update can be silently wrong.

| DTO writes | Generated |
|---|---|
| scalars only | `ApplyTo(entity)` |
| a collection or a nested DTO, EF Core referenced | `ApplyTo(entity, DbContext)` and `ApplyToLoaded(entity)` |
| a collection or a nested DTO, no EF Core | `ApplyTo(entity)` and `ApplyToLoaded(entity)` |

A merge decides what to keep by looking at what is there. In EF, a collection that was never included
and an empty one are the same object, so merging into an unloaded one writes every incoming element
again — measured: a write that sent the same two rows back wrote four, with no exception and a
successful `SaveChanges`. An unloaded reference fails the same way in reverse: it looks absent, so a
second child is built beside the one in the database. Changing the collection strategy does not help;
`AddOnly` reads the collection to decide what is *new* and duplicates identically.

`ApplyTo(entity, context)` asks EF at each navigation it is about to write, at every level, and throws
naming the one that was not loaded. It is the form to use against a tracked entity. `ApplyToLoaded` is
the same write with the question skipped — its name is the precondition, and it is what the generated
mutation and patch entry points call, because the invoker has already included what the DTO writes.

Where EF is present, a DTO that writes a navigation does **not** get a plain `ApplyTo(entity)`: the
ambiguous call does not compile rather than being flagged after the fact.

## Status

**Stable** within 1.0.0-alpha — the attribute surface and generated API are settled. See the
[roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

| [Concepts](/modules/mapping/concepts/) | Architecture, core concepts, and decision guide |
| [Getting Started](/modules/mapping/getting-started/) | Your first mapping from entity to DTO |
| [Attributes Reference](/modules/mapping/attributes-reference/) | Every attribute, property-matching rules, customization hooks, EF Core helpers, mutation helpers, diagnostics |
| [Projections](/modules/mapping/projections/) | SQL-translatable Expression mappings and Include detection |
| [Custom Converters](/modules/mapping/custom-converters/) | `IValueConverter<TSource, TTarget>` and manual property mapping |
| [Feature Matrix](/modules/mapping/feature-matrix/) | FromEntity vs Selector vs Projection — full comparison |
| [Common Mistakes](/modules/mapping/common-mistakes/) | The most frequent mapping pitfalls |
| [Troubleshooting](/modules/mapping/troubleshooting/) | Problem/solution guide with diagnostics reference |

## Cross-module integration

Pragmatic.Mapping is the DTO layer across the ecosystem: `[MapFrom<T>]` DTOs are the input/output of
[Persistence](/modules/persistence/overview/) mutations and queries,
[Endpoints](/modules/endpoints/overview/) responses, and [Actions](/modules/actions/overview/);
`[GenerateProjection]` DTOs are projected before [Caching](/modules/caching/overview/).

## Samples

`samples/Pragmatic.Mapping.Samples/` — runnable scenarios covering the core attributes, 4-level
nesting, nullable intermediates, self-referencing round-trip, converter combinations, property-name
mismatches, multi-level flattening, `ApplyTo`, the `BodyOnly` variant, `MapConstructor`, and
bidirectional mapping with target paths.

## Requirements

- .NET 10.0+
- `Pragmatic.SourceGenerator` analyzer (ships with the package)

## License

Part of the [Pragmatic.Design](/modules/mapping/overview/) ecosystem — see [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.Mapping is **MIT-licensed**.
