# Troubleshooting

Practical problem/solution guide for Pragmatic.Persistence. Each section covers a common issue, the likely causes, and the fix.

---

## Entity Not Appearing in DbContext

Your entity compiles, has `[Entity]`, but the DbContext does not include a `DbSet` for it.

### Checklist

1. **Does the entity have `[BelongsTo<TBoundary>]`?** Without a boundary assignment, the SG does not know which DbContext should include the entity. Add the attribute.

2. **Does the host declare a database?** A class deriving from `PragmaticDatabase` and carrying `[PragmaticDatabase(Provider = …, ConfigKey = …)]`. The topology is a host decision, not a module one.

3. **Does the host place your entity's module on it?** `[Include<TModule, TDatabase>]` on the host module. A module nobody includes has no database, so nothing is generated for its entities.

4. **Is `ConfigKey` set?** Left empty, the host looks up an empty configuration key at startup, gets `null`, and fails with an exception that names something else entirely.

5. **Is the SG analyzer referenced?** In your `.csproj`, verify the Pragmatic.SourceGenerator is referenced with `OutputItemType="Analyzer"`:

   ```xml
   <ProjectReference Include="..\Pragmatic.SourceGenerator\...\Pragmatic.SourceGenerator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
   ```

6. **Is `Pragmatic.Persistence.EFCore` referenced?** The repository and entity configuration are only generated when the EF Core runtime package is referenced. Without it, you get basic entity members but no DbContext integration.

---

## Repository Not Resolving from DI

You inject `IRepository<Order>` but get `InvalidOperationException` at runtime: "No service for type has been registered."

### Checklist

1. **Are you inside a Pragmatic host?** If so this should not happen: the generated host calls
   `RegisterAllRepositories()` itself. Check that `PragmaticApp.RunAsync()` is what starts the app.

2. **Outside a host, did you call the registration?** It is
   `services.AddPragmaticPersistenceRepositories<BillingDbContext>()`, one generic method per
   DbContext, not a per-boundary `Add{Boundary}Repositories()`. That name does not exist.

3. **Did you register the DbContext first?** `AddBillingDbContext(options => …)`, or
   `AddAllPragmaticDbContexts(options => …)`. The repository registration resolves `TDbContext` from
   the container.

4. **Is the entity in a boundary?** Repositories are only generated for entities with
   `[BelongsTo<TBoundary>]`.

5. **Which type are you injecting?** All three are registered and resolve to the same scoped instance:
   the concrete `Order.Repository`, `IRepository<Order>` and `IReadRepository<Order>`.
   Nothing needs registering by hand.

6. **Check the SG output.** In Visual Studio, expand **Dependencies > Analyzers >
   Pragmatic.SourceGenerator**. The files are `{Namespace}.Order.Repository.g.cs` and
   `_Infra.Persistence.RepositoryRegistration.g.cs`.

### If Using Composition

When you use `Pragmatic.Composition` with `PragmaticApp.RunAsync()`, repository and DbContext registration is automatic -- the SG-generated host calls the registration methods. If repositories still do not resolve, verify the Composition package is referenced and `PragmaticApp.RunAsync()` is called.

---

## Soft Delete Not Filtering

You added `[SoftDelete]` to an entity, but soft-deleted rows still appear in query results.

### Checklist

1. **Did you register generated query filters?** Call `AddMyAppQueryFilters()` or `AddGeneratedQueryFilters()` in your service registration. Without this, the `SoftDeleteFilter` class exists but is never registered in DI.

2. **Is `IQueryFilterToggle` accidentally in `Raw` mode?** Check if any code in the request pipeline calls `filterToggle.UseMode(FilterMode.Raw)` or `filterToggle.DisableAll()`. These disable all filters including soft-delete.

3. **Are you using raw `DbContext` instead of the repository?** The filter pipeline runs through `IQueryFilterProvider`, which is called by the generated repository. If you query `dbContext.Set<Order>()` directly, you bypass the Pragmatic filter pipeline. EF Core global query filters (`HasQueryFilter`) serve as a fallback but must be configured in the entity configuration.

4. **Check the generated SoftDeleteFilter file.** Look for `Order.SoftDeleteFilter.g.cs` in the SG output. Verify it exists and implements `IQueryFilter<Order>`.

5. **Is the entity actually soft-deletable?** Confirm `[SoftDelete]` is on the entity class declaration, not just on a related entity.

---

## Audit Fields Not Populating

`[Auditable]` is on the entity, but `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy` remain default values after save.

### Checklist

1. **Is `Pragmatic.Persistence.EFCore` referenced?** The `AuditingInterceptor` lives in the EF Core package. Without it, audit fields are generated but never populated.

2. **Is the interceptor registered?** If using Composition, this is automatic. Without Composition, verify the DbContext options include the auditing interceptor.

