# Architecture and Core Concepts

This guide explains **why** Pragmatic.Actions exists, how its pieces fit together, and how to choose the right abstraction for each situation. Read this before diving into the individual feature guides.

---

## The Problem

In a typical .NET application, business operations end up scattered across service classes. Each service grows organically: constructor parameters multiply, validation logic gets duplicated, error handling is inconsistent, and cross-cutting concerns (authorization, logging, telemetry) are wired by hand.

### The service class approach: scattered and brittle

```csharp
public class ReservationService
{
    private readonly IRepository<Reservation> _reservations;
    private readonly IReadRepository<Property> _properties;
    private readonly IReadRepository<RoomType> _roomTypes;
    private readonly IValidator<CreateReservationRequest> _validator;
    private readonly IAuthorizationService _authService;
    private readonly ILogger<ReservationService> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ReservationService(
        IRepository<Reservation> reservations,
        IReadRepository<Property> properties,
        IReadRepository<RoomType> roomTypes,
        IValidator<CreateReservationRequest> validator,
        IAuthorizationService authService,
        ILogger<ReservationService> logger,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _reservations = reservations;
        _properties = properties;
        _roomTypes = roomTypes;
        _validator = validator;
        _authService = authService;
        _logger = logger;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<Guid>> CreateReservationAsync(
        CreateReservationRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        // 1. Authorization: manual, easy to forget
        var authResult = await _authService.AuthorizeAsync(user, "booking.reservation.create");
        if (!authResult.Succeeded)
            return Result<Guid>.Failure(ForbiddenError.MissingPermission("booking.reservation.create"));

        // 2. Validation: manual, duplicated across methods
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return Result<Guid>.Failure(BadRequestError.Create(validation.ToString()));

        // 3. Business logic, finally
        _logger.LogInformation("Creating reservation for guest {GuestId}", request.GuestId);

        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
            return Result<Guid>.Failure(NotFoundError.For("Property", request.PropertyId));

        var roomType = await _roomTypes.GetByIdAsync(request.RoomTypeId, ct);
        if (roomType is null)
            return Result<Guid>.Failure(NotFoundError.For("RoomType", request.RoomTypeId));

        var nights = (int)(request.CheckOut - request.CheckIn).TotalDays;
        var totalAmount = nights * roomType.BaseRate;

        var reservation = Reservation.Create(
            request.GuestId, request.PropertyId, request.RoomTypeId,
            request.CheckIn, request.CheckOut, request.NumberOfGuests,
            totalAmount, roomType.Currency, request.SpecialRequests);

        _reservations.Add(reservation);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("Reservation {Id} created in {Duration}ms", reservation.Id, /* ... */);

        return reservation.Id;
    }

    // 10 more methods, each repeating the auth + validate + log + try/catch pattern...
}
```

A single operation: 60+ lines, and most of it is plumbing. The constructor has 8 parameters and will grow with every new method. Authorization and validation are duplicated in every method body. Logging follows no consistent pattern. Telemetry is absent.

Now multiply this by 50 operations across 10 services. The plumbing-to-logic ratio makes the codebase difficult to navigate and easy to break.

### Entity CRUD: even more repetition

```csharp
public async Task<Result<Amenity>> CreateAmenityAsync(
    string name, AmenityCategory category, string? iconName, CancellationToken ct)
{
    // Validate inputs (manual or via validator)
    if (string.IsNullOrWhiteSpace(name))
        return Result<Amenity>.Failure(BadRequestError.InvalidParameter("name", "is required"));

    // Create entity
    var amenity = new Amenity();
    amenity.SetName(name);
    amenity.SetCategory(category);
    if (iconName is not null)
        amenity.SetIconName(iconName);

    // Persist
    _amenities.Add(amenity);
    await _unitOfWork.SaveChangesAsync(ct);

    return amenity;
}
```

The entire method is ceremony: parameter-by-parameter mapping to entity setters, validation, persistence. The business intent -- "create an amenity with these properties" -- is buried in boilerplate.

**The fundamental issues:**
1. **No structure** -- operations live in service methods with no enforced shape
2. **No pipeline** -- cross-cutting concerns are manually wired in every method
3. **No composition** -- calling one operation from another requires passing dependencies through
4. **No observability** -- logging and telemetry are inconsistent or absent

---

## The Solution

Pragmatic.Actions inverts the model. Each business operation is a self-contained class with declared inputs, typed outputs, and explicit dependencies. The source generator produces the invoker pipeline, dependency injection wiring, and DI registration at compile time.

The same reservation creation:

