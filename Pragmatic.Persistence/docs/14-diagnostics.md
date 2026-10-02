# Diagnostics Guide

Diagnostics are part of the product experience, not just compiler noise.

For a novice user, the most useful way to read a Pragmatic diagnostic is:

1. Understand what the generator was trying to protect you from.
2. Decide whether the message is blocking correctness or only warning about cost.
3. Apply the smallest fix that restores a clear model.

This page focuses on the persistence diagnostics that users are most likely to hit while adopting the stack for the first time.

## How to read a diagnostic

| Severity | What it means | What to do |
|----------|---------------|------------|
| Error | The generator cannot produce a safe or coherent model | Fix it before trusting generated output |
| Warning | Generation can continue, but the shape may be inefficient or ambiguous | Review the design and either simplify it or accept the tradeoff consciously |
| Info | The generator is telling you about a behavior choice | Read it once so the generated result does not surprise you later |

## Fast triage checklist

Before looking at the individual code:

1. Confirm the type is `partial` when using Pragmatic attributes.
2. Confirm navigation properties are declared through `[Relation.*]`, not manually mixed with inferred relationships.
3. Confirm the DTO really matches the entity graph you expect to load.
4. Confirm warnings about depth or many navigations are acceptable for the query volume you expect in production.

## Common diagnostics

| Code | Severity | Meaning | First fix to try |
|------|----------|---------|------------------|
| `PRAG0600` | Error | An attributed persistence type is not `partial` | Add `partial` to the class declaration |
| `PRAG0602` | Error | A generated DbContext type is not `partial` | Add `partial` to the DbContext declaration |
| `PRAG0612` | Warning | Several relations to the same entity all derive the same navigation name | Name each one: `[Relation.X<T>.WithNavigation("...")]` |
| `PRAG0613` | Error | `Inverse = "..."` matches no navigation on the target | Name an existing one, or declare the other side |
| `PRAG0614` | Error | The inverse navigation has the wrong type | Point it at the navigation that is a `{this entity}` |
| `PRAG0615` | Error | Two relations would generate the same navigation name | Rename one with `.WithNavigation("...")` |
| `PRAG0620` | Warning | A state machine has no `[InitialState]` | Mark one enum value with `[InitialState]` |
| `PRAG0621` | Warning | A state has no incoming transition and is not initial — it is unreachable | Add a `[TransitionFrom(...)]`, or mark it `[InitialState]` |
| `PRAG0622` | Error | `[TransitionFrom(...)]` names a value that is not in the enum | Use a real enum member |
| `PRAG0623` | Error | The state machine governs a property the entity does not have | `[StateMachine<T>(Property = nameof(YourProperty))]` — it defaults to `Status` |
| `PRAG0624` | Error | A trait's properties are declared by hand, but only some of them | Declare them all, or delete the ones you wrote |
| `PRAG0651` | Warning | A property type may need an EF Core value converter | Add a converter or map to a provider-friendly type |
| `PRAG0701` | Error | `[Filter(Operator = FilterOperator.Between)]` — no generator renders it | Two properties over one column: `GreaterOrEqual` + `LessOrEqual`, same `MapTo` |
| `PRAG0702` | Error | `[CascadeOn<TSource>]` on an entity with no `{Source}Id` foreign key | Add the FK, or declare `[Relation.ManyToOne<TSource>]` so it is generated |
| `PRAG0703` | Warning | An attribute argument that is parsed and consumed by nothing — the `[Join]` key-join arguments, `Type`, `Alias` | Remove it, or use the shape the message names |
| `PRAG0704` | Error | A `[Query<T, R>]` whose `R` has no `[GenerateProjection]` | Add it beside `[MapFrom<T>]` on the result type |
| `PRAG0707` | Error | A query input that generates **no filter**: neither `[Filter]`, nor `required`, nor nullable. The value the caller sends is read and dropped, and a `Single = true` query answers 200 with whichever row comes first | Write `required T` for an input always supplied, or `T?` for an optional one |
| `PRAG0708` | Error | `[GenerateHierarchy]` on an entity with no self-referencing parent key. The parent is found by name — `ParentId` or `{Type}ParentId` — and nothing else matches | Rename the property, or add one. A parent of a different type is not this attribute |
| `PRAG0709` | Error | A `[BindSpecification]` input on a query that declares no `Specification<T>` property — the attribute claims a reader that does not exist | Write the specification the input builds, or drop the attribute and let the property be a filter |
| `PRAG0705` | Warning | A **required** navigation points at a `[SoftDelete]` entity | See below — the subtlest trap in the model |
| `PRAG0706` | Warning | `[ReadAccess]` names an entity owned by a boundary on another database | Read it through that boundary's operations |
| `PRAG0710` | Warning | A DTO looks like it references a navigation, but the name does not match the entity graph | Align the DTO property name or load the data explicitly |
| `PRAG0711` | Warning | `[LoadWith]` asks for include depth greater than 3 | Reduce depth or move to projection |
| `PRAG0716` | Warning | A DTO loads many navigation paths | Consider projection or split queries |
| `PRAG1100` | Error | `[HasOwner]` on a non-`partial` type | Add `partial` |
| `PRAG1104` | Info | The type already declares `OwnerId`, so only the ownership filter is generated | Nothing required; drop the manual property to use the generated one |