3. **Is `ICurrentUser` registered in DI?** The `*By` fields (`CreatedBy`, `UpdatedBy`) are populated from `ICurrentUser`. If it is not registered, these fields remain `null`. The `*At` fields (`CreatedAt`, `UpdatedAt`) still populate because they use the clock.

4. **Are you using `SaveChangesAsync` through the correct path?** The interceptor hooks into EF Core's `SaveChangesAsync`. If you bypass it (e.g., raw SQL, bulk operations without interceptor), audit fields are not set.

5. **For background jobs**: If no HTTP request context exists, `ICurrentUser` may not be available. The `*By` fields will be `null`, which is expected. The `*At` fields still populate.

---

## Migration Errors

`dotnet ef migrations add` fails or the migration produces unexpected results.

### Possible Causes

**You are probably not meant to be running `dotnet ef migrations add` at all.** `Pragmatic.Migrations`
reads the generated schema and applies a declarative diff at startup: no migration files, no
`Add-Migration`. See the [migrations guide](../../Pragmatic.Migrations/README.md).

**Wrong migration context.** The migration context is **per database, not per boundary**: boundaries
sharing a connection share the schema, so they share the context that describes it. It is named after
the database (`SalesDatabaseMigrationDbContext`), or plain `MigrationDbContext` when no per-database
topology is declared.

**Generated entity configuration not applied.** The migration context must include the SG-generated
`IEntityTypeConfiguration<T>`. Verify the generated `OnModelCreating` applies them.

**Cross-boundary FK without matching entity.** If an entity references another entity in a different boundary, the FK column is generated but the referenced entity is not in the same DbContext. This can cause "entity type not found" warnings during migration. This is expected -- the FK column is a raw Guid/int column without a foreign key constraint across boundaries.

**Pending model changes.** If you add an attribute (like `[Auditable]`) and rebuild, the SG generates new properties. Run `dotnet ef migrations add` to capture these changes. If you forget, the database schema diverges from the model.

---

## Query Filter Not Applying

A custom `IQueryFilter` or `IPermissionBasedFilter` is registered but does not affect query results.

### Checklist

1. **Is the filter registered with the correct DI lifetime?** Query filters should be `Scoped`:

   ```csharp
   services.AddScoped<IQueryFilter, TeamOrdersFilter>();
   ```

   If registered as `Singleton`, the filter cannot access scoped services like `ICurrentUser`.

2. **Does the filter return the correct entity type?** `IQueryFilter<Order>` only applies to `Order` queries. Check the generic parameter matches your entity.

3. **Is `FilterMode` overriding your filter?** The modes are cumulative: `Admin` skips visibility and
   permission filters, `Elevated` also skips data-scope ones, `Background` also skips the tenant
   filter, and `Raw` skips everything including soft-delete.

4. **For `IPermissionBasedFilter`:** Is `ICurrentUser` available? If no authenticated user exists in the current scope (background job, migration), permission-based filters are automatically skipped. This is by design -- to prevent crashes in non-HTTP contexts.

5. **Does the user have the bypass permission?** `IPermissionBasedFilter` has a `BypassPermission` property. If the current user has that permission, the filter is skipped for them.

---

## GridFilter / Query Returning Wrong Results

The `[Filter]` or `[Filterable]` properties produce unexpected SQL or return the wrong rows.

### Possible Causes

**Property name does not match entity property.** The SG derives the entity property from the filter property name. `CustomerNameSort` maps to `CustomerName` (removes "Sort" suffix). If the entity property is `Name` but your filter is `CustomerName`, use `MapTo`:

```csharp
[Filter(MapTo = "Customer.Name")]
public string? CustomerName { get; init; }
```

**Wrong operator for the data type.** It is not a compile error and not a runtime error: the generated
switch renders `Contains`/`StartsWith`/`EndsWith` only for a `string` and `In` only for a collection,
and everything else falls through to `==`. So `FilterOperator.Contains` on an `int?` builds, runs, and
quietly filters by equality. `Between`, which would disappear the same way, is **PRAG0701**, an
error.

**Required filter property is null.** If a filter property is `required` but the caller sends `null`, the filter is always applied with the default value (empty string, zero), potentially returning no results.

**ComplexFilter JSON not parsing.** `[ComplexFilter]` properties expect JSON in the query string. Verify the JSON is URL-encoded and matches the `[FilterDto<T>]` shape.

**Sort priority confusion.** When multiple `[Sort]` properties have values, the SG applies them in `Priority` order (lower first). If `Priority` is not set, the order is declaration order.

---

## Concurrency Conflict

`DbUpdateConcurrencyException` is thrown when saving an entity.

### Checklist

1. **Is `[ConcurrencyAware]` on the entity?** It declares a `RowVersion` **shadow property** (nothing
   is added to your entity), configured as a concurrency token in the generated DbContext, and
   provider-specific: a native `rowversion` on SQL Server, the `xmin` system column on PostgreSQL.

