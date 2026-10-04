# Entity System

This guide explains the beginner mental model for Pragmatic entities:

- you declare intent with attributes
- the generator adds the repetitive persistence members
- EF Core support is generated only when the project also references `Pragmatic.Persistence.EFCore` and the `Pragmatic.SourceGenerator` analyzer

If you are new to the stack, read this page before writing your first entity.

## The core rule: attributes describe intent

In a normal EF Core project, every entity tends to accumulate the same infrastructure code:

- persistence ID
- factory methods
- private-set mutation helpers
- indexes for business keys
- navigation wiring

Pragmatic moves that boilerplate into generated code.

You write the business shape. The generator writes the persistence plumbing.

## `[Entity]`

`[Entity]` marks a type as a persistence entity and tells the generator what identifier type it uses.

```csharp
[Entity]
[BelongsTo<SalesBoundary>]
public partial class Order : IEntity
{
    public decimal Total { get; private set; }
}
```

What this means:

- the type participates in generated persistence artifacts
- the entity gets a generated `PersistenceId`
- repositories and EF Core configuration are generated for it, when EF Core generation is active

**Declare `IEntity` yourself.** It is the one interface the generator never adds: it writes the
*members* that satisfy it, and the repository it generates implements `IRepository<Order>`,
whose constraint is `where TEntity : class, IEntity`. Leave the interface out and that constraint
fails with a CS0311 in generated code you did not write. The trait interfaces are the opposite:
`IAuditable`, `ISoftDelete` and `IAuditedEntity` are added to the base list for you, unless you already
declared them.

## `partial` is mandatory

Every generated persistence type must be declared as `partial`.

Why:

- your source file contains one part of the class
- the generator emits another part in a `.g.cs` file
- C# merges them only when both declarations are `partial`

Broken:

```csharp
[Entity]
public class Order
{
    public decimal Total { get; private set; }
}
```

Correct:

```csharp
[Entity]
public partial class Order
{
    public decimal Total { get; private set; }
}
```

Forget it and you get **PRAG0600**, which carries a code fix: the IDE offers to add the keyword.

## What gets generated

For a simple entity, the generator adds:

- `PersistenceId`
- `Id` convenience alias
- `Create(...)` factory
- setter helpers for `private set` properties
- repository and EF Core configuration, when EF Core generation is active

Example source:

```csharp
[Entity]
[BelongsTo<SalesBoundary>]
public partial class Order
{
    [LogicKey]
    public string OrderNumber { get; private set; } = "";
    public decimal Total { get; private set; }
}
```

The generated part, verbatim in shape:

```csharp
// Sales.Order.Traits.g.cs (note: no base list, that half is yours)
public partial class Order
{
    // IEntity
    public global::System.Guid PersistenceId { get; set; } = global::System.Guid.CreateVersion7();

    public global::System.Guid Id => PersistenceId;
}
```

The key is assigned at construction, not by the database and not by an interceptor. Which files appear
depends on the attributes on the entity and on whether EF Core generation is active.

## The key is a Guid

There is no ID type to choose: `PersistenceId` is a `Guid`, a version 7 value assigned at
construction. `[Entity]` takes no type argument, because every other answer is worse in the same
ways: a natural key spreads into every foreign key, a string key scatters the primary index, and on
a tenant entity a domain-assigned key collides between tenants.

