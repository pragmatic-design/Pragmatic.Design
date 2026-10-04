# Mutations Guide

Mutations are structured entity operations that follow a strict pipeline: validate, load/create, apply changes, validate entity, check invariants, persist, dispatch events. The `MutationInvoker` handles all of this automatically.

## What you write, and what the generator writes

Everything below shows generated code, and it is worth saying once what that means: **you write the
class, and nothing else.** A mutation is a declaration (a name, a mode, some input properties), and the
seven files the generator answers with are the ones a hand-written version would have had to contain.

For `CreateAmenityMutation` in the reference application, a class of eight lines:

| Generated | What it is | Trigger |
|---|---|---|
| `.MutationInvoker.g.cs` | the `Invoker`: dependency injection, load-or-create, apply, validate, persist, events | `[Mutation]` |
| `.ApplyToEntity.g.cs` | `ApplyToEntity(entity)`: one `entity.SetX(this.X)` per matching property | a property whose name matches a settable member |
| `.Validator.g.cs` | `ISyncValidator` on the mutation itself, built from the annotations on its inputs | `[Required]`, `[MaxLength]`, … |
| `.RequestBody.g.cs` | `{Mutation}Body`, the record the endpoint binds: only what the caller may send | `[Endpoint]` |
| `{Mutation}Body.Validator.g.cs` | the same validation, on the body | `[Endpoint]` + annotations |
| `.Endpoint.g.cs` | the `RequestDelegate`, its binding, its status codes and its authorization | `[Endpoint]` |
| `.CacheInvalidator.g.cs` | `InvalidateAsync`, called after the commit | `[InvalidatesCache]` |

