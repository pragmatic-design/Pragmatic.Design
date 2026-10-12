---
name: pragmatic-use-actions-endpoints
description: Use when implementing a CRUD or business operation or exposing it over HTTP (Mutation, DomainAction, [Endpoint], validation and authorization on an action, with Pragmatic.Actions and Pragmatic.Endpoints).
---

# Pragmatic Use Actions And Endpoints

**Covers:** Implement domain operations (Mutation, DomainAction) and expose them as HTTP APIs with Pragmatic.Actions + Pragmatic.Endpoints consumed from NuGet. Attribute-first pattern driven by the source generator.

`Pragmatic.Actions` models business behaviour; `Pragmatic.Endpoints` exposes it over HTTP. Both are **attribute-driven, code-generation**: you decorate a class, the unified source generator produces the invoker (DI + pipeline) and the ASP.NET mapping. The consumer writes no manual handlers.

## When to use

- You are implementing a write/command (`Mutation`) or an orchestration (`DomainAction`).
- You are exposing an operation or query over HTTP with `[Endpoint]`.
- You are versioning APIs, adding processors, rate limiting, or mapping errors.

For entities/queries/repositories: `pragmatic-use-persistence`. For DI/host: `pragmatic-use-composition`. For permissions on actions: `pragmatic-use-authorization`.

## Packages

```xml
<PackageReference Include="Pragmatic.Actions" Version="1.0.0-alpha.2" />
<PackageReference Include="Pragmatic.Endpoints" Version="1.0.0-alpha.2" />
<PackageReference Include="Pragmatic.Result" Version="1.0.0-alpha.2" />
<PackageReference Include="Pragmatic.Validation" Version="1.0.0-alpha.2" />        <!-- if you validate input -->
<PackageReference Include="Pragmatic.SourceGenerator" Version="1.0.0-alpha.2">
  <IncludeAssets>analyzers; build; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

Dense API surface with all signatures: **`../pragmatic-ecosystem/references/api-surface/actions-endpoints.md`**.
Cross-cutting patterns catalogue: **`../pragmatic-ecosystem/references/patterns-map.md`** (P05–P08).

## Mental model

```
   You decorate       Source generator produces      You / client consumes
  [Mutation]     →    {Type}.MutationInvoker.g.cs  →  dispatcher resolves the invoker
  [DomainAction] →    {Type}.Invoker.g.cs          →  IDomainActionInvoker<T> in DI
  [Endpoint]     →    {Type}.Endpoint.g.cs         →  ASP.NET route mounted
  private field  →    {Type}.SetDependencies.g.cs  →  dependencies resolved from DI
```

## The three operation forms

| Form | Base class | Purpose | Persists? |
|---|---|---|---|
| **Mutation** | `Mutation<TEntity>` | CRUD on a single entity | Yes, automatic `SaveChanges` |
| **DomainAction** | `DomainAction<TReturn>` | Orchestration with multiple dependencies, returns a value | Yes if inside the boundary |
| **VoidDomainAction** | `VoidDomainAction` | Orchestration with no return value | Yes if inside the boundary |

Put **business behaviour** in Actions/Mutations, never in generated handlers.

## Core patterns

### 1. Boundary

Mark the domain library (namespace `Pragmatic.Actions.Attributes`):

```csharp
[Boundary] public partial class SalesBoundary;
```

All code under the boundary's namespace flows into it; sub-folders (`Orders/`, `Customers/`) create inferred sub-boundaries: the SG generates separate action interfaces (`ISalesOrdersActions`) composed into `ISalesActions`.

**Those interfaces are how another boundary calls this one. Do not hand-write a contract beside them.**

```csharp
public partial class WriteStoryAction : DomainAction<WriteStoryResult>
{
    private IKnowledgeActions _knowledge = null!;        // injected, AddScoped by the generator