```csharp
[DomainAction]
[RequirePolicy<ReservationManagementPolicy>]
[Endpoint(HttpVerb.Post, "api/v1/reservations")]
[Validate]
public partial class CreateReservationAction : DomainAction<Guid, RoomUnavailableError>
{
    private IRepository<Reservation> _reservations = null!;
    private IReadRepository<Property> _properties = null!;
    private IReadRepository<RoomType> _roomTypes = null!;
    private IClock _clock = null!;

    public required CreateReservationRequest Request { get; init; }

    public override async Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
    {
        var property = await _properties.GetByIdAsync(Request.PropertyId, ct).ConfigureAwait(false);
        if (property is null)
            return NotFoundError.For<Property, Guid>(Request.PropertyId);

        var roomType = await _roomTypes.GetByIdAsync(Request.RoomTypeId, ct).ConfigureAwait(false);
        if (roomType is null)
            return NotFoundError.For<RoomType, Guid>(Request.RoomTypeId);

        if (Request.NumberOfGuests > roomType.MaxOccupancy)
            return new RoomUnavailableError { /* ... */ };

        var nights = (int)(Request.CheckOut - Request.CheckIn).TotalDays;
        var totalAmount = nights * roomType.BaseRate;

        var reservation = Reservation.Create(
            Request.GuestId, Request.PropertyId, Request.RoomTypeId,
            Request.CheckIn, Request.CheckOut, Request.NumberOfGuests,
            totalAmount, roomType.Currency, Request.SpecialRequests);

        _reservations.Add(reservation);
        return reservation.Id;
    }
}
```

And the amenity creation collapses to just the intent:

```csharp
[Mutation(Mode = MutationMode.Create)]
[RequirePermission(CatalogPermissions.Amenity.Create)]
[Endpoint(HttpVerb.Post, "api/v1/amenities")]
public partial class CreateAmenityMutation : Mutation<Amenity>
{
    public required string Name { get; init; }
    public AmenityCategory Category { get; init; }
    public string? IconName { get; init; }
}
```

That is the entire class. The source generator:
- Detects private fields and generates `SetDependencies()` for DI injection
- Generates an `Invoker` nested class with the full pipeline (filters, logging, telemetry)
- For mutations, generates `ApplyToEntity()` that maps matching properties via `entity.SetX(this.X)`
- Generates a `MutationInvoker` with the lifecycle: validate, load/create, apply, entity-validate, persist, dispatch events
- Registers the invoker in DI automatically
- When paired with `[Endpoint]`, generates a full HTTP endpoint

No reflection at runtime. No runtime code generation. The generated code is visible in your IDE under `obj/`, fully debuggable.

---

## How It Works: The Pipeline

Every action executes through a deterministic pipeline. The source generator assembles the pipeline at compile time based on the base class and attributes you choose.

### DomainAction Pipeline

```
Caller (endpoint, boundary interface, or programmatic)
  |
  v
InjectDependencies(action)           SG-generated: resolves private fields from DI
  |
  v
BeforeExecute filters (ascending order)
  100: ValidationFilter              [Validate] --> ISyncValidator / IAsyncValidator
  200: PermissionAuthorizationFilter  [RequirePermission], [RequireAnyPermission]
  210: PolicyEvaluationFilter         [RequirePolicy<T>]
  250: ResourceAuthorizationFilter    IResourceAuthorizer<T>
  ???: IActionFilter<TAction>         Custom typed filters
 1000: LoggingFilter                  Structured logging
  |                                  Any filter can short-circuit with failure
  v
PrepareActionAsync(action)           [FromClock]/[FromCurrentUser] written, then [LoadCurrentUser]
  |                                  and [LoadEntity<T>]/[LoadEntities<T>] pre-load the signed-in user
  |                                  and entities by ID, by a list of IDs, or by a named specification
  |                                  (RequireReadPermission: the entity's read permission first)
  |                                  (404 if not found)
  |                                  After authorization (no 404-vs-403 probing), inside the
  |                                  [WithoutFilter]/[FilterMode] scopes like the body
  v
action.Execute(ct)                   Your business logic
  |
  v
AfterExecute filters (descending order)
  |                                  Cannot modify result, used for side effects
  v
SaveChangesAsync(ct)                 Boundary-keyed IUnitOfWork (on success only)
  |
  v
Result<TReturn, IError>             Typed success or error
```

### Mutation Pipeline

The mutation pipeline is more structured, with two levels of validation and automatic entity lifecycle management:

