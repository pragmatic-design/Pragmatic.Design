# DbContext Generation

## The Problem

EF Core requires a `DbContext` plus a consistent set of registrations:

- `DbSet<T>` properties
- `OnModelCreating` configuration
- interceptors
- keyed boundary registration
- an `IUnitOfWork` per boundary

That is repetitive code, especially when each module follows the same conventions.

## Activation model

DbContext generation becomes active when the consuming project references:

- `Pragmatic.Persistence`
- `Pragmatic.Persistence.EFCore`
- the `Pragmatic.SourceGenerator` analyzer

The runtime packages alone are not enough. The analyzer must also be referenced.

## You do not write a DbContext

There is no `DbContext` class to declare. You declare a **database**, the host says which module lives
on it, and the generator emits one `DbContext` per boundary.

### 1. The database

A class deriving from `PragmaticDatabase`, carrying its provider and the configuration key its
connection string is read from.

```csharp
[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
public sealed class AppDatabase : PragmaticDatabase;
```

`ConfigKey` is required in practice: left empty, the host looks up an empty key at startup, gets
`null`, and fails with an exception that names something else. `MigrationConfigKey` is optional and
separates DDL access from DML access; without it, migrations use `ConfigKey`.

| Provider | `DatabaseProvider` |
|----------|--------------------|
| PostgreSQL | `PostgreSql` |
| SQL Server | `SqlServer` |
| SQLite | `SqLite` |
| InMemory | `InMemory` |

### 2. The host places each module on a database

```csharp
[Include<BookingModule, AppDatabase>]
[Include<BillingModule, FinancialDatabase>]
public sealed class AppHostModule;
```

That pairing *is* the topology. Two modules on one database share a connection; two databases mean two,
and the generator emits accordingly: the number of `DbContext` types it produces follows from this
declaration and from nothing else.

### 3. What the generator emits

One `DbContext` per boundary, marked `[PragmaticDbContext("{boundary}")]`, plus:

- `DbSet<T>` for every entity that `[BelongsTo]` that boundary
- `OnModelCreating` applying the generated entity configurations
- the interceptor set
- keyed `DbContext` and keyed `IUnitOfWork` registrations
- a migration DbContext where migrations are used

The generated host registers each of them (`AddSalesDbContext(...)`, `AddBillingDbContext(...)`),
building the options from the database's `ConfigKey`; `AddAllPragmaticDbContexts(o => …)` is the same
set behind one call, for a project wiring it by hand.

The keying is what keeps one boundary's unit of work from committing another's, and **the key is the
boundary marker type**: `AddKeyedScoped<DbContext>(typeof(BookingBoundary), …)`, resolved with
`[FromKeyedServices(typeof(BookingBoundary))]`. Not the `DbContext` type.

## What the generated registration looks like

The generated extension method shape is:

```csharp
public static IServiceCollection AddSalesDbContext(
    this IServiceCollection services,
    Action<DbContextOptionsBuilder> configure)
{
    services.AddDbContext<SalesDbContext>((sp, options) =>
    {
        configure(options);
        options.AddInterceptors(
            new AuditingInterceptor(
                sp.GetService<TimeProvider>() ?? TimeProvider.System,
                sp.GetService<ICurrentUser>()));
    });

    services.AddKeyedScoped<DbContext>(
        typeof(SalesBoundary),
        (sp, _) => sp.GetRequiredService<SalesDbContext>());

    services.AddKeyedScoped<IUnitOfWork>(
        typeof(SalesBoundary),
        (sp, _) => new EfCoreUnitOfWork(sp.GetRequiredService<SalesDbContext>()));

    return services;
}
```

Important corrections:

- The generated method takes `Action<DbContextOptionsBuilder>`, not `IConfiguration`.
- `IUnitOfWork` is registered keyed by boundary.
- Query executors and filters are registered by separate generated extensions.

## Startup usage

```csharp
builder.Services.AddSalesDbContext(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Sales")));

builder.Services.AddPragmaticPersistenceRepositories<SalesDbContext>();
builder.Services.AddMyAppQueryFilters();
```

Inside a Pragmatic host none of these three are yours to call: the generated host makes them itself,
from the topology and the persistence metadata.

The query-filter extension name comes from the common namespace prefix of the generated entities. If no common prefix can be derived, the fallback name is `AddGeneratedQueryFilters()`.

If you want every generated DbContext in one call, the generator also emits:

```csharp
builder.Services.AddAllPragmaticDbContexts(options =>
    options.UseSqlite(connection));
```

## Multi-tenant connection strings

The runtime includes `IConnectionStringProvider<TDbContext>` for cases where the connection string
cannot be static.

⚠️ **Nothing generated consumes it.** The generated `Add{Boundary}DbContext(Action<DbContextOptionsBuilder>)`
passes your callback straight to `AddDbContext`, and never asks the container for a provider. Registering
one changes nothing on its own; you have to read it in the callback yourself:

```csharp
builder.Services.AddSalesDbContext(options =>
{
    var cs = sp.GetRequiredService<IConnectionStringProvider<SalesDbContext>>()
        .GetConnectionStringAsync().AsTask().GetAwaiter().GetResult();
    options.UseNpgsql(cs);
});
```

The interface is the shape to implement against (`TenantConnectionStringProvider` in
`Pragmatic.MultiTenancy.Persistence` is the ready-made one), not a hook the generator calls.

Typical pattern:

```csharp
public sealed class TenantConnectionStringProvider :
    IConnectionStringProvider<SalesDbContext>
{
    private readonly ITenantContext _tenant;
    private readonly IConfiguration _config;

    public TenantConnectionStringProvider(ITenantContext tenant, IConfiguration config)
    {
        _tenant = tenant;
        _config = config;
    }

    public ValueTask<string> GetConnectionStringAsync(CancellationToken ct = default)
        => ValueTask.FromResult(_config.GetConnectionString($"Sales_{_tenant.TenantId}")!);
}
```

Keep the provider deterministic and side-effect free. Test it separately from the generated DbContext.

## Related guides

- [Repository Implementation](02-repository-implementation.md)
- [Interceptors & Runtime](04-interceptors-runtime.md)
- [Testing Generated Persistence](07-testing-generated-persistence.md)