`PRAG0600` and `PRAG0602` come from `Pragmatic.SourceGenerator.Analyzers`, which is also what offers
the "make class partial" fix in the IDE.

### Diagnostics over your own code

`Pragmatic.Persistence.Analyzers` inspects application code rather than generated code:

| Code | Severity | Meaning | First fix to try |
|------|----------|---------|------------------|
| `PRAG0680` | Warning | An entity is built with `new` instead of the generated factory | `Entity.Create(...)` |
| `PRAG0681` | Warning | An entity is produced via `default` | `Entity.Create(...)` |
| `PRAG0682` | Warning | An entity is produced via `Activator.CreateInstance` | `Entity.Create(...)` |
| `PRAG0683` | Warning | An entity declares a behavior method — entities are anemic | Move the state change into a mutation or domain action |
| `PRAG0684` | Warning | Non-constant SQL in `FromSqlRaw`/`ExecuteSqlRaw` — a SQL injection hole | Use interpolated `FromSql`/`ExecuteSql`, or pass parameters |
| `PRAG0685` | Info | An aggregate owns many child collections | Split it, or reference other aggregates by id |
| `PRAG0686` | Warning | A type injects another boundary's `DbContext` | Call that boundary's actions/queries, or react to its events |
| `PRAG0687` | Warning | `ExecuteDelete` on a `[SoftDelete]` entity | It goes straight to SQL and is never intercepted, so the row is gone. Use `Remove()`, or `SoftDeleteScope.Suspend()` if you mean it |

## PRAG0600: type must be partial

This is the most common onboarding error.

Why it exists:

- Source generators add members in separate `.g.cs` files.
- Without `partial`, C# cannot merge your declaration with the generated one.

Broken:

```csharp
[Entity]
public class Order
{
    public decimal Total { get; private set; }
}
```

Fixed:

```csharp
[Entity]
public partial class Order
{
    public decimal Total { get; private set; }
}
```

The same rule applies to generated DbContexts, which is what `PRAG0602` enforces.

## Relation attributes are the source of truth

Pragmatic wants one unambiguous relationship model. If you write a manual entity navigation but do not declare the relation with `[Relation.*]`, the generator cannot know whether that navigation is:

- intentional and owned by the persistence model
- a leftover property from an older manual mapping
- incomplete and missing its inverse side

Mixing the two styles is a modelling choice, not an error, and no diagnostic forbids it — an entity may
legitimately declare a plain navigation and let EF Core's conventions map it. What **is** reported is a
relation that cannot work: `PRAG0612` when several relations to the same entity would collide on one
derived name, `PRAG0613` / `PRAG0614` when an `Inverse` does not resolve or resolves to the wrong type,
and `PRAG0615` when two relations would generate the same navigation. The safest rule is still:

- use `[Relation.*]` to describe persistence relationships
- let the generator own the navigation shape
- avoid mixing hand-authored navigation properties with generated ones unless you know exactly why

See [Entity System](02-entity-system.md) and [Repository](05-repository.md) for the broader model.