2. **Are you loading stale data?** If you load an entity, hold it for a long time (e.g., across an HTTP request-response cycle), and then save, another request may have modified it. Reload the entity before saving, or implement retry logic.

3. **Where it surfaces depends on how you save.** Through the generated repository it is a `Result`,
   not an exception: on a `[ConcurrencyAware]` entity `SaveChangesAsync` returns
   `Result<int, ConcurrencyError>` (`Code = "CONCURRENCY_CONFLICT"`, status 409):

   ```csharp
   var saved = await repository.SaveChangesAsync(ct);
   return saved.Match(
       success: _ => Results.NoContent(),
       failure: e => Results.Problem(statusCode: e.StatusCode, detail: e.Message));
   ```

   Through `IUnitOfWork.SaveChangesAsync` the `DbUpdateConcurrencyException` travels out as-is, on
   purpose: a stale row is two writers meeting, not a rule the schema enforces, so it is not
   reclassified alongside constraint violations.

4. **Bulk operations and concurrency.** Bulk insert/upsert operations may bypass EF Core's concurrency token checking. Use `UpsertOptions.ConcurrencyCheck` for bulk upserts if you need conflict detection.

---

## Multi-Tenant Data Leaking

Data from other tenants appears in query results.

### Checklist

1. **Does the entity implement `ITenantEntity`?** The SG generates a tenant filter only for entities with a `TenantId` property that implements `ITenantEntity`.

2. **Is `ITenantContext` registered and populated?** That is the interface the generated filter takes
   in its constructor: `Pragmatic.MultiTenancy.ITenantContext`, not `IMultiTenancyContext`. Unregistered,
   the filter cannot even be constructed; returning `null`, it has nothing to compare against.

3. **Is `FilterMode` at `Background` or above?** `Background` and `Raw` skip tenant filtering;
   `Normal`, `Admin` and `Elevated` do not. A background job in `Background` mode sees every tenant's
   data, which is the point of the mode and the risk of it.

4. **Are you querying the DbContext directly?** The Pragmatic tenant filter runs through `IQueryFilterProvider`. Direct `dbContext.Set<T>()` queries bypass it. Ensure EF Core global query filters are configured as a safety net.

5. **Check the generated TenantFilter file.** Look for `{Namespace}.{Entity}.TenantFilter.g.cs` in the SG output; the filter is a class nested in the entity, `{Entity}.TenantFilter`.

---

## Diagnostics Reference

### Persistence Diagnostics (PRAG06xx)

| ID | Severity | Cause | Fix |
|----|----------|-------|-----|
| PRAG0600 | Error | A persistence-attributed type is not `partial` | Add `partial` to the class declaration |
| PRAG0602 | Error | A generated DbContext type is not `partial` | Add `partial` to the DbContext declaration |
| PRAG0612 | Warning | Several relations to one entity collide on a derived navigation name | `.WithNavigation("...")` on each |
| PRAG0613 | Error | `Inverse` matches no navigation on the target | Name an existing one, or declare the other side |
| PRAG0614 | Error | The inverse navigation has the wrong type | Point it at the navigation that comes back here |
| PRAG0615 | Error | Two relations would generate the same navigation | Rename one |
| PRAG0620 | Warning | State machine has no `[InitialState]` | Mark one enum value `[InitialState]` |
| PRAG0621 | Warning | State is unreachable | Add a `[TransitionFrom(...)]`, or mark it `[InitialState]` |
| PRAG0622 | Error | `[TransitionFrom(...)]` names a non-existent enum member | Use a real enum member |
| PRAG0623 | Error | The state machine governs a property the entity lacks | `Property = nameof(...)`; defaults to `Status` |
| PRAG0624 | Error | A trait's properties are only partly declared by hand | Declare them all, or none |
| PRAG0651 | Warning | Property type may need value converter | Add EF Core value converter or use provider-native type |
| PRAG0701 | Error | `FilterOperator.Between`, which no generator renders | Two properties over one column: `GreaterOrEqual` + `LessOrEqual`, same `MapTo` |
| PRAG0702 | Error | `[CascadeOn<TSource>]` without the `{Source}Id` foreign key | Add the FK, or declare the relation |
| PRAG0703 | Warning | An attribute argument nothing consumes | Remove it; the message names what does work |
| PRAG0704 | Error | The query's result type has no `Projection` | `[GenerateProjection]` beside `[MapFrom<TEntity>]` on the DTO |
| PRAG0705 | Warning | A required navigation points at a `[SoftDelete]` entity | `Required = false`, or drop `[SoftDelete]`, or `IgnoreQueryFilters()` |
| PRAG0706 | Warning | `[ReadAccess]` crosses a database | Read through that boundary's operations |
| PRAG0710 | Warning | DTO navigation name mismatch | Align DTO property name with entity navigation |
| PRAG0711 | Warning | Include depth greater than 3 | Reduce depth or use projection |
| PRAG0716 | Warning | DTO with many navigation paths | Use projection or split queries |
| PRAG1100 | Error | `[HasOwner]` on a non-`partial` type | Add `partial` |
| PRAG1104 | Info | `OwnerId` is declared manually | Nothing required; drop it to use the generated property |