    // Items is the sub-boundary; the composed interface exposes each one as a property.
    var r = await _knowledge.Items.IngestText(itemId, text, slot: "", ct);
}
```

What the generator emits per boundary, all registered for you:

| | |
|---|---|
| `I{Boundary}{Sub}Actions` | one method per action/mutation, in **two forms**: taking the action object, and with the parameters flattened |
| `I{Boundary}Actions` | the composed one: sub-boundaries are **properties**, not flattened methods |
| `I{Boundary}InternalActions` | the same, plus the operations kept off the public one. `internal`, so same-assembly only |
| `{Boundary}LocalActions` / `{Boundary}RemoteActions` | in-process today; HTTP the day the boundary is split out, with no change at the call site |

#### What is on the public interface, and what is not

**An operation with no `[Endpoint]` is internal.** Leaving the endpoint out says it is not HTTP
surface, and it almost always means the operation is a step or a helper. It lands on
`I{Boundary}InternalActions` only: reachable from its own assembly, invisible to other modules.

`Internal` has three states, on `[DomainAction]` and on `[Mutation]` alike:

| Written | Meaning |
|---|---|
| nothing | inferred from `[Endpoint]`: routed is surface, unrouted is not |
| `Internal = false` | public anyway: **this is how an endpoint-less operation is offered** |
| `Internal = true` | internal anyway, endpoint or not |

```csharp
// No endpoint, and called from another module on every story written: it has to say it is offered.
[DomainAction(Internal = false)]
[UndoWith<RemoveIngestedText>]
public partial class IngestTextAction : DomainAction<IngestTextResult>;

// A step of a composite. Nothing declared, and nothing needs to be.
[DomainAction]
public partial class ResolveCoveredCandidatesAction : DomainAction<int>;
```

⚠️ **To call an internal operation from your own module, inject `I{Boundary}InternalActions`.** It is
generated for every boundary and is the point of it.

⚠️ **If the operation is in a group, inject the group's twin (`I{Boundary}{Group}InternalActions`)
or reach it as `root.{Group}`.** Both resolve the same object, so it is a question of how short you
want the call to be. A `[MessageHandler]` or a `[Job]` (the two callers that arrive with no principal)
are the ones that usually want this interface.

⚠️ **`[Boundary(Visibility = Internal)]` is a different thing**: it hides the *whole boundary*, and a
boundary hidden that way cannot be consumed remotely either. `Internal` on an operation hides *one*.

### Handing a mutation a row you already hold, and writing across boundaries

Both are in **[references/cross-boundary.md](references/cross-boundary.md)**:

- the internal interface takes a mutation **with the row you already loaded**, so the mutation does not
  read it again (internal interface only, never for a `Create`);
- writing into two boundaries in one invocation is not atomic: **PRAG0424** asks you to record
  `[UndoWith<T>]` or `[AcceptsPartialWrites("why")]`; an undo is best effort, in-request, and never runs
  behind a `[RemoteBoundary<T>]` (**PRAG1689**).

⚠️ Do not hand-write a service (`ITermExtraction`-style) for an operation another boundary calls: it
loses the invoker's permission check, validation, unit of work and audit entry. Inject the generated
`I{Boundary}Actions` (or `I{Boundary}InternalActions` from your own module).

### 2. Mutation

```csharp
using Pragmatic.Actions.Mutation;
using Pragmatic.Endpoints.Attributes;
using System.ComponentModel.DataAnnotations;