```
Caller (endpoint, boundary interface, or programmatic)
  |
  v
InjectDependencies(mutation)          SG-generated: resolves private fields from DI
  |
  v
L1 Sync Validation                    ISyncValidator on mutation (generated from attributes)
  |
  v
L1 Async Validation                   IAsyncValidator<TMutation> from DI
  |
  v
IActionFilter<TMutation>             Typed business policy filters (ascending order)
  |                                   Any can short-circuit
  v
PrepareMutationAsync(mutation)        [FromClock]/[FromCurrentUser] written, then
  |                                   [LoadEntity<T>]/[LoadEntities<T>] pre-load other entities by ID or
  |                                   by a list of IDs (404 when missing)
  |
  v
Load or Create entity                 Mode-dependent:
  |                                   - Create: new TEntity()
  |                                   - Update: LoadEntityAsync(mutation, ct)
  |                                   - Delete: LoadEntityAsync + DeleteEntity
  |                                   - Restore: LoadEntityAsync + RestoreEntity
  |                                   - CreateOrUpdate: try load, fallback to create
  v
mutation.ApplyToEntity(entity)        SG auto-mapped: entity.SetX(this.X) for matching properties
  |
  v
mutation.ApplyAsync(entity, ct)       Developer override for custom logic (state transitions, etc.)
  |
  v
L2 Entity Validation                  IValidator<TEntity> (change-aware, only validates modified props)
  |
  v
Persist                               Add if new, SaveChangesAsync
  |
  v
Dispatch domain events                IDomainEventDispatcher (if entity implements IHasDomainEvents)
  |
  v
Cache invalidation                    ICacheInvalidator (if mutation implements ICacheInvalidator)
  |
  v
Result<TEntity, IError>              The modified entity on success, or an error
```

The pipeline steps that apply depend on the base class and attributes. A `DomainAction<T>` with no `[Validate]` and no `[RequirePermission]` skips validation and authorization filters. A `Mutation<T>` always gets the full lifecycle. The SG only generates the steps that are declared -- there is no performance penalty for unused features.

---

## Choosing the Right Base Class

The framework provides three base classes (with void variants), each designed for a specific level of complexity. The decision tree below helps you pick the right one.

### Decision Table

