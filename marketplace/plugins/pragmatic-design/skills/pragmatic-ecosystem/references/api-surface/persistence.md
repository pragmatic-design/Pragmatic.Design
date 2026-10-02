# API Surface — Pragmatic.Persistence + Pragmatic.Persistence.EFCore

Attribute-first reference for consumers. Each entry follows the triple: **decorate → SG generates → consume**.
Signatures here are those of the published NuGet packages. For the complete list of PRAG diagnostics: `../diagnostics.md`.

## Packages

| Package | When | Notes |
|---|---|---|
| `Pragmatic.Persistence` | Always when there are entities/queries/repositories | No EF Core, no provider. Carries its analyzers (PRAG0680-0688, e.g. `new Entity()`) |
| `Pragmatic.Persistence.EFCore` | EF Core runtime for real providers | Adds interceptors, query filters, providers |
| `Pragmatic.SourceGenerator` (analyzer) | Always — produces entity/repo/query/dbcontext | `<IncludeAssets>analyzers; build; buildtransitive</IncludeAssets>` |
| `Pragmatic.Migrations` | Replaces EF Migrations with declarative diff | Activated with `app.UsePragmaticMigrations()` |

Providers a host can declare (`DatabaseProvider`): `PostgreSql`, `SqlServer`, `SQLite`, `MySql`, `InMemory`.
`MySql` is Oracle's `MySql.EntityFrameworkCore` — GPL-2.0 with the FOSS exception, referenced by the
application, by no Pragmatic package. Marker types (`IDatabaseProvider`, namespace
`Pragmatic.Persistence.EFCore.Providers`) exist for `PostgreSQL`, `SQLServer`, `SQLite`, `InMemory`.

## Identifiers — `Pragmatic.Persistence.Identifiers`

| Type | Form | Typical use |
|---|---|---|
| `Guid7` | `static Guid7.New()`, `Guid7.NewForSqlServer()` | PK ID for `[Entity]` (default for Postgres/SQLite) |
| `ShortGuid` | `static Encode(Guid) → string`, `Decode(string) → Guid` | URL-safe, 22-char base64url |
| `OpaqueId` | `OpaqueId(salt)` instance, `Encode(long)`/`Decode(string)` | Mask auto-increment IDs in URLs |
| `Slug` | slugification helper | Convert titles → URL slugs |

There is no separate `Pragmatic.Identifiers` package: identifiers live here.

---

# Entity Attributes — `Pragmatic.Persistence.Entity`