So the question the examples invite (*wouldn't the original code have been better?*) is the wrong
comparison. The original code is the eight lines. The generated files are what you do **not** write, and
the reason to read them here is to know what the declaration commits you to: which pipeline stages run,
in which order, and what the endpoint will accept and answer.

Two consequences worth carrying into the rest of the page:

- **Every artefact has a trigger.** No `[Endpoint]`, no body, no endpoint, no binding: the mutation is
  invoked in process and nothing about HTTP exists. Adding the attribute later adds four files and
  changes nothing you wrote.
- **The generator owns those members.** Writing `ApplyToEntity` or a setter by hand duplicates a member
  it emits, and the build fails with `CS0111` rather than silently preferring one. Override
  `ApplyAsync` instead: it is the extension point, and auto-mapping still runs first.

Inspect them whenever the behaviour surprises you: `EmitCompilerGeneratedFiles` puts every one of these
on disk under `obj/`, and they are ordinary C# with the trigger named in the header comment.

---

## When to Use Mutation vs DomainAction

| Use Case | Choose |
|----------|--------|
| Standard entity CRUD (create, update, delete) | `Mutation<TEntity>` |
| Query / read operation | `DomainAction<TReturn>` |
| Complex orchestration across multiple entities | `DomainAction<TReturn>` |
| Operation that doesn't modify a single entity | `DomainAction<TReturn>` or `VoidDomainAction` |
| Upsert with non-PK lookup | `DomainAction<TReturn>` (use `CreateOrUpdate` mode only for PK-based) |

---

## Mutation Modes

The `MutationMode` enum controls how the invoker handles the entity:

### Create

Creates a new entity instance and persists it.

```csharp
[Mutation(Mode = MutationMode.Create)]
public partial class CreateAmenityMutation : Mutation<Amenity>
{
    public required string Name { get; init; }
    public AmenityCategory Category { get; init; }
}
```

Pipeline: `new TEntity()` -> `ApplyToEntity()` -> `ApplyAsync()` -> validate entity -> `Add()` -> `SaveChanges()` -> dispatch events.

### Update

Loads an existing entity by `Id` and applies changes.

```csharp
[Mutation(Mode = MutationMode.Update)]
public partial class UpdateAmenityMutation : Mutation<Amenity>
{
    public required Guid Id { get; init; }
    public string? Name { get; init; }
    public AmenityCategory? Category { get; init; }
}
```

Pipeline: load entity by `Id` -> reset change tracking -> `ApplyToEntity()` -> `ApplyAsync()` -> validate entity (change-aware) -> `SaveChanges()` -> dispatch events.

If the entity is not found, the invoker returns `NotFoundError`.

### CreateOrUpdate

Attempts to load by `Id`; if not found, creates a new entity instead.

```csharp
[Mutation(Mode = MutationMode.CreateOrUpdate)]
public partial class UpsertProductMutation : Mutation<Product>
{
    public Guid Id { get; init; }  // non-default = update, default = create
    public required string Name { get; init; }
}
```

### Delete

Loads the entity, optionally runs `ApplyAsync` for pre-delete logic, then removes it.

```csharp
[Mutation(Mode = MutationMode.Delete)]
public partial class DeleteAmenityMutation : Mutation<Amenity>
{
    public required Guid Id { get; init; }
}
```

If the entity implements `ISoftDelete`, the invoker sets `IsDeleted = true`, `DeletedAt`, and `DeletedBy`. If `SaveChanges` fails, soft-delete fields are rolled back (compensation pattern).

### Restore

Restores a soft-deleted entity by resetting `ISoftDelete` fields. The invoker disables the soft-delete query filter to load the entity.

```csharp
[Mutation(Mode = MutationMode.Restore)]
public partial class RestorePropertyMutation : Mutation<Property>
{
    public required Guid Id { get; init; }
}
```

### Mode Inference

If `Mode` is not set explicitly, it is inferred from the class name prefix:
- `Create{Entity}...` -> `MutationMode.Create`
- `Update{Entity}...` -> `MutationMode.Update`
- `Delete{Entity}...` -> `MutationMode.Delete`

---

## Auto-Mapping (ApplyToEntity)

For mutations where you don't override `ApplyAsync`, the source generator creates an `ApplyToEntity()` override that maps matching properties from the mutation to the entity via `entity.SetX()` calls.

**Matching rules**:
- Mutation property `Name` maps to entity method `SetName()`
- Mutation property `Category` maps to entity method `SetCategory()`
- Only properties with matching `SetX()` methods on the entity are mapped
- Null values in update mutations skip the setter (only non-null values are applied)

**Example**: Given this mutation:

```csharp
[Mutation(Mode = MutationMode.Create)]
public partial class CreateAmenityMutation : Mutation<Amenity>
{
    public required string Name { get; init; }
    public AmenityCategory Category { get; init; }
    public string? IconName { get; init; }
}
```

The SG generates:

```csharp
public partial class CreateAmenityMutation
{
    public override void ApplyToEntity(Amenity entity)
    {
        entity.SetName(Name);
        entity.SetCategory(Category);
        entity.SetIconName(IconName);
    }
}
```

### Auto-Mapping Always Runs

`ApplyToEntity()` runs BEFORE `ApplyAsync()`. This means auto-mapped properties are always applied, even when you override `ApplyAsync` for custom logic. You don't need to manually call `entity.SetX()` for the mapped properties.

---

## ApplyAsync -- Custom Logic

Override `ApplyAsync` when you need logic beyond simple property mapping: state machine transitions, creating child entities, or complex business rules.

```csharp
[Mutation(Mode = MutationMode.Update)]
public partial class CheckInGuestMutation : Mutation<Reservation, ConflictError>
{
    private ICurrentUser _currentUser;
    private IRepository<RoomAssignment> _roomAssignments;

    public required Guid Id { get; init; }
    public string? RoomNumber { get; init; }

    public override async Task<Result<Reservation, IError>> ApplyAsync(
        Reservation entity, CancellationToken ct = default)
    {
        var checkedInBy = _currentUser.IsAuthenticated ? _currentUser.Id : "system";

        // State machine transition (entity domain method)
        var result = entity.CheckInGuest(checkedInBy);
        if (result.IsFailure)
            return Result<Reservation, IError>.Failure(result.Error!);

        // Create a related entity in the same UoW
        if (!string.IsNullOrWhiteSpace(RoomNumber))
        {
            var assignment = RoomAssignment.Create(entity.Id, entity.GuestId, RoomNumber);
            _roomAssignments.Add(assignment);
        }

        return Result<Reservation, IError>.Success(entity);
    }
}
```

### ApplyAsync with Auto-Mapping

When both auto-mapping and `ApplyAsync` are present, the execution order is:

1. `ApplyToEntity(entity)` -- auto-mapped setters run first
2. `ApplyAsync(entity, ct)` -- your custom logic runs second

This means `entity` already has the auto-mapped properties set when `ApplyAsync` receives it.

### Creating Child Entities in ApplyAsync

A common pattern is creating child entities alongside the parent:

```csharp
[Mutation(Mode = MutationMode.Create)]
public partial class CreateDraftInvoiceMutation : Mutation<Invoice>
{
    public required decimal SubTotal { get; init; }
    // ... other properties auto-mapped

    public override Task<Result<Invoice, IError>> ApplyAsync(
        Invoice entity, CancellationToken ct = default)
    {
        // Auto-mapped properties (ReservationId, GuestId, etc.) are already set
        // Add a child entity that needs the parent's Id
        var lineItem = LineItem.Create(entity.Id, SubTotal, SubTotal);
        lineItem.SetDescription("Room charge");
        entity.LineItems.Add(lineItem);

        return Task.FromResult(Result<Invoice, IError>.Success(entity));
    }
}
```

---

## Mutation Pipeline -- Full Sequence

Here is the complete pipeline executed by `MutationInvoker<TMutation, TEntity>`:

### 0. Inject Dependencies

The generated invoker calls `InjectDependencies(mutation)` which resolves all private fields from DI.

### 1. L1 Sync Validation

If the mutation implements `ISyncValidator` (generated from validation attributes), `Validate()` is called. Failure short-circuits with `ValidationError`.

### 2. L1 Async Validation

If an `IAsyncValidator<TMutation>` is registered in DI, `ValidateAsync()` is called. Failure short-circuits.

### 2b. Typed Action Filters

Any `IActionFilter<TMutation>` registered in DI runs (ordered by `Order`). These can short-circuit with business rule errors.

### 3. Load or Create Entity

Based on `MutationMode`:
- **Create**: calls `CreateEntity()` (typically `new TEntity()`), marks as new
- **Update**: calls `LoadEntityAsync()` from repository, resets change tracking
- **Delete**: loads entity from repository
- **Restore**: loads entity bypassing soft-delete filter
- **CreateOrUpdate**: tries load; if null, creates

If the entity is not found (Update/Delete/Restore), returns `NotFoundError`.

For Create mode, `ApplyComputedDefaultsAsync()` runs (if entity has `[ComputedDefault]` properties).

### 4a. Apply Auto-Mapping

`mutation.ApplyToEntity(entity)` runs -- the SG-generated property mapping via `entity.SetX()` calls.

### 4b. Apply Custom Logic

`mutation.ApplyAsync(entity, ct)` runs -- the developer's override (or no-op default).

### 5. L2 Entity Validation

After changes are applied, entity validation runs:
- First tries `IValidator<TEntity>` from DI (unified sync + async, change-aware)
- Falls back to `ISyncValidator` on the entity itself

Change-awareness: For updates, `IChangeTracking.ModifiedProperties` is passed to the validator, allowing it to validate only modified fields.

### 5b. Invariant Checks (`[Invariant]`)

After entity validation, any `[Invariant]` methods on the entity are evaluated. An invariant is a
parameterless `bool` method on the aggregate that must hold before persist:

```csharp
[Invariant("An amenity cannot have more than 20 keywords", MessageKey = "error.too_many_keywords")]
public bool KeywordsWithinLimit() => Keywords.Count <= 20;
```

If one returns `false`, the pipeline short-circuits **before persist** with an `InvariantViolationError`
(`Code = "INVARIANT_VIOLATION"`, HTTP 422): a typed `Result` failure, not a thrown exception. The
source generator discovers `[Invariant]` methods (public/internal, parameterless, inherited) and
generates the `CheckInvariants` override the pipeline calls.

`MessageKey` is the key the refusal reports, so it can be read in the caller's language: an error key's
base, with `{key}.title` and `{key}.detail` in the translation files, and `Message` as the answer where
the key has no translation. It is a string and not a `TKeys` constant, because a base is a nested type
in that class and those two suffixes are its members. A rule that names no key reports
`error.invariant.violation`, which every invariant in the application shares: one text that cannot say
which rule refused.

### 6. Persist

- **Create**: `PersistNew(entity)` adds to repository, then `ApplyPresetsAsync()` creates any preset child entities
- **Update/Delete/Restore**: entity is already tracked by EF Core

`SaveChangesAsync()` commits via the boundary's `IUnitOfWork`.

**Deferred**: a mutation nested inside an invoker that already owns its unit of work does not save. The entity is accumulated and its events deferred; the owner commits once and then flushes them. Ownership is by unit of work identity, so a step in another boundary is unaffected and commits itself.

Two things put a mutation in that position: an outer invoker of the same boundary (an action, a `[CompositeAction]`) or a `BatchContext` opened by hand over that unit of work. ⚠️ In the second case **you** save: `BatchContext` performs no commit of its own. See `[CommitStrategy]` to choose otherwise.

### 7. Dispatch Domain Events

If the entity implements `IHasDomainEvents` and has pending events, they are dispatched via `IDomainEventDispatcher` and then cleared. Events declared on the mutation with `[Raises<T>]` (see below) are dispatched here too, after the commit.

### 8. Cache Invalidation

If the mutation implements `ICacheInvalidator`, `InvalidateAsync()` is called with the resolved `ICacheStack`.

---

## Raising Domain Events (`[Raises<T>]`)

Declare the domain events an operation produces with `[Raises<TEvent>]`. The generator builds the event instance for you, matching the event constructor's parameters **by name** against the mutation's inputs and the entity's members, and the pipeline dispatches it **after a successful commit** (step 7). The entity needs no `RaiseEvent` call: the behavior lives on the operation.

```csharp
[Mutation(Mode = MutationMode.Update)]
[Raises<ReservationConfirmed>]
public partial class ConfirmReservationMutation : Mutation<Reservation>
{
    public required Guid ReservationId { get; init; }
}

// Event ctor params are filled by name: ReservationId (from the mutation),
// GuestId / ConfirmedAt (from the entity). Unmatched params get `default`.
public sealed record ReservationConfirmed(Guid ReservationId, Guid GuestId, DateTimeOffset ConfirmedAt) : IDomainEvent;
```

**What you get**: no hand-written event construction, no `entity.RaiseEvent(...)`, dispatch guaranteed to run only after the transaction commits. Multiple `[Raises<T>]` are allowed. Works identically on `DomainAction` and `VoidDomainAction`.

> Dispatch requires an `IDomainEventDispatcher` in DI (provided by `Pragmatic.Events`). Without one, raised events are silently dropped.

---

## Navigation Includes

When loading an entity for Update/Delete, specify navigation properties to eagerly load:

```csharp
[Mutation(Mode = MutationMode.Update)]
[Include("Lines")]
[Include("Lines.Product")]
public partial class UpdateOrder : Mutation<Order>
{
    public required Guid Id { get; init; }
    // ...
}
```

The generated invoker uses these to build the EF Core query with `.Include()` calls.

---

## Return Type Control

The `ReturnType` property on `[Mutation]` decides what the mutation returns: what its boundary member
returns in process, and what its endpoint answers with.

| `ReturnType` | Boundary member returns | Endpoint answers |
|---|---|---|
| unset (`Entity`) | the entity | the DTO declared with `[ReturnsDto<T>]`; without one, `{"id": "…"}` (201) on a create of an entity and **204** otherwise |
| `Entity`, written | the entity | the entity, or the DTO declared with `[ReturnsDto<T>]` |
| `Id` | `Guid`, the key | `{"id": "…"}` |
| `LogicalKey` | `{Mutation}.LogicalKey`, a generated record with one property per `[LogicKey]` part | the same record, under the parts' wire names |

⚠️ Left unset, the endpoint never answers with the entity. The entity is the persistence
shape (every column the server owns, loaded only as far as the mutation writes), so it goes on the
wire only when you ask for it by writing `ReturnType = MutationReturnType.Entity`.

```csharp
[Mutation(ReturnType = MutationReturnType.Id)]
[Endpoint(HttpVerb.Post, "api/shipments")]
public partial class CreateShipmentMutation : Mutation<Shipment> { /* … */ }

Guid id = (await sales.CreateShipment(code, carrier)).Value;   // the boundary returns the key
```

The invoker always returns the entity: persistence, events and the `Location` below need it. Only the
member's answer is projected.

`LogicalKey` needs a `[LogicKey]` the mutation can type. An entity without one, or a part that is a
foreign key a relation declared on the other entity puts there (its type is known only to the relation
graph), is **PRAG0403**.

### The 201's `Location`

A `Create` answering **201** carries a `Location` when a `Single` query on the same entity answers at
the create's own route plus `/{id}`: `POST api/shipments` points at `GET api/shipments/{id}`. The header
is built from the request path, so the configured route prefix and any group prefix are in it. Without
such a read the header is absent. The generator does not guess an address. `[CreatedAt("…")]` still
names one explicitly and wins.

---

## Typed Errors

Mutations support up to 6 typed error variants, just like DomainActions:

```csharp
// One error type
public partial class CheckInGuest : Mutation<Reservation, ConflictError> { }

// Two error types
public partial class TransferRoom : Mutation<Reservation, ConflictError, NotFoundError> { }
```

The error types are used by the SG to generate accurate OpenAPI error schemas for endpoints.

---

## Soft-Delete Compensation

When a Delete mutation targets an entity with `ISoftDelete`, the invoker:

1. Captures current soft-delete state (`IsDeleted`, `DeletedAt`, `DeletedBy`)
2. Sets `IsDeleted = true`, `DeletedAt`, `DeletedBy`
3. Calls `SaveChangesAsync()`
4. If save fails, restores the captured state (compensation)

This ensures the in-memory entity stays consistent even if the database write fails. The same compensation pattern applies to Restore mutations.

For entities with `[SoftDelete(Cascade = true)]`, the generated invoker overrides `CompensateSoftDeleteCascade()` to restore cascade targets as well.

Soft-delete is applied automatically when the target entity implements `ISoftDelete`. You can also force it explicitly on the mutation with `[Mutation(Mode = MutationMode.Delete, SoftDelete = true)]`: useful to make the intent unambiguous at the operation site.

---

## Composing Mutations (`[CompositeAction]`)

A mutation works on **one** entity. When an operation writes several, `[CompositeAction]` composes
mutations into one transaction: each step runs without saving, the composite commits once, and the
deferred domain events and cache invalidations are flushed after that single commit. If any step
fails, nothing is persisted.

### Declaring one

The steps are properties. You write no body.

```csharp
[DomainAction]
[CompositeAction]
[BelongsTo<CatalogBoundary>]
[Endpoint(HttpVerb.Post, "api/amenities/pairs")]
[RequirePermission(CatalogPermissions.Amenity.Create)]
public partial class CreateAmenityPairAction : VoidDomainAction
{
    public required CreateAmenityMutation First { get; init; }
    public required CreateAmenityMutation Second { get; init; }
}
```

The generator emits a nested `CompositeInvoker`, injects one invoker per step, and overrides the
action's `ExecuteActionAsync` to call it. `Execute` is generated too, and it throws
`NotSupportedException`: the steps *are* the body, and nothing ever calls it. Do not write one.

A step can be a mutation, a `DomainAction<T>` or a `VoidDomainAction`: anything with an invoker in
the same boundary.

### Reaching it from the frontend

A mutation-typed property is an ordinary body property, so the generated request body nests one JSON
object per step:

```csharp
// generated
public partial record CreateAmenityPairActionBody
{
    public required CreateAmenityMutation First { get; init; }
    public required CreateAmenityMutation Second { get; init; }
}
```

```http
POST /api/amenities/pairs
Content-Type: application/json

{
  "first":  { "name": "Sauna", "category": "Spa" },
  "second": { "name": "Lap pool", "category": "Pool" }
}

204 No Content
```

The nested step type is registered in the generated `JsonSerializerContext` alongside the body, so the
path is AOT-safe: no reflection fallback involved.

⚠️ The body nests the **mutation type**, not the `{Mutation}Body` its own endpoint uses. Anything the
mutation exposes as a public settable property is therefore on the wire here, including what
`{Mutation}Body` would have left out: the implicit `Id` a `MutationMode.Update` step carries, for one.
Prefer create-shaped steps, or a step whose writable surface is exactly what the caller may send.

### The permission is the composite's, and only the composite's

⚠️⚠️ **This is the one that bites.** The steps run as *internal calls*: the composite claims the
authorization boundary, and each step's own `[RequirePermission]` is deliberately **not** re-checked.
So a composite exposed with `[Endpoint]` and **no** `[RequirePermission]` runs its steps unchecked:
an authenticated caller holding no permissions at all gets `204` and the rows are created, even though
each step's own endpoint would have refused them `403`.

**`PRAG0440` refuses the build** when a composite carries `[Endpoint]`, declares no authorization, and
at least one step requires a permission. `[AllowAnonymous]` is how a composite says it is deliberately
public. Before it existed nothing caught this:
`PragmaticEndpointsOptions.RequireAuthorizationByDefault` demands only that the caller be
*authenticated*, and permission auto-derivation does not reinstate a step's permission.

### What it does not do

⚠️ **A collection of mutations is not a set of steps.** Steps are read one property at a time, and a
property whose type is not itself a mutation or an action (`List<CreateAmenityMutation>`, an array,
anything wrapping them) is skipped, with no diagnostic (`PRAG0427` fires only when there are *zero*
steps). The composite compiles, runs the properties it did recognise, and ignores the collection.

So `[CompositeAction]` composes a **fixed, named** set of writes, decided at compile time. For the
other shape (a nested DTO whose collections carry a variable number of children) the mechanism is
`[PartOf<TParent>]` with a `CollectionStrategy` on the parent's mutation, which writes the children
alongside the parent.

### Testing one

Two things are worth separate assertions, and the second is the one that is usually missing:

```csharp
// it commits both
var response = await PostAsync("/api/amenities/pairs", new
{
    first  = new { name = $"{prefix}-A", category = "Spa" },
    second = new { name = $"{prefix}-B", category = "Pool" }
});
response.StatusCode.Should().Be(HttpStatusCode.NoContent);

// and a failing step leaves nothing behind
var failed = await PostAsync("/api/amenities/pairs", new
{
    first  = new { name = $"{prefix}-A", category = "Spa" },
    second = new { name = "", category = "Pool" }          // fails L2 entity validation
});
failed.IsSuccessStatusCode.Should().BeFalse();
(await SearchAmenitiesByNameAsync(prefix)).GetArrayLength().Should().Be(0);
```

Invoking the composite **in process** (`IVoidDomainActionInvoker<T>` out of a bare DI scope) needs a
principal on `IHttpContextAccessor`, because that is where `ICurrentUser` reads from. Without one, a
permissioned composite fails for a reason that has nothing to do with what the test is about. The
worked version is `StepsCompositeActionTests` in the reference application.

---

## Mutation Dependencies

Like DomainActions, mutations resolve dependencies from private fields:

```csharp
[Mutation(Mode = MutationMode.Update)]
public partial class CheckInGuestMutation : Mutation<Reservation, ConflictError>
{
    private ICurrentUser _currentUser;                        // identity
    private IRepository<RoomAssignment> _roomAssignments; // sibling repo
    private IFeatureFlagStore _featureFlags;                  // feature flags

    public required Guid Id { get; init; }
    // ...
}
```

The SG generates `SetDependencies()` that resolves each field. All dependencies within the same boundary share the same `DbContext` and `IUnitOfWork`, so creating related entities in `ApplyAsync` is transactionally safe.

---

## Testing Mutations

### Unit Testing

Create the mutation, set its properties, and verify `ApplyAsync` behavior:

```csharp
[Fact]
public async Task CheckIn_ValidReservation_TransitionsToCheckedIn()
{
    var reservation = Reservation.Create(/* ... */);
    reservation.Confirm("agent"); // prerequisite state

    var mutation = new CheckInGuestMutation { Id = reservation.Id };
    // Inject test doubles for private fields via generated SetDependencies
    // or call ApplyAsync directly for pure logic testing

    var result = await mutation.ApplyAsync(reservation);

    result.IsSuccess.Should().BeTrue();
    reservation.Status.Should().Be(ReservationStatus.CheckedIn);
}
```

### Integration Testing

Use the invoker interface to test the full pipeline:

```csharp
[Fact]
public async Task CreateAmenity_WithValidInput_PersistsEntity()
{
    var invoker = _serviceProvider.GetRequiredService<
        IMutationInvoker<CreateAmenityMutation, Amenity>>();

    var mutation = new CreateAmenityMutation
    {
        Name = "Pool",
        Category = AmenityCategory.Recreation
    };

    var result = await invoker.InvokeAsync(mutation);

    result.IsSuccess.Should().BeTrue();
    result.Value.Name.Should().Be("Pool");
}
```
