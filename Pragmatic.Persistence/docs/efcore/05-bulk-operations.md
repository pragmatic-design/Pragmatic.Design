# Bulk Operations

## The Problem

`SaveChangesAsync()` is the right default for normal business writes, but it is a poor fit for large imports, wide updates, or provider-specific upsert flows.

For those cases, the generated repository exposes bulk methods that bypass normal change tracking and execute provider-specific SQL.

## Current API shape

Today, the generated concrete repository class is the primary entry point for bulk operations.

Typical methods on the generated repository:

```csharp
public Task<int> BulkUpdateAsync(
    ISpecification<TEntity> filter,
    Action<UpdateSettersBuilder<TEntity>> updateAction,
    CancellationToken ct = default);

public Task<int> BulkDeleteAsync(
    ISpecification<TEntity> filter,
    CancellationToken ct = default);

public Task<int> BulkInsertAsync(
    IReadOnlyList<TEntity> entities,
    BulkInsertOptions? options = null,
    CancellationToken ct = default);

public Task<int> BulkUpsertAsync(
    IReadOnlyList<TEntity> entities,
    UpsertOptions? options = null,
    CancellationToken ct = default);

public Task<int> UpsertAsync(
    TEntity entity,
    UpsertMatch matchOn = UpsertMatch.PrimaryKey,
    CancellationToken ct = default);
```

These methods are generated onto the concrete repository — on **every** entity, not only some — so
inject that type when you need them. There is no separate `IBulkOperations<TEntity>` service to
resolve. The filter parameter is `ISpecification<TEntity>`; `Spec<T>.Where(...)` returns a
`Specification<T>`, which implements it.

## Usage examples

### Bulk update

```csharp
await products.BulkUpdateAsync(
    Spec<Product>.Where(p => p.CategoryId == categoryId),
    setters => setters.SetProperty(p => p.Price, p => p.Price * 1.1m),
    ct);
```

### Bulk delete

```csharp
await orders.BulkDeleteAsync(
    Spec<Order>.Where(o => o.CreatedAt < cutoffDate),
    ct);
```

If the entity uses `[SoftDelete]`, the generated repository performs a soft-delete update rather than a physical delete.

### Bulk insert

```csharp
await products.BulkInsertAsync(
    importedProducts,
    new BulkInsertOptions { BatchSize = 1000, CommandTimeout = 60 },
    ct);
```

### Bulk upsert

```csharp
await products.BulkUpsertAsync(
    externalProducts,
    new UpsertOptions { MatchOn = UpsertMatch.LogicKey },
    ct);
```

## Bulk descriptor generation

The generator emits a zero-reflection `BulkEntityDescriptor<TEntity>` inside the generated repository class:

```csharp
private static readonly BulkEntityDescriptor<Order> _bulkDescriptor = new()
{
    Columns =
    [
        ("PersistenceId", BulkColumnRole.Key),
        ("OrderNumber", BulkColumnRole.LogicKey),
        ("Total", BulkColumnRole.Regular),
        ("CreatedAt", BulkColumnRole.InsertOnly),
        ("UpdatedAt", BulkColumnRole.UpdateOnly)
    ],
    ReadValue = static (entity, prop) => prop switch
    {
        "PersistenceId" => entity.PersistenceId,
        "OrderNumber" => entity.OrderNumber,
        "Total" => entity.Total,
        _ => null
    }
};
```

That descriptor is what allows bulk operations to avoid runtime reflection.

## Column roles

| Role | Insert | Upsert update | Typical properties |
|------|--------|---------------|--------------------|
| `Key` | Yes | No | `PersistenceId` |
| `LogicKey` | Yes | Match-only | `Sku`, `OrderNumber` |
| `Regular` | Yes | Yes | Business data columns |
| `InsertOnly` | Yes | No | `CreatedAt`, `CreatedBy` |
| `UpdateOnly` | No | Yes | `UpdatedAt`, `UpdatedBy` |
| `SoftDelete` | Yes | No | `IsDeleted`, `DeletedAt`, `DeletedBy` |
| `Computed` | No | No | concurrency/computed columns |

## Provider-specific SQL

| Provider | Insert | Upsert |
|----------|--------|--------|
| SQL Server | multi-row `INSERT` | `MERGE` |
| PostgreSQL | multi-row `INSERT` | `INSERT ... ON CONFLICT DO UPDATE` |
| SQLite | multi-row `INSERT` | `INSERT ... ON CONFLICT DO UPDATE` |

Keep provider-specific behavior covered by targeted tests, especially around upsert semantics.

## When to use bulk vs normal SaveChanges

| Scenario | Recommended path |
|----------|------------------|
| 1-10 tracked entities in a normal business workflow | `Add` / `Update` / `Remove` plus `SaveChangesAsync` |
| Large import or sync job | `BulkInsertAsync` / `BulkUpsertAsync` |
| Wide update by predicate | `BulkUpdateAsync` |
| Wide delete by predicate | `BulkDeleteAsync` |
| Change tracking, domain hooks, normal aggregate behavior | Normal repository plus `IUnitOfWork` |

## Important caveats

- Bulk operations bypass EF Core change tracking.
- Interceptors do not run in the same way they do for normal tracked writes.
- Audit/soft-delete metadata is handled by the generated bulk descriptor and bulk executor, not by normal tracked-save behavior.

## Related guides

- [Repository Implementation](02-repository-implementation.md)
- [Testing Generated Persistence](07-testing-generated-persistence.md)
- [Migration Patterns](08-migration-patterns.md)
