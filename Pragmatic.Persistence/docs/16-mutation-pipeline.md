# The Mutation Pipeline

> From HTTP request to entity change and back: every step, every validation layer, every hook.

Mutations are how entities change state in Pragmatic. This document explains the complete pipeline: how a mutation DTO flows through validation, entity loading, application, persistence, event dispatch, and cache invalidation.

---

## Pipeline Overview

```
HTTP POST /api/v1/invoices
    │
    ▼
┌──────────────────────────────────────────────────┐
│  1. Endpoint Handler (source-generated)          │
│     • Read the body into {Mutation}Body           │
│       (a JsonException is a 400 naming "body")    │
│     • Claim binding from HttpContext.User         │
│     • Build the mutation from body + route + claims│
│     • Pre-processors, when declared               │
│     • No validation here: the invoker owns it     │
└───────────────────┬──────────────────────────────┘
                    │
                    ▼
┌──────────────────────────────────────────────────┐
│  2. MutationInvoker.InvokeAsync(mutation, ct)    │
│     ┌────────────────────────────────────────┐   │
│     │ 0.  Inject dependencies                │   │
│     │ 0b. Permission check (async)           │   │
│     │ 0c. Policy + resource authorization    │   │
│     │ 1.  L1 sync validation (the input)     │   │
│     │ 2.  L1 async validation (the input)    │   │
│     │ 2b. IActionFilter<TMutation>           │   │
│     │ 3.  Load the entity, or construct it:  │   │
│     │       new TEntity() → IsNew = true      │   │
│     │       → ApplyComputedDefaultsAsync      │   │
│     │       → IEntityLifecycle.OnCreating     │   │
│     │ 4a. Generated ApplyToEntity (always)   │   │
│     │     [TransitionsTo] BeforeBody move    │   │
│     │ 4b. Your ApplyAsync override, if any   │   │
│     │ 4c. [TransitionsTo] AfterBody move, or │   │
│     │     the ByBody check                   │   │
│     │ 5.  L2 entity validation, change-aware │   │
│     │ 5b. [Invariant] methods                │   │
│     │ 5c. Temporal constraints (on create)   │   │
│     │ 5d. IEntityLifecycle.OnSaving          │   │
│     │ 6.  Persist, and on create, presets     │   │
│     │ 7+8 Events + cache, after the commit    │   │
│     └────────────────────────────────────────┘   │
└───────────────────┬──────────────────────────────┘
                    │
                    ▼
┌──────────────────────────────────────────────────┐
│  3. Result<TEntity, IError>                      │
│     • Success → HTTP 201 Created / 200 OK        │
│     • ValidationError → HTTP 400 Bad Request     │
│     • NotFoundError → HTTP 404 Not Found         │
└──────────────────────────────────────────────────┘
```

---

## Mutations and Actions: The Relationship

In Pragmatic, a **mutation** is a specialized kind of **action**. Both share the same pipeline concepts (filters, validation, DI), but they solve different problems:

| Concept | Purpose | Generated Invoker |
|---------|---------|-------------------|
| **DomainAction** | Custom business logic with explicit `Execute()` | `DomainActionInvoker<T>` |
| **Mutation** | Entity CRUD via declarative DTO | `MutationInvoker<TMutation, TEntity>` |

A `DomainAction` is what you write when you need full control: you implement `Execute()` and decide what happens. A `Mutation` is what you write when the operation is structural: create/update/delete an entity from a DTO.

Both flow through the same endpoint pipeline:
- Both support `[Endpoint]` for HTTP handler generation
- Both support `[Validate]` for async validation
- Both support pre/post-processors
- Both produce `Result<T, IError>`

The key difference: mutations have a **generated invoker** that handles load-apply-save automatically, while actions have an `Execute()` method you write yourself.

---

## Step 1: Endpoint Handler

When a mutation class inherits from `Mutation<TEntity>` and has `[Mutation]` + `[Endpoint]`, the SG
generates two things: a **request-body DTO** carrying the properties the caller sends, and a
`RequestDelegate` that reads it, builds the mutation and calls the invoker.

```csharp
// ═══ What YOU write ═══
[Mutation(Mode = MutationMode.Create)]
[Endpoint(HttpVerb.Post, "api/v1/invoices")]
[Validate]
[BelongsTo<BillingBoundary>]
public partial class CreateInvoiceMutation
{
    public required Guid ReservationId { get; init; }
    public required Guid GuestId { get; init; }
    public List<CreateLineItemDto>? Lines { get; init; }
}
```