An identifier that comes from elsewhere (a code from a system being replaced, a country's `"IT"`)
is an ordinary property with its own uniqueness (`[LogicKey]`, or `[Unique(nameof(LegacyCode))]`),
where it can be queried and corrected without touching a foreign key.

## `PersistenceId` vs business identifiers

Pragmatic separates two ideas:

- `PersistenceId`: the technical identifier used for persistence identity
- business identifiers such as order number or SKU: human-facing keys

Do not overload one to behave like the other unless the business model truly demands it.

## Generated `Create(...)`

For non-abstract entities the generator produces a static `Create(...)` factory that takes the
required members as parameters and assigns the persistence ID. Abstract entities get none: there is
nothing to construct.

Typical outcome:

```csharp
var order = Order.Create("ORD-001", 120m);
```

Why this is useful:

- creation stays consistent
- required values are obvious at the call site
- the ID strategy stays centralized

For novice users, the simplest rule is:

- prefer the generated `Create(...)` when it exists
- do not manually assign generated persistence members unless the model explicitly requires it

## Generated setters

Properties with `private set` can still be updated through generated typed setters.

That keeps the public surface controlled while still allowing generated mutation pipelines and repository helpers to work cleanly.

Example source:

```csharp
[Entity]
public partial class Product
{
    public string Name { get; private set; } = "";
    public decimal Price { get; private set; }
}
```

Typical generated helpers:

```csharp
public partial class Product : IChangeTracking
{
    internal void SetName(string value);      // internal, not public
    internal void SetPrice(decimal value);

    // and the change tracking they feed
    public IReadOnlySet<string> ModifiedProperties { get; }
    public IReadOnlySet<string> CollectionsModified { get; }
    public bool IsNew { get; set; }
    public void ResetModifiedProperties();
}
```

Two things the shape tells you. They are **`internal`**: the mutation pipeline and the generated
repository write through them, application code outside the boundary does not. And each one *tracks*:
it returns early when the value is unchanged, and records the property name in `ModifiedProperties`,
which is what lets change-aware validation and the roll-ups know what actually moved.

## `[LogicKey]`

Use `[LogicKey]` for a human-meaningful business identifier such as:

- SKU
- order number
- invoice number
- slug

Example:

```csharp
[Entity]
public partial class Product
{
    [LogicKey]
    public string Sku { get; private set; } = "";

    public string Name { get; private set; } = "";
}
```

What it gives you:

1. A unique index in the generated EF Core configuration, **partial** when the entity is
   `[SoftDelete]`, so a deleted row does not stop you re-using the same SKU.
2. A generated lookup on the concrete repository class: `GetBySkuAsync(sku, ct)`.

Two `[LogicKey]` properties make **one composite key**, and the lookup names both parts:

```csharp
[LogicKey] public string Code { get; private set; } = "";
[LogicKey] public int Season { get; private set; }
// → await repo.GetByCodeAndSeasonAsync(code, season, ct)
```

### The order of the parts

Position is not cosmetic. It decides two things:

- the columns of the unique index, so which one is the **leading column**, the only one the index can
  be searched by on its own;
- the parameters of the generated lookup, so `GetByCodeAndSeasonAsync(code, season)` and not the other
  way round.

**By default the order is the order you declare the properties in.** Say it explicitly when the
positions carry meaning, which, with two strings, they always do:

```csharp
[LogicKey(Order = 1)] public string CountryCode { get; private set; } = "";
[LogicKey(Order = 2)] public string Vat { get; private set; } = "";
// → index on (CountryCode, Vat)
// → await repo.GetByCountryCodeAndVatAsync(countryCode, vat, ct)
```

Lower first; parts that leave `Order` unset keep the order they are declared in. So an entity written
before `Order` existed generates exactly what it generated before, and adding `Order` to an entity is
a migration only if you actually change the sequence.

⚠️ `Order` is unset on *both* parts or on neither. Unset means `0`, so
`[LogicKey(Order = 1)]` on one property of two puts the **other** one first, the opposite of what it
looks like.

⚠️ Without `Order`, moving one of the two properties up or down in the class is a schema change *and*
a signature change. The build stays green and every caller passing the arguments positionally now
passes them swapped. This is the reason to write the positions down.

⚠️ Adding a second `[LogicKey]` to an entity that already has one changes the unique index from one
column to two: a migration that drops the old index.

### A key that includes a foreign key

A relation's key is a generated member, so no attribute can sit on it. When a part of the domain key
is a foreign key, as in a membership identified by (workspace, external id), declare the whole key on the
**class**, naming the parts in order:

```csharp
[Entity]
[Relation.OneToMany<Member>]
public partial class Workspace : IEntity { }

[Entity]
[LogicKey("WorkspaceId", nameof(ExternalId), Scope = UniquenessScope.Global)]
public partial class Member : IEntity
{
    public string ExternalId { get; private set; } = "";
}
// → index on (WorkspaceId, ExternalId)
// → await repo.GetByWorkspaceIdAndExternalIdAsync(workspaceId, externalId, ct)
```

The generated key has no symbol to `nameof`, so it is a string; a string that matches neither a
property nor a generated key is `PRAG0636`. One entity uses one form: `[LogicKey]` on the class and on
a property at once is `PRAG0637`. Writing the key by hand to put the attribute on it is `PRAG0619`.

Important nuance:

- `GetBySkuAsync(...)` is generated on `Product.Repository` (nested class)
- it is not part of the stable `IRepository<Product>` abstraction

That distinction matters for dependency injection:

- inject `IRepository<TEntity>` for stable CRUD/specification behavior
- inject the concrete repository when you need logic-key helpers

## `[GeneratedValue]`

`[GeneratedValue]` is for generated business keys that follow a format.

Example:

```csharp
[Entity]
public partial class Order
{
    [GeneratedValue("ORD-{YYYY}{MM}-{SEQ:5}")]
    public string OrderReference { get; private set; } = "";

    public decimal Total { get; private set; }
}
```

The placeholders are these seven, and no others; anything else in the string is a literal:

| Token | Value | Cost |
|---|---|---|
| `{YYYY}`, `{YY}` | year, 4 or 2 digits | from `LifecycleContext.Now`, not `DateTime.Now` |
| `{MM}`, `{DD}` | month, day, 2 digits | same |
| `{RANDOM:N}` | `N` alphanumeric characters | `Random.Shared`: **not** cryptographic, do not use it as a secret |
| `{GUID:N}` | the first `N` characters of a dashless GUID (clamped at 32) | none |
| `{SEQ:N}` | a database sequence value, zero-padded to `N` | a round trip: the generator becomes async and takes the boundary's `DbContext` |

Use this when the business key has a real formatting rule.

Do not use it just to make IDs look nicer. A business key should exist because the business uses it.

### Two limits, before you reach for it

**It runs only under a create mutation.** The generator is invoked from the mutation invoker, and
only when the mutation is `MutationMode.Create`. An entity built any other way (a domain action
calling `Entity.Create()`, a seeder, a bulk insert) gets the property's default and no warning.
The attribute is written on the entity, next to `[Auditable]` and `[SoftDelete]`, which do hold on
every path; this one does not, and the difference is not visible at the declaration.

**A sequence is unique, not gapless.** A create that draws a number and then fails has spent it, and
nothing gives it back: that is what a database sequence is, on every provider. For a reference
people quote in a meeting it is the right trade. For a number that must be consecutive by law
(invoices in most of Europe) it is not, and Pragmatic has no mechanism for that: it needs a counter
committed in the same transaction as the row, with the contention that implies. The example above
is deliberately not an invoice.

## `[PartOf<TParent>]`

`[PartOf<TParent>]` declares that an entity has no life of its own: it is part of its parent's
aggregate, and it is written through the parent.

```csharp
[Entity]
[PartOf<Order>]
public partial class LineItem
{
    public string Description { get; private set; } = "";
    public decimal Amount { get; private set; }
}
```

An operation on `Order` may then carry `LineItem` DTOs and have them created, updated and removed
alongside the order, in the same transaction. Without the attribute it may not, and the generator says
so at build time rather than writing a row past the permissions, validation and events that row's own
operations would have applied.

The relation cannot answer this on its own. `Invoice` declares `[Relation.OneToMany<LineItem>]` and
`Property` declares `[Relation.OneToMany<RoomType>]` (the same metadata), yet a line item exists only
inside its invoice, while a room type has its own mutations, endpoints and permissions. Which of the
two a relation is, is a fact about the domain, and this is where you state it.

It follows that a `[PartOf<T>]` entity cannot also carry a `[Resource]` or a `Mutation<T>` of its own:
that is a contradiction between two declarations, and it is reported as one: **PRAG2611** for the
resource, **PRAG0438** for the mutation.

## Relations belong in attributes

Pragmatic expects persistence relations to be declared with `[Relation.*]`.

Examples:

```csharp
[Relation.OneToMany<OrderLine>]
[Relation.ManyToOne<Customer>.WithNavigation("Customer", Inverse = "Orders")]
```

Why the explicit model matters:

- navigation names become predictable
- inverse relations can be validated
- generated configuration stays coherent

For novice users, the safest rule is:

- let relation attributes be the source of truth
- avoid mixing ad-hoc manual navigation properties with generated relation metadata

## `[ValueObject]`

`[ValueObject]` is for immutable value-centric types, usually represented as `record`s.

Example:

```csharp
[ValueObject]
public partial record EmailAddress
{
    public string Value { get; init; } = "";

    private static Result<EmailAddress, ValidationError> Validate(string value)
    {
        if (!value.Contains('@'))
            return ValidationError.For("Email", "validation.email");

        return new EmailAddress { Value = value };
    }
}
```

Two factories come out of that, each under its own condition:

| Generated | When | Shape |
|---|---|---|
| `Create(...)` | the type declares a **`private static Validate`** method and you have not written `Create` yourself | mirrors `Validate`'s parameters and return type, and delegates straight to it |
| `CreateUnsafe(...)` | the type has a constructor and you have not written `CreateUnsafe` yourself | calls that constructor, skipping validation; deserialization and trusted paths only |

The type must be `partial` (**PRAG2700**) and, for `Create`, must have that `Validate` method
(**PRAG2701**). A `record` is the usual shape but nothing requires it.

Use a value object when:

- equality should be by value, not by identity
- the type has validation rules of its own
- the concept is richer than a raw primitive

## Common mistakes

### Treating `LogicKey` as the entity ID

Keep the technical ID and the business key separate unless the business key is truly immutable and universal.

### Forgetting `partial`

This is still the most common onboarding mistake.

### Expecting interface injection to expose convenience helpers

Stable interfaces intentionally stay small. If you need `GetBySkuAsync(...)`, inject the concrete generated repository.

### Adding business formatting before defining business rules

`[GeneratedValue]` and `[LogicKey]` add constraints. Use them after the business semantics are clear, not before.

## Recommended follow-up reading

- [Getting Started](01-getting-started.md)
- [Repository](05-repository.md)
- [Query Filters](07-query-filters.md)
- [Diagnostics Guide](14-diagnostics.md)
