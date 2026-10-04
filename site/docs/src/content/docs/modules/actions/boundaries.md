---
title: "Boundaries Guide"
description: "Boundaries group related domain actions and mutations into a strongly-typed interface, providing module-level composition, transaction isolation, and topology v"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Actions/docs/boundaries.md
sidebar:
  order: 3
---
Boundaries group related domain actions and mutations into a strongly-typed interface, providing module-level composition, transaction isolation, and topology validation.

## What is a Boundary?

A boundary is a lightweight marker class annotated with `[Boundary]`. It represents a bounded context in your domain. The source generator scans all `[DomainAction]` and `[Mutation]` classes whose namespace matches the boundary's namespace and generates a typed interface for invoking them.

```csharp
using Pragmatic.Actions.Attributes;

namespace Showcase.Catalog;

[Boundary]
public partial class CatalogBoundary;
```

This generates `ICatalogActions` with typed invoke methods for every action in the `Showcase.Catalog.*` namespace tree.

---

## Namespace-Based Capture

The default assignment rule is **namespace prefix matching**:

- `CatalogBoundary` is in `Showcase.Catalog`
- All actions in `Showcase.Catalog.*` are captured
- `Showcase.Catalog.Amenities.Mutations.CreateAmenityMutation` belongs to `CatalogBoundary`
- `Showcase.Catalog.Properties.Actions.UploadPropertyPhotoAction` belongs to `CatalogBoundary`

If a sub-namespace defines its own boundary, it is subtracted from the parent. Actions in that sub-namespace belong to the child boundary instead.

### Capture Rules

1. A boundary captures its namespace AND all sub-namespaces (deep capture)
2. If a sub-namespace has its own `[Boundary]`, it subtracts from the parent
3. Actions with `Internal = true` or `System = true` are excluded from the interface
4. Actions with `[BelongsTo<T>]` override namespace assignment

---

## Explicit Assignment

Override namespace-based assignment with `[BelongsTo<T>]`:

```csharp
[DomainAction]
[BelongsTo<ShippingBoundary>]
public partial class SpecialShipment : DomainAction<ShipmentId>
{
    // Lives in OrdersBoundary's namespace but belongs to ShippingBoundary
}
```

---

## Internal and System Actions

### Internal Actions

```csharp
[DomainAction(Internal = true)]
public partial class RecalculateTotals : DomainAction<decimal>
{
    // NOT included in boundary interface
    // Still goes through the full pipeline
    // Cannot have [Endpoint]
}
```

Internal actions are helper operations called from other actions within the same boundary. They run through the pipeline but are not exposed in the boundary interface.

### System Actions

```csharp
[DomainAction(System = true)]
public partial class ExportReport : DomainAction<byte[]>
{
    // NOT included in boundary interface
    // CAN have [Endpoint] for direct HTTP exposure
}
```

System actions are infrastructure operations (exports, health checks, diagnostics) that bypass boundary grouping.

---

## Boundary Visibility

```csharp
[Boundary(Visibility = BoundaryVisibility.Public)]    // default
public partial class CatalogBoundary;

[Boundary(Visibility = BoundaryVisibility.Internal)]
public partial class InternalServiceBoundary;
```

- **Public**: Generates both public and internal interfaces. The boundary is callable from other assemblies and can be exposed via endpoints or remote boundaries.
- **Internal**: Generates only an internal interface. The boundary is only callable within the same assembly.

---

## Sub-Boundaries

Large boundaries are split into sub-groups for better interface segregation. Normally the **namespace**
decides: an operation in `Booking.Reservations.Mutations` lands in the `Reservations` group without
anyone writing anything, and **PRAG0413** says so on the operation that produced it.

`[SubBoundary]` is how an operation overrides that: it goes **on the operation**, which is what the
generator reads:

```csharp
[Mutation(Mode = MutationMode.Update)]
[SubBoundary(Name = "Reservations", Description = "Reservation management operations")]
public partial class ConfirmReservationMutation : Mutation<Reservation> { /* ... */ }
```

⚠️ The attribute goes on an operation. On a marker class of its own (`public class
ReservationSubBoundary { }`) it is on something that is not an operation, and nothing reads it there.

`Name` wins over the namespace and is not reported as inferred. A name that is empty, or the
boundary's own, is **PRAG0416**: the first would fall back to the namespace and make the declaration a
no-op, the second would publish `IBookingBookingActions`. `Description` becomes the generated group
interface's summary.

Sub-boundaries generate their own interface (e.g., `IReservationsActions`) and local implementation. The root boundary interface composes them as properties:

