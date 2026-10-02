---
title: "Entity Configuration"
description: "EF Core needs to know how to map your entities to the database: which property is the primary key, which properties need indexes, how relationships are configur"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Persistence/docs/efcore/03-entity-configuration.md
sidebar:
  order: 24
---
## The Problem

EF Core needs to know how to map your entities to the database: which property is the primary key, which properties need indexes, how relationships are configured, what the column types should be. Without configuration, EF Core guesses — and sometimes guesses wrong.

Writing `IEntityTypeConfiguration<T>` for each entity is repetitive: every entity needs a primary key on `PersistenceId`, every `[LogicKey]` property needs a unique index, every `[SoftDelete]` entity needs a query filter.

## The Solution

The source generator produces a complete `IEntityTypeConfiguration<T>` for each entity, based on the attributes you've declared.

```csharp
// ═══ Generated: EntityConfig.{Namespace}.Order.g.cs ═══
internal sealed class OrderEntityConfig : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");                       // the type name, pluralised
        builder.HasKey(e => e.PersistenceId);

        // A Guid key is assigned by the trait at construction, so the store must not claim it
        builder.Property(e => e.PersistenceId).ValueGeneratedNever();

        // [LogicKey] → unique index
        builder.HasIndex(e => e.OrderNumber).IsUnique();

        // private set → EF writes the field, not the property
        builder.Property(e => e.OrderNumber).UsePropertyAccessMode(PropertyAccessMode.Field).IsRequired();

        // [SoftDelete] → a *named* EF query filter, plus an index on the flag
        builder.HasQueryFilter("SoftDelete", e => !e.IsDeleted);
        builder.HasIndex(e => e.IsDeleted);

        // [Relation.OneToMany<LineItem>] declared on the parent → Cascade
        builder.HasMany(e => e.LineItems)
            .WithOne(e => e.Order)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

Three things that are **not** in there:

- **The concurrency token.** `[ConcurrencyAware]` produces a *shadow* property, declared in the
  generated DbContext and shaped by the provider — there is no `e.RowVersion` to configure.
- **The audit fields.** `[Auditable]` adds the four properties to the entity; nothing configures them
  here, so they take EF's conventions.
- **The filtered unique index.** On a `[SoftDelete]` entity the logic-key index has to exclude deleted
  rows, or a re-insert with the same key fails. That filter is raw provider SQL, so it is emitted in
  the DbContext where the provider is known:
  `modelBuilder.Entity<Order>().HasIndex(e => e.OrderNumber).IsUnique().HasFilter("\"IsDeleted\" = 0")`.

## Property Mapping Rules

The SG applies these rules when generating property configuration:

| C# Type | Generated Configuration | Notes |
|---------|------------------------|-------|
| `string` | `.IsRequired()` if not nullable | `.HasMaxLength(n)` if `[MaxLength(n)]` present |
| `decimal` | `.HasPrecision(18, 2)` | Default precision for monetary values |
| `byte[]` (RowVersion) | `.IsRowVersion()` | Only for `[ConcurrencyAware]` entities |
| `enum` | Stored as `int` | Default EF Core behavior |
| `DateTimeOffset` | `.IsRequired()` if not nullable | Audit fields: `CreatedAt` is required, `UpdatedAt` is optional |

## Relationship Configuration

For each `[Relation.*]` attribute on the entity class, the SG generates the corresponding Fluent API calls:

```csharp
// [Relation.OneToMany<LineItem>] on Order — declared by the parent, so Cascade
builder.HasMany(e => e.LineItems)
    .WithOne(e => e.Order)
    .OnDelete(DeleteBehavior.Cascade);

// [Relation.ManyToOne<Customer>] on Order — declared by the child, so Restrict
builder.HasOne(e => e.Customer)
    .WithMany()
    .HasForeignKey(e => e.CustomerId)
    .IsRequired()
    .OnDelete(DeleteBehavior.Restrict);

// [Relation.OneToOne<RelationSide>] — the principal holds the FK
builder.HasOne(e => e.RelationSide)
    .WithOne()
    .HasForeignKey<Order>(e => e.RelationSideId)
    .OnDelete(DeleteBehavior.Restrict);
```

The navigation names come from the **target type**, pluralised for collections — `LineItems`, not any
property you wrote. `.WithNavigation("Items")` on the relation renames it.

The delete behaviour depends on **which side declares the relation**: `[Relation.OneToMany]` on the parent gives `Cascade`, `[Relation.ManyToOne]` on the child gives `Restrict`, and an inverse the generator derives on its own gets `NoAction`. See [Relationships](/modules/persistence/04-relationships/#delete-behavior) — the default is not the cautious one.

## Two Levels of Query Filters

The generated configuration includes **two complementary filtering mechanisms**:

### 1. EF Core Global Query Filter (in EntityConfiguration)

```csharp
builder.HasQueryFilter("SoftDelete", e => !e.IsDeleted);
```

EF Core's built-in mechanism, applied at the model level. It is a **named** filter, so it can be lifted
on its own with `IgnoreQueryFilters(["SoftDelete"])` rather than all-or-nothing.

### 2. Repository-Level Query Filter (IQueryFilter pipeline)

```csharp
// In the generated repository
var filter = _filterProvider?.GetCombinedFilter<Order>(context);
query = query.Where(filter);
```

This is Pragmatic's filter pipeline. It supports runtime toggle (`IQueryFilterToggle`), filter modes, and navigation-level filtering.

**Why both?** The EF Core filter provides a safety net — even if code bypasses the repository and queries the `DbSet` directly, soft-deleted records are still excluded. The Pragmatic filter pipeline adds runtime configurability (disable, modes, navigation filtering).

## Inheritance Configuration

For entities with `[Inheritance]`, a separate static configuration class is generated:

```csharp
// ═══ Generated: {Namespace}.Fee.InheritanceMapping.g.cs ═══
public static class FeeInheritanceConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Fee>()
            .HasDiscriminator<string>("FeeType")
            .HasValue<ServiceFee>("ServiceFee")
            .HasValue<CancellationFee>("CancellationFee");
    }
}
```

This is called in `OnModelCreating` after the `ApplyConfiguration` calls:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    // Entity configurations
    modelBuilder.ApplyConfiguration(new FeeEntityConfiguration());
    modelBuilder.ApplyConfiguration(new ServiceFeeEntityConfiguration());

    // Inheritance mappings (after configurations)
    FeeInheritanceConfiguration.Configure(modelBuilder);
}
```

The order matters: entity configurations define properties and relationships, then inheritance configuration defines the discriminator and type mappings.