## PRAG0651: property may need value converter

This warning is not saying the property is invalid. It is saying:

"EF Core may not know how to persist this property type cleanly on every provider."

Common examples:

- custom value objects
- opaque identifier wrappers
- JSON-heavy types
- strongly typed IDs not mapped by a converter

Mitigations:

1. Use a provider-native primitive if the type has no strong domain value.
2. Add a `ValueConverter` in entity configuration.
3. Keep the complex type in the domain model, but persist a simpler representation.

## PRAG0705: required navigations to a soft-deletable parent

The subtlest trap in the persistence model, and one the generator names for you.

Scenario:

- `OrderLine` has a required navigation to `Order`
- both entities use `[SoftDelete]`
- EF Core uses an `INNER JOIN` for the required navigation
- when the parent row is soft-deleted, the child may disappear from query results even if the child itself is not deleted

Why this trap is hard to spot:

- the model looks correct in C#
- the bug appears only after some real data is soft-deleted
- the missing children look like "query randomness"

Mitigations — the diagnostic names all three:

1. `Required = false` on the `[Relation.*]` attribute: EF emits a `LEFT JOIN` and the dependent
   survives with a null reference.
2. Drop `[SoftDelete]` from the target.
3. `IgnoreQueryFilters()` on the queries that must still see the rows.

It stays silent on the two shapes where the disappearance is intended or cannot happen: an owning
parent hiding its own children is not reported.

If your use case is "restore deleted aggregates", also review [Query Filters](07-query-filters.md).

## PRAG0710: DTO references navigation without Include

This warning exists to catch a mismatch between:

- what your DTO shape implies
- what the generator can actually infer from the entity graph

Typical example:

```csharp
[LoadWith<Order>(MaxDepth = 2)]
public sealed class OrderDto
{
    public CustomerSummaryDto Buyer { get; init; } = default!;
}
```

If the entity navigation is named `Customer` rather than `Buyer`, the generator cannot assume that `Buyer` should be auto-included.

Mitigations:

1. Rename the DTO property to match the entity navigation when the intent is one-to-one mapping.
2. Use projection and map the DTO explicitly.
3. Keep the custom DTO property name, but stop expecting `[LoadWith]` to infer it automatically.

For novice teams, the simplest rule is:

- if you rely on auto-loading, keep DTO navigation names aligned with entity navigation names

See [Data Sources & Loading](12-datasource-loading.md) for the loading profile model.

## PRAG0711: include depth greater than 3

This is one of the most important DX warnings because the code often "works" while still being a bad default.

Why it exists:

- each extra include level increases query complexity
- deeper joins usually pull many more columns than the caller needs
- SQL becomes harder to tune and easier to break with provider differences

Depth 4 is not forbidden. The warning means:

"You are entering a zone where projection or split loading is usually easier to reason about."

Mitigations:

1. Reduce `MaxDepth` to the smallest value that satisfies the screen or endpoint.
2. Move to `QueryStrategy.Projection` and select only the fields you need.
3. Split one deep graph into two targeted queries.

Good instinct for novice users:

- if a UI needs a summary, do not load the full object graph just because it is convenient

## PRAG0716: DTO with many navigation paths

This warning is about breadth rather than depth.

Even when each path is shallow, many independent paths can still create:

- large SQL
- duplicate data due to joins
- confusing performance regressions when the DTO evolves

Mitigations:

1. Prefer projection for dashboard-style or report-style DTOs.
2. Split "header data" and "detail collections" into separate queries when they have different lifecycles.
3. Keep `[LoadWith]` for entity-shaped views, not for giant read models.

## When a warning is acceptable

Not every warning must be removed.

A warning is usually acceptable when:

- the query runs rarely
- the dataset is small and bounded
- the behavior is understood and covered by tests
- the team has documented why the tradeoff is intentional

What you want to avoid is silent acceptance. A novice-friendly codebase makes tradeoffs explicit.

## Recommended follow-up reading

- [Getting Started](01-getting-started.md)
- [Entity System](02-entity-system.md)
- [Repository](05-repository.md)
- [Query Filters](07-query-filters.md)
- [Data Sources & Loading](12-datasource-loading.md)