[Mutation(Mode = MutationMode.Create)]                 // Create|Update|CreateOrUpdate|Delete|Restore
[Endpoint(HttpVerb.Post, "api/orders")]
public partial class CreateOrderMutation : Mutation<Order>
{
    [Required]  public required Guid CustomerId { get; init; }
    [Range(0.01, double.MaxValue)] public required decimal Total { get; init; }
}
```

- `Mode` can be omitted if it can be inferred from the name prefix (`Create*`→Create, `Update*`→Update, `Delete*`→Delete); otherwise **PRAG0410**.
- `ReturnType`: `Entity` (default: the entity, or its `[ReturnsDto<T>]`), `Id` (the boundary returns the `Guid`, the endpoint `{"id": …}`), `LogicalKey` (a generated `{Mutation}.LogicalKey` record with the `[LogicKey]` parts; PRAG0403 when the entity has none). `[ReturnsDto<T>]` beside `Id` or `LogicalKey` is PRAG0535: the key answers. A Create answering 201 gets a `Location` when a `Single` query answers at its route plus `/{id}`. `[Mutation(Mode = Delete, SoftDelete = true)]` for logical delete.
- The SG generates `ApplyToEntity(entity)` with **auto-mapping** property→`entity.SetXxx(...)`. A mutation property without a corresponding setter on the entity → **PRAG0414**.
- The SG generates `{Type}.MutationInvoker.g.cs` with pipeline: input validation → load/create entity → auto-map → `ApplyAsync` → entity validation → persist → `SaveChanges`.

Custom logic (cross-field rules, domain methods): override `ApplyAsync`; the auto-mapping has **already** been applied before it runs.

A **state transition** is declared, not written: `[TransitionsTo<TState>(target)]` and the generated invoker moves the entity (a mutation's own, or the one `[LoadEntity]` row of an action whose `[StateMachine<TState>]` matches), answering 409 (documented) when the state machine refuses:

```csharp
[Mutation(Mode = MutationMode.Update)]
[Endpoint(HttpVerb.Post, "api/orders/{id}/submit")]
[TransitionsTo<OrderStatus>(OrderStatus.Submitted)]
public partial class SubmitOrderMutation : Mutation<Order>
{
    public required Guid Id { get; init; }
}
```

- `When = TransitionTiming.BeforeBody` (default): moved before the body, which sees the new state; a refusal skips the body. The only one for an action, which builds its response in the body.
- `When = TransitionTiming.AfterBody` (mutations only, **PRAG0467** on an action): moved after a successful body, so the body's own refusals keep their own code.
- `When = TransitionTiming.ByBody`: the body moves it (typically a domain method writing state plus who/when/why), and the invoker throws if a successful body left another state; `IsConditional = true` drops that check for a move made only sometimes.
- ⚠️ Keeping `entity.TransitionTo(target)` in the body with BeforeBody/AfterBody is **PRAG0466** (the second call is refused as target → target, a 409 every time). **PRAG0465**: no entity with that state machine, or several loaded. **PRAG0468**: not an `Update` mutation.

#### How many transactions a shape costs

**The outermost invoker that owns a unit of work commits it, once.** Mutations invoked from an action
of the same boundary, a `[CompositeAction]`, and setters inside one action all cost **one**
`SaveChanges`; a step in another boundary commits on its own. The measured table, and what a
`[CompositeAction]` demands (its own `[RequirePermission]` when exposed, **PRAG0440**; steps as typed
properties, never a collection), are in **[references/transactions.md](references/transactions.md)**.

#### Raising a domain event: `[Raises<TEvent>]`

Declare the event on the operation and the generated invoker builds and dispatches it **after the
commit**. Constructor parameters bind by name to the operation's input properties, and for a mutation
to the entity's as well, with `Id`/`PersistenceId`/`{Entity}Id` all resolving to the entity's id.
`OccurredAt` is filled in.

```csharp
[Mutation(Mode = MutationMode.Update)]
[Raises<ReservationCancelled>]
public partial class CancelReservationMutation : Mutation<Reservation>
{
    public required string Reason { get; init; }   // binds to the event's Reason
}
```

⚠️ **A parameter that matches nothing is passed as `default`** (`Guid.Empty`, or `null`), and the
event is dispatched anyway: the write happened, the handler ran, and only the contents are wrong. That
is `PRAG0433`, and it is almost always a typo in a name.

**Nested operations defer their events.** A mutation invoked from inside an action of the same boundary
does not commit, so it hands its events to the batch the action owns and the action flushes them after
the single commit, all of them, in order. Measured across a boundary: N cancellations in one pass
produce N handler runs, and dropping any one of them fails `DeferredEventsTests`.

⚠️ Inside `[Transactional]` the dispatch is still tied to the **save**, not to the transaction's commit,
so handlers run before the commit lands. The framework says so and logs it; where that matters, the
outbox is the answer.

#### Composing, and saying how it commits: PRAG0428

An action that invokes other operations declares how it commits: `[Transactional]` (one transaction,
each step saves and the next one sees it), `[CommitStrategy(CommitMode.Once)]` or
`[CommitStrategy(CommitMode.PerStep)]`, on the action or once on the boundary. ⚠️ A `[Transactional]`
operation can **run twice** on a transient failure, so its body must have no effect outside its
transaction. The details, **PRAG0431**/**PRAG0432** and `BatchContext`, are in
**[references/transactions.md](references/transactions.md)**.

### 3. DomainAction

```csharp
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;

