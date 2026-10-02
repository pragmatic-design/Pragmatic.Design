# Pragmatic.Design — Patterns Map

Map of **blessed** patterns derived by cross-referencing the features of the unified source generator with their occurrences in the showcase (`examples/showcase/src/`).

A pattern is included here only if:
1. it is explicitly recommended by the design docs, or
2. the SG generates dedicated code that supports it, or
3. it appears ≥ 2 times in the showcase combining ≥ 2 modules.

Patterns scoped to a single module remain in the module skill (`pragmatic-use-{name}`).

---

## Doctrine — the 6 guiding principles

Summarized from the design docs. These are the constraints that apply across every pattern below.

| Principle | Meaning | Source |
|---|---|---|
| **Source-Generator-First** | Topology and contracts at compile-time, runtime with no reflection (exception: EF Core internally). Add NuGet → SG detects → generates. | `pragmatic-architecture.md`, `building-block-guide.md` |
| **3-tier config** | Topology (SG, compile-time) → Strategy (`IPragmaticBuilder.Use*()` in `Program.cs`) → Business (`IStartupStep`) | `pragmatic-architecture.md:143-149` |
| **Result over Exceptions** | `Result<T, IError>` for business failures; exceptions only for bugs. `Ensure` for parameter guards. | `building-block-guide.md:30-85` |
| **Composition by presence** | What is referenced (NuGet or boundary) is discovered and activated. No manual `services.Add*()` for Pragmatic features. | `building-block-guide.md:23` |
| **Convention over config** | Folder = sub-boundary; namespace = boundary; entity requires `partial class`. `[BelongsTo<>]` only for exceptions. | `subboundary-design.md` |
| **Zero allocation hot path** | `[LoggerMessage]`, no LINQ in pipeline, precompiled registry/lookup (no `Type.GetType()`). | `building-block-guide.md:108-222` |

---

## Quick-pick (by task type)

| You need to… | Pattern | Primary skill |
|---|---|---|
| Model an aggregate root | **P01 — Full Entity** | `pragmatic-use-persistence` |
| Expose CRUD HTTP | **P05/P06/P07 — Mutation Create/Update/Delete + Endpoint** | `pragmatic-use-actions-endpoints` |
| Orchestrate logic with dependencies | **P08 — DomainAction** | `pragmatic-use-actions-endpoints` |
| Paginated list with filters | **P09 — Query<T, Dto>** | `pragmatic-use-persistence` |
| UI grid with runtime operator selection | **P10 — GridFilter<T>** | `pragmatic-use-persistence` |
| Partial PATCH | **P11 — GeneratePatch<T>** | `pragmatic-use-foundation` |
| Visibility per user / team | **P12/P13 — Owned / Scoped Entity** | `pragmatic-use-authorization` |
| Materialized scope rules | **P14 — DataScopeRule<T>** | `pragmatic-use-authorization` |
| Declarative permissions per role | **P15 — Role + Permission** | `pragmatic-use-authorization` |
| Instance-level authorization (ABAC) | **P16 — IResourceAuthorizer<T>** | `pragmatic-use-authorization` |
| React to a change in the same boundary (in-process) | **Domain event + [EventHandler]** | `pragmatic-use-events` |
| React to cross-boundary events | **P17 — MessageHandler with Retry** | `pragmatic-use-messaging` |
| Guarantee entity + event consistency | **P18 — Transactional Outbox** | `pragmatic-use-messaging` |
| Multi-step workflow with compensation | **P19 — Saga<TState>** | `pragmatic-use-messaging` |
| Cron / recurring job | **P20 — RecurringJob** | `pragmatic-use-jobs` |
| Multiple implementations of a capability | **P22 — Keyed Service + Decorator** | `pragmatic-use-composition` |
| Integrate an external Pragmatic package | **P24 — UsePackage<T>** | `pragmatic-use-composition` |
| State machine on an entity | **P03 — StateMachine** | `pragmatic-use-persistence` |
| DTO + LINQ projection | **P21 — MapFrom<T> with projection** | `pragmatic-use-foundation` |

---

# Pattern catalog

## Domain Modeling

### P01 — Full Entity (aggregate root)