## `[Entity]`

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EntityAttribute : Attribute;
```

- **Target**: `class`. Must be `partial`.
- **Key**: always a `Guid` — `PersistenceId`, a version 7 value assigned at construction, with `Id` as its
  read-only alias. There is no type argument. An identifier carried from another system is an ordinary
  property with its own uniqueness (`[Unique(nameof(LegacyCode))]`).
- **Generates**:
  - `{Ns}.{Entity}.Traits.g.cs` — `PersistenceId`, `Id`, and the Audit/SoftDelete properties when
    those attributes are present. **One file**, not one per trait
  - `{Ns}.{Entity}.Create.g.cs` — factory `Create(...)` when applicable
  - `{Ns}.{Entity}.Setters.g.cs` — `internal Set{Property}(...)` with change tracking
  - `{Ns}.{Entity}.Specs.g.cs` — `{Entity}Specifications.ById(...)`, and `By{LogicKey}(...)`
  - `{Ns}.{Entity}.Repository.g.cs` — the nested `{Entity}.Repository`
  - `EntityConfig.{Ns}.{Entity}.g.cs` (HOST-side) — the EF configuration
- **Consume**:
  ```csharp
  [Entity] public partial class Booking { }
  var b = Booking.Create(...);                    // generated factory
  var repo = sp.GetRequiredService<IRepository<Booking>>();
  ```
- **Diagnostics**: PRAG0600 (must be partial), PRAG0680 (`new Booking()` forbidden — use `Create`).

## `[Auditable]`

- **Target**: class with `[Entity]`.
- **Generates**: `CreatedAt: DateTimeOffset`, `CreatedBy: string?`, `UpdatedAt: DateTimeOffset?`, `UpdatedBy: string?`. Populated by the EFCore interceptor in `SaveChanges`.
- **Consume**: read-only on the domain side. No explicit calls required.

## `[SoftDelete(Cascade = false)]`

- **Target**: `[Entity]` class.
- **Generates**: implements `ISoftDelete` (`IsDeleted`, `DeletedAt`, `DeletedBy`); a nested `{Entity}.SoftDeleteFilter` registered globally, plus EF Core’s own named filter `HasQueryFilter("SoftDelete", ...)` as a safety net for code that queries the DbSet directly; with `Cascade=true` propagates to `[Relation.X]` members marked for soft-delete.
- **Consume**: queries automatically exclude deleted records. To read them too:
  `filterToggle.Disable<{Entity}.SoftDeleteFilter>()`, `UseMode(FilterMode.Raw)`, or
  `Query(QueryStrategy.Raw)`.
  ⚠️ **`IgnoreQueryFilters()` does not do it**: it removes EF Core's own named filter, while the
  Pragmatic one is a `Where` the repository applies on top.

## `[BelongsTo<TBoundary>]`

- **Target**: `[Entity]` class, and a domain action or mutation — one attribute for both
  (`Pragmatic.Persistence.Entity`), generic only.
- **Generates**: routes the type to the DbContext of the specified boundary. If omitted, the boundary is
  inferred from the namespace. On an operation it chooses the boundary only: the group still comes from
  the namespace.

## `[HasOwner]` / `[HasAccessScopes]`

- Generate `OwnerId: string` or `AccessScopes: List<string>`, plus the respective filters (`OwnershipFilter` Priority 200, `ScopedDataFilter` Priority 250). Bypass: permission `{boundary}.{entity-kebab}.view-all`.
- **Both are written at `SaveChanges`**, by `OwnershipInterceptor` and `ScopeInterceptor` — not by the mutation invoker, so a row created by an action through a repository is attributed too. Insert-only, and only when the value is absent; no current user means nothing is written and the row is the system's. `ScopeInterceptor` then materialises the registered `DataScopeRule<T>` (inserts **and** updates), in that order.
- **Diagnostics**: PRAG1100 (partial, from `Pragmatic.SourceGenerator.Analyzers`), PRAG1104 (manual OwnerId → SG skips generation, filter still emitted).

## `[LogicKey]`

- **Target**: property.
- **Generates**: a unique DB index — **partial** when the entity is `[SoftDelete]`, so a deleted row
  does not block re-inserting the same code — and a `GetBy{Property}Async(value, ct)` method on the
  **concrete** repository (`{Entity}.Repository`), not on `IRepository<T>`.
  ```csharp
  [LogicKey] public string Code { get; private set; }
  // → await repo.GetByCodeAsync("ABC123", ct)
  ```
  Two `[LogicKey]` properties make one composite key: `GetByCodeAndSeasonAsync(code, season, ct)`.
- **Order**: `[LogicKey(Order = n)]`, lower first. It decides the index's leading column — the only one
  it can be searched by alone — and the parameter order of the lookup. Unset keeps declaration order,
  so nothing existing moves. ⚠️ Unset means `0`, so setting it on one part of two puts the **other**
  first; ⚠️ without it, moving a property up or down in the class is a schema change *and* a signature
  change, with a green build.

⚠️ **`[Resource("name")]` with no `Capabilities` generates nothing.** The default is
`ResourceCapabilities.None`, so the attribute names a resource, configures no operation, produces no
`{E}ReadDto`, and reports nothing. Name the capabilities: `Capabilities = ResourceCapabilities.All`, or
the subset the entity actually offers.

## Publishing a rule — `[Query]` on a `Specification<T>`

The inverse of the section below, and the shorter of the two. `[Query]` on a **static** member
returning `Specification<TEntity>` derives `{Member}Query` in the container's namespace: the member's
parameters become the query's inputs, `Paged = true` adds the paging surface, and `[Endpoint]` /
`[RequirePermission]` written beside the member apply to the derived query. The specification itself
is untouched — it stays a predicate and still composes with `&`.

- **Opt-in twice.** No `[Query]`, no query; no `[Endpoint]`, no route. `[Endpoint]` on a member that
  derives nothing is **PRAG0525**.
- **The entity comes from the specification**, not from the attribute's argument: two answers could
  disagree.
- ⚠️ **PRAG0726** — two specifications in one namespace whose members share a name would derive one
  type twice. It is an **error**, not a rename: two generated files would carry one hint name, and
  Roslyn answers a duplicate hint by discarding the whole generator's output under a warning.

```csharp
// The other half of the entity's generated KnowledgeItemSpecifications, in the entities' namespace —
// not a class of its own: see pragmatic-use-foundation, "Where to put them".
public static partial class KnowledgeItemSpecifications
{
    [Query<KnowledgeItem>(Paged = true)]
    [Endpoint(HttpVerb.Get, "api/knowledge/confirmed")]
    public static Specification<KnowledgeItem> Confirmed()
        => Spec<KnowledgeItem>.Where(k => k.IsConfirmed);
}
```

⚠️ The derived query is emitted in the **namespace of the class that declares the rule**: with the rules on
the entity's partial, that is the entities' namespace.

Use the class form below instead when the read **composes** — several filters, a sort, a projection.

## Reusing a rule — `Specification<T>` on a query

- **A property of type `Specification<TEntity>` is applied whole**, in the generated `Apply()` and in
  `ToSpecification()`. Recognised by its type; `null` is skipped, so a rule that applies sometimes
  answers null when it does not.
- **`[BindSpecification]`** marks the input the rule reads. A specification cannot be bound from a
  request — there is no way to deserialize a predicate — so it is a computed, get-only property and what
  crosses the wire is the value it is built from. The attribute takes that value out of the filter model:
  without it a nullable input would generate a `Where` **and** feed the rule, applying it twice.
- **Composition is AND**, like every contribution. An alternative goes *inside* one property — `a | b` —
  where the reader can see it, which is the rule `[FilterDto]` groups already follow.
- ⚠️ **Not for ordinary filters.** `[Filter] Guid? GuestId` is shorter and clearer than any
  specification of the same predicate. Reach for one when the predicate has a name worth writing down
  and more than one caller.
- **PRAG0709**: a `[BindSpecification]` input on a query with no `Specification<T>` property at all —
  the attribute claims a reader that does not exist.

```csharp
[Query<KnowledgeItem, KnowledgeItemDto>]
public partial class SuggestTermsQuery : IPagedInput
{
    [Filter(Operator = FilterOperator.Contains)] public string? Term { get; init; }