| Question | If Yes | If No |
|----------|--------|-------|
| Custom business logic that returns a value? | `DomainAction<T>` | Keep reading |
| Operation that succeeds or fails, no return value? | `VoidDomainAction` | Keep reading |
| Standard CRUD on a persisted entity? | `Mutation<T>` | Keep reading |
| Need the entity lifecycle (load/create, apply, validate entity, persist, events)? | `Mutation<T>` | `DomainAction<T>` |
| Orchestrating a **fixed, named** set of writes atomically? | `[CompositeAction]` on `DomainAction<T>`; see [Mutations: Composing Mutations](mutations.md#composing-mutations-compositeaction) | Single mutation |
| A nested DTO whose **collections** carry a variable number of children? | `[PartOf<TParent>]` + `CollectionStrategy` on the parent's mutation | Keep reading |

### Base Class Reference

| Base Class | Handler Method | Return Type | Pipeline | Use When |
|------------|---------------|-------------|----------|----------|
| `DomainAction<T>` | `Execute` | `Result<T, IError>` | Full filter pipeline | Queries, orchestrations, complex writes |
| `VoidDomainAction` | `Execute` | `VoidResult<IError>` | Full filter pipeline | Fire-and-forget, side effects, state transitions |
| `Mutation<T>` | `ApplyAsync` (or auto-mapped) | `Result<TEntity, IError>` | Full lifecycle pipeline | Entity CRUD: create, update, delete, restore |

### Error Type Variants

Each base class supports up to 6 typed error parameters:

```csharp
// No explicit errors (IError is always available)
public partial class GetUser : DomainAction<UserDto> { }

// One error type
public partial class CreateReservation : DomainAction<Guid, RoomUnavailableError> { }

// Two error types
public partial class TransferFunds : DomainAction<TransferResult, InsufficientFundsError, AccountFrozenError> { }

// Void with one error type
public partial class MarkInvoicePaid : VoidDomainAction<NotFoundError> { }

// Mutation with one error type
public partial class CancelReservation : Mutation<Reservation, ConflictError> { }
```

The `IProducesError<T>` marker interfaces are used by the SG to generate OpenAPI error schemas when the action is paired with `[Endpoint]`.

### When NOT to Use Each

**Do not use `Mutation<T>` for read operations.** Mutations are tied to the persistence unit-of-work and call `SaveChangesAsync` at the end. Use `DomainAction<T>` for queries and lookups.

**Do not use `DomainAction<T>` for simple entity CRUD.** If your operation is "take these properties, map them to an entity, validate, persist" -- that is exactly what `Mutation<T>` automates. Writing `Execute()` to manually call `entity.SetName(this.Name)` duplicates what the SG generates for free.

**Do not use `VoidDomainAction` when you need to return data.** If the caller needs the ID of a created resource, use `DomainAction<Guid>`. `VoidDomainAction` returns 204 No Content when paired with `[Endpoint]`.

**Do not use `[CompositeAction]` for a single entity.** Composite actions are for orchestrating multiple mutations within a single transaction. If you are modifying one entity, use a regular `Mutation<T>`.

**Do not use `[CompositeAction]` for a collection.** Its steps are individual properties, named at compile time; a `List<TMutation>` property is not a step and is skipped without a diagnostic. Children arriving as a collection inside one nested DTO are `[PartOf<TParent>]` with a `CollectionStrategy`, written alongside the parent.

⚠️ **A composite exposed with `[Endpoint]` must declare its own `[RequirePermission]`.** Its steps run as internal calls and their permissions are not re-checked, so a composite without one answers `204` to a caller who holds nothing. Both points are worked through in [Mutations: Composing Mutations](mutations.md#composing-mutations-compositeaction).

---

## Action Registration Model

The source generator produces everything needed to invoke actions through the pipeline.

### 1. `[DomainAction]` / `[Mutation]` attribute -- compile time

Marks a class for source generation. The SG produces:
- A `SetDependencies()` partial method that injects private fields from DI
- A nested `Invoker` class that implements the pipeline
- DI registration code

```csharp
[DomainAction]
public partial class CreateReservationAction : DomainAction<Guid> { /* ... */ }

[Mutation(Mode = MutationMode.Create)]
public partial class CreateAmenityMutation : Mutation<Amenity> { /* ... */ }
```

### 2. `AddPragmaticActions()` -- core service registration

Registers the pipeline infrastructure: `ActionCallContext`, `ValidationFilter`, `LoggingFilter`, `PermissionAuthorizationFilter`, `PolicyEvaluationFilter`, `ResourceAuthorizationFilter`.

### 3. Generated `AddXxxActions()` / `AddXxxMutations()` -- invoker registration

The SG generates assembly-level extension methods that register all invokers:

```csharp
// Generated: _Infra.Actions.Registration.g.cs
public static IServiceCollection AddShowcaseActions(this IServiceCollection services)
{
    services.AddScoped<IDomainActionInvoker<CreateReservationAction, Guid>,
        CreateReservationAction.Invoker>();
    return services;
}

// Generated: _Infra.Mutations.Registration.g.cs
public static IServiceCollection AddShowcaseMutations(this IServiceCollection services)
{
    services.AddScoped<IMutationInvoker<CreateAmenityMutation, Amenity>,
        CreateAmenityMutation.Invoker>();
    return services;
}
```

### With Pragmatic.Composition (recommended)

When you use `Pragmatic.Composition` and `PragmaticApp.RunAsync()`, all registration is **automatic** -- the SG-generated host calls `AddPragmaticActions()`, `AddXxxActions()`, and `AddXxxMutations()`:

```csharp
// Program.cs -- with Composition, no manual registration needed
await PragmaticApp.RunAsync(args, app =>
{
    app.UseAuthentication<NoOpAuthenticationHandler>("Default");
});
```

### Without Composition (standalone)

If you use Actions without Composition, you call them manually:

```csharp
// Program.cs -- standalone, manual registration
builder.Services.AddPragmaticActions();
builder.Services.AddShowcaseActions();       // generated
builder.Services.AddShowcaseMutations();     // generated
```

---

## Dependency Injection Model

Pragmatic.Actions uses a field-based injection pattern. The source generator scans private fields in your action class and generates a `SetDependencies()` method.

### Convention: fields vs. properties

```csharp
[DomainAction]
public partial class CreateReservation : DomainAction<Guid>
{
    // Dependencies -- private fields, resolved from DI
    private IRepository<Reservation> _reservations = null!;
    private IClock _clock = null!;

    // Inputs -- public properties, set by the caller
    public required Guid GuestId { get; init; }
    public required DateOnly CheckIn { get; init; }
}
```

**Private fields** = dependencies (resolved from the DI container via `SetDependencies()`).
**Public properties** = inputs (set by the caller before invocation).

The `= null!` initializer on dependency fields silences the nullable warning. The SG generates `SetDependencies()` to populate them before `Execute()` runs.

### What the SG generates

For the class above, the SG produces:

```csharp
// CreateReservation.SetDependencies.g.cs
public partial class CreateReservation
{
    internal void SetDependencies(
        IRepository<Reservation> reservations,
        IClock clock)
    {
        _reservations = reservations;
        _clock = clock;
    }
}
```

And in the generated `Invoker`:

```csharp
public sealed class Invoker : DomainActionInvoker<CreateReservation, Guid>
{
    private readonly IRepository<Reservation> _reservations;
    private readonly IClock _clock;

    public Invoker(IServiceProvider serviceProvider,
        IRepository<Reservation> reservations,
        IClock clock) : base(serviceProvider)
    {
        _reservations = reservations;
        _clock = clock;
    }

    protected override void InjectDependencies(CreateReservation action)
    {
        action.SetDependencies(_reservations, _clock);
    }
}
```

The invoker resolves dependencies once through constructor injection (standard DI), then injects them into each action instance via `SetDependencies`. This avoids service locator patterns while keeping action classes clean.

### Invoking actions programmatically

Inject the invoker interface to call actions from other code:

```csharp
public class PaymentOrchestrator(
    IMutationInvoker<CreateInvoiceMutation, Invoice> createInvoice,
    IVoidDomainActionInvoker<MarkInvoicePaidAction> markPaid)
{
    public async Task<Result<Invoice, IError>> CreateAndPayAsync(
        Guid reservationId, decimal amount, CancellationToken ct)
    {
        var mutation = new CreateInvoiceMutation
        {
            ReservationId = reservationId,
            TotalAmount = amount
        };
        var result = await createInvoice.InvokeAsync(mutation, ct);
        if (result.IsFailure) return result;

        var markAction = new MarkInvoicePaidAction { Id = result.Value.Id };
        var voidResult = await markPaid.InvokeAsync(markAction, ct);
        // ...
    }
}
```

### Invoker interfaces

| Interface | For | Return |
|-----------|-----|--------|
| `IDomainActionInvoker<TAction, TReturn>` | `DomainAction<TReturn>` | `Result<TReturn, IError>` |
| `IVoidDomainActionInvoker<TAction>` | `VoidDomainAction` | `VoidResult<IError>` |
| `IMutationInvoker<TMutation, TEntity>` | `Mutation<TEntity>` | `Result<TEntity, IError>` |

---

## Error Handling Model

Pragmatic.Actions uses the Result pattern to make error paths explicit and type-safe. Every possible error type is declared in the class signature.

### Standard Result types

```csharp
// DomainAction always returns Result<TReturn, IError>
public override async Task<Result<Guid, IError>> Execute(CancellationToken ct)
{
    // Return success
    return reservation.Id;

    // Return typed error (implicit conversion)
    return NotFoundError.For<Property, Guid>(Request.PropertyId);

    // Return custom error
    return new RoomUnavailableError { PropertyId = id };
}

// VoidDomainAction returns VoidResult<IError>
public override async Task<VoidResult<IError>> Execute(CancellationToken ct)
{
    // Return success (convenience property)
    return Success;

    // Return typed error
    return VoidResult<IError>.Failure(NotFoundError.For<Invoice, Guid>(Id));
}
```

### Error type declaration

Declare error types in the generic parameters to get compile-time type safety and OpenAPI schema generation:

```csharp
// The SG knows this action can produce NotFoundError and RoomUnavailableError
public partial class CreateReservation : DomainAction<Guid, RoomUnavailableError> { }

// When paired with [Endpoint], the SG generates:
// - 200 OK with Guid response
// - 404 Not Found (built-in for all actions)
// - 422 Unprocessable Entity for RoomUnavailableError
```

### Mutation error handling

Mutations return `Result<TEntity, IError>` from `ApplyAsync`. The pipeline maps errors automatically:

```csharp
[Mutation(Mode = MutationMode.Update)]
public partial class CancelReservation : Mutation<Reservation, ConflictError>
{
    public override async Task<Result<Reservation, IError>> ApplyAsync(
        Reservation entity, CancellationToken ct)
    {
        if (_clock.UtcNow > cutoff)
            return new ConflictError { Reason = "Cancellation window expired" };

        entity.Cancel(Reason);
        return entity;
    }
}
```

### Pipeline errors

Errors can also come from the pipeline itself:

| Source | Error Type | When |
|--------|-----------|------|
| ValidationFilter | `ValidationError` | Input fails sync or async validation |
| PermissionAuthorizationFilter | `ForbiddenError` | User lacks required permissions |
| PolicyEvaluationFilter | `ForbiddenError` | Policy evaluation fails |
| ResourceAuthorizationFilter | `ForbiddenError` | Resource-level authorization fails |
| MutationInvoker entity load | `NotFoundError` | Entity not found for Update/Delete |
| L2 Entity Validation | `ValidationError` | Entity state is invalid after apply |

All pipeline errors short-circuit execution and return immediately to the caller.

---

## Filter Pipeline

### Built-in filters

Pragmatic.Actions ships with five built-in filters, all enabled by default:

| Filter | Order | Trigger | Purpose |
|--------|-------|---------|---------|
| `ValidationFilter` | 100 | `[Validate]` on action | Runs `ISyncValidator` and `IAsyncValidator<T>` |
| `PermissionAuthorizationFilter` | 200 | `[RequirePermission]`, `[RequireAnyPermission]` | Checks user permissions via `IPermissionChecker` |
| `PolicyEvaluationFilter` | 210 | `[RequirePolicy<T>]` | Evaluates `ResourcePolicy` against `ICurrentUser` |
| `ResourceAuthorizationFilter` | 250 | `IResourceAuthorizer<T>` registered in DI | Resource-level authorization |
| `LoggingFilter` | 1000 | Always active | Structured logging of action execution |

Filters execute in ascending order for `BeforeExecuteAsync`, and in descending order for `AfterExecuteAsync`.

### Filter ordering gaps

The order values have intentional gaps for custom filters:

```
100  Validation
     150  -- your custom filter here --
200  PermissionAuthorization
210  PolicyEvaluation
     225  -- your custom filter here --
250  ResourceAuthorization
300  Transaction (reserved)
400  Caching (reserved)
     500-999 -- your custom filters --
1000 Logging
```

### Custom filters

Three filter interfaces serve different needs:

```csharp
// Global filter: applies to ALL actions
public class AuditFilter : IActionFilter
{
    public int Order => 900;

    public Task<VoidResult<IError>> BeforeExecuteAsync<TAction, TReturn>(
        TAction action, CancellationToken ct)
        where TAction : DomainAction<TReturn>
    {
        // Log audit trail
        return Task.FromResult(VoidResult<IError>.Success());
    }

    public Task AfterExecuteAsync<TAction, TReturn>(
        TAction action, Result<TReturn, IError> result, CancellationToken ct)
        where TAction : DomainAction<TReturn>
    {
        return Task.CompletedTask;
    }
}

// Typed filter: applies to a specific action type
public class ReservationBusinessRule : IActionFilter<CreateReservationAction>
{
    public int Order => 250;

    public Task<VoidResult<IError>> BeforeExecuteAsync(
        CreateReservationAction action, CancellationToken ct)
    {
        // Custom business rule check
    }
}

// Typed filter with result access
public class OrderResultFilter : IActionFilter<PlaceOrder, OrderResult>
{
    public int Order => 900;
    // Has access to both action and typed result in AfterExecuteAsync
}
```

Register filters in DI:

```csharp
// Global
services.AddActionFilter<AuditFilter>();

// Action-specific
services.AddActionFilter<ReservationBusinessRule, CreateReservationAction>();

// With result type
services.AddActionFilter<OrderResultFilter, PlaceOrder, OrderResult>();
```

### Internal call bypass

When one action calls another within the same boundary through `I{Boundary}InternalActions`, authorization filters are skipped. The `ActionCallContext.IsInternalCall` flag is set by that interface's implementation, and only by it: the public `I{Boundary}Actions` (what another module injects) enforces the permission of the operation it invokes, unless the caller declares `[AbsorbsChildPermissions]`.

```csharp
// PermissionAuthorizationFilter checks:
if (callContext.IsInternalCall) return VoidResult<IError>.Success();
```

This prevents redundant permission checks when action A orchestrates action B within the same boundary.

---

## What Gets Generated

For each class marked with `[DomainAction]`, the source generator produces one or more files:

### DomainAction output

| Generated File | Content | Condition |
|----------------|---------|-----------|
| `{Type}.SetDependencies.g.cs` | `SetDependencies()` method for DI field injection | If private fields exist |
| `{Type}.Invoker.g.cs` | Nested `Invoker` class implementing the pipeline | Always |
| `{Type}.Versioning.g.cs` | `ExecuteActionAsync` override for versioned dispatch | If `ExecuteV2`, `ExecuteV3`, etc. exist |
| `_Infra.Actions.Registration.g.cs` | Assembly-level `AddXxxActions()` method | Once per assembly |
| `_Metadata.Actions.g.cs` | Action metadata for discovery | Once per assembly |

### Mutation output

| Generated File | Content | Condition |
|----------------|---------|-----------|
| `{Type}.ApplyToEntity.g.cs` | `ApplyToEntity()` with property-to-setter mappings | If matching properties exist |
| `{Type}.SetDependencies.g.cs` | `SetDependencies()` for mutation-specific dependencies | If private fields exist |
| `{Type}.MutationInvoker.g.cs` | Nested `Invoker` class with full lifecycle | Always |
| `_Infra.Mutations.Registration.g.cs` | Assembly-level `AddXxxMutations()` method | Once per assembly |

### Boundary output

| Generated File | Content | Condition |
|----------------|---------|-----------|
| `_Boundary.{Name}.Interface.g.cs` | `I{Name}Actions` interface with typed invoke methods | For each `[Boundary]` class |
| `_Boundary.{Name}.Implementation.g.cs` | Implementation class that resolves invokers from DI | For each `[Boundary]` class |
| `_Boundary.{Name}.Registration.g.cs` | DI registration for boundary interface | For each `[Boundary]` class |

### When combined with `[Endpoint]`

When a `[DomainAction]` or `[Mutation]` also has `[Endpoint]`, the Endpoints SG generates additional files:

| Generated File | Content |
|----------------|---------|
| `{Type}.Endpoint.g.cs` | HTTP handler that resolves the invoker, invokes, and maps result to HTTP |
| `{Type}Body.RequestBody.g.cs` | Body DTO record for non-route/query properties |

All generated files live under `obj/Debug/net10.0/generated/` and are fully visible in the IDE. You can set breakpoints in generated code.

---

## Mutation Mode Reference

The `MutationMode` enum determines the entity lifecycle:

| Mode | Entity Source | Auto-mapping | Persist | Use Case |
|------|--------------|-------------|---------|----------|
| `Create` | `new TEntity()` | Runs `ApplyToEntity` | `Add` + `SaveChanges` | New entity creation |
| `Update` | `LoadEntityAsync` by ID | Runs `ApplyToEntity` | `SaveChanges` | Modify existing entity |
| `CreateOrUpdate` | Try load, fallback to create | Runs `ApplyToEntity` | `Add` if new + `SaveChanges` | Upsert semantics |
| `Delete` | `LoadEntityAsync` by ID | Runs `ApplyAsync` only | Hard/soft delete + `SaveChanges` | Remove entity |
| `Restore` | `LoadEntityAsync` (bypasses soft-delete filter) | Runs `ApplyAsync` only | Reset soft-delete fields + `SaveChanges` | Undo soft-delete |

### Mode inference

If you do not set `Mode` explicitly, the SG infers it from the class name:

| Class Name Prefix | Inferred Mode |
|-------------------|---------------|
| `Create*` | `MutationMode.Create` |
| `Update*` | `MutationMode.Update` |
| `Delete*` | `MutationMode.Delete` |
| `Restore*` | `MutationMode.Restore` |

If none matches, `PRAG0410` is emitted.

### Return type control

The `ReturnType` property on `[Mutation]` controls what the generated endpoint returns:

| ReturnType | Endpoint Response | Default For |
|------------|-------------------|-------------|
| `Id` | Entity's ID (typically `Guid`) | Create, Update |
| `LogicalKey` | Entity's logical key (string) | Business-key entities |
| `Entity` | Full entity | Simple cases |

---

## Boundary Model

Boundaries group related actions into strongly-typed interfaces for cross-module composition.

### How boundaries work

```csharp
// Define a boundary
[Boundary]
public partial class BookingBoundary;
```

The SG captures all `[DomainAction]` and `[Mutation]` classes whose namespace starts with the boundary's namespace. It generates:

1. **`IBookingActions`** -- interface with typed invoke methods
2. **`BookingActions`** -- implementation that resolves invokers from DI
3. **DI registration** -- `services.AddScoped<IBookingActions, BookingActions>()`

### Boundary assignment

Actions are assigned to boundaries by namespace prefix (default) or explicit attribute:

```csharp
// Implicit: Showcase.Booking.Reservations.Mutations.* -> BookingBoundary
[Mutation(Mode = MutationMode.Update)]
public partial class CheckInGuestMutation : Mutation<Reservation> { }

// Explicit: override namespace-based assignment
[DomainAction]
[BelongsTo<ShippingBoundary>]
public partial class SpecialShipment : DomainAction<ShipmentId> { }
```

### Sub-boundaries (feature grouping)

When a boundary has many operations, organize them into **feature folders**. The SG automatically infers sub-boundaries from the namespace and generates separate interfaces:

```
Showcase.Booking/
├── Guests/                          → IBookingGuestsActions
│   ├── Guest.cs                     (entity at sub root)
│   ├── Mutations/CreateGuestMutation.cs
│   └── Queries/SearchGuestsQuery.cs
├── Reservations/                    → IBookingReservationsActions
│   ├── Reservation.cs
│   ├── Mutations/ConfirmReservationMutation.cs
│   └── Actions/CreateReservationAction.cs
├── Events/                          (cross-feature, boundary level)
│   └── Handlers/
└── Infrastructure/                  (authorization, services, etc.)
```

**Generated**:
```csharp
// Sub-interfaces (one per feature folder)
public interface IBookingGuestsActions
{
    Task<Result<Guest, IError>> CreateGuest(CreateGuestMutation mutation, CancellationToken ct = default);
    Task<Result<Guest, IError>> UpdateGuest(UpdateGuestMutation mutation, CancellationToken ct = default);
}

// Root interface with property composition (not inheritance)
public interface IBookingActions
{
    IBookingGuestsActions Guests { get; }
    IBookingReservationsActions Reservations { get; }
}
```

**Usage with ISP** -- inject only the sub-interface you need:
```csharp
public class GuestService(IBookingGuestsActions guests)
{
    public Task CreateGuest(...) => guests.CreateGuest(mutation, ct);
}
```

**No configuration needed** -- the SG detects feature folders automatically from namespaces. Without feature folders, the boundary generates a flat interface (backward compatible).

### Cross-boundary calls

Inject the boundary interface to call actions across module boundaries:

```csharp
public class InvoicePaidHandler(IBookingActions booking)
{
    public async Task HandleAsync(InvoicePaidEvent evt, CancellationToken ct)
    {
        // Cross-boundary call. An event handler runs inside an internal call (the dispatcher enters
        // it), so the callee's permission is not asked of the HTTP user. Outside a handler,
        // IBookingActions enforces it, unless the caller declares [AbsorbsChildPermissions].
        await booking.MarkPaymentReceivedAsync(
            new MarkPaymentReceivedMutation { Id = evt.ReservationId }, ct);
    }
}
```

---

## Ecosystem Integration

Pragmatic.Actions is the business logic layer. Other Pragmatic modules plug into it to provide pipeline features and expose actions as HTTP endpoints.

### Endpoints

When a class inherits `DomainAction<T>` or `Mutation<T>` and has `[Endpoint]`, the SG generates a handler that resolves the invoker from DI and calls `InvokeAsync`. The full action pipeline runs for every HTTP request.

```csharp
[DomainAction]
[Endpoint(HttpVerb.Post, "api/v1/reservations")]
public partial class CreateReservationAction : DomainAction<Guid> { /* ... */ }
```

### Validation

The `[Validate]` attribute triggers the `ValidationFilter` to run `ISyncValidator` (from validation attributes on properties) and `IAsyncValidator<T>` (from DI) before `Execute()`. For mutations, L1 validation is built into the pipeline and runs automatically.

### Persistence

`Mutation<T>` integrates with the persistence unit-of-work. The SG generates code that loads entities by ID, maps input properties via generated setters, and persists via `SaveChangesAsync`. `[BelongsTo<T>]` routes to the correct boundary-keyed `IUnitOfWork`.

### Authorization

Three pipeline filters enforce authorization declaratively:
- `[RequirePermission("booking.reservation.create")]` -- all listed permissions required
- `[RequireAnyPermission("booking.admin", "booking.reservation.read")]` -- at least one
- `[RequirePolicy<ReservationManagementPolicy>]` -- composable policy evaluation

### Events

Entities implementing `IHasDomainEvents` get their events dispatched after `SaveChangesAsync` in the mutation pipeline. Event handlers can trigger cross-boundary actions via boundary interfaces.

### Caching

Mutations implementing `ICacheInvalidator` trigger cache invalidation after successful persistence. The SG generates the invalidation call in the pipeline.

### Telemetry

Every action and mutation invocation creates an `Activity` with OpenTelemetry-compatible tags. Counters and histograms track invocation counts, durations, failures, and validation errors.

---

## See Also

- [Getting Started](getting-started.md) -- Create your first DomainAction and Mutation
- [Pipeline](pipeline.md) -- Filter ordering, lifecycle hooks, custom filters
- [Mutations](mutations.md) -- Entity mutations, ApplyAsync, auto-mapping
- [Boundaries](boundaries.md) -- Boundary interfaces, internal calls, remote
- [Common Mistakes](common-mistakes.md) -- Avoid the most frequent pitfalls
- [Troubleshooting](troubleshooting.md) -- Problem/solution guide with diagnostics reference
- [Showcase: Booking actions](../../examples/showcase/src/Showcase.Booking/) -- Real-world examples
- [Showcase: Billing actions](../../examples/showcase/src/Showcase.Billing/) -- VoidDomainAction and cross-boundary patterns