[DomainAction]
[LoadEntity<Order>(nameof(Id))]                          // loads _order, or answers 404 before Execute
[Endpoint(HttpVerb.Post, "api/orders/{id}/refund")]
public partial class RefundOrderAction : DomainAction<RefundReceipt, NotFoundError>
{
    private IPaymentService _payments = null!;          // dependency: uninitialised private field

    public required Guid Id { get; init; }               // bound from route {id}

    public override async Task<Result<RefundReceipt, IError>> Execute(CancellationToken ct)
    {
        // _order is generated by [LoadEntity] and is never null here; changes to it are saved by the invoker.
        // ...
    }
}
```

**What does not go in `Execute`**: each has a declared home, and writing it by hand is the mistake the
examples kept repeating:

| Instead of, in `Execute` | Declare |
|---|---|
| `var x = await repo.GetByIdAsync(Id); if (x is null) return NotFoundError…` | `[LoadEntity<T>(nameof(Id))]` → field `_x`, 404 by the invoker, after validation and authorization (no 404-vs-403 probing), past a `[WithoutFilter<T>]` the operation declares. The key is the entity's key type (**PRAG0411**) |
| a second query for a navigation of a preloaded entity (`FindMembersOfAsync(teamId)` beside `[LoadEntity<Team>]`) | `[LoadEntity<Team>(nameof(Id), Include = "Members")]`: dotted paths, comma-separated; the key may live inside the request (`[LoadEntity<RoomType>("Request.RoomTypeId")]`, every segment a property);
without it `_team.Members` is **empty** (no lazy loading); a path that names no navigation is **PRAG0453**, as is an `[EagerLoad]` path of a mutation (**PRAG0736** on a query), checked at build time and not emitted |
| `var rows = await repo.FindAsync(Spec.Where(x => ids.Contains(x.PersistenceId)))` and a comparison for the missing ones | `[LoadEntities<T>(nameof(Ids))]` on an `IReadOnlyList<Guid>`/array/list → field `IReadOnlyList<T> _ts` (plural; `FieldName` overrides), **one** query, rows in the order of the keys, duplicates once; the missing keys are **one** 404 naming them all (`NotFoundError.ForAll`); empty list → empty field, no query. Not a collection of the key type → **PRAG0411** |
| `var x = await repo.FirstOrDefaultAsync(Specs.ActiveWithNumber(Number)); if (x is null) return NotFound…` / `var xs = await repo.FindAsync(Specs.PendingOf(Id))` | `[LoadEntity<T>(Specification = nameof(TSpecifications.ActiveWithNumber))]` / `[LoadEntities<T>(Specification = …)]`: the rule's parameters bind **by name** to the operation's properties (`PendingOf(Guid id)` ← `Id`); single: none is 404; list: none is `[]`, or 404 with `RequireAny = true`. Not a specification of the entity **PRAG0454**, unbound parameter **PRAG0455**, key and rule together **PRAG0456** |
| `var x = await repo.GetByNumberAsync(Number, ct); if (x is null) return NotFoundError…`, a row named by its domain key (a number, a code, a slug) | `[LoadEntity<T>(nameof(Number), By = nameof(T.Number))]`: read through the generated logic-key lookup (`{Entity}Specifications.GetBy{Key}Async`), same filters and tracking as by id; 404 carries the value. `By` not the entity's single-part `[LogicKey]` (or beside a `Specification`) **PRAG0460**, property of another type **PRAG0461** |
| two `[LoadEntity]` of one entity, or two round trips for one table | give each a distinct `FieldName` (`[LoadEntity<Employee>(nameof(ManagerId), FieldName = "_manager")]`, `[LoadEntity<Employee>(nameof(DeputyId), FieldName = "_deputy")]`): read in **one** `WHERE Id IN (…)`, each field from it, 404 per missing key; different entities stay separate queries |
| the row only has to exist (a foreign key in the body of a create): `[LoadEntity<T>]` for a row nobody reads, or the database's FK violation answering | `[RequireExists<T>(nameof(TeamId))]`: `ExistsAsync` before the body (same filters, an `EXISTS`, nothing loaded, no field), 404 naming the key; a null nullable key is not checked; on a mutation the key is still written. Key of another type **PRAG0411**; beside a `[LoadEntity]` of the same key **PRAG0462** (only the load runs) |
| `var r = await _boundary.Group.GetX(new GetXQuery { A = A }, ct); if (r.IsFailure) return …; var data = r.Value;`, a declared query's answer inside an operation | `[LoadFrom<GetXQuery>] private IReadOnlyList<XDto> Data { get; set; } = [];`: the query's inputs bind **by name** from the operation's properties (a private computed one is fine: `private int Year => From.Year;`); run by the **query's own invoker** (its permission asked of the caller; the internal facade skips it); its failure is the operation's; the property is no input. Not the answer type **PRAG0458**, unbound required input **PRAG0459** |
| an operation that hands back what it loaded and should answer only a caller who may read those rows | `RequireReadPermission = true` on the load: the entity's CRUD `Read` permission, checked **before** the read (401/403); an entity with no known read permission is **PRAG0457** |
| `if (ManagerId is { } id) { var m = await repo.GetByIdAsync(id); … }`, to load it only if given | `[LoadEntity<T>(nameof(ManagerId))]` on a `Guid? ManagerId`: field `T?`, nothing read when null, 404 when it names no row |
| `if (To < From) return ValidationError.For(…)`, the shape of the input | validation attributes on the property (`[GreaterThanOrEqualProperty(nameof(From))]`, `[Range]`, a custom `ValidationAttribute`), message via `MessageKey = TKeys.…` |
| a rule on an entity the operation loads (`[LoadEntity]`, `[LoadCurrentUser]`), such as "hours only on a kind counted in hours" | `private ValidationError ValidateLoaded()` (or `Task<ValidationError> ValidateLoadedAsync(CancellationToken)`) on the operation: the invoker calls it right after the preload, before `Execute`, and a failure is the 422 with its fields. Not a validator that reads the row a second time. Another signature with that name is **PRAG0452** |
| a rule that needs a lookup of something the operation does not load (a uniqueness) | a `[Validator]` class implementing `IAsyncValidator<TOperation>` (same assembly) |
| `repo.FindAsync(Spec<T>.Where(x => …))` inline | a named specification in the entity's generated `static partial class {Entity}Specifications` (a file next to the entity, entity namespace); the generator gives `repo.Find{Name}Async(…)` |
| `Ensure.ThrowIfNull(found)` for a missing row | nothing: `Ensure.ThrowIf*` is for a broken invariant (a bug → exception → 500), never for a 404 or a 422 |
| `private IClock _clock = null!;` only to read `_clock.UtcNow` / today | `[FromClock] public DateTimeOffset Now { get; private set; }` (or `DateOnly Today` → `IClock.UtcToday`), written by the invoker before `Execute`/`ApplyAsync` from the registered `IClock`, so a test that pins it pins this too |
| `private EmployeeResolver _currentEmployee` + `ResolveAsync` + `if (employee is null) return NotFoundError…` | `[LoadCurrentUser]` → field `_current{User}` (`_currentEmployee`), filled by the invoker through the generated resolver after authorization: 401 for nobody signed in, 404 for an account with no user entity (**PRAG0451** when the module has no `[PragmaticUser]`) |
| `ICurrentUser` injected only to read `.Id` | `[FromCurrentUser] public string CallerId { get; private set; }` (401 when not authenticated), or `[FromCurrentUser(nameof(Employee.Id))]` for a member of the `[PragmaticUser]` entity (404 when the caller has none) |

What stays in `Execute`: what only the domain can answer, its typed errors (overlap, overdraft,
state), and the writes. The signed-in user's entity is `[LoadCurrentUser]`; one member of it, as a
property, is `[FromCurrentUser(nameof(Employee.Id))]`.

`[FromClock]` / `[FromCurrentUser]` on an action or a mutation mean what they mean on a query: the
property is `{ get; private set; }` (**PRAG0730**/**PRAG0734** otherwise), it is never a parameter
(body, route, OpenAPI, boundary overload), and the invoker writes it after validation and authorization,
so a validation attribute on it sees `default`. On a mutation it is also written to the entity when the
entity has a member of the same name (`[FromClock] DateOnly DecidedOn` → `entity.DecidedOn`); without one
it is simply the body's to read, and no `PRAG0414` is raised.

- **Dependency injection**: ONLY via uninitialised private fields (`= null!`). The SG detects them and generates `SetDependencies` + injects them in the `Invoker`. There is **no** `[Inject]` or `[FromServices]` for Action/Mutation.
- `DomainAction<TReturn>` has typed-error variants `DomainAction<TReturn, TError1...TError6>`. `Execute` always returns `Result<TReturn, IError>`.
- `VoidDomainAction` exposes the `Success` shorthand. Variants `VoidDomainAction<TError1...>`.
- Generates `{Type}.Invoker.g.cs` (`IDomainActionInvoker<TAction,TReturn>` in DI) and, if there are dependencies, `{Type}.SetDependencies.g.cs`.

### 4. Validation

`System.ComponentModel.DataAnnotations` attributes + those from `Pragmatic.Validation` on action properties are validated **sync** by default. For **async** validation (e.g. DB lookup) declare a `[Validator]` class implementing `IAsyncValidator<T>` for the action or mutation, in the same assembly: that is the whole opt-in, and the generator wires the call (see `pragmatic-use-foundation`). `[Validate]` only changes the default (`Async = false`, `AsyncOnly`). `[NoValidation]` skips everything, on an action and on a mutation.

### 5. Endpoint and binding

An `Endpoint<TResponse>` is the whole operation for a plain read: no action, no mutation. Same
dependency rule as an action (uninitialised private fields, injected by the generator), and you
override `HandleAsync`.

```csharp
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Endpoints.Base;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result;
using Pragmatic.Result.Http;