- **Modules**: Persistence + (opt.) Authorization + Lifecycle + Events
- **Attributes**: `[Entity]`, `[Auditable]`, `[SoftDelete(Cascade)]`, `[ConcurrencyAware]`, `[BelongsTo<TBoundary>]`, `[Resource("name")]`, opt. `[HasOwner]/[HasAccessScopes]`, opt. `[StateMachine<TStatus>]`
- **Decorate**: `partial class` with private setters on properties; domain methods that mutate state.
- **SG generates**: `PersistenceId` (Guid v7 at construction) + `Id` alias — **not** equality, which stays reference-based — traits (Audit/SoftDelete), factory `Create()`, the nested `Entity.Repository` implementing `IRepository<T>`, EF Core EntityConfig, optional filters (Soft/Owned/Scoped) + StateMachine API.
- **Consume**:
  ```csharp
  var booking = Reservation.Create(...);
  repo.Add(booking);                       // void and synchronous — there is no AddAsync
  await uow.SaveChangesAsync(ct);          // IUnitOfWork, keyed on the boundary
  ```
- **Showcase**: `Showcase.Booking/Reservations/Reservation.cs` (maximum combination at 7 modules).
- **Typical diagnostics**: PRAG0600 (partial), PRAG0680 (`new` instead of `Create`).
- **Non-blessed variant**: POCO classes without `[Entity]` manually configured in `OnModelCreating` — you lose the factory, generated repo, and automatic filters.

### P02 — ValueObject

- **Modules**: Persistence
- **Attributes**: `[ValueObject]` on `record`.
- **SG generates**: factory `Create(...)` that invokes `Validate()` (user implements it); `CreateUnsafe(...)` for deserialization.
- **Variant**: `[Entity]` on independent sub-aggregates (e.g. `RoomAssignment`, `Fee`, `Payment`).

### P03 — Entity with State Machine

- **Modules**: Persistence + Events
- **Attributes on entity**: `[StateMachine<OrderStatus>]` (default `Property = "Status"`).
- **Attributes on enum**: `[InitialState]`, `[TransitionFrom(StateA)]` — one source state per attribute, repeated for several (`AllowMultiple`); a two-argument form does not compile.
- **SG generates**: `TransitionTo(state)` (returns `VoidResult<IError>`), `CanTransitionTo(state)`, `AllowedTransitions()`, `RaisesEvent(...)` when combined with `[RaisesEvent]`.
- **Consume**:
  ```csharp
  var r = order.TransitionTo(OrderStatus.Submitted);
  if (r.IsFailure) return r.Error;
  ```
- **Showcase**: `Showcase.Booking/Reservations/Reservation.cs` with `ReservationStatus`.
- **Diagnostics**: PRAG0620 (missing `[InitialState]`), PRAG0621 (unreachable state).

### P04 — Database marker

- **Modules**: Composition + Persistence.EFCore
- **Attribute**: `[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]` on `sealed class : PragmaticDatabase`.
- **SG generates**: DbContext for the database, registers `DbContextOptions`, discovers all `[Entity]` from referenced boundaries.
- **Showcase**: `Showcase.Host/ShowcaseAppDatabase.cs` (App-DB) + `Showcase.Host/ShowcaseFinancialDatabase.cs` (separate Billing-DB).
- **There is no hand-written DbContext variant.** The generated one calls `partial void OnModelCreatingPartial(ModelBuilder)` after every generated configuration; a `partial` part of the context implements it. ⚠️ It changes the model EF holds in memory, not the migrated schema — an index or a table added there is not created by `Pragmatic.Migrations`.

---

## Operations: Mutations

### P05 — Mutation Create + Endpoint

- **Modules**: Actions + Endpoints + Persistence + Validation + Identity (if `[HasOwner]`)
- **Attributes**: `[Mutation(Mode = Create)]`, `[Endpoint(HttpVerb.Post, "api/{boundary}")]` on the mutation; validation attrs on properties.
- **Decorate**: `partial class CreateXMutation : Mutation<X>` with `required`/`[Required]`/`[Range]`/... properties.
- **SG generates**: `CreateXMutationInvoker` (DI resolved, validation pipeline, `Result<X, ValidationError|...>`); ASP.NET endpoint with route binding and ProblemDetails for errors; auto-set of `OwnerId` from `ICurrentUser` if the entity is `[HasOwner]`.
- **Consume**: the client calls `POST` with JSON; the Pragmatic dispatcher resolves the invoker.
- **Showcase**: `Showcase.Billing/Invoices/Mutations/CreateInvoiceMutation.cs`.

### P06 — Mutation Update with state transition