```csharp
// ═══ Generated: {Namespace}.CreateInvoiceMutation.RequestBody.g.cs ═══
// The wire shape. It is not the mutation: the mutation carries the id, the permissions and the
// dependencies, the body carries only what the caller is allowed to send.
public sealed class CreateInvoiceMutationBody
{
    public Guid ReservationId { get; init; }
    public Guid GuestId { get; init; }
    public List<CreateLineItemDto>? Lines { get; init; }
}

// ═══ Generated: {Namespace}.CreateInvoiceMutation.Endpoint.g.cs (shape, not verbatim) ═══
public static IEndpointConventionBuilder MapEndpoint(IEndpointRouteBuilder endpoints)
{
    var builder = endpoints.MapPost("/api/v1/invoices", (RequestDelegate)(async httpContext =>
    {
        // 1. read the body: a JsonException is a 400 that names "body"
        CreateInvoiceMutationBody? read;
        try
        {
            read = await httpContext.Request.ReadFromJsonAsync(
                RequestJson.TypeInfo<CreateInvoiceMutationBody>(httpContext), httpContext.RequestAborted);
        }
        catch (JsonException ex)
        {
            await BindingFailure.WriteAsync(httpContext, "body", ex.Message);
            return;
        }
        if (read is null)
        {
            await BindingFailure.WriteAsync(httpContext, "body", "the request body is required");
            return;
        }

        // 2. resolve the invoker and build the mutation from the body
        var invoker = httpContext.RequestServices
            .GetRequiredService<IMutationInvoker<CreateInvoiceMutation, Invoice>>();
        var mutation = new CreateInvoiceMutation
        {
            ReservationId = read.ReservationId, GuestId = read.GuestId, Lines = read.Lines
        };

        // 3. one call: validation, load, apply, persist and events all live in the invoker
        var result = await invoker.InvokeAsync(mutation, httpContext.RequestAborted);

        var response = result.Match(
            success => Results.Created($"/api/v1/invoices/{success.Id}", InvoiceDto.FromEntity(success)),
            (IError error) => ErrorExtensions.ToResult(error, httpContext));
        await response.ExecuteAsync(httpContext);
    }));

    builder.WithTags("Invoices");
    builder.RequireAuthorization(policy => policy.AddRequirements(new PragmaticPermissionRequirement(/* … */)));
    return builder;
}
```

Three things worth reading off that shape:

- **The endpoint validates nothing.** There is no `ISyncValidator` call here, deliberately: the invoker
  runs L1 validation in its own pipeline, and validating inline as well would run every sync validator
  twice per request. A query endpoint does the same: the query has its own invoker, and the endpoint
  defers validation to it.
  `[PreProcessor<T>]` and `[PostProcessor<T>]`, when declared, *are* rendered here, around the
  `InvokeAsync` call.
- **What `Created` carries depends on `[ReturnsDto<T>]`.** Declared, the response body is that DTO,
  projected from the saved entity; omitted, a create answers `{"id": …}` and any other mutation answers
  204 with no body. The tracked entity is the persistence shape, loaded only as far as the mutation
  writes: it reaches the wire only when `ReturnType = MutationReturnType.Entity` is written explicitly.
  The boundary member still returns the entity in process. The location header is built from the new
  id either way.
- **Serialization goes through `RequestJson.TypeInfo<T>`**, the generated `JsonTypeInfo`, so the path is
  AOT-safe. A body that does not parse is a 400 naming `body`, never a 500.

### Body Binding

The body DTO carries the properties the caller may send. What is bound from elsewhere is not in it:

| Scenario | Where it comes from |
|----------|---------------------|
| A property of the mutation, publicly writable | the generated body DTO |
| The entity's id on an Update or Delete | the route, through `{Mutation}.Id.g.cs` |
| `[FromClaim]` properties | `HttpContext.User`, in the generated delegate |
| Injected services | DI, in the invoker's `InjectDependencies` |

---

## Step 2: MutationInvoker

The heart of the pipeline. `MutationInvoker<TMutation, TEntity>` is an abstract base class with generated concrete implementations per entity.

### 2a. Inject Dependencies

```csharp
// Generated: override InjectDependencies
protected override void InjectDependencies(CreateInvoiceMutation mutation)
{
    // If the mutation declares service dependencies, inject them
    mutation.SomeService = _someService;
}
```

