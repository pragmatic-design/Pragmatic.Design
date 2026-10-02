# Boundaries

> Logical partitions for entities, DbContexts, and transaction scopes.

---

## What Is a Boundary?

A boundary is a marker class that groups entities into a **logical domain partition**. Each boundary maps to:

- A **DbContext** (with only the entities in that boundary)
- A **keyed IUnitOfWork** (for transactions)
- A set of **repositories** (one per entity in the boundary)

```csharp
// Define boundaries — empty marker classes
public class BillingBoundary;
public class BookingBoundary;
public class CatalogBoundary;
```

Persistence asks for no attribute on them: `[BelongsTo<T>]` on the entity is what assigns it, and the
names of the generated types come from the class with the `Boundary` suffix trimmed —
`BillingBoundary` → `BillingDbContext`, `AddBillingDbContext(...)`.

Add `[Boundary]`, from `Pragmatic.Actions`, when the same marker should also group the namespace's
**operations** into an interface. That one requires `partial` (**PRAG0406**):

```csharp
[Boundary]
public partial class BillingBoundary;
```

It collects every action, mutation and query under the boundary's namespace into `IBillingActions`,
and one sub-interface per intermediate namespace segment: `Billing.Invoices.Mutations.*` lands on
`IBillingInvoicesActions`. Segments that name an operation kind — `Mutations`, `Actions`, `Queries`,
`Endpoints`, `Entities`, `Dtos`, `Events`, `Errors`, `Services`, `Validators` and a few more — end the
path rather than becoming part of it. A sub-boundary deeper than two levels is reported as
**PRAG0412**, a warning; `[SubBoundary(Name = "…")]` names one explicitly instead of inferring it.

```csharp
// Assign entities to boundaries
[Entity]
[BelongsTo<BillingBoundary>]
public partial class Invoice { /* ... */ }

[Entity]
[BelongsTo<BookingBoundary>]
public partial class Reservation { /* ... */ }

[Entity]
[BelongsTo<CatalogBoundary>]
public partial class Property { /* ... */ }
```

---

## Why Boundaries Exist

### 1. Focused DbContexts

Without boundaries, one `DbContext` contains every entity in your application. For large applications (50+ entities), this causes:
- Slow model building at startup
- Confusing `IntelliSense` with hundreds of `DbSet` properties
- No isolation — any code can query any entity

With boundaries, each DbContext contains only 10-30 related entities:

```csharp
// Generated: BillingDbContext has only billing entities
public class BillingDbContext : PragmaticDbContext
{
    public DbSet<Invoice> Invoices { get; set; }
    public DbSet<LineItem> LineItems { get; set; }
    public DbSet<Fee> Fees { get; set; }
    // ... only billing entities
}
```

### 2. Transaction Isolation

`SaveChanges()` within a boundary is atomic — all changes to entities in that boundary are saved in one transaction. Changes to entities in different boundaries are **independent transactions**.

This is by design. Boundaries are the unit of transactional consistency. If you need to coordinate across boundaries, use domain events (eventually consistent) or explicit distributed transactions (rarely needed).

### 3. Database Flexibility

Each boundary can use a different database:

```csharp
// Billing → PostgreSQL
services.AddBillingDbContext(o => o.UseNpgsql(billingConnection));

// Catalog → PostgreSQL (same or different server)
services.AddCatalogDbContext(o => o.UseNpgsql(catalogConnection));

// Analytics → read replica
services.AddAnalyticsDbContext(o => o.UseNpgsql(replicaConnection));
```

---

## How Boundaries Work in DI

The source generator registers boundary-scoped services using **keyed DI** (a .NET 8+ feature):

```csharp
// Generated DI registration
services.AddKeyedScoped<DbContext, BillingDbContext>(typeof(BillingBoundary));
services.AddKeyedScoped<IUnitOfWork, BillingUnitOfWork>(typeof(BillingBoundary));

services.AddKeyedScoped<DbContext, BookingDbContext>(typeof(BookingBoundary));
services.AddKeyedScoped<IUnitOfWork, BookingUnitOfWork>(typeof(BookingBoundary));
```

### Resolving in Mutations

The generated `MutationInvoker` uses `[FromKeyedServices]` to get the right unit of work:

```csharp
public CreateInvoiceMutationInvoker(
    IRepository<Invoice> repository,
    [FromKeyedServices(typeof(BillingBoundary))] IUnitOfWork unitOfWork,
    IServiceProvider serviceProvider)
```

### Resolving in Endpoints

The generated endpoint handler uses keyed DI for the DbContext:

```csharp
app.MapGet("api/v1/invoices", async (
    [FromKeyedServices(typeof(BillingBoundary))] DbContext dbContext,
    [FromServices] IQueryExecutor executor,
    CancellationToken ct
) => { /* ... */ });
```

### Resolving Manually

When you need the `IUnitOfWork` in your own service:

```csharp
public class InvoiceService(
    IRepository<Invoice> invoices,
    IServiceProvider serviceProvider)
{
    public async Task CreateInvoice(/* ... */, CancellationToken ct)
    {
        invoices.Add(invoice);
        var uow = serviceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(BillingBoundary));
        await uow.SaveChangesAsync(ct);
    }
}
```

---

## Cross-Boundary Relationships

Entities from different boundaries can reference each other via foreign keys:

```csharp
// Invoice (BillingBoundary) references Reservation (BookingBoundary).
// ReservationId is generated by the relation — never written by hand (PRAG0619).
[Entity]
[Relation.ManyToOne<Reservation>.WithNavigation("Reservation")]
public partial class Invoice : IEntity
{
}
```