```csharp
// Generated
public interface IBookingActions
{
    IReservationsActions Reservations { get; }
    IGuestsActions Guests { get; }
    // ...
}
```

When `Name` is not specified, it is inferred from the namespace relative to the parent boundary.

---

## Cross-Boundary Read Access

When a boundary needs to read entities from another boundary via SQL join (both must share the same physical database):

```csharp
[Boundary]
[ReadAccess<Property>]
[ReadAccess<RoomType>]
public partial class BookingBoundary;
```

Effects:
- The persistence SG adds `DbSet<Property>` and `DbSet<RoomType>` to `BookingBoundaryDbContext`
- Both DbSets are configured with `ExcludeFromMigrations()` -- migrations don't create duplicate tables
- The composition SG validates at compile time that both boundaries target the same physical database (via topology metadata)

This enables efficient read queries that JOIN across boundaries without HTTP calls:

```csharp
// In BookingBoundary action:
var reservations = await _reservations.Query()
    .Include(r => r.Property)     // Property is from CatalogBoundary
    .Where(r => r.Property.City == "Rome")
    .ToListAsync(ct);
```

---

## Cross-Boundary Writes (PRAG0424)

Reading across boundaries is a join. Writing across them is not: each boundary owns a `DbContext`, and
`DomainActionInvoker` saves it once, at the end, only when the action succeeded. Call another
boundary's actions from inside yours and there are two saves, the inner one first, so a failure after
that point leaves the inner writes committed, with nothing to roll them back.

Measured on a real application: a deliberate failure left five rows in the callee's table, pointing at
a parent row that was never written.

The generator warns when a single invocation commits into more than one store:

```
PRAG0424: 'WriteStoryAction' writes in more than one boundary within one invocation
          (IKnowledgeActions). Each boundary saves separately, inner first, so a failure
          after that point leaves the inner writes committed.
```

Four ways out, in order of preference:

1. **Move the work into one boundary.** If the two writes are one fact, they belong to one owner.
2. **Make the inner step undo itself**, the only option that repairs rather than accepts:

   ```csharp
   [DomainAction]
   [UndoWith<RemoveIngestedText>]
   public partial class IngestTextAction : DomainAction<IngestResult>;

   public sealed class RemoveIngestedText(IRepository<Term> terms)
       : ICompensates<IngestResult>
   {
       public Task<VoidResult<IError>> Undo(IngestResult committed, CancellationToken ct = default)
       {
           // remove what the ingestion added
       }
   }
   ```

   The invoker registers the undo *after* its commit succeeds. If an outer action in the same request
   then fails, its invoker runs the registered undos in reverse order before returning. The compensator
   is an ordinary scoped service, not an action: the generator registers it, and commits it through the
   unit of work of the boundary that owns the data.

   **Best effort, in-request, and that is the whole guarantee.** A crash between the inner commit and
   the compensation leaves the work committed: nothing here is durable and nothing is retried. That is
   where a saga starts. A compensator that itself fails is logged at `Error` and reported to the caller
   as `COMPENSATION_FAILED`, carrying both the original error and the undo's: the response says the
   system is inconsistent rather than only that the operation failed.

   Each facade method whose action declares an undo is marked `[CompensableStep]`, and PRAG0424 goes
   quiet for the callers that invoke *those* steps. Per method, not per facade: a caller should not have
   to compensate the operations it never touches.

   `PRAG0425` is an error when the declared compensator does not implement
   `ICompensates<TReturn>` (or `ICompensatesVoid`) for that action; otherwise the declaration would
   silence PRAG0424 while undoing nothing.

3. **Make the caller tolerate the leftovers**, and say so:

   ```csharp
   [DomainAction]
   [AcceptsPartialWrites("The glossary keeps unreferenced candidates; a nightly job prunes them.")]
   public partial class WriteStoryAction : DomainAction<WriteStoryResult>;
   ```

   The reason is required. A decision without one is indistinguishable from silencing the warning,
   which is the thing the diagnostic exists to prevent.
4. **Use a saga** when the leftovers are unacceptable and the work genuinely spans boundaries:
   durable state, retries, and compensation that survives a crash.

The warning fires on *more than one* commit scope, not on any cross-boundary call: an action that
writes nothing itself and calls a single other boundary is atomic, and stays quiet.