This is for mutations that need DI services (e.g., to call an external API during `ApplyAsync`). Most mutations don't need this.

### 2b. Level 1: Sync Validation (Input)

```csharp
if (mutation is ISyncValidator syncValidator)
{
    var syncResult = syncValidator.Validate();
    if (syncResult.IsFailure)
        return Failure(syncResult);   // → ValidationError → HTTP 400
}
```

Sync validation runs attribute-based validation (`[Required]`, `[Email]`, `[Range]`, etc.) on the mutation's properties. The `Validate()` method is source-generated from your validation attributes.

This is **fast, synchronous, and does not hit the database**. It catches obvious input errors early.

### 2c. Level 1: Async Validation (Input)

```csharp
var asyncValidator = _serviceProvider.GetService<IAsyncValidator<TMutation>>();
if (asyncValidator is not null)
{
    var asyncResult = await asyncValidator.ValidateAsync(mutation, ct);
    if (asyncResult.IsFailure)
        return Failure(asyncResult);   // → ValidationError → HTTP 400
}
```

Async validation runs **database-aware** checks: uniqueness, existence, business rule validation that requires querying the database. You write the validator class:

```csharp
public class CreateInvoiceValidator : IAsyncValidator<CreateInvoiceMutation>
{
    private readonly IReadRepository<Reservation> _reservations;

    public async Task<ValidationError> ValidateAsync(
        CreateInvoiceMutation mutation, CancellationToken ct)
    {
        var exists = await _reservations.ExistsAsync(
            Spec<Reservation>.Where(r => r.PersistenceId == mutation.ReservationId), ct);

        if (!exists)
            return ValidationError.Create("ReservationId", "Reservation not found");

        return ValidationError.None;
    }
}
```

This requires `[Validate]` on the mutation class. Without it, async validators are not resolved.

### 2d. Load or Create Entity

Based on `MutationMode`:

| Mode | Behavior |
|------|----------|
| **Create** | Skip loading. `new TEntity()`, **not** the generated `Create()` factory, and no factory is looked for. The entity is marked `IsNew`, then computed defaults and `OnCreating` run on it. |
| **Update** | Load by ID from repository. Return `NotFoundError` if missing. |
| **CreateOrUpdate** | Try to load. If not found, create new. |
| **Delete** | Load by ID. Perform soft-delete or hard-delete. |
| **Restore** | Load by ID **bypassing all filters** (including soft-delete). Reset soft-delete fields. |

For **Update**, the generated invoker uses the mutation's ID property:

```csharp
// Generated:
protected override async Task<Invoice?> LoadEntityAsync(
    CreateInvoiceMutation mutation, CancellationToken ct)
{
    return await _repository.GetByIdAsync(mutation.Id, ct);
}
```

For **Restore**, the invoker disables all filters to find the soft-deleted entity:

```csharp
protected override async Task<Invoice?> LoadEntityAsync(
    RestoreInvoiceMutation mutation, CancellationToken ct)
{
    using var _ = _filterToggle?.DisableAll();
    return await _repository.Query()
        .IgnoreQueryFilters()   // Bypass EF Core global filter too
        .FirstOrDefaultAsync(e => e.PersistenceId == mutation.Id, ct);
}
```

### 2e. Apply Computed Defaults

For `MutationMode.Create`, the invoker applies computed defaults before the mutation:

```csharp
// If the entity has [ComputedDefault<Invoice, string, InvoiceNumberGenerator>]
var generator = _serviceProvider.GetRequiredService<IDefaultValueGenerator<Invoice, string>>();
var invoiceNumber = await generator.GenerateAsync(entity, lifecycleContext, ct);
entity.SetInvoiceNumber(invoiceNumber);   // or a direct assignment, when the setter is public
```

The `LifecycleContext` in the middle is what carries `Now`, `UserId` and `TenantId`; a generator that
needs the clock takes it from there rather than from `DateTime.UtcNow`.

This runs **after construction and before `ApplyToEntity`**, so a value the caller sent overrides the
computed default rather than the other way round. `IEntityLifecycle<T>.OnCreating` runs immediately
after it, on the same context.

⚠️ Presets are the exception to that placement: `ApplyPresetsAsync` runs **after** the entity is
persisted (step 6), because the children it creates need the parent to exist.

### 2f. mutation.ApplyAsync(entity)