    [BindSpecification] public KnowledgeKind? Kind { get; init; }

    public Specification<KnowledgeItem> Rule =>
        Kind is { } kind ? KnowledgeItemSpecifications.Suggestable(kind) : KnowledgeItemSpecifications.Confirmed();

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
```

## `[PartOf<TParent>]`

- **Target**: entity class.
- **Means**: this entity has no life of its own — it is written through `TParent`.
- **Enables**: a mutation on `TParent` may carry this entity's DTOs and have them created, updated and
  removed alongside it, in the parent's transaction.
  ```csharp
  [Entity] [PartOf<Order>]
  public partial class LineItem : IEntity { … }

  [Mutation(Mode = MutationMode.Update)]
  public partial class UpdateOrderMutation : Mutation<Order>
  {
      public required Guid Id { get; init; }

      // Named after the NAVIGATION, not after the collection you had in mind:
      // [Relation.OneToMany<LineItem>] on Order produces LineItems.
      public required List<LineItemDto> LineItems { get; init; }
  }
  ```
- **Fail-closed**: without it the parent may not write the child — the relation cannot tell a line item
  from a room type, and both are `[Relation.OneToMany]`. **PRAG0436**.
- **Diagnostics**: PRAG0436 (child not declared part of this aggregate), PRAG0437 (its elements have no
  key to be matched by), PRAG0438 (the child also has a mutation of its own — the two declarations
  contradict each other), **PRAG0439** (the property matches no navigation — the child would be
  dropped and the endpoint answer 200 having written nothing).
- The load brings the child with it: a keyed merge decides what to remove by looking at what is there,
  so an unloaded collection would remove nothing and add everything.
- Removing a `[SoftDelete]` child **flags it**, like any other delete of that entity. Enforced at save
  time, so it holds on every path — repository, mutation, this merge, a hand-written `context.Remove`.
  The stamp is never rewritten (a cascade shares one instant and restore matches on it).
- Erasure that must really delete says so: `using (SoftDeleteScope.Suspend()) { … }`.
- ⚠️ `ExecuteDelete` goes straight to SQL and is never intercepted — **PRAG0687** on a `[SoftDelete]`
  entity.

## Eager loading — `RequiredNavigations` and `[EagerLoad]`

- **Derived, not declared, for what the DTO reads.** Every `[MapFrom]` DTO carries a generated
  `RequiredNavigations` — flattened paths (`[MapProperty("Customer.Name")]`), nested DTOs, and
  collections of element DTOs. Emitted on every DTO, empty included, so generated code can name it
  without checking.
- **Consumed by**: a mutation's load (from `[ReturnsDto<T>]` and from the children it writes), a
  generated query as its include paths, and `WithIncludesFor{Dto}()` for a hand-assembled query.
- **Declared for what your own code reads** — a validator, a lifecycle hook, an `ApplyAsync`:
  `[EagerLoad("Customer")]`, `[EagerLoad("OrderLines.Product")]` on the mutation or the query. The two
  sources merge.
- **Missing navigation is loud**: the generated mapping throws naming the navigation and both ways
  out, rather than reading null and carrying on.
- ⚠️ **EF drops an `Include` before a type-changing `Select`, silently.** Measured. Project with
  `{Dto}.Projection` when you are projecting anyway — it resolves the navigation in SQL.
- ⚠️ **Cross-boundary relations generate no navigation**: different DbContexts, no property to include.
  Flattening across one is **PRAG0334** — unless the boundary declares `[ReadAccess<T>]`, which
  generates the navigation, read-only.

Full treatment: `Pragmatic.Persistence/docs/20-eager-loading.md`.

## `[CollectionStrategy(CollectionStrategy.X)]`

- **Target**: a collection property on a mutation or a `[Patch]`.
- **You normally do not write it.** The strategy is derived: `[Patch]` ⇒ `AddOnly` (a partial
  representation says nothing about what it omits), anything else ⇒ `Sync` (a full representation says
  what it omits is not there).
- **Values**: `Sync` (update, add, **remove what was not sent**) · `AddOnly` (update and add) ·
  `Replace` (discard and rebuild — new rows, new identities) · `Ignore`.
- **Matching key**: the element DTO's `Id`, else the child's `[LogicKey]`. With neither: **PRAG0333**
  on a mutation, **PRAG2203** on a patch.
  ```csharp
  [CollectionStrategy(CollectionStrategy.AddOnly)]   // only where the default reads the operation wrong
  public required List<LineItemDto> LineItems { get; init; }
  ```

## `[GeneratedValue("ORD-{YYYY}{MM}-{SEQ:5}")]`

- **Target**: `string` property.
- **Tokens**: `{YYYY}` `{MM}` `{DD}` `{SEQ:N}` `{RANDOM:N}` `{GUID:N}`.
- **Properties**: `AutoGenerate=true`, `SequenceName`.
- **Generates**: validation, parsing, EF unique constraint — and the value itself: the unit of work fills
  it before the save. No `[ComputedDefault]` beside it.

## `[ComputedDefault<TEntity, TValue, TGenerator>]` and `[HasPresets]` + `[PresetProvider<TProvider>]`

For what a format cannot express, both run in the **create** mutation's invoker:

- `[ComputedDefault<Invoice, string, InvoiceNumberGenerator>]` on a property — the generator implements
  `IDefaultValueGenerator<TEntity, TValue>` and computes the value from the new entity and the
  `LifecycleContext` (clock, user, tenant).
- `[HasPresets]` + `[PresetProvider<ReservationPresetProvider>]` on the entity — the provider implements
  `IPresetProvider<TParent>.CreatePresetsAsync(parent, context, ct)` and returns child entities saved
  with the parent (a reservation created with its default room assignment). Several providers may be
  declared.

## `[Lookup]`

- **Target**: small "lookup table" classes (countries, statuses).
- **Generates**: `ILookupCache<T, TId>` with in-memory cache; implicit navigations on entities that have a FK `{Type}Id`.

## `[Inheritance(InheritanceStrategy.TPH | TPT | TPC, DiscriminatorColumn = "Type")]`

- **Target**: base class of the hierarchy.
- **Generates**: EF hierarchy mapping configuration.

## `[Relation.OneToMany<TRelated>]` / `[Relation.ManyToOne<>]` / `[Relation.OneToOne<>]` / `[Relation.ManyToMany<>]`

- Fluent extension `.WithNavigation("Items", Inverse = "Parent", OnDelete = DeleteBehavior.Cascade)`.
- **Generates**: navigation properties, FK, EF config. Cross-boundary: FK only, no navigation.
- **Relations are declared, not written.** A navigation or a `{Entity}Id` key typed as a property is `PRAG0619`; EF Core's `[ForeignKey]`/`[InverseProperty]` are `PRAG0635`. The generator owns every member a relation produces, and the features that read keys (`[GenerateHierarchy]`, `[TemporalRelation]`, `[CascadeOn]`, `[Lookup]`, `[MapFrom]`, events) read the declared relation, not a member found by name.
- **Diagnostics**: PRAG0612/PRAG0617 (several relations to one type without `WithNavigation`/`Inverse`); PRAG0610/0611 (the two ends contradict each other); PRAG0618 (one-to-one without exactly one principal). Cross-boundary stays FK-only unless the boundary declares `[ReadAccess<T>]` on the target, in which case the navigation is generated.
- **Many-to-many with a join entity** needs `LeftKey` / `RightKey` on `.WithNavigation(...)`, naming the join entity's properties that hold each side's key — unless the join entity declares its own `[Relation.ManyToOne<>]` to both ends, which generates them. ⚠️ Without either, EF Core binds shadow foreign keys named after the navigation that the migration never creates, and the first write dies on a missing column: `PRAG0616` refuses the declaration instead.
- **A self-referencing many-to-many** names its join columns from the navigations, not the entity — otherwise both ends produce the same name and the database refuses the primary key. It also needs `Inverse` on both sides (`PRAG0613`), so a symmetric relation arrives as two collections to union.

## `[ValueObject]`

- **Target**: `record`.
- **Generates**: `Create()` with `Validate()`; `CreateUnsafe()` for deserialization.

## `[StateMachine<TStatusEnum>]` + `[InitialState]` / `[TransitionFrom(...)]` on enum values

- **Target**: `[Entity]` class with a `Status: TStatusEnum` property (private setter).
- **Generates**: `TransitionTo(state)`, `CanTransitionTo(state)`, `AllowedTransitions()`.
- **Diagnostics**: PRAG0620 (missing initial), PRAG0621 (unreachable state).

---

# Query Attributes — `Pragmatic.Persistence.Query`

## `[Query<TEntity, TResult>]` or `[Query<TEntity>]`

- **Target**: `partial class`. Implements `IQuery<TEntity, TResult>` or `IQuery<TEntity>`.
- **Property conventions**:
  - `required` → filter always applied.
  - nullable + `[Filter(...)]` → conditional filter (null = skip).
  - `[Sort(...)]` on `SortDirection?` → user-driven sorting.
  - `Page` + `PageSize` → automatically becomes `IPagedQuery<>`.
  - `[FromCurrentUser]` → filled by the invoker from the caller, always applied (see below).
  - `[FromClock]` → filled by the invoker from `IClock`; no filter, a specification reads it (see below).
  - `[SearchAcross("A", "B", IgnoreCase = true)]` on a `string?` → one value against several columns, `||`.
- **Generates**: `Apply(IQueryable<T>) → IQueryable`; `Projection: Expression<Func<T, TResult>>` when `TResult` is present.
- **Consume**: as an operation (permission, validation, its own endpoint) through the boundary facade,
  `boundary.Orders.SearchOrders(…)`; from inside another operation, against a repository you already
  hold:
  ```csharp
  var page = await _orders.RunAsync(new SearchOrders { CustomerId = id, Page = 1 }, ct);
  ```

## `[FromCurrentUser]` / `[FromCurrentUser(string? member)]` — `Pragmatic.Identity`

- **Target**: a property of a `[Query]`, written `{ get; private set; }` (**PRAG0730** otherwise).
- **Binds**: without a member, `ICurrentUser.Id` (a `string` property); with one —
  `nameof(Employee.Id)` — that member of the `[PragmaticUser]` entity, read through the generated
  `{User}Resolver` (same compilation, `Pragmatic.Identity.Persistence` referenced). The property has the
  member's type.
- **Who writes it**: the query's generated invoker, after validation and the permission check, before
  the read. Not authenticated → `UnauthorizedError` (401); no user entity → `NotFoundError` (404).
- **Never a parameter**: not query string, route, body, OpenAPI, or boundary-member argument. Still a
  property: part of the `[Cacheable]` key and of the serialized query.
- **As a filter**: always applied, `==` even on a `string`; `[Filter(MapTo = …)]` still renames the column.
- **PRAG0731**: the binding cannot be generated — unknown member, different type, `nameof` over another
  type, no `[PragmaticUser]` entity or resolver, member-less form on a non-`string`.

```csharp
[Query<Allowance, AllowanceBalanceDto>]
[Endpoint(HttpVerb.Get, "api/me/balances")]
public partial class GetMyBalancesQuery
{
    [FromCurrentUser(nameof(Employee.Id))] public Guid EmployeeId { get; private set; }
    [Filter] public required int Year { get; init; }
}
```

## `[FromClock]` — `Pragmatic.Temporal.Clock`

- **Target**: a property of a `[Query]`, a `[DomainAction]` or a `[Mutation]`, `{ get; private set; }`,
  of type `DateOnly` (gets `IClock.UtcToday`) or `DateTimeOffset` (gets `IClock.UtcNow`). **PRAG0734**
  otherwise.
- **Who writes it**: the operation's generated invoker, from `GetRequiredService<IClock>()`, after
  validation and authorization — before the read, or before `Execute`/`ApplyAsync` and the
  `[LoadEntity]` preload. On a mutation it is also written to an entity member of the same name, if any.
- **Not a filter, not a parameter**: a specification property of the query reads it — typically passing
  it to a `[ComputedFilter]` method (`EmployeeComputedFilters.IsAwayOnSpec(Today)`). Part of the
  `[Cacheable]` key.

## `[ComputedFilter]` on a method

- `[ComputedFilter] public bool IsAwayOn(DateOnly day) => …` → `IsAwayOnSpec(DateOnly day)` and
  `query.WhereAwayOn(day)` on `{Entity}ComputedFilters`. No `Expr` member.
- **PRAG0733**: not `bool`, static, generic, block body, or a `ref`/`out`/`in`/`params` parameter.

## `[Filter(Operator, MapTo, IgnoreCase)]`

- `Operator`: `Equals|NotEquals|Contains|StartsWith|EndsWith|GreaterThan|...|In|Between`.
- `MapTo`: nested path e.g. `"Customer.Name"`.

## `[Sort(MapTo, DefaultDirection, Priority)]`

- `DefaultDirection`: `SortDirection?` — `Ascending`, `Descending`, or unset for no default.
- `Priority`: multi-sort order (lower = first).

## `[GridFilter<TEntity>]`

- **Target**: `partial class`. Designed for UI grids with operator selection.
- **Property pattern**: `string? Name` + `StringOperator? NameOperator` (operator chosen at runtime).
- **`[Filterable(Operators = FilterOps.String|Range|...)]`** on the property.
- **Generates**: `Apply(IQueryable<T>)` with a switch on the runtime operator. Page/PageSize built-in.

## `[FilterDto<TEntity>]`

- **Target**: `partial class/struct`. Composable filter DTO.
- Properties: `[Filter]` for individual filters, `[FilterGroup(FilterLogic.Or)]` for OR groups.
- **Generates**: extension `ApplyFilter(this IQueryable<T>, dto)`.

## `[Projectable]`

- **Target**: expression-bodied getter-only property.
- **Generates**: nested class `{Entity}.Expr` with SQL-translatable `Expression<Func<TEntity, TResult>>`.
- **Consume**:
  ```csharp
  public partial class Order { [Projectable] public decimal Total => SubTotal + Tax; }
  query.Select(Order.Expr.Total);
  ```

---

# Patch Attributes — `Pragmatic.Persistence.Patch`

## `[Patch<TEntity>]`

- **Target**: `partial class/struct`.
- **Generates**: `ApplyPatch(TEntity)`, internal set tracking (`_setProperties`), `MarkSet(name)`, `IReadOnlyCollection<string> SetProperties`.
- **Tri-state semantics**: an unmarked property is ignored; a nullable property explicitly set to `null` is applied. For "not sent vs sent null" use `Patch<T>` from `Pragmatic.Patch` (separate package) on the properties.
- **Consume**:
  ```csharp
  var patch = new UpdateOrderPatch { Total = 99 };
  patch.ApplyPatch(order);   // sets only Total
  ```

---

# Less common attributes — reach for them when the case fits

## `[RollUp<TChild>(childProperty, aggregation)]` — a stored aggregate over children

```csharp
[RollUp<InvoiceLine>(nameof(InvoiceLine.Amount))]          // Sum (default)
public decimal Subtotal { get; private set; }

[RollUp<OrderLine>("", RollUpAggregation.Count)]           // Count: the child property is ignored — pass ""
public int LineCount { get; private set; }
```

Kept current as children are created and deleted, in the child's own unit of work — no recompute by
hand. The host registers the rules; `AddPragmaticPersistenceRepositories<TDbContext>()` does too for a
hand-wired app, without doubling them.

## `[CascadeOn<TSource>(nameof(TSource.Prop))]` and `[CascadeSource]`

A property kept in step with a property of another entity (`LineItem.UnitPrice` follows
`RoomType.BaseRate`). In one assembly `[CascadeOn]` alone is enough; when the target lives in **another
assembly**, mark the source property `[CascadeSource]` so its generated setter raises the
`EntityPropertyChanged` event the other side listens to.

## `[GenerateTimeline]` — periods of a temporal relation, with their neighbours

On an `ITemporalRelation` entity (`ValidFrom`/`ValidTo`): generates `GetTimeline()` on the DbContext —
a LAG/LEAD CTE returning each period with the end of the one before and the start of the one after,
partitioned by the owning relation. ⚠️ The row type is nested in the generated class:
`{Entity}TimelineExtensions.{Entity}TimelineEntry`.

## `[Join<TTarget>(Via = "…")]` on a query

Adds a join to a `[Query]`: `Via` is the navigation path (`"Customer"`, `"Lines.Product"`) for declared
relations; `ForeignKey`/`TargetKey` for a POCO without navigations.

## `[PolymorphicAttachment]` + `[Attachable<TOwner>]` — one child type, several owner types

```csharp
[Entity]
[PolymorphicAttachment]          // stored with OwnerType/OwnerId columns
[Attachable<Invoice>]            // one per owner type
[Attachable<Reservation>]
public partial class Document : IEntity { }
```

Generates `Query{Attachment}s(owner, repository)` and `Load{Attachment}sBatchAsync(…)` on each owner and
`ForOwner<TOwner>()` on the attachment. ⚠️ The attributes go on the **attachment**, naming the owner:
`[Attachable<Document>]` on `Invoice` generates nothing, silently. No foreign key exists to an owner —
for integrity per owner, `[HasAttachments]` (`pragmatic-use-traits`) gives each owner its own table.

## Data grids — `[GenerateGridBridge]`, `[GridAdapter<T>]`, `[GridField]`, `[GridExclude]`

For a front-end grid (DevExpress, PrimeNG) that sends its filter/sort/group state as JSON:

- `[GenerateGridBridge]` on the entity generates `query.ApplyCanonical(GridFilterRequest)` — a switch per
  field, no reflection. It is generated anyway for an entity with a `[GridFilter<T>]` DTO.
- `[GridAdapter<TEntity>(Framework = GridFramework.DevExpress | PrimeNG, SupportNestedFilters,
  SupportGrouping)]` on a class generates the translation from that grid's JSON to the canonical request.
- `[GridField("customerName", Property = "Customer.Name", Filterable, Sortable, Groupable,
  AllowedOperators = …)]` maps a JSON field to a property path; `[GridExclude("Secret")]` takes back a
  property `[Filterable]` exposed.

⚠️ Only mapped fields are reachable: a grid asking for an unmapped field cannot reach an arbitrary
column — keep it that way, and expose sensitive columns to no grid.

# Main public types

## Repository

```csharp
public interface IReadRepository<TEntity>
    where TEntity : class, IEntity
{
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<TEntity>> FindAsync(ISpecification<TEntity> spec, CancellationToken ct = default);
    Task<int> CountAsync(ISpecification<TEntity> spec, CancellationToken ct = default);
    Task<bool> ExistsAsync(ISpecification<TEntity> spec, CancellationToken ct = default);
    Task<TEntity?> FirstOrDefaultAsync(ISpecification<TEntity> spec, CancellationToken ct = default);
    IQueryable<TEntity> Query();              // raw — bypasses filters in FilterMode.Raw

    // The declared query, run against this repository's own set.
    Task<IReadOnlyList<TResult>> RunAsync<TResult>(IQuery<TEntity, TResult> query, CancellationToken ct = default)
        where TResult : class;
    Task<PagedResult<TResult>> RunAsync<TResult>(IPagedQuery<TEntity, TResult> query, CancellationToken ct = default)
        where TResult : class;
}
// ⚠️ RunAsync runs no operation pipeline: its caller is already inside one. Invoking the query *as an
// operation* — with its own permission checked — is the boundary facade's job.
// Declared in Pragmatic.Persistence, namespace Pragmatic.Persistence.Repository.

public interface IRepository<TEntity> : IReadRepository<TEntity>
    where TEntity : class, IEntity
{
    void Add(TEntity entity);
    void AddRange(IEnumerable<TEntity> entities);
    void Update(TEntity entity);
    void Remove(TEntity entity);              // not "Delete"
    void RemoveRange(IEnumerable<TEntity> entities);
}
```

One type argument: `IRepository<Order>`. The transaction commit is done by the
generated invoker via `IUnitOfWork` — repositories do not expose `SaveChangesAsync`. Bulk operations
live in `Pragmatic.Persistence.EFCore` extensions. For each `[LogicKey] public TKey Code { get; }` a
`GetByCodeAsync(TKey, CancellationToken)` lookup is generated on the **concrete** `{Entity}.Repository`
(see `[LogicKey]` above), not on `IRepository<T>`.

## UnitOfWork

```csharp
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task<ITransaction> BeginTransactionAsync(CancellationToken ct = default);
}
// Extension:
public static Task<Result<T, TError>> ExecuteInTransactionAsync<T, TError>(
    this IUnitOfWork uow,
    Func<CancellationToken, Task<Result<T, TError>>> op,
    CancellationToken ct = default);