`PRAG0600` and `PRAG0602` are reported by `Pragmatic.SourceGenerator.Analyzers`, which is what makes
the "make class partial" quick fix available in the IDE.

### Analyzer Diagnostics over your own code (PRAG068x)

| ID | Severity | Cause | Fix |
|----|----------|-------|-----|
| PRAG0680 | Warning | `new Entity()` instead of the generated factory | `Entity.Create(...)` |
| PRAG0681 | Warning | `default` used for an entity | `Entity.Create(...)` |
| PRAG0682 | Warning | `Activator.CreateInstance` used for an entity | `Entity.Create(...)` |
| PRAG0683 | Warning | Entity declares a behavior method | Move the state change into a mutation or domain action |
| PRAG0684 | Warning | Non-constant SQL in `FromSqlRaw`/`ExecuteSqlRaw` | Use interpolated `FromSql`/`ExecuteSql`, or pass parameters |
| PRAG0685 | Info | Aggregate with many child collections | Split it, or reference other aggregates by id |
| PRAG0686 | Warning | Injecting another boundary's `DbContext` | Call that boundary's actions/queries instead |
| PRAG0687 | Warning | `ExecuteDelete` on a `[SoftDelete]` entity | It never reaches the change tracker: the row is really gone |

### Reading Diagnostics

| Severity | What It Means | What to Do |
|----------|---------------|------------|
| Error | The SG cannot produce a safe model | Fix before trusting generated output |
| Warning | Generation continues but shape may be inefficient | Review and either simplify or document the tradeoff |
| Info | The SG is informing you about a behavior choice | Read once so the result does not surprise you later |

---

## FAQ

### Can I use multiple boundaries for the same database?

Yes. Multiple boundaries can share the same connection string -- they just have separate DbContexts. This gives you logical isolation (separate DbSets, separate migration history) while sharing the physical database.

### How do I see the generated SQL?

Enable EF Core logging:

```csharp
optionsBuilder.LogTo(Console.WriteLine, LogLevel.Information);
```

Or in `appsettings.Development.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  }
}
```

### Why does my entity get `PersistenceId` instead of just `Id`?

`PersistenceId` is the technical identifier used for database persistence. `Id` is a convenience alias (`Guid Id => PersistenceId`). The distinction keeps the naming clear: `PersistenceId` is the column in the database, `Id` is what your code uses. Both point to the same value.

### Can I override the generated EF Core configuration?

Yes. Create a custom `IEntityTypeConfiguration<T>` in your project. EF Core's `ApplyConfigurationsFromAssembly` will pick it up. If you need to override specific generated configuration, apply your overrides after the generated configuration runs.

### How do I restore a soft-deleted entity?

Use `MutationMode.Restore`:

```csharp
[Mutation(Mode = MutationMode.Restore)]
[Endpoint(HttpVerb.Post, "api/v1/orders/{id}/restore")]
public partial class RestoreOrderMutation : Mutation<Order>
{
    public required Guid Id { get; init; }
}
```

This bypasses all filters (including soft-delete), loads the entity, and resets `IsDeleted`, `DeletedAt`, and `DeletedBy`.

### Why does my repository return stale data?

If you use `AsNoTracking()` (the default for queries), the data is a snapshot at query time. For mutations, ensure the entity is loaded within the same scope as the save. If you cache entity references across scopes, they may become stale.

### How do I debug generated code?

All generated files live under `obj/Debug/net10.0/generated/`. In Visual Studio, expand **Dependencies > Analyzers > Pragmatic.SourceGenerator** in Solution Explorer. You can set breakpoints in generated `.g.cs` files.

---

## Getting Help

- **GitHub Issues**: [github.com/pragmatic-design/Pragmatic.Design/issues](https://github.com/pragmatic-design/Pragmatic.Design/issues)
- **Showcase Examples**: See the `Showcase.Catalog`, `Showcase.Booking`, and `Showcase.Billing` projects for working entity, repository, query, and mutation implementations.
- **Diagnostics Guide**: See [diagnostics.md](14-diagnostics.md) for detailed explanation of every PRAG06xx diagnostic.
- **Query Pipeline**: See [query-pipeline.md](15-query-pipeline.md) for the full query execution flow.
- **Mutation Pipeline**: See [mutation-pipeline.md](16-mutation-pipeline.md) for the full mutation execution flow.
