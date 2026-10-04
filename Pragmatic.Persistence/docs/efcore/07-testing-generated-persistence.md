# Testing Generated Persistence

This guide is written for application teams that use generated repositories and DbContexts and want tests that are realistic without becoming fragile.

The key idea is simple:

- test your business rules through the same generated APIs you will use in production
- choose the cheapest provider that still validates the behavior you care about
- replace request-scoped services such as `ICurrentUser` or `IQueryFilterToggle` with deterministic test doubles

## Start with the right test level

| Test level | What it validates well | What it does not validate well |
|------------|------------------------|--------------------------------|
| Pure unit test | Specifications, DTO mapping, domain logic outside EF Core | Provider SQL, interceptors, transactions, query translation |
| Integration test with SQLite | Generated repositories, query filters, soft-delete, auditing, relational behavior | Provider-specific SQL Server or PostgreSQL quirks |
| Integration test with real provider in Docker | Bulk SQL, provider-specific upsert behavior, migrations | Fast local feedback |

For most repository behavior, SQLite-backed integration tests are the best default.

## Provider choice

| Provider | Use it for | Avoid it for |
|----------|------------|--------------|
| EF Core InMemory | Very small tests that only care about change tracking or simple interceptor flows | Relational semantics, query translation, include behavior, SQL-specific features |
| SQLite in-memory | Default integration test choice for generated repositories | SQL Server or PostgreSQL specific SQL behavior |
| SQL Server / PostgreSQL in Docker | Final confidence for bulk operations and provider-specific behavior | Everyday fast feedback loops |

If you are testing bulk upsert SQL or provider-specific migrations, do not rely only on InMemory or SQLite.

## Minimal integration harness

This is the smallest useful service-registration pattern for testing generated persistence.

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Identity;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;

var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();

var services = new ServiceCollection();

services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 3, 11, 10, 0, 0, TimeSpan.Zero)));
services.AddScoped<ICurrentUser>(_ => FakeCurrentUser.Authenticated("user-42", "Test User"));

services.AddSalesDbContext(options => options.UseSqlite(connection));
services.AddPragmaticPersistenceRepositories<SalesDbContext>();
services.AddMyAppQueryFilters();

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();

var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
await db.Database.EnsureCreatedAsync();

var orders = scope.ServiceProvider.GetRequiredService<IRepository<Order>>();
var uow = scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(SalesBoundary));
```

Why this setup is a good default:

- it uses the generated DI extensions instead of bypassing them
- it exercises keyed `IUnitOfWork`
- it registers filter infrastructure exactly as production does
- it keeps current user and clock deterministic

## A simple fake `ICurrentUser`

Auditing and some filters depend on `ICurrentUser`, so tests should provide a predictable implementation.

This is the one the persistence suite itself uses: eight members, and the two that carry the weight
are delegated to null objects rather than reimplemented:

```csharp
using Pragmatic.Authorization;
using Pragmatic.Identity;

internal sealed class FakeCurrentUser : ICurrentUser
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyClaims =
        new Dictionary<string, IReadOnlyList<string>>();

    public string Id { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool IsAuthenticated { get; set; }

    public PrincipalKind Kind => IsAuthenticated ? PrincipalKind.User : PrincipalKind.Anonymous;
    public string? TenantId => null;
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims => EmptyClaims;

    public IUserAuthorization Authorization => NullUserAuthorization.Instance;
    public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;

    public static FakeCurrentUser Authenticated(string id, string? displayName = null)
        => new() { Id = id, IsAuthenticated = true, DisplayName = displayName };

    public static FakeCurrentUser Anonymous()
        => new() { Id = string.Empty, IsAuthenticated = false };
}
```

Note what is **not** on the interface: there is no `Roles`, and no `HasPermission`; permissions live
behind `Authorization`, an `IUserAuthorization`, and a claim maps to a *list* of values rather than one.
`Pragmatic.Testing` can also generate the double for you with `[assembly: GenerateMock<ICurrentUser>]`,
which is the better route when the test needs to assert calls rather than just supply an identity.

Use an authenticated fake when you want to assert `CreatedBy` or `UpdatedBy`.

Use an anonymous fake, or no registration, when you want to verify backward-compatible behavior with no user identity available.

## Control time explicitly

Generated auditing logic uses `TimeProvider`, so tests should not depend on the system clock.

```csharp
var fixedNow = new DateTimeOffset(2026, 3, 11, 10, 0, 0, TimeSpan.Zero);
services.AddSingleton<TimeProvider>(new FakeTimeProvider(fixedNow));
```

This makes assertions on `CreatedAt`, `UpdatedAt`, or soft-delete timestamps deterministic.

## Using `IQueryFilterToggle` in tests

`IQueryFilterToggle` is registered by the generated `Add{NamespacePrefix}QueryFilters()` extension.

It uses `AsyncLocal`, which means:

- it is scoped to the current async flow
- it is safe to use with `await`
- it automatically restores the previous state when the `using` block ends

Example:

```csharp
var toggle = scope.ServiceProvider.GetRequiredService<IQueryFilterToggle>();
var orders = scope.ServiceProvider.GetRequiredService<IRepository<Order>>();