**Known blind spot.** The marker the diagnostic reads (`[BoundaryActions<TBoundary>]`) is emitted onto
the generated facade, and a facade generated in the compilation being analysed is invisible to the
generator that produced it. Two boundaries declared in the *same assembly* are therefore not detected.
Every topology the framework produces puts a boundary in its own assembly (calling a facade means
referencing the assembly carrying it), but a same-assembly pair slips through.

---

## Boundary Configuration

### Local Boundaries

Local boundaries run in-process with a database connection:

```csharp
services.AddBoundary<CatalogBoundary>(cfg => cfg
    .UseLocal()
    .UseDatabase(opt => opt.UseNpgsql(connectionString)));
```

Each local boundary gets its own `IUnitOfWork` keyed by boundary type, ensuring transaction isolation.

In a generated host the DbContext registration applies this delegate **after** its own provider call,
which reads the connection string from `[PragmaticDatabase(ConfigKey = …)]` and, for SQL Server and
PostgreSQL, turns on `EnableRetryOnFailure()`. So `UseDatabase` adds to what is generated, or changes
it: a provider call without a connection string keeps the generated one.

```csharp
services.AddBoundary<CatalogBoundary>(cfg => cfg.UseLocal().UseDatabase(opt =>
{
    opt.EnableSensitiveDataLogging();
    opt.UseNpgsql(npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 10));
}));
```

With a retrying strategy, a `[Transactional]` operation runs as one retriable unit: a transient failure
before the commit runs it again from a cleared change tracker. So its body must have no effect outside
its transaction. Mail, messages and events go through the outbox; a direct HTTP call or a file write
runs again.

### Remote Boundaries

Remote boundaries are accessed via HTTP. The invoker serializes the action and sends it as an HTTP request:

```csharp
services.AddBoundary<BillingBoundary>(cfg => cfg
    .UseRemote("https://billing-api.example.com"));
```

This registers a typed `HttpClient` named after the boundary type. The generated remote invoker uses this client to dispatch actions.

#### What a remote composition does *not* register

`AddRemote` registers `I{Boundary}Actions` and nothing else. `I{Boundary}InternalActions` -- and a
group's `I{Boundary}{Group}InternalActions` -- are **local only** and are deliberately absent.

They cannot be otherwise. The internal interface declares the *preloaded* shapes of the operations the
public interface also carries: they take a tracked entity, and a tracked entity does not cross a
process. An HTTP proxy that implemented them would have to re-read the entity at the far end, which is
a different operation with different concurrency.

The consequence is a rule about **where code lives**, not about how to inject it:

> Anything that injects an internal boundary interface is code of that module, and it runs in the host
> that owns the module.

That is why a host declaring `[RemoteBoundary<TModule>]` registers none of that module's in-process
workers either -- its message handlers, domain event handlers, jobs and sagas. They run the module's
own operations, so they belong in the process that hosts it; registering them in the calling host both
failed to resolve (the interface they need is not there) and subscribed the same handler twice.

A module is never written differently because of the topology. If an operation of *another* module has
to reach this one from a host where it is remote, it injects the **public** interface, and the
permission declared on the operation is enforced at the far end exactly as it would be in-process.

### Configuration Validation

Calling `Validate()` on a configuration checks:
- Local boundaries must have `DatabaseOptions` set (via `UseDatabase()`)
- Remote boundaries must have a base URL

---

## Topology

The topology is checked when the **host is built**, not at startup: the host's generator sees every
module it includes, so a wrong graph fails the build.

- A module depends on another with `[IncludeModule<TModule>]`. A dependency that names no known module is
  **PRAG1601**; a cycle is **PRAG1602**.
- A host registers exactly what it declares (`[Include<T>]`, `[RemoteBoundary<T>]`, its own modules) and
  does not follow a module's dependencies. A hosted module whose dependency the host neither includes nor
  declares remote is **PRAG1603**, an error on the host: include it, or declare it remote.
- A boundary that reads another boundary's entities declares `[ReadAccess<T>]`, checked against the
  database topology at compile time.

A boundary is registered **once**: a second `AddBoundary<T>(…)` for the same `T` throws
`InvalidOperationException` naming it. Accepted, it would leave the first configuration in force and ignore
the second: a `UseRemote` meant to replace a `UseLocal` that did nothing.

`services.GetAllBoundaryConfigurations()` lists what is registered (type, mode, remote URL) for
diagnostics.

> A dependency the host does not host is PRAG1603, checked by the host's generator at compile time;
> there is no runtime topology check.

---

## Module Metadata

The source generator writes one `[PragmaticModuleMetadata]` per boundary, which a host's generator reads
at compile time from the modules it references:

