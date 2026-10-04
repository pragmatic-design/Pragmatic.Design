# Relationships

## The Problem

Entities don't exist in isolation. An Order has LineItems. A Reservation belongs to a Guest. In a traditional EF Core project, you write navigation properties, foreign key properties, and Fluent API configuration for each relationship, all manually.

For a project with 50 entities and 80 relationships, this means hundreds of lines of configuration code that all follows the same pattern.

## The Solution: `[Relation.*]` Attributes

You declare relationships as attributes **on the entity class** (not on individual properties). The source generator produces the navigation properties, foreign key properties, and EF Core configuration.

```csharp
// ═══ What YOU write ═══
[Entity]
[BelongsTo<BillingBoundary>]
[Relation.OneToMany<LineItem>]
[Relation.ManyToOne<Customer>]
public partial class Order
{
    public string OrderNumber { get; private set; } = "";
    public decimal Total { get; private set; }
}
```

**Important**: `[Relation.*]` attributes go on the **class declaration**, not on a property. They tell the source generator "this entity has a relationship with that other entity."

```csharp
// ═══ What the SG generates ═══

// Navigation properties ({Namespace}.Order.Relations.g.cs)
public partial class Order
{
    public ICollection<LineItem> LineItems { get; set; }        // OneToMany → collection
    public Guid CustomerId { get; private set; }                // ManyToOne → FK property
    public Customer Customer { get; set; }                      // ManyToOne → navigation
    internal void SetCustomerId(Guid value)                     // the typed setter for the FK
}

// EF Core configuration (EntityConfig.{Namespace}.Order.g.cs)
builder.HasMany(e => e.LineItems)
    .WithOne(e => e.Order)
    .OnDelete(DeleteBehavior.Cascade);

builder.HasOne(e => e.Customer)
    .WithMany()
    .HasForeignKey(e => e.CustomerId)
    .IsRequired()
    .OnDelete(DeleteBehavior.Restrict);
```

> **The collection is named after the target type, pluralised**: `LineItem` becomes `LineItems`, and
> that is the name you write in `Include(o => o.LineItems)`. The rule is regular: `Category` becomes
> `Categories`, `Child` becomes `Childs`. Name it yourself with
> `[Relation.OneToMany<LineItem>.WithNavigation("Lines")]` when the derived name reads badly.

---

## Relationship Types

### One-to-Many

"An Order has many LineItems." The parent entity declares the relationship.

```csharp
[Entity]
[Relation.OneToMany<LineItem>]
public partial class Order { /* ... */ }
```