### What Works

- **FK property** — `ReservationId` is generated and stored in the database
- **Querying by FK** — `query.Where(i => i.ReservationId == reservationId)` works
- **Filtering by FK** — `[Filter]` on `ReservationId` in a query class works

### What Works Only With `[ReadAccess<Reservation>]` on the Billing Boundary

- **Navigation property** — `invoice.Reservation` is generated, read-only, because the attribute
  gives Billing's DbContext a `DbSet<Reservation>` to join
- **Include()** — `query.Include(i => i.Reservation)` and DTO includes work through that navigation
- Writing through it does not: a nested write is `PRAG0444`, and the context refuses to commit an
  entity it only reads

### What Does Not Work

- **Navigation property without `[ReadAccess]`** — only the key is generated
- **FK constraint** — the referenced table belongs to the other boundary, so none is emitted
- **Cascade operations** — EF Core cascade delete across boundaries is not supported
- **A self-referential many-to-many of the read entity** — the navigation is dropped in the reading
  context

### The Read Entity Arrives With Its Configuration

`[ReadAccess<T>]` applies the owner's whole per-entity configuration, so the closure of `T`'s graph
comes with it: the types the reader does not own go into `modelBuilder.Ignore<T>()`. That closes a
many-to-many towards **another** type, because ignoring the target removes the skip navigation with
it.

A **self-referential** one cannot close that way — the target is the entity being read, so it
survives while its join entity is ignored. The reading context therefore drops those navigations by
name:

```csharp
// Generated in the reading boundary's context, after ApplyConfiguration
modelBuilder.Entity<KnowledgeItem>().Ignore("SeeAlso");
modelBuilder.Entity<KnowledgeItem>().Ignore("SeenFrom");
```

What you lose is traversing that one link from the reading side; what you would otherwise get is EF
refusing the model at **first use** — "the skip navigation … doesn't have a foreign key" — over a
clean compilation, which means every request of the boundary answering 500. `[ReadAccess]` gives you
the other boundary's rows, not its graph.

### Loading Cross-Boundary Data

Load related data separately:

```csharp
var invoice = await invoiceRepo.GetByIdAsync(invoiceId, ct);
var reservation = await reservationRepo.GetByIdAsync(invoice.ReservationId, ct);
```

Or use a query with a join (within the same boundary only).

---

## Boundaries and Actions

The boundary concept appears in two places:

### 1. Persistence (`[BelongsTo<T>]`)

Defines which DbContext owns the entity. This is the most common use.

```csharp
[Entity]
[BelongsTo<BillingBoundary>]
public partial class Invoice { /* ... */ }
```

### 2. Actions (`[BelongsTo<T>]`)

DomainActions also use `[BelongsTo<T>]` to route to the correct `IUnitOfWork`:

```csharp
[DomainAction]
[BelongsTo<BillingBoundary>]
public partial class CreateInvoiceAction : DomainAction<Invoice>
{
    public override async Task<Result<Invoice, IError>> Execute(CancellationToken ct)
    {
        // The generated invoker uses BillingBoundary's IUnitOfWork
    }
}
```

There are two distinct `BelongsToAttribute` classes (one in `Pragmatic.Persistence.Entity`, one in `Pragmatic.Actions.Attributes`) but they serve the same purpose: routing the operation to the correct boundary's services.

---

## Boundary Configuration

### In the Host

Inside a Pragmatic host, nothing: the generated host registers each boundary's DbContext from the
database's `ConfigKey`, then the repositories and the query filters, before any `IStartupStep` runs.

Outside one — a test project, a console app — the same calls by hand:

```csharp
services.AddBillingDbContext(o => o.UseNpgsql(config.GetConnectionString("Billing")));
services.AddBookingDbContext(o => o.UseNpgsql(config.GetConnectionString("Booking")));

// one call per DbContext; there is no Add{Boundary}Repositories()
services.AddPragmaticPersistenceRepositories<BillingDbContext>();
services.AddPragmaticPersistenceRepositories<BookingDbContext>();

services.AddMyAppQueryFilters();
```

### Migrations

The migration context is **per database, not per boundary** — boundaries that share a connection share
the schema, so they share the context that describes it:

```csharp
// Generated, one per declared database, named after it
public partial class SalesDatabaseMigrationDbContext : DbContext { }

// When no per-database topology is declared, a single one:
public partial class MigrationDbContext : DbContext { }
```

`Pragmatic.Migrations` reads that schema and applies a declarative diff at startup — no
`Add-Migration`, no migration files. See the [migrations guide](../../Pragmatic.Migrations/README.md).

---

## Design Decisions

### Why not one DbContext per entity?

Too granular. You'd need cross-DbContext transactions for every operation that touches two related entities (e.g., Invoice + LineItem). A boundary groups entities that change together.

### Why not one DbContext for everything?

Works for small applications. Breaks down at scale — slow startup, no isolation, no database-per-service option. Boundaries let you start monolithic and split later.

### When should I create a new boundary?

| Signal | Action |
|--------|--------|
| Entities change together in the same transaction | Same boundary |
| Entities are in different domain modules | Different boundaries |
| You need a different database/connection | Different boundaries |
| You need different migration schedules | Different boundaries |

A good starting heuristic: **one boundary per module/assembly**.

---

## Related Guides

- [DbContext Generation](efcore/01-dbcontext-generation.md)
- [Mutation Pipeline](16-mutation-pipeline.md) — How mutations use boundary-keyed DI
- [Migration Patterns](efcore/08-migration-patterns.md)
- [Relationships](04-relationships.md) — Cross-boundary relationships