```

## Query types

```csharp
public interface IQuery<TEntity> { IQueryable<TEntity> Apply(IQueryable<TEntity> source); }
public interface IQuery<TEntity, TResult> : IQuery<TEntity>
{
    Expression<Func<TEntity, TResult>>? Projection { get; }
}
public interface IPagedQuery<TEntity, TResult> : IQuery<TEntity, TResult>
{
    int Page { get; } int PageSize { get; } int Skip { get; } int Take { get; }
}

// Pragmatic.Persistence.Query.Results — a result, not a bare page: check IsSuccess before Items.
public sealed class PagedResult<T>
{
    bool IsSuccess { get; } bool IsFailure { get; } QueryError? Error { get; }
    IReadOnlyList<T> Items { get; } int TotalCount { get; } int Page { get; } int PageSize { get; }
    int TotalPages { get; } bool HasPreviousPage { get; } bool HasNextPage { get; }
}
```

`Pragmatic.Pagination.Page<T>` (Abstractions) is a different thing: a plain page with `Number`, never a
failure, returned by Mapping.EFCore's `ToPagedDtoAsync()`.

## Filter primitives

```csharp
public enum FilterOperator { Equals, NotEquals, Contains, StartsWith, EndsWith,
    GreaterThan, GreaterOrEqual, LessThan, LessOrEqual, In, Between }