The generated `ApplyAsync` method applies the mutation's properties to the entity:

```csharp
// ═══ Generated for CreateInvoiceMutation ═══
public async Task<Result<Invoice, IError>> ApplyAsync(Invoice entity, CancellationToken ct)
{
    // Required properties: always applied
    entity.SetReservationId(ReservationId);
    entity.SetGuestId(GuestId);

    // Optional properties: applied when non-null
    // (decimal? Total → only set if provided)
    if (Total.HasValue)
        entity.SetTotal(Total.Value);

    // Children of the aggregate, merged against what the load brought with it.
    // Sync here: what was sent stays, what was not is gone. [Patch] would derive AddOnly.
    MutationHelpers.MapOneToMany(
        this.Lines, entity.Lines,
        d => d.Id, e => e.Id,
        d => d.ToEntity(), (d, e) => d.ApplyTo(e),
        CollectionStrategy.Sync);

    return entity;  // Success
}
```

The entity's **setter methods** (`SetReservationId`, `SetTotal`) are also source-generated. They are the only way to modify entity properties: direct property assignment is not possible because setters are `private set`.

A state-machine move is not written here: `[TransitionsTo<TState>(target)]` on the mutation and the invoker performs it, before `ApplyAsync` by default, after it with `When = AfterBody`, or checks that `ApplyAsync` made it with `When = ByBody`. A refused move answers 409 and nothing is saved. See [State Machine](19-state-machine.md#usage-in-an-operation-transitionsto).

### 2g. Level 2: Entity Validation (Change-Aware)

After applying the mutation, the invoker validates the **entity itself**, not the input DTO:

```csharp
// Detect which properties changed (for update mode)
IReadOnlySet<string>? modifiedProperties = null;
if (!isCreate && entity is IChangeTracking tracking)
    modifiedProperties = tracking.ModifiedProperties;

// Full entity validation
var entityValidator = _serviceProvider.GetService<IValidator<TEntity>>();
if (entityValidator is not null)
{
    var result = await entityValidator.ValidateAsync(entity, modifiedProperties, ct);
    if (result.IsFailure) return Failure(result);
}
// Fallback: sync validation on entity
else if (entity is ISyncValidator syncEntityValidator)
{
    var result = syncEntityValidator.Validate(modifiedProperties);
    if (result.IsFailure) return Failure(result);
}
```

**Why two validation levels?**

| Level | Target | When | What It Catches |
|-------|--------|------|-----------------|
| L1 (step 2b-2c) | Mutation DTO | Before entity load | Invalid input (missing fields, bad format, non-existent references) |
| L2 (step 2g) | Entity | After apply | Business invariants (balance < 0, invalid state transition, cross-field rules) |

L1 is fast and prevents unnecessary database work. L2 catches violations that only become apparent after the mutation is applied to the entity.

**Change-aware validation**: for updates, the validator receives the set of modified properties. This allows rules like "email must be unique" to run **only when email actually changed**, avoiding unnecessary uniqueness checks on unmodified fields.

### 2h. Persist

```csharp
// For new entities:
_repository.Add(entity);

// Apply presets (if [HasPresets] + [PresetProvider<T>])
foreach (var provider in _presetProviders)
{
    var presets = await provider.CreatePresetsAsync(entity, lifecycleContext, ct);
    foreach (var preset in presets)
        _unitOfWork.Add(preset);
}

// Check batch mode
if (BatchContext.Current is { } batch)
{
    batch.AccumulateEntity(entity);
    // Skip SaveChanges: batch will save later
}
else
{
    await _unitOfWork.SaveChangesAsync(ct);
}
```

**Batch mode**: when a `BatchContext` is active (e.g., during bulk imports), the invoker accumulates entities instead of saving each one individually. The batch controls chunking and transaction boundaries.

**UnitOfWork is keyed by boundary**: `[FromKeyedServices(typeof(BillingBoundary))] IUnitOfWork` ensures the mutation saves to the correct DbContext. If your entity has `[BelongsTo<BillingBoundary>]`, the generated invoker uses keyed DI to resolve the right unit of work.

### 2i. Dispatch Domain Events

```csharp
if (entity is IHasDomainEvents eventSource && eventSource.DomainEvents.Count > 0)
{
    var dispatcher = _serviceProvider.GetService<IDomainEventDispatcher>();
    if (dispatcher is not null)
        await dispatcher.DispatchAsync(eventSource.DomainEvents, ct);

    eventSource.ClearDomainEvents();
}
```

Domain events are raised by entity methods (e.g., `TransitionTo()` on a state machine, or `[CascadeSource]` setters). They are dispatched **after** `SaveChanges`: the entity is already persisted when handlers run.

In batch mode, events are deferred to `BatchContext` and dispatched when the batch completes.

### 2j. Invalidate Cache

```csharp
if (mutation is ICacheInvalidator cacheInvalidator)
{
    var cacheStack = _serviceProvider.GetService<ICacheStack>();
    if (cacheStack is not null)
        await cacheInvalidator.InvalidateAsync(cacheStack, ct);
}
```

If the mutation implements `ICacheInvalidator`, it can evict specific cache entries after the entity is saved.

---

## Soft Delete and Restore

These deserve special attention because they have unique behaviors.

### Soft Delete

When `MutationMode.Delete` targets a `[SoftDelete]` entity:

```csharp
entity.IsDeleted = true;
entity.DeletedAt = timeProvider.GetUtcNow();
entity.DeletedBy = currentUser?.Id;
```

With `[SoftDelete(Cascade = true)]`, all child entities (from `[Relation.OneToMany]`) are also soft-deleted. The invoker uses a **snapshot + compensation** pattern:

1. **Snapshot**: Record the current `IsDeleted` state of all children
2. **Mark deleted**: Set `IsDeleted = true` on parent and all children
3. **SaveChanges**: Persist everything
4. **On failure**: Revert to snapshot (compensation)

This ensures atomicity: either everything is soft-deleted or nothing is.

### Restore

Restore **bypasses all filters** to find the soft-deleted entity:

```csharp
using var _ = _filterToggle?.DisableAll();    // Pragmatic filters
var entity = await _repository.Query()
    .IgnoreQueryFilters()                      // EF Core global filter
    .FirstOrDefaultAsync(e => e.PersistenceId == mutation.Id, ct);

entity.IsDeleted = false;
entity.DeletedAt = null;
entity.DeletedBy = null;
```

Both Pragmatic's filter pipeline and EF Core's global query filters are disabled; otherwise the soft-deleted entity would be invisible to the query.

---

## The Generated Invoker

For each mutation, the SG generates a concrete invoker class:

```csharp
// ═══ Generated: {Namespace}.CreateInvoiceMutation.MutationInvoker.g.cs ═══
// It is nested inside the mutation, not a type of its own: CreateInvoiceMutation.Invoker
public partial class CreateInvoiceMutation
{
    public sealed class Invoker : MutationInvoker<CreateInvoiceMutation, Invoice>
    {
        public Invoker(
            IRepository<Invoice> repository,
            [FromKeyedServices(typeof(BillingBoundary))] IUnitOfWork unitOfWork,
            IServiceProvider serviceProvider,
            ICurrentUser? currentUser = null)
            : base(serviceProvider) { /* … */ }

        protected override MutationMode GetMode() => MutationMode.Create;
        protected override Task<Invoice?> LoadEntityAsync(CreateInvoiceMutation m, CancellationToken ct);
        protected override Invoice CreateEntity() => new Invoice();
        protected override string? GetEntityIdString(CreateInvoiceMutation m);
        protected override void PersistNew(Invoice entity);
        protected override void DeleteEntity(Invoice entity);
        protected override Task SaveChangesAsync(CancellationToken ct);
        protected override IUnitOfWork? UnitOfWork { get; }
        protected override void InjectDependencies(CreateInvoiceMutation m);
        // and, when the entity declares them: ApplyComputedDefaultsAsync, ApplyPresetsAsync,
        // RestoreEntity, CompensateSoftDeleteCascade
    }
}
```

The `[FromKeyedServices(typeof(BillingBoundary))]` on the unit of work is what ties the mutation to its
boundary's transaction: the repository is unkeyed, the unit of work is not.

The DI registration is generated too, one file per assembly:

```csharp
// _Infra.Mutations.Registration.g.cs
public static partial class BillingMutationsRegistrationExtensions
{
    public static IServiceCollection AddBillingMutations(this IServiceCollection services)
    {
        services.AddScoped<
            IMutationInvoker<CreateInvoiceMutation, Invoice>,
            CreateInvoiceMutation.Invoker>();
        // … one per mutation in the assembly
        return services;
    }
}
```

---

## Boundary and Transaction Scope

A **boundary** maps to a DbContext partition. Each boundary has its own:
- `DbContext` (with only the entities in that boundary)
- `IUnitOfWork` (keyed by boundary type)
- Repository instances

```csharp
// Define boundary
public class BillingBoundary;

// Assign entities
[Entity]
[BelongsTo<BillingBoundary>]
public partial class Invoice { /* ... */ }

[Entity]
[BelongsTo<BillingBoundary>]
public partial class LineItem { /* ... */ }
```

When a mutation saves, it calls `_unitOfWork.SaveChangesAsync()`, which saves **all tracked changes within that boundary's DbContext**. This means:

- Creating an `Invoice` with `LineItems` saves both in one transaction
- Changes to entities in **different boundaries** require separate `SaveChanges` calls
- Cross-boundary operations are eventually consistent (not transactional)

### Why Boundaries Matter

| Without Boundaries | With Boundaries |
|-------------------|-----------------|
| One giant DbContext with all entities | Focused DbContexts per domain |
| Slow model building (hundreds of entities) | Fast model building (10-30 entities each) |
| No isolation between domains | Clear ownership and access control |
| All entities share one connection string | Different boundaries can use different databases |

---

## DomainAction vs Mutation: When to Use What

| Scenario | Use | Why |
|----------|-----|-----|
| Create/update/delete an entity from a DTO | **Mutation** | Generated load-apply-save pipeline |
| Custom business logic (calculate pricing, orchestrate services) | **DomainAction** | You need `Execute()` control |
| CRUD with extra side effects | **Mutation** + `IEntityLifecycle<T>` | Lifecycle hooks run in the pipeline |
| Complex multi-step operation | **DomainAction** | Full control over order of operations |
| Bulk import / migration | **Mutation** + `BatchContext` | Batch mode with chunking |

### DomainAction Pipeline (for comparison)

```
DomainActionInvoker.InvokeAsync(action, ct):
  1. InjectDependencies(action)
  2. PrepareActionAsync(action, ct)     ← Load entities, setup
  3. Filters: BeforeExecuteAsync()      ← ValidationFilter, custom filters
  4. action.Execute(ct)                 ← YOUR code
  5. Filters: AfterExecuteAsync()       ← Post-processing
  6. SaveChangesAsync(ct)
```

The mutation pipeline (section 2) replaces steps 3-4 with a structured load-validate-apply-validate-persist flow.

---

## Validation Architecture Summary

```
┌──────────────────────────────────────────────────────┐
│  Endpoint Handler                                    │
│  └─ nothing: `required` on the body DTO rejects a    │  ← a missing field is a 400 on the
│     missing field before the mutation exists          │     body, from deserialization
└───────────────────┬──────────────────────────────────┘
                    │
┌───────────────────▼──────────────────────────────────┐
│  MutationInvoker: Level 1                            │
│  ├─ ISyncValidator.Validate() (mutation)             │  ← Attribute-based, no DB
│  └─ IAsyncValidator<TMutation>.ValidateAsync()       │  ← DB-aware (existence, uniqueness)
└───────────────────┬──────────────────────────────────┘
                    │  (entity loaded and mutation applied)
┌───────────────────▼──────────────────────────────────┐
│  MutationInvoker: Level 2                            │
│  ├─ IValidator<TEntity>.ValidateAsync(entity, mods)  │  ← Entity invariants
│  └─ ISyncValidator.Validate(mods) on entity          │  ← Attribute-based on entity
└──────────────────────────────────────────────────────┘
```

The sync validation appears **once**, in the invoker. A mutation endpoint deliberately does not repeat
it; a query endpoint does run `ISyncValidator` inline, because there is no invoker to defer to.

This two-level design means:
- Bad input is rejected **before** loading the entity (saves a DB round-trip)
- Business invariants are checked **after** the mutation is applied (catches domain rule violations)
- Change-aware validation avoids unnecessary checks on unmodified fields

---

## Related Guides

- [Mutations](06-mutations.md): Declaring mutations, collection strategies, nested mutations
- [Entity Attributes](03-attributes.md): `[SoftDelete]`, `[Auditable]`, `[ConcurrencyAware]`, `[StateMachine]`
- [Advanced Features](08-advanced.md): Temporal, hierarchy, polymorphic, lifecycle, presets
- [Query Pipeline](15-query-pipeline.md): The read-side counterpart
- [Repository](05-repository.md): Repository interface and unit of work