- **Modules**: Actions + Endpoints + Authorization (if `[RequirePermission]`) + Persistence
- **Attributes**: `[Mutation(Mode = Update)]`, `[Endpoint(HttpVerb.Post, "api/.../{id}/confirm")]`, `[RequirePermission("...")]`.
- **Decorate**: `[TransitionsTo<TState>(newState)]` — the invoker performs the move (default before the body; `When = AfterBody` to keep the body's own refusals first; `When = ByBody` when a domain method in `ApplyAsync` makes it). No `TransitionTo` call in the body (PRAG0466).
- **SG generates**: invoker that loads the entity by `Id` (LoadEntity), runs validation + permission, moves the state (409 on refusal, documented), applies the mutation, and saves.
- **Showcase**: `Showcase.Booking/Reservations/Mutations/ConfirmReservationMutation.cs`.

### P07 — Mutation Delete (soft-delete)

- **Modules**: Actions + Endpoints + Persistence
- **Attributes**: `[Mutation(Mode = Delete)]`, `[Endpoint(HttpVerb.Delete, "api/.../{id}")]`. The entity has `[SoftDelete]`.
- **SG generates**: invoker that sets `IsDeleted=true` (with error compensation) and applies global filters.
- **Showcase**: `Showcase.Catalog/Amenities/Mutations/DeleteAmenityMutation.cs`.
- **Variant**: `[Mutation(Mode = Update)]` `RestoreXMutation` — undoes soft-delete.

### P08 — DomainAction (orchestration)

- **Modules**: Actions + Endpoints + Validation + (opt.) FeatureFlags + Temporal + Authorization
- **Attributes**: `[DomainAction]`, `[Endpoint(...)]`, `[RequirePolicy<...>]`, `[Validate]`, opt. `[SinceVersion("2.0")]`.
- **Decorate**: `partial class XAction : DomainAction<TResult, TError>` with `Execute(ct)`. Inject services only through uninitialized private fields (`private IRepository<Order> _orders = null!;`) — not through a constructor. The current time is `[FromClock]`, not an injected clock.
- **SG generates**: `SetDependencies.g.cs` (DI dependency init), `Invoker.g.cs` (pipeline: validation → policy → permission → execute), `Versioning.g.cs` for `V1/V2` when present.
- **Showcase**: `Showcase.Booking/Reservations/Actions/CreateReservationAction.cs` — combines 6 modules (loyalty discount via FeatureFlag, IClock, async availability check).

---

## Operations: Queries

### P09 — Declarative Query<TEntity, TDto>

- **Modules**: Persistence + Query + Endpoints + (opt.) Authorization + Mapping
- **Attributes**: `[Query<Reservation, ReservationDto>]`, `[Endpoint(Get, "api/...")]`, opt. `[LoadWith<Reservation>("Guest", "Property")]`, `[RequirePermission("...")]`.
- **Decorate**: optional properties with `[Filter(...)]`, `[Sort(...)]`; `Paged = true` writes `Page`/`PageSize` — declare them by hand only to change the defaults (both is **PRAG0727**).
- **Shorter form first**: when the rule is already a `Specification<T>`, put `[Query]` on that static member instead and the query is derived from it — P09b below. The class is for a read that **composes**.
- **SG generates**: `Apply(IQueryable<T>)` with null-skip filters + sort + paging; `Projection: Expression<Func<T, TDto>>` (auto when names match, otherwise add `[MapFrom<T>]` on DTO); endpoint that runs the query and returns `PagedResult<TDto>`.
- **Showcase**: `Showcase.Booking/Reservations/Queries/SearchReservationsQuery.cs` — and `Showcase.Booking/Guests/Queries/SearchGuestsQuery.cs` for `Paged = true`.

### P09b — Specification promoted to a query

- **Modules**: Persistence + Query + Specification + (opt.) Endpoints + Authorization
- **Attributes**: `[Query<Order>]` or `[Query<Order, OrderDto>]` on a **static** member returning `Specification<Order>`; opt. `[Endpoint(Get, "api/...")]`, `[RequirePermission("...")]`, `Paged = true`.
- **SG generates**: `{Member}Query` in the container's namespace, holding the specification, with the member's parameters as its inputs — then the ordinary query templates render `Apply()` and the route.
- **The specification is not modified**: it stays a predicate and still composes with `&`.
- ⚠️ **PRAG0726** when two specifications in one namespace derive the same name — an error, because two files with one hint name cost the whole generator's output under a warning.
- **Use P09 instead** when the read composes several filters, a sort and a projection.

### P10 — GridFilter<T> with runtime operator

- **Modules**: Persistence + Query + Endpoints
- **Attribute**: `[GridFilter<Property>]`. Property pattern: value + companion `XOperator: StringOperator?` for runtime selection.
- **Attributes on properties**: `[Filterable(Operators = FilterOps.String|Range|All)]`, `[Sort(...)]`.
- **SG generates**: `Apply(IQueryable<T>)` with a switch on the operator; multi-sort via priority.
- **Showcase**: `Showcase.Catalog/Properties/Queries/PropertyGridFilter.cs`.
- **When to use**: data-table grids where the user selects the operator.

---

## Mapping & Patch

### P11 — GeneratePatch<T> tri-state

- **Modules**: Patch + Mapping + (opt.) Endpoints
- **Attribute**: `[GeneratePatch<Amenity>]` on `partial class XPatch`.
- **SG generates**: an `Optional<T>` for each entity property (distinguishes unset / set-null / set-value), `ApplyTo(entity)` that applies only modified fields, `ModifiedProperties` IReadOnlyCollection.
- **Consume**:
  ```csharp
  patch.ApplyTo(amenity);          // applies only optionals with HasValue
  ```
- **Excluding a property**: `[PatchIgnore(nameof(Entity.X))]` on the same partial. Infrastructure
  columns are already out — id, audit, soft-delete, tenant, owner, scope — so this is for a **domain**
  invariant: a value the domain forbids correcting. Without it the only place left to refuse is the
  endpoint, at runtime, while the published contract goes on offering the field. ⚠️ A name matching
  nothing is `PRAG2206`, not a silent no-op.
- **Showcase**: `Showcase.Catalog/Amenities/Mutations/PatchAmenityDto.cs`.
- **Companion endpoint**: `[Endpoint(HttpVerb.Patch, "...", BindBodyDirectly = true)]`. Without the
  flag a hand-written endpoint wraps its single `[FromBody]` property in a generated record, so the
  body is a patch document inside a field rather than the document itself.

### P21 — DTO with MapFrom<T> + Projection

- **Modules**: Mapping + (opt.) Persistence (for LINQ projection) + i18n (for StatusLabel)
- **Attributes**: `[MapFrom<Reservation>]` on DTO record/class, opt. `[GenerateProjection]`, `[GenerateBodyOnlyVariant]`, `[MapProperty("Property.Name")]` (the path is the constructor argument), `[MapIgnore]`.
- **SG generates**: `static FromEntity(entity)`; the SQL-translatable `static Expression<Func<TEntity, TDto>> Projection` and its compiled `Selector`; flat-map of nav properties; integration with `T.{key}` for localized labels.
- **Consume in query**:
  ```csharp
  query.Select(ReservationDto.Projection)   // SG-generated expression
  ```
- **Showcase**: `Showcase.Booking/Reservations/Dtos/ReservationSummaryDto.cs`.

---

## Authorization & Identity

### P12 — OwnedEntity (L1 Creator Ownership)

- **Modules**: Persistence + Authorization + Identity
- **Attribute**: `[HasOwner]` on entity `partial class`.
- **SG generates**: `OwnerId: string`, `IOwnedEntity` impl, `OwnershipFilter` (Priority 200). `OwnerId = ICurrentUser.Id` is written at `SaveChanges` by `OwnershipInterceptor` — insert only, when absent — so a row an action creates through a repository is attributed too.
- **Bypass**: permission `{boundary}.{entity-kebab}.view-all` (admin).
- **Combine with**: `[HasAccessScopes]` for L1+L2 (see P13).

### P13 — ScopedEntity (L2 Data Scope)

- **Modules**: Persistence + Authorization
- **Attribute**: `[HasAccessScopes]`.
- **SG generates**: `AccessScopes: List<string>` (JSON column), `IScopedEntity` impl, `ScopedDataFilter` (Priority 250) that uses `IUserScopeResolver`. Default resolver: `user:{id}`, `role:{role}`, `scope:{claim}`.
- **Materialization**: `AccessScopes` is populated at `SaveChanges` by `ScopeInterceptor` — the caller's own `user:{id}` on an insert with no scopes, then `IScopeMaterializer` over the registered `DataScopeRule<T>` (see P14), in that order. The order matters: the stamp only writes into an empty list.
- **Combine**: `[HasOwner] + [HasAccessScopes]` → `DataAccessFilter` with OR (see `Showcase.Booking/Guests/Guest.cs`).

### P14 — DataScopeRule<T> (L3 Specification scope)

- **Modules**: Persistence + Authorization
- **Decorate**: class inheriting `DataScopeRule<TEntity>` with `ScopeName`, `ToExpression()`, `Strategy` (`Materialized | Computed | Hybrid`).
- **Registration**: `services.AddDataScopeRule<TRule, TEntity>()` in `IStartupStep`.
- **Runtime**: `ComputedScopeFilter<T>` OR-composes the expressions of active rules for the current user.
- **Showcase**: `Showcase.Billing/Infrastructure/Scopes/EurInvoiceScopeRule.cs` (currency = EUR → scope `billing-eu`).
- **When to use**: dynamic scopes based on entity attributes (geographic zones, currency, monetary ranges).

### P15 — Role + Permission compile-time

- **Modules**: Authorization + Identity
- **Decorate**:
  - In each boundary library: `IRoleDefinition` (e.g. `BookingOperator`) with `Permissions = new[] { "booking.reservation.*", "booking.guest.*" }`.
  - In Host: `IRole` (e.g. `ShowcaseAdmin`, `BookingManagerRole`) with `Code`, `Name`, and `IncludeDefinition<>()`/`WithPermissions(...)` in `app.UseAuthorization(authz => authz.MapRole<TRole>(...))`.
- **SG generates**: `PermissionRegistry` per boundary (no reflection lookup); one typed `{Boundary}Permissions` class of `const`s — the entities' CRUD (`BookingPermissions.Reservation.Read`) and the custom ones declared with `[assembly: Permission("booking.reservation.approve", "…")]` (`BookingPermissions.Reservation.Approve`).
- **Showcase**: `Showcase.Host/Program.cs:113-170`.

### P16 — IResourceAuthorizer<T> (ABAC, instance-level)

- **Modules**: Authorization
- **Decorate**: class implementing `IResourceAuthorizer<TAction>` with `CanAccessAsync(action, ct)`.
- **Registration**: `authz.AddResourceAuthorizer<InvoiceAuthorizer>()`.
- **Runtime**: `ResourceAuthorizationFilter` Order 250 runs the check after the base permission.
- **Showcase**: `Showcase.Billing/Infrastructure/Authorization/InvoiceAuthorizer.cs` (admin bypass + dept check on refund).
- **When to use**: when the base permission is insufficient — the decision depends on the instance (e.g. "you can only refund invoices from your department").

---

## Async work: Messaging & Jobs

### P17 — MessageHandler with Retry

- **Modules**: Messaging + Resilience + (opt.) Persistence (for outbox-driven)
- **Attributes**: `[MessageHandler]`, `[Retry(MaxAttempts = 3, Strategy = ExponentialWithJitter)]`, opt. `[Timeout]`, `[CircuitBreaker]`.
- **Decorate**: class `IMessageHandler<TEvent>` with `HandleAsync(message, context, ct)`.
- **SG generates**: `{Handler}_Pipeline.g.cs` with an inline-wired middleware chain (validation → resilience → user code → audit); no reflection dispatch.
- **Showcase**: `Showcase.Billing/Events/Handlers/ReservationConfirmedHandler.cs` (cross-boundary: Booking → Billing creates draft invoice).

### P18 — Transactional Outbox (entity + event in the same transaction)

- **Modules**: Persistence + Messaging + Persistence.EFCore + Messaging.EFCore
- **Decorate**: the `[Boundary]` with `[EnableOutbox]` (there is no hand-written DbContext; without `Pragmatic.Messaging.EFCore` it is **PRAG0831**). Events published via `IMessageBus.PublishAsync(evt)` during a mutation. For in-process dispatch the boundary takes `[EnableEventOutbox]` instead — one outbox per boundary.
- **SG generates**: `OutboxMessage` entity config in DbContext; `{DbContext}OutboxSource.g.cs` impl of `IOutboxSource`; `OutboxInterceptor` registers events pre-commit.
- **Runtime**: `OutboxDeliveryService` (hosted) drains messages; `MessageTypeRegistry` (FQN→Type) deserializes safely.
- **When to use**: guarantee that entity-change and event are atomic (no lost events on crash).

### P19 — Saga<TState> with orchestrator

- **Modules**: Messaging + Actions + Persistence (saga state)
- **Attributes**:
  - on class: `[Saga<CheckInState>]` on `ISaga<CheckInState>`
  - on step methods: `[SagaStart]`, `[InState(CheckInState.X)]`, `[CompensateWith<CompensationAction>]`, `[SagaTimeout(Minutes = 10)]`
- **SG generates**: `{Saga}.Orchestrator.g.cs` derives the state graph from `[InState]`, validates transitions at compile-time, dispatches subsequent actions via `IMessageBus`.
- **Showcase**: `Showcase.Booking/Reservations/Sagas/CheckInSaga.cs` (4 steps + compensation + timeout).
- **Diagnostics**: PRAG0811 (state with no `[InState]` handler), PRAG0813 (`TState` is not an enum), PRAG0814 (no `[SagaStart]`), PRAG0820/0821 (event correlation).
- **Non-blessed variant**: `[StateMachine]` on a saga — that is for synchronous entities, not asynchronous workflows.

### P20 — RecurringJob

- **Modules**: Jobs + Resilience + (opt.) Notifications + Persistence
- **Attributes**: `[RecurringJob("0 * * * *")]`, `[Retry(MaxAttempts = 2)]`, `[Timeout(TimeoutSeconds = 120)]`. Standard cron.
- **Decorate**: class `IJob` (no params) or `IJob<TParams>` with `ExecuteAsync(ctx, ct)` or `ExecuteAsync(params, ctx, ct)`.
- **SG generates**: a nested `{Job}.Invoker` per job, listed in the assembly's job type registry; enabled via `app.UseJobs(jobs => jobs.WithWorkerCount(2))` in Host.
- **Distributed lock**: lease-based (atomic UPDATE). Works with EF Core store; in-memory store for dev.
- **Showcase**: `Showcase.Booking/Infrastructure/Jobs/NoShowDetectionJob.cs`, `SendCheckInReminderJob.cs`.
- **Variant**: `[Job]` (one-shot) for delayed/triggered jobs.

---

## Infrastructure

### P22 — Keyed Service + Decorator

- **Modules**: Composition
- **Attributes**:
  - `[Service<IPaymentProvider>(Key = "stripe")]`, `[Service<IPaymentProvider>(Key = "bank")]` for multiple implementations.
  - `[Decorator(Order = 1)]` on a class implementing the same interface and depending on the inner — automatic wrapping.
- **SG generates**: registrations `services.AddKeyedScoped<IPaymentProvider, StripeProvider>("stripe")` and decorator wrapping.
- **Showcase**: `Showcase.Billing/Infrastructure/Services/{Stripe,BankTransfer}PaymentProvider.cs` + `Showcase.Booking/Infrastructure/Services/LoggingReservationPricingService.cs`.

### P23 — Ordered IStartupStep

- **Modules**: Composition
- **Attribute**: `[StartupStep]` on class `IStartupStep`. Property `Order` (default 0; 0-99 infrastructure, 100-499 modules, 500+ the application). Optional `[RequiresConfig("Key")]`.
- **When to use**: business wiring (register custom query filters, warm lookup caches, customize OpenAPI). NOT for infrastructural choices (those go in `Program.cs` via `IPragmaticBuilder.Use*()`).
- **Showcase**: `Showcase.Host/ShowcaseStartupStep.cs:32` (Order=60, post-routing config).

### P24 — UsePackage<T> + ExposeEndpoint<T>

- **Modules**: Composition + Endpoints
- **Attributes on `[Module]`**: `[UsePackage<TIdentityLocal>(RoutePrefix = "auth")]`, `[ExposeEndpoint<TLoginAction>]`.
- **SG generates**: handler in the host that dispatches the package action; route prefix taken from `IPackageDefinition` (override possible via attribute).
- **When to use**: integrate a self-contained Pragmatic package (Identity.Local, future: Documents, Tags, etc.) as a full-stack module.

### P25 — IPragmaticBuilder.Use*() in Program.cs

- **Modules**: Composition + target module
- **Pattern**:
  ```csharp
  await PragmaticApp.RunAsync(args, app =>
  {
      app.UsePragmaticMigrations();         // schema diff
      app.UseMultiTenancy(mt => mt.UseHeader());
      app.UseJobs(jobs => jobs.WithWorkerCount(2));
      app.UseI18N(i18n => i18n.AddJsonTranslations(...));
      app.UseNotifications(n => n.AddSmtp(...));
      app.UseMessaging(msg => { msg.UseChannels(...); msg.EnableAuditing(...); });
      app.UseJwtAuthentication(jwt => { ... });
      app.UseAuthorization(authz => { ... });
      app.UseLogging(log => { ... });
      app.UseStorage(...);
  }).ConfigureAwait(false);
  ```
- **Use* catalog**: `UseAgent`, `UseMaintenanceMode`, `UsePragmaticMigrations`, `UseMultiTenancy`, `UseJobs`, `UseI18N`, `UseNotifications`, `UseMessaging` (+`UseChannels`/`UseRabbitMq`/`EnableAuditing`), `UseJwtAuthentication`/`UseAuthentication<>`, `UseAuthorization` (+`MapRole`/`MapGroup`/`AddResourceAuthorizer`/`UsePermissionCache`/`SeedFromJson`), `UseLogging`, `UseStorage`, `UseEmail`, `UseTemporal`, `UseDatabaseEnsureCreated`/`UseDatabaseMigrate`.
- **Doctrine**: infrastructural topology choices go here (provider, transport, auth scheme); business wiring goes in `IStartupStep` (P23).

---

## Cross-cutting

### P26 — TranslationKeys + LocalizedString

- **Modules**: Internationalization + (opt.) Persistence
- **Decorate**: `[assembly: TranslationKeys]` in `AssemblyAttributes.cs`; use `LocalizedString` as a property type on entities for multilingual text stored in DB.
- **SG generates**: static class `T` with a property per key (`T.Reservation.Status.Pending.Value`, or `["it"]` for a culture), and `TKeys` with the same hierarchy as `const string` for attribute arguments.
- **Consume**: in DTOs with `[MapFrom<E>]`, in error message keys, in `Pragmatic.Internationalization` seeding.
- **Showcase**: `Showcase.Catalog/AssemblyAttributes.cs`, `Showcase.Catalog/translations/`.

### P27 — Cacheable + InvalidatesCache

- **Modules**: Caching + Actions + Persistence
- **Attributes**: `[Cacheable(Duration = "10m", Tags = ["amenities"])]` on query, `[InvalidatesCache("amenities")]` on whatever writes those rows — a mutation, a `[DomainAction]`, an event.
- **SG generates**: cache key from properties (with `[CacheKey]` for override), invalidation hook in the mutation pipeline.
- **Showcase**: `Showcase.Catalog/Amenities/Queries/SearchAmenitiesQuery.cs`.

### P28 — Resource CRUD scaffolding + per-operation decoration

- **Modules**: Persistence + Actions + Endpoints + Mapping
- **Attributes**: `[Resource("guests", Capabilities = ResourceCapabilities.All)]` on the entity;
  `[ReturnsDto<TDto>]`, `[RequirePermission]`, `[AllowAnonymous]`, `[RequirePolicy<T>]` and `[Filter]`
  properties on the partial part of each generated operation.
- **SG generates**: one type per capability in the entity's namespace — `ResourceCreate{E}Mutation`,
  `ResourceRead{E}Query`, `ResourceUpdate{E}Mutation`, `ResourceDelete{E}Mutation`,
  `ResourceRestore{E}Mutation` (soft delete only), `ResourceList{E}Query`, `ResourceSearch{E}Query` —
  plus `{E}ReadDto` / `{E}ListItemDto` and their mappings, the endpoints, and the manifest entries.
- **The entity says only which operations exist and on what route.** Everything else is declared on
  the operation, because a partial part makes the scaffolded type and a hand-written one identical to
  decorate:
  ```csharp
  [ReturnsDto<GuestDto>]                              // the shape it answers with
  [RequirePermission(BookingPermissions.Guest.Read)]  // replaces the default, does not add to it
  public partial class ResourceSearchGuestQuery
  {
      [Filter(Operator = FilterOperator.Contains)] public string? LastName { get; init; }
  }
  ```
  Declaring filters replaces the convention (every text column as `Contains`); declaring a DTO
  replaces the scaffolded one, which then stops being generated if nothing else answers with it.
- **What a write answers with**: the read shape while the resource still exists — Create 201, Update /
  Restore / soft Delete 200 — and 204 for a hard delete. `[ReturnsDto<T>]` works on a hand-written
  mutation too, where the default is the new id on a create and 204 on anything else — never the
  entity, unless `ReturnType = MutationReturnType.Entity` is written.
  - It can name the `{E}ReadDto` that `[Resource]` scaffolds: `[ReturnsDto<WidgetReadDto>]` on a
    hand-written mutation of `Widget` answers with the resource's read shape. That type does not exist in
    the compilation the generator reads — its `[MapFrom]` arrives in generated source — so it is
    recognised by the rule `[Resource]` writes it with: a DTO named `{Entity}ReadDto`, for a mutation
    whose entity carries `[Resource]`. Any other DTO needs its own `[MapFrom<TEntity>]`, or **PRAG0531**.
  - ⚠️ A **create** cannot answer with a DTO that flattens a navigation at all — it has nothing loaded
    and `[EagerLoad]` is inert on it. See the eager-loading guide.
  - ⚠️ `[ReturnsDto<T>]` does not combine with `[Mutation(ReturnType = Id | LogicalKey)]`: the key
    answers, and the DTO applies only to a mutation that returns the entity. **PRAG0535** (Error).
- **Diagnostics**: PRAG2607 (decorates nothing — a typo), PRAG2608 (the DTO has no projection, so the
  query would return nothing), PRAG2609 (the DTO maps from another entity), PRAG0531
  (`[ReturnsDto<T>]` on a mutation whose entity the DTO does not map from), PRAG0535
  (`[ReturnsDto<T>]` beside a key `ReturnType`), **PRAG2611**
  (`[Resource]` on an entity that also declares `[PartOf<TParent>]` — see below).
- ⚠️ **Not on an aggregate part.** `[PartOf<TParent>]` says the entity is written *through* its parent,
  behind the parent's permissions, validation and events; `[Resource]` would give it create, update and
  delete endpoints of its own. The two cannot both be true, and **PRAG2611** is an error. Scaffold the
  parent and let the operation carry the children — see the `[PartOf]` section of the persistence guide.
- **Showcase**: `Showcase.Booking/Guests/Guest.cs`, `Showcase.Booking/Guests/ResourceSearchGuestQuery.cs`.

---

# SG Features cross-reference

One package, `Pragmatic.SourceGenerator`, holds the unified generator. Its features: Actions,
Caching, Composition, Configuration, Endpoints, FastEnum, FeatureFlags, Glossary, I18n, Identity, Jobs,
Lifecycle, Manifest, Mapping, Messaging, Migrations, Patch, Persistence, Privacy, Redaction,
Resilience, Resource, Result, Serialization, Specification, Temporal, Traits, Validation, ValueObject.
`FeatureDetector` records which packages are referenced (`DetectedFeatures`) and the features read it;
the host wires a capability only when some assembly **declares** it.

Generators that ship on their own, because their input is not the module's compilation or their
package is used without the rest: `Pragmatic.Result` (write extensions), the country / currency /
language code generators of `Pragmatic.Internationalization`, `Pragmatic.Documents.Csv.Generator`,
`Pragmatic.Client` (reads the API manifest), and the three of `Pragmatic.Testing` (contract tests,
mocks, comparers) that run in the **test** project.

---

# Antipatterns (DO NOT)

| ❌ | ✅ | Why |
|---|---|---|
| `throw new NotFoundException()` | `Result<T, NotFoundError>` | Doctrine: Result over Exceptions |
| Manual `services.AddScoped<X>()` for Pragmatic features | `[Service]` or `app.Use*()` | Composition by presence, avoids duplication |
| `Type.GetType(fqn)` to deserialize events | SG-generated `MessageTypeRegistry` | Assembly-version safe, no reflection |
| `[StateMachine]` on a saga | `[Saga<>] + [InState]` | StateMachine = sync entity; Saga = async workflow |
| `[BelongsTo<>]` everywhere | namespace = boundary, `[BelongsTo]` only for exceptions | Convention over config |
| An attribute taking a type as `[Attr(typeof(X))]` | `[Attr<X>]` | Attributes that name a type are generic |
| `_logger.LogInformation($"...")` | `[LoggerMessage]` | Zero-allocation, doctrine pattern |
| Custom `IQueryFilter` for ownership | `[HasOwner]` / `[HasAccessScopes]` | SG generates filter + bypass + audit |
| `repo.Add(x); db.SaveChanges()` in controller | Mutation with SG-invoker | Loses validation pipeline, permission, audit, outbox |
| `new Order()` directly | `Order.Create(...)` | Factory guarantees invariants — PRAG0680 |

---

Patterns scoped to one module, and one-off variations, stay in the module skills.