```csharp
[assembly: PragmaticModuleMetadata(
    BoundaryType = typeof(CatalogBoundary),
    ReadAccessTypes = new[] { typeof(Property), typeof(RoomType) }   // only with [ReadAccess<T>]
)]
```

---

## Internal Calls and Authorization Bypass

When one action invokes another within the same boundary through `I{Boundary}InternalActions`, that interface's implementation wraps the call in an internal call scope:

```csharp
// Generated I{Boundary}InternalActions implementation (simplified):
public async Task<Result<Guid, IError>> CreateReservation(
    CreateReservationAction action, CancellationToken ct)
{
    using var scope = _callContext.EnterInternalCall();
    return await _createReservationInvoker.InvokeAsync(action, ct);
}
```

The public `I{Boundary}Actions` does not. It is the contract another module injects, and a call through it enforces the permission of the operation it invokes, unless the caller is already inside an internal call: an event handler, or an operation that declares `[AbsorbsChildPermissions]`.

While `IsInternalCall` is true:
- `PermissionAuthorizationFilter` skips permission checks
- `PolicyEvaluationFilter` skips policy evaluation
- Validation still runs (internal calls should still be validated)

This prevents redundant authorization when action A orchestrates action B. The top-level action (called by the endpoint or external code) handles authorization; internal calls trust the caller.

**Nesting**: `ActionCallContext` uses a depth counter. Multiple levels of internal calls work correctly -- the context only returns to `IsInternalCall = false` when all scopes are disposed.

---

## Boundary Interface (IBoundary)

The `IBoundary` marker interface is used as a constraint on `BoundaryConfiguration<T>`:

```csharp
public interface IBoundary;

public sealed class BoundaryConfiguration<TBoundary>
    where TBoundary : IBoundary
{
    public BoundaryMode Mode { get; }
    public string? RemoteBaseUrl { get; }
    public Delegate? DatabaseOptions { get; }
    // ...
}
```

Your boundary class does not need to implement `IBoundary` directly -- it is used primarily for the configuration and DI registration constraints.

---

## Boundary Service Registration Extensions

The `BoundaryServiceCollectionExtensions` class provides helper methods:

```csharp
// Register a boundary with configuration
services.AddBoundary<CatalogBoundary>(cfg => cfg.UseLocal().UseDatabase(...));

// Check if a boundary is registered
bool exists = services.HasBoundary<CatalogBoundary>();

// Get configuration for a boundary
var config = services.GetBoundaryConfiguration<CatalogBoundary>();

// Get all registered boundary configurations (for validation/introspection)
var all = services.GetAllBoundaryConfigurations();
```

---

## Real-World Example: Showcase

The Showcase application demonstrates a multi-boundary architecture:

```
Showcase.Catalog/          [CatalogBoundary]
  Amenities/Mutations/       CreateAmenityMutation, UpdateAmenityMutation, DeleteAmenityMutation
  Properties/Mutations/      CreatePropertyMutation, UpdatePropertyMutation, DeletePropertyMutation, RestorePropertyMutation
  RoomTypes/Mutations/       CreateRoomTypeMutation, UpdateRoomTypeMutation
  CancellationPolicies/     CreateCancellationPolicyMutation, UpdateCancellationPolicyMutation

Showcase.Booking/          [BookingBoundary]  [ReadAccess<Property>] [ReadAccess<RoomType>]
  Reservations/Actions/      CreateReservationAction
  Reservations/Mutations/    ConfirmReservationMutation, CheckInGuestMutation, CancelReservationMutation
  Guests/Actions/            SetGuestPreferencesAction
  Guests/Mutations/          CreateGuestMutation, UpdateGuestMutation

Showcase.Billing/          [BillingBoundary]
  Actions/                   MarkInvoicePaidAction, RefundInvoiceAction
  Mutations/                 CreateDraftInvoiceMutation
```

Key patterns demonstrated:
- **Namespace capture**: All types under `Showcase.Catalog.*` belong to `CatalogBoundary`
- **Cross-boundary read**: `BookingBoundary` declares `[ReadAccess<Property>]` to JOIN with catalog entities
- **Internal mutation**: `CreateDraftInvoiceMutation` has no `[Endpoint]` -- it is invoked programmatically by event handlers
- **State machine**: `CheckInGuestMutation` uses `ApplyAsync` for controlled state transitions
- **Full CRUD**: Amenities show Create/Update/Delete with permissions and endpoints
- **Soft-delete + Restore**: Properties show Delete and Restore mutations