public enum StringOperator { Equals, Contains, StartsWith, EndsWith, NotEquals }
[Flags] public enum FilterOps { None=0, Equality=1, String=2, Compare=4, Range=8, All=15 }
public enum SortDirection { Ascending, Descending }
public enum FilterMode { Normal, Admin, Background, Raw }   // bypasses filters/scopes
```

## Declared visibility rules

A rule the entity carries, applied to every query of it — root, `Include`, projected subqueries, and
a raw `context.Set<T>()` alike. The attribute is what installs it: the generated entity configuration
turns each one into an **EF Core named global query filter**, beside `"SoftDelete"` and `"Tenant"`.

```csharp
[Entity]
[VisibleWhen<ActiveOnly>]                    // repeatable; rules compose as AND, in Priority order
public partial class Property { ... }

public sealed class ActiveOnly : VisibilityRule<Property>   // the rule IS the IQueryFilter<T>
{
    public override Expression<Func<Property, bool>> ToExpression() => p => p.IsActive;
}
```

- `VisibilityRule<T>` — `ToExpression()` is the predicate. `Priority` and `Scope` are inherited from
  `IQueryFilter<T>` but decide nothing for a declared rule: EF owns the enforcement.
- ⚠️ **The rule must be concrete, non-generic, and constructible with no arguments.** Its predicate is
  read once, from `new TRule().ToExpression()`, while EF builds the model. `PRAG0718` says so. A rule
  that wants `ICurrentUser` cannot be one at all — the model is cached per context type, so a scoped
  dependency captured there is served to every later request. Caller-dependent predicates are the
  ownership and scope filters, which compose additively and run per request.
- **No `FilterMode` lifts it** except `Raw`. `Background` lifts only `"Tenant"`, by name.
- Reading past it is `[WithoutFilter<ActiveOnly>]` on the operation, which the generator turns into
  `IgnoreQueryFilters(["Visibility:{FullName}"])`. The attribute still also takes an entity type,
  which lifts everything that entity carries through the Pragmatic provider.
- In hand-written code: `IQueryFilterToggle.DisableVisibilityRule<TRule>()`, or
  `DisableQueryFilter(VisibilityFilterName.Of<TRule>())`. ⚠️ **Not `Disable<TRule>()`** — that reaches
  `DefaultQueryFilterProvider`, where a declared rule is not registered, so it compiles and does
  nothing. **`PRAG0720`** rejects it.
- **`PRAG0719`** refuses `[WithoutFilter<T>]` on an operation with no `[RequirePermission]`. Reading
  past a filter is a privilege; it asks that the privilege be named, not that it be the right one.
- `PRAG0717` — the rule is typed for another entity.
- ⚠️ **A hidden row is hidden from writes too**: the row is invisible to every query of the entity,
  so an update loads nothing. The operations that manage those rows need
  `[WithoutFilter<TRule>]`, or the state the rule keys on is a one-way door.
- `IVisibilityFilterProvider` / `QueryFilterProviderAdapter` — what puts **DI-registered** filters into
  the navigation `FilterMap`. That path is for filters registered by hand; a declared rule does not use
  it.

## Soft delete / ownership runtime hooks

- `ISoftDelete`, `IOwnedEntity`, `IScopedEntity`, `IAuditable` — implemented by SG on classes using the corresponding attributes.
- `IPermissionBasedFilter` — interface of automatic filters (Ownership/Scoped).
- `ICurrentUser` (from `Pragmatic.Identity`) — read by MutationInvoker to populate OwnerId.

---

# EFCore — `Pragmatic.Persistence.EFCore`

## Recommended pattern (consumer): `PragmaticDatabase`

Empty decorated class, one per logical database:

```csharp
using Pragmatic.Composition.Attributes;   // [PragmaticDatabase]
using Pragmatic.Composition.Database;     // PragmaticDatabase
using Pragmatic.Composition.Enums;        // DatabaseProvider

