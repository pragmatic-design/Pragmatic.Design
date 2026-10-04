# Repository Implementation

## The Problem

Each entity needs a repository that:

- wraps the correct `DbSet<T>`
- resolves the right `DbContext` when multiple boundaries exist
- applies filters on every read path
- performs soft-delete correctly

That is all infrastructure code. The generator should own it.

## What the generator emits

For each entity, the generator emits a concrete repository class. That class implements `IRepository<TEntity>` and can also include convenience members such as:

- logic-key helpers
- include overloads
- bulk methods

See the core [Repository](../05-repository.md) guide for the distinction between stable interfaces and the concrete generated surface.

## DbContext resolution

Generated repositories resolve the boundary `DbContext` through keyed DI:

```csharp
// Generated in {Namespace}.Order.Repository.g.cs
public partial class Order
{
    public partial class Repository : IRepository<Order>
    {
        public Repository(
            [FromKeyedServices(typeof(SalesBoundary))] DbContext db,
            [FromKeyedServices(typeof(SalesBoundary))] IUnitOfWork unitOfWork,
            IQueryFilterProvider? filterProvider = null,
            FilterMapComposer? filterMapComposer = null,
            ITenantContext? tenantContext = null,
            IQueryFilterToggle? filterToggle = null,
            IEnumerable<RollUpRule>? rollUpRules = null,
            TimeProvider? timeProvider = null,      // only on [Auditable] / [SoftDelete] entities
            ICurrentUser? currentUser = null)       // same
        { /* … */ }
    }
}
```

Two things follow from that constructor. **Both** the context and the unit of work are keyed on the
boundary, and on the same key, so the repository saves through the very instance an invoker holds,
rather than a second one over the same context. And every collaborator after them is optional: a
container missing the filter services builds the repository happily and reads unfiltered.

## Repository registration

The generated repository registration method shape is:

```csharp
// _Infra.Persistence.RepositoryRegistration.g.cs: one generic method for the whole assembly
public static IServiceCollection AddPragmaticPersistenceRepositories<TDbContext>(
    this IServiceCollection services) where TDbContext : DbContext
{
    // per boundary
    services.AddKeyedScoped<DbContext>(typeof(SalesBoundary), (sp, _) => sp.GetRequiredService<TDbContext>());
    services.AddKeyedScoped<IUnitOfWork>(typeof(SalesBoundary),
        (sp, _) => new EfCoreUnitOfWork(sp.GetRequiredService<TDbContext>(), /* logger */ null, /* dispatcher */ null));

    // per entity
    services.AddScoped<Order.Repository>();
    services.AddScoped<IRepository<Order>>(sp => sp.GetRequiredService<Order.Repository>());
    services.AddScoped<IReadRepository<Order>>(sp => sp.GetRequiredService<Order.Repository>());
    return services;
}
```

Inside a Pragmatic host you do not call it: the generated host emits the same registrations directly,
in `RegisterAllRepositories()`.

Important correction:

- repositories are registered unkeyed for normal injection
- keyed resolution is used for the `DbContext` and `IUnitOfWork`, not for the repository itself

## Filter integration

Generated repositories apply both root and navigation filtering:

```csharp
private IQueryable<Order> ApplyFilters(IQueryable<Order> query)
{
    var context = BuildFilterContext();

    if (_filterProvider is not null)
    {
        var filter = _filterProvider.GetCombinedFilter<Order>(
            context,
            NavigationContext.Root<Order>());

        if (filter is not null)
            query = query.Where(filter);
    }

    if (_filterMapComposer is not null)
        query = _filterMapComposer.ApplyNavigationFilters(query, context);

    return query;
}
```

If query-filter services are not registered, the repository still functions, but those filters are not applied.

## Soft-delete behavior

For entities with `[SoftDelete]`, `Remove()` becomes a metadata update, not a physical delete:

```csharp
public void Remove(Order entity)
{
    entity.IsDeleted = true;
    entity.DeletedAt = _timeProvider.GetUtcNow();
    entity.DeletedBy = _currentUser?.Id;
}
```

Entities without `[SoftDelete]` still use a physical EF Core remove.

## Keyed `IUnitOfWork`

`EfCoreUnitOfWork` wraps the boundary DbContext and is registered keyed by boundary:

```csharp
var uow = services.GetRequiredKeyedService<IUnitOfWork>(typeof(SalesBoundary));
await uow.SaveChangesAsync(ct);
```

`BeginTransactionAsync(...)` returns `ITransaction`, not plain `IAsyncDisposable`.

## Related guides

- [DbContext Generation](01-dbcontext-generation.md)
- [Bulk Operations](05-bulk-operations.md)
- [Testing Generated Persistence](07-testing-generated-persistence.md)