What gets generated:
- On `Order`: `public ICollection<LineItem> LineItems { get; set; }`
- On `LineItem`: `public Guid OrderId { get; private set; }` (FK back to parent)
- EF Core config: `HasMany` / `WithOne` / `OnDelete(Cascade)`; see [Delete Behavior](#delete-behavior)

### Many-to-One

"A LineItem belongs to one Order." The child entity declares the relationship.

```csharp
[Entity]
[Relation.ManyToOne<Order>]
public partial class LineItem { /* ... */ }
```

What gets generated:
- On `LineItem`: `public Guid OrderId { get; private set; }` (FK), plus `internal void SetOrderId(...)`
- On `LineItem`: `public Order Order { get; set; }` (navigation, named after the type)
- EF Core config: `HasOne` / `WithMany` / `HasForeignKey` / `OnDelete(Restrict)`

### When to use OneToMany vs ManyToOne

You can declare the relationship from **either side**. The foreign key is the same either way, but
the **delete behaviour is not**, so the choice is not purely stylistic. See
[Delete Behavior](#delete-behavior).

| Declare on... | Use when... |
|---------------|-------------|
| Parent (`[Relation.OneToMany<Child>]`) | You think of the parent as "owning" the children |
| Child (`[Relation.ManyToOne<Parent>]`) | You think of the child as "belonging to" the parent |
| Both sides | You want navigation properties on both entities |

If you declare both, the SG detects they refer to the same relationship and generates a single FK + configuration.

### Many-to-Many

"An Order can have many Tags, and a Tag can be on many Orders."

Declare it with a join entity when the join carries columns of its own:

```csharp
[Entity]
[Relation.ManyToMany<Tag, OrderTag>]        // second type argument: the join entity
public partial class Order { /* ... */ }

// The join entity
[Entity]
[Relation.ManyToOne<Order>]
[Relation.ManyToOne<Tag>]
public partial class OrderTag { /* ... */ }
```

What gets generated:
- On `Order`: `public ICollection<Tag> Tags { get; set; }`
- EF Core config: `builder.HasMany(e => e.Tags).WithMany().UsingEntity<OrderTag>();`

When it carries nothing, the one-argument form is enough (`[Relation.ManyToMany<Tag>]`), and the join
table is created for you; name it with `.WithNavigation("Tags", JoinTable = "OrderTags")`.

---

## `.WithNavigation(...)`: naming and configuring one relation

The bare form derives everything by convention. `.WithNavigation("Name")` takes it over, and carries
the options that have nowhere else to live:

```csharp
[Relation.ManyToOne<User>.WithNavigation("CreatedBy", Inverse = "CreatedOrders")]
[Relation.ManyToOne<User>.WithNavigation("AssignedTo", Inverse = "AssignedOrders")]
public partial class Order { /* ... */ }
```

| Option | On | What it does |
|---|---|---|
| the constructor argument | all | The navigation property's name. Without it, the target type's name (pluralised for collections) |
| `Inverse` | all | The navigation on the other entity that is the other end of *this* relationship |
| `ForeignKey` | `ManyToOne`, `OneToOne` | The FK property's name. Defaults to `{NavigationName}Id` |
| `OnDelete` | `ManyToOne`, `OneToOne`, `OneToMany` | See [Delete Behavior](#delete-behavior) |
| `Required` | `ManyToOne` | Defaults to `true`. `false` makes the FK nullable and the join a `LEFT JOIN` |
| `IsPrincipal` | `OneToOne` | Which side owns the relationship |
| `JoinTable` | `ManyToMany` | Names the auto-generated join table |

**Two relations to the same type need it.** Both would otherwise derive the same navigation name from
the target type, collide, and the generator would keep only the first, which is **PRAG0612**.

⚠️ `Required = true` pointing at a `[SoftDelete]` entity is the subtlest trap in the model: EF turns a
required navigation into an `INNER JOIN`, so soft-deleting the target hides this row too. That is
**PRAG0705**, and it names the three ways out.

---

## Relations are declared, never written

The SG generates the foreign key and the navigation of every `[Relation.*]`. Neither is ever written
by hand: a property typed as a navigation, or a `{Entity}Id` scalar, is **PRAG0619**, and EF Core's
`[ForeignKey]`/`[InverseProperty]` are **PRAG0635**.

```csharp
[Entity]
[Relation.ManyToOne<Reservation>]
[Relation.ManyToOne<Guest>]
public partial class Invoice
{
    // ReservationId, GuestId, Reservation and Guest are generated: nothing to write here.
}
```

The other generators do not need the members in source: `[MapFrom]` DTOs, `[GenerateHierarchy]`,
`[TemporalRelation]`, `[CascadeOn]`, `[Lookup]` and raised events all predict the generated key
from the declared relation, through the same naming rule the generator emits with
(`RelationForeignKeyNaming`). ⚠️ Declare the relation; do not hand-write the key beside it. A foreign
key property on its own reaches nothing (no navigation, no cascade, no constraint in the schema), and
nothing reports it, because a plain property is a legitimate thing to declare.

---

## Cross-Boundary Relationships

Entities from different boundaries can reference each other. The foreign key always crosses; the
navigation crosses only when the boundary declares it reads the other side.

```csharp
// Billing boundary
[Entity]
[Relation.ManyToOne<Reservation>.WithNavigation("Reservation")]   // Reservation is in BookingBoundary
public partial class Invoice : IEntity { /* ... */ }
```

**Always**: the FK property (`ReservationId`) is generated and stored in the database. You can query by
FK. No FK constraint is emitted: the referenced table belongs to the other boundary.

**With `[ReadAccess<Reservation>]` on `BillingBoundary`**: the navigation `Reservation` is generated
too. The attribute adds a read-only `DbSet<Reservation>` to Billing's DbContext, so EF maps the
navigation by convention, `Include(i => i.Reservation)` works, and DTOs can carry it. It is read-only:
a mutation that nests a write through it is `PRAG0444`, and the context refuses to commit changes to
an entity it only reads.

**Without `[ReadAccess]`**: only the key exists. Load the other side through its own boundary:

```csharp
var invoice = await invoiceRepo.GetByIdAsync(invoiceId, ct);
var reservation = await reservationRepo.GetByIdAsync(invoice.ReservationId, ct);
```

---

## Delete Behavior

**The side that declares the relation decides.** This is the part most worth reading twice, because the
default is not the cautious one:

| Declared as | On the `HasOne` side | On the `HasMany` side |
|---|---|---|
| `[Relation.OneToMany<Child>]` on the parent | `Cascade` | `Cascade` |
| `[Relation.ManyToOne<Parent>]` on the child | `Restrict` | n/a |
| `[Relation.OneToOne<T>]` | `Restrict` | n/a |
| `[Relation.ManyToMany<T>]` | `Cascade` | `Cascade` |
| an inverse collection the generator derives on its own | n/a | `NoAction` |

So a parent that declares `[Relation.OneToMany<LineItem>]` gets:

```csharp
// ═══ generated in the entity configuration ═══
builder.HasMany(e => e.LineItems)
    .WithOne(e => e.Order)
    .OnDelete(DeleteBehavior.Cascade);
```

**Deleting the order deletes its line items, in the database.** That is a real delete, not a soft one:
`[SoftDelete]` changes what `Remove()` and the save-time interceptor do, and does not change this
foreign key.

If that is not what you want, say so on the relation:

```csharp
[Relation.OneToMany<LineItem>.WithNavigation("LineItems", OnDelete = DeleteBehavior.Restrict)]
```

`Restrict` refuses the delete while children exist; `NoAction` leaves it to the database, which usually
means the same refusal from the constraint itself.

### Why the default is Cascade here

A one-to-many declared **from the parent** is the shape of an aggregate: the children have no life
outside it, and leaving them behind would leave rows nothing can reach. Declared **from the child**
(`[Relation.ManyToOne<Parent>]`) the relationship reads the other way: the child references something
that exists on its own, so the default protects the parent instead.

The consequence to keep in mind: the two directions of *the same* relationship are not interchangeable
in this one respect. Which side you write it on changes what a delete does.

## Related guides

- [Entity System](02-entity-system.md): attributes on the entity itself
- [Eager Loading](20-eager-loading.md): which navigations a query loads, and who decides
- [Boundaries](17-boundaries.md): why a relation across a boundary has no navigation