[Endpoint(HttpVerb.Get, "api/orders/{id}")]
public partial class GetOrderEndpoint : Endpoint<OrderDto>
{
    private IReadRepository<Order> _orders = null!;   // dependency: uninitialised private field

    [FromRoute] public required Guid Id { get; init; }

    public override async Task<Result<OrderDto>> HandleAsync(CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(Id, ct).ConfigureAwait(false);
        if (order is null)
            return Result<OrderDto>.Failure(NotFoundError.For<Guid>("Order", Id));

        return OrderDto.FromEntity(order);
    }
}
```

⚠️ The example shows the mechanics, not the case. One row by id, as a DTO, **is** a declared query,
`[Query<Order, OrderDto>(Single = true)]` with `[Filter(MapTo = "PersistenceId")] public Guid Id`, which
answers 404 when nothing matches and gets the projection, the permission and the contract without a line
of `HandleAsync` (`pragmatic-use-persistence`). Reach for `Endpoint<T>` when no query can say what is read.

`HandleAsync` is `abstract`, so the compiler asks for it. Typed-error variants exist up to six:
`Endpoint<TResponse, TError1>` … `Endpoint<TResponse, TError1..TError6>`, whose `HandleAsync` returns
the matching `Result<TResponse, TError1, …>`.

**Which of the four to reach for.** Getting this wrong is easy and shows up only when somebody reads
the code:

| | use it for |
|---|---|
| `Endpoint<T>` | reading and answering something no query can declare. |
| `[Query]` (Persistence) | filtering, sorting, paging a list, and a get-by-id, which is a query with `Single = true` (404 when nothing matches); see `pragmatic-use-persistence`. |
| `[Mutation]` | changing one entity, through validate → load/create → apply → persist → events. |
| `[DomainAction]` | an orchestrated operation that is none of the above: several entities, an external service, a side effect. |

Wrapping a read in a `DomainAction` compiles and works, and it is still wrong: it buys the whole
action pipeline for something that only needed a repository call.

⚠️ **"My …" reads are queries too.** A read filtered by the caller (`GET api/me`, `GET api/me/balances`)
is not a `DomainAction` that looks the caller up by hand and passes the value to a query. Declare
it as a `[Query]` whose property is `[FromCurrentUser]` (or `[FromCurrentUser(nameof(Employee.Id))]`,
a member of the `[PragmaticUser]` entity): the query's invoker fills it after validation and the
permission check, and it is never a request parameter. See
`../pragmatic-use-persistence/references/queries.md` → *Reading by the caller*.

- `HttpVerb`: `Get | Post | Put | Patch | Delete | Head | Options`.
- Property binding: `[FromRoute]`, `[FromQuery]`, `[FromBody]`, `[FromForm]`, `[FromHeader]`, `[FromClaim("sub")]`. Without an attribute → implicit bind to body (**PRAG0512** info).
  ⚠️ **Except on an operation that carries an `IFormFile`**: the request is then `multipart/form-data`,
  there is no JSON body, and every unmarked scalar is bound from the **form** under its property name,
  so `[FromForm]` on the rest is explicit, not load-bearing. A nested object has no form-field shape and
  is reported: **PRAG0552**.
- `[FromClaim]` vs `[FromCurrentUser]`: `[FromClaim]` is binding; the endpoint reads one raw claim into
  a public property, so an in-process caller still sets it. `[FromCurrentUser]` (on a `[Query]`, an action or a mutation) is not
  binding: the endpoint never reads it, the invoker writes it on every door, and the property is
  `{ get; private set; }` (**PRAG0730**). A route placeholder that names it binds nothing (**PRAG0504**).
- A `{id}` placeholder in the route without a corresponding PascalCase property → **PRAG0504**.
- Two endpoints on the same **verb and full route** (group prefix included) → **PRAG0529**, an error.
  Both would be registered and routing could not choose, so the route answers **500 per request** while
  the build stays clean and OpenAPI lists it once.
- `[Endpoint]` applies directly to `Mutation`/`DomainAction` too: the operation is both business logic and endpoint.
- Grouping: `[EndpointGroup("api/admin")]` on a `sealed` marker class declares the group; `[EndpointGroup<AdminGroup>]` beside `[Endpoint]` joins it. A nested group carries both. There is no `Group = typeof(…)`.
- OpenAPI: `[ApiTags(...)]`, `[ApiSummary("...")]`, `[ApiDescription("...")]`, `[AllowAnonymous]`. Versioning: `[ApiVersion("2.0")]`, `[SinceVersion("2.0")]` on properties + the `ExecuteV{n}` (action) / `HandleAsyncV{n}` (endpoint) convention, which needs `Asp.Versioning.Http`.
- Throttling/cache: `[RateLimit(Requests = 100, Window = "1m")]`, `[ResponseCache(Duration = 60)]`.
  ⚠️ `[ResponseCache]` in its default form (`Location.Any`) is ASP.NET's output cache. The generated
  host adds it (`AddOutputCache` and `OutputCacheStep`, after authorization) when a module declares one,
  and it keeps **anonymous answers only**: an authenticated request is never cached, whatever
  `VaryByHeaders` says, and on a route that requires authentication the build says so (`PRAG0554`). For a
  signed-in user's own data use
  `Location = ResponseCacheLocation.Client` (`Cache-Control: private`, kept by the browser); to cache a
  computed answer on the server, `[Cacheable]` on the query or action (`pragmatic-use-caching`).

### 6. Errors → HTTP

Return `Result<T, IError>` with concrete `Error` types. Default mapping to ProblemDetails:

| Error | HTTP |
|---|---|
| `ValidationError` | 422: the request was read and the rules refuse it |
| a body that cannot be read (malformed JSON, a string where a number goes) | 400, before any rule runs |
| `UnauthorizedError` | 401 |
| `ForbiddenError` | 403 |
| `NotFoundError` | 404 |
| `ConflictError` | 409 |
| `BusinessRuleError`, and a rule derived from it (`record WorksiteClosedError : BusinessRuleError`) | 422 |

A custom error answers its own `StatusCode`. For the **documented** contract (OpenAPI, client manifest)
the generator reads, in this order: `[HttpStatus(402)]` on the error or on a base of it, **wherever that
base lives**; then a literal `public override int StatusCode => 402;` on the error or a base in the same
project; then the first base named like a framework HTTP error; then 400. ⚠️ A base in another project is
otherwise read by name only (a property value is not metadata, an attribute argument is), which is why
`[HttpStatus]` is the form that always crosses an assembly boundary. Declaring a status that the type's own
`StatusCode` contradicts is **PRAG0537**: the document would promise one status and the response carry the
other. On the **endpoint** class the same attribute is the success status. Maximum 6 typed error types per
endpoint (**PRAG0503**).

### 7. Processor

`[PreProcessor<T>]` / `[PostProcessor<T>]` on the endpoint for cross-cutting concerns (custom authorisation, audit). The pre-processor can short-circuit with `PreProcessorResult.Fail(error)` or `.NotFound(...)`.

### 8. Exposing package actions

`[ExposeEndpoint<TLoginAction>(HttpVerb.Post, "login")]` goes on the `[Module]` (not on the action). Its usual source is an action folded in by `[UsePackage<T>]`, which carries no `[Endpoint]` of its own; nothing restricts it to those, and a module's own action is mapped on the host root instead of the package prefix; see `pragmatic-use-composition`. `[ExposeEndpoint<TAction, TGroup>]` maps it inside an endpoint group instead (prefix and `ConfigureGroup` options); a `TGroup` that is not an `[EndpointGroup("…")]` is PRAG1682.

### 9. Generated endpoints and the published contract

Three attributes generate a whole endpoint: **`[Autocomplete]`** (search-as-you-type from one
property, gated on a derived read permission: **PRAG1692** on an anonymous host), **`[Sse]`** (a
`text/event-stream` feed) and **`[McpTool]`** (the operation as an MCP tool). `[FromCookie]` and
`[RequireAntiforgery]` complete the binding attributes.

A host that references `Pragmatic.Endpoints.OpenApi` publishes `/openapi/v1.json` in Development
without being asked. Authentication is described in `Program.cs` with `app.DescribeSecurityScheme(…)`
(the JWT entry points already do it). Both in full: **[references/generated-endpoints.md](references/generated-endpoints.md)**.

## Most frequent diagnostics

| ID | Sev | Trigger | Fix |
|---|---|---|---|
| **PRAG0400** | Error | `[DomainAction]`/`[Boundary]` not `partial` | Add `partial` |
| **PRAG0401** | Error | `[DomainAction]` does not inherit `DomainAction<T>`/`VoidDomainAction` | Change base class |
| **PRAG0409** | Error | `[Mutation]` does not inherit `Mutation<TEntity>` | Change base class |
| **PRAG0410** | Error | `Mode` cannot be determined | Add `Mode = ...` or rename with a prefix |
| **PRAG0414** | Warning | Mutation property without `SetXxx()` on the entity | Add setter or `[MapIgnore]` |
| **PRAG0500** | Error | `[Endpoint]` not `partial` | Add `partial` |
| **PRAG0502** | Error | Missing route | Specify the route |
| **PRAG0504** | Warning | Route param without property | Add property or fix the route |
| **PRAG0505** | Error | Duplicate endpoint name | Set a unique `Name` |

Fix Errors before Warnings.

## Troubleshooting

**Endpoint returns 404.** Are `[Boundary]` and `[Module]` present in the library? Is the library referenced by the host? Does `[Endpoint]` have the correct route?

**Dependency is `null` at runtime.** Was it declared as an uninitialised private field (`= null!`)? An initialised field is not injected. Is the type registered in DI?

**Async validation does not fire.** Is the `[Validator]` class in the same assembly as the operation (**PRAG0215** says so when it is not)? Does the operation carry `[Validate(Async = false)]`? Is the validator's `T` the operation type itself, and not its entity or a DTO?

**`Invoker` not resolved.** Is `Pragmatic.SourceGenerator` referenced as an analyzer with `<IncludeAssets>analyzers; build; buildtransitive</IncludeAssets>`?

## Build verification

```powershell
dotnet build                                                # no PRAG = topology OK
dotnet test path\to\App.Tests --no-restore -v minimal
ls obj\Debug\net10.0\generated\Pragmatic.SourceGenerator    # inspect generated invokers/endpoints
```

The `generated` folder exists only when the project sets
`<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>`; without it the generated code lives in the
compilation alone. The folder is not cleaned either: a file left there by an earlier build is not evidence
of what the current one generates.

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Invoicing example application (code
that compiles and that `Invoicing.IntegrationTests` exercises) and kept identical to it by the gate: a
create and an update mutation carrying child mutations, two domain actions with state transitions and
typed errors, a paged and a single query, a file endpoint, and a domain error. Read them for the whole
shape of an operation; the sections above say why each piece is there.