[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
public sealed class AppDatabase : PragmaticDatabase;
```

- **`Provider`**: `PostgreSql | SqlServer | SQLite | MySql | InMemory`.
- **`ConfigKey`**: key in `IConfiguration` for the connection string.
- The SG generates the DbContext (`AppDbContext`), registers `DbContextOptions`, discovers all `[Entity]` across all mapped boundaries.
- Multi-database: declare multiple `PragmaticDatabase` classes and map the boundaries.

## Declaring the topology

You do not write a DbContext. Declare a database in the host, then pair modules to it:

```csharp
using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Enums;

[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
public sealed class AppDatabase : PragmaticDatabase;

[Include<BookingModule, AppDatabase>]
[Include<BillingModule, FinancialDatabase>]     // a second database when they must not share one
public sealed class AppHostModule;
```

- The pairing **is** the topology: how many `DbContext` types get emitted follows from it.
- One `DbContext` per boundary, stamped `[PragmaticDbContext("{boundary}")]`, with `DbSet<T>`,
  `OnModelCreating`, traits, relations and filters applied; the host registers them with the generated
  `AddAllPragmaticDbContexts()`.
- `ConfigKey` is effectively required: empty means the host looks up an empty key and fails at startup
  with an exception that names something else.

## Migrations

`Pragmatic.Migrations` replaces EF Migrations:

```csharp
app.UsePragmaticMigrations();   // on empty DB → creates schema; on existing → incremental diff
```

No `Add-Migration`/`Update-Database`. Declarative snapshot.

---

# Key diagnostics (consumer-facing)

| ID | Severity | Cause | Fix |
|---|---|---|---|
| PRAG0600 | Error | `[Entity]` on a non-partial class | Add `partial` |
| PRAG0602 | Error | `[Database]` DbContext on a non-partial class | Add `partial` |
| PRAG0620 | Error | StateMachine without `[InitialState]` | Mark one state |
| PRAG0622 | Error | `[TransitionFrom]` names a non-existent enum member | Use a real member |
| PRAG0680 | Warning | `new Entity()` instead of factory | Use `Entity.Create(...)` |
| PRAG0684 | Warning | Non-constant SQL in `FromSqlRaw`/`ExecuteSqlRaw` | Use interpolated `FromSql`/`ExecuteSql` |
| PRAG0686 | Warning | Injecting another boundary's DbContext | Call that boundary's actions/queries |
| PRAG0730 | Error | `[FromCurrentUser]` property with a `public`/`internal`/`init` setter | `{ get; private set; }` |
| PRAG0731 | Error | `[FromCurrentUser]` binding cannot be generated | Follow the message: member, type, `[PragmaticUser]` |
| PRAG0732 | Error | `[GroupBy]` key or `Via` names no member of the entity | A declared or generated member; `Via` for another entity |
| PRAG0733 | Error | `[ComputedFilter]` the generator cannot write | A `bool` property or instance method with an expression body |
| PRAG0734 | Error | `[FromClock]` binding cannot be generated | `DateOnly`/`DateTimeOffset`, `{ get; private set; }` |
| PRAG1100 | Error | `[HasOwner]` on a non-partial class | Add `partial` |

Full catalog: `../diagnostics.md`.

---

# Reference user patterns (showcase)

| Pattern | File | What it shows |
|---|---|---|
| Entity with relations | `examples/showcase/src/Showcase.Catalog/Properties/Property.cs` | `[Entity]`, `[Auditable]`, `[SoftDelete(Cascade=true)]`, `[BelongsTo<>]`, `[Relation.OneToMany<>]`, `[Relation.ManyToMany<>.WithNavigation]`, `[LogicKey]` |
| Boundary | `examples/showcase/src/Showcase.Catalog/CatalogBoundary.cs` | `[Boundary]` empty partial class |
| Database | `examples/showcase/src/Showcase.Host/ShowcaseAppDatabase.cs` | `[PragmaticDatabase(...)]` |
| Mutation | `examples/showcase/src/Showcase.Catalog/Properties/Mutations/DeletePropertyMutation.cs` | `[Mutation(Mode=Delete)] [Endpoint(Delete, "...")]` |
| Query GridFilter | `examples/showcase/src/Showcase.Catalog/Properties/Queries/PropertyGridFilter.cs` | `[GridFilter<>]`, `[Filterable]`, multi-sort |
| Query Projection | `examples/showcase/src/Showcase.Catalog/Amenities/Queries/SearchAmenitiesQuery.cs` | `[Query<E,DTO>]`, `[Filter]`, `[Sort]`, paging |

For a complete E2E setup (csproj + Program.cs + entity + mutation + endpoint): `../cookbook/crud-web-api.md`.