using (toggle.Disable<Order.SoftDeleteFilter>())
{
    var deletedOrders = await orders.Query().ToListAsync(ct);
}
```

Use `Disable<TFilter>()` when you want a narrow test.

Use `UseMode(FilterMode.Admin)` or `UseMode(FilterMode.Raw)` when you want to test broader behavior:

```csharp
using (toggle.UseMode(FilterMode.Raw))
{
    var allOrders = await orders.Query().ToListAsync(ct);
}
```

## Testing through the abstraction vs the concrete repository

Use the stable abstractions when the behavior under test is part of the shared contract:

- `IRepository<TEntity>`
- `IReadRepository<TEntity>`
- keyed `IUnitOfWork`

Use the generated concrete repository when the behavior is only exposed there:

- `GetBySkuAsync(...)`
- `GetByOrderNumberAsync(...)`
- include overloads
- bulk methods

Rule of thumb:

- if you are asserting a stable business workflow, prefer the interface
- if you are asserting a generated convenience method, use the concrete repository

## Example: auditing integration test

```csharp
var order = Order.Create("ORD-001", 120m);
orders.Add(order);
await uow.SaveChangesAsync(ct);

order.CreatedBy.Should().Be("user-42");
order.CreatedAt.Should().Be(fixedNow);
```

This test is much more valuable than a pure mock-based test because it proves the generator, the interceptor, and the DI wiring all cooperate correctly.

## Example: soft-delete integration test

```csharp
orders.Remove(order);
await uow.SaveChangesAsync(ct);

var visible = await orders.GetByIdAsync(order.PersistenceId, ct);
visible.Should().BeNull();

var toggle = scope.ServiceProvider.GetRequiredService<IQueryFilterToggle>();

using (toggle.Disable<Order.SoftDeleteFilter>())
{
    var deleted = await orders.GetByIdAsync(order.PersistenceId, ct);
    deleted.Should().NotBeNull();
    deleted!.IsDeleted.Should().BeTrue();
}
```

This validates both the write-side behavior and the read-side filtering contract.

## If you instantiate the generated repository directly

Most teams should not do this in tests unless they are isolating a very narrow constructor-level behavior.

If you instantiate the generated repository manually, you are responsible for supplying what the
generated DI normally wires. The two that are **not** optional are the first two:

- the boundary's `DbContext`
- the boundary's `IUnitOfWork`, the same instance an invoker would hold, since every save goes through it
- optional `IQueryFilterProvider`, `FilterMapComposer`, `ITenantContext`, `IQueryFilterToggle`
- optional `IEnumerable<RollUpRule>`: how the repository knows whether a bulk delete has to keep a
  parent aggregate current
- optional `TimeProvider` and `ICurrentUser`, present only on `[Auditable]` / `[SoftDelete]` entities

Passing `null` for the optional ones is legal and silent: the repository builds and reads unfiltered.

For novice teams, the safer path is to build a `ServiceProvider` and resolve the repository from DI.

## Bulk test strategy

Bulk methods need a stricter test pyramid because they bypass normal change tracking and may use provider-specific SQL.

Recommended split:

1. Template or generation tests
   Validate that the generated repository exposes the expected bulk method shape.
2. SQLite integration tests
   Validate the happy path for insert, update, delete, and soft-delete semantics.
3. Provider-specific Docker tests
   Validate SQL Server `MERGE`, PostgreSQL `ON CONFLICT`, batching, and edge cases.

Do not mark the feature as fully tested if only the template tests pass.

## Recommended test matrix

| Behavior | Minimum useful test |
|----------|---------------------|
| CRUD via stable repository | SQLite integration |
| Auditing with `ICurrentUser` and `TimeProvider` | SQLite integration |
| Soft-delete and filter toggling | SQLite integration |
| Logic-key helper methods | SQLite integration against concrete repository |
| Include overloads | SQLite integration |
| Bulk insert/update/delete/upsert | SQLite plus provider-specific Docker tests |
| Migrations | Real provider migration test on the provider you ship |

## Common mistakes

### Using only EF Core InMemory

InMemory is fast, but it does not behave like a relational provider. It can hide problems in:

- query translation
- join behavior
- uniqueness assumptions
- transaction behavior

### Forgetting query-filter registration

If you call `AddPragmaticPersistenceRepositories<SalesDbContext>()` but not
`AddMyAppQueryFilters()`, the reads still work and come back **unfiltered**, which is not how
production behaves, since there the generated host makes that call for you.

### Injecting `IUnitOfWork` without the boundary key

Generated `IUnitOfWork` is keyed by boundary. In tests, resolve it like this:

```csharp
var uow = scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(SalesBoundary));
```

### Sharing mutable test doubles across tests

Prefer a new service scope and fresh test doubles per test. That keeps filter state, current user state, and DbContext state isolated.

## Related guides

- [DbContext Generation](01-dbcontext-generation.md)
- [Repository Implementation](02-repository-implementation.md)
- [Bulk Operations](05-bulk-operations.md)
- [Query Filters](../07-query-filters.md)
