# API Surface: Pragmatic.Actions + Pragmatic.Endpoints

Attribute-first reference for consumers. Each entry follows the triple: **decorate → SG generates → consume**.
Complete diagnostics: `../diagnostics.md` (PRAG0400-0499 Actions, PRAG0500-0599 Endpoints).

## Namespaces

| Namespace | Content |
|---|---|
| `Pragmatic.Actions.Attributes` | `[Boundary]`, `[DomainAction]`, `[SubBoundary]`, `[Validate]`, `[NoValidation]`, `[LoadEntity<T>]`, `[LoadEntities<T>]`, `[LoadFrom<TQuery>]`, `[LoadCurrentUser]`, `[CompositeAction]`, `[StartsDelegation]`, `[Transactional]`, `[CommitStrategy]`, `[UndoWith<T>]`, `[CompensableStep]`, `[AcceptsPartialWrites]` |
| `Pragmatic.Actions.Commit` | `CommitMode.Once`, `CommitMode.PerStep` |
| `Pragmatic.Actions.Mutation` | `[Mutation]`, `MutationMode`, `MutationReturnType`, `Mutation<TEntity>`, `[Include]` |
| `Pragmatic.Actions.Abstractions` | `DomainAction<TReturn>`, `VoidDomainAction`, interfaces `IExecutable<T>` |
| `Pragmatic.Endpoints.Attributes` | `[Endpoint]`, `[ExposeEndpoint<T>]`, `[EndpointGroup]`, binding, processor, OpenAPI |
| `Pragmatic.Endpoints` | `HttpVerb` |
| `Pragmatic.Endpoints.Base` | `Endpoint<TResponse>`, `VoidEndpoint` (+ variants up to 6 error types) |

---

# Actions

## `[Boundary]`

- **Target**: `class`; must be `partial` and in a namespace (PRAG0406/0407).
- **Properties**: `Name` (`string?`), `Visibility` (`BoundaryVisibility`: `Public` default | `Internal`).
- **Generates**: `I{Boundary}Actions` (public interface of all `InvokeAsync`) + sub-interfaces per sub-boundary, composed as properties.

## `[Mutation]`

- **Target**: `partial` `class` inheriting `Mutation<TEntity>` (PRAG0409).
- **Properties**:
  - `Mode` (`MutationMode`): `Create | Update | CreateOrUpdate | Delete | Restore`. If omitted, inferred from the class name prefix (`Create*`→Create…); not inferable → PRAG0410.
  - `ReturnType` (`MutationReturnType`): `Entity` (default) | `Id` (boundary returns `Guid`, endpoint `{"id": …}`) | `LogicalKey` (generated `{Mutation}.LogicalKey` record; PRAG0403 without a `[LogicKey]`).
  - `SoftDelete` (`bool`): for `Mode = Delete`, performs a logical delete.
- **Generates**:
  - `{Type}.ApplyToEntity.g.cs`: `override void ApplyToEntity(TEntity)` with auto-mapping property→`entity.SetXxx(...)`.
  - `{Type}.MutationInvoker.g.cs`: nested `Invoker : MutationInvoker<TMutation, TEntity>`.
  - `{Type}.SetDependencies.g.cs`: if the mutation has private dependency fields.
- **Invoker pipeline**: input validation → load/create entity → `ApplyToEntity` → `ApplyAsync` (custom override) → entity validation → persist → `SaveChanges` via `IUnitOfWork`.
- `[EagerLoad("Lines.Product")]` (`AllowMultiple`) eager-loads a navigation on the loaded entity. (`[Include<TModule, TDatabase>]` is host topology, not this.)

## `Mutation<TEntity>`

```csharp
public abstract class Mutation<TEntity> where TEntity : class
{
    public virtual void ApplyToEntity(TEntity entity);                              // SG override (auto-map)
    public virtual Task<Result<TEntity, IError>> ApplyAsync(TEntity entity, CancellationToken ct);
}
// Variants: Mutation<TEntity, TError1> ... Mutation<TEntity, TError1..TError6>
```

Override `ApplyAsync` for custom logic (state transitions, cross-field logic): auto-mapping is **already** applied.

## `[DomainAction]`

- **Target**: `partial` `class` inheriting `DomainAction<TReturn>` or `VoidDomainAction` (PRAG0400/0401).
- **Properties**: `Internal` (exclude from `I{Boundary}Actions`), `System` (exclude from boundary + pipeline).
- **Generates**: `{Type}.Invoker.g.cs` (`IDomainActionInvoker<TAction,TReturn>` in DI), `{Type}.SetDependencies.g.cs` if it has dependencies.
- **Pipeline**: inject dependencies → Before filters (authorization, validation) → `PrepareActionAsync` (`[LoadEntity]`: 404 when missing, after the permission check) → `Execute` → `SaveChanges` if within the boundary → After filters.

```csharp
public abstract class DomainAction<TReturn>
{
    public abstract Task<Result<TReturn, IError>> Execute(CancellationToken ct = default);
}
// Variants DomainAction<TReturn, TError1..TError6>
public abstract class VoidDomainAction
{
    protected static VoidResult<IError> Success { get; }
    public abstract Task<VoidResult<IError>> Execute(CancellationToken ct = default);
}
// Variants VoidDomainAction<TError1..TError6>
```

## Dependency injection

**Only** via uninitialized private fields. There is no `[Inject]`/`[FromServices]` for Action/Mutation.

```csharp
private IRepository<Order> _orders = null!;     // SG generates SetDependencies + injects them in the Invoker
```

## Other Actions attributes

| Attribute | Target | Effect |
|---|---|---|
| `[Validate]` | class | Changes the validation default: `Async = false` switches the async half off, `AsyncOnly` skips the sync pass. It does not enable async validation: declaring a `[Validator]` for the operation (or for a property's type) in the same assembly is the opt-in |
| `[NoValidation]` | class | Skips all validation |
| `[LoadEntity<TEntity>("IdProp")]` | class, `AllowMultiple` | Loads entity by id before `Execute` (on a mutation, before `ApplyAsync`, for another entity than its own); 404 when missing; optional `FieldName` |
| `[LoadEntities<TEntity>("IdsProp")]` | class, `AllowMultiple` | Loads the rows a list of ids names, in one query, into an `IReadOnlyList<TEntity>` in the order of the ids; one 404 naming every missing id; optional `FieldName`, `RequireAny` |
| `[LoadFrom<TQuery>]` | property | Fills the property with the declared query's answer before `Execute`, through the query's own invoker (permission included), inputs bound by name; not an input |
| `Specification = nameof(…)` on either load | named arg | Reads by a static `Specification<TEntity>` instead of a key, its parameters bound by name to the operation's properties; `RequireReadPermission = true` asks the entity's read permission first |
| `[CompositeAction]` | class | Composes fixed steps (mutations, actions or void actions) with one atomic commit |
| `[Transactional]` | class | One database transaction around the body or the steps; each step saves, so a later one reads what an earlier wrote |
| `[DomainAction(Internal = …)]` | class | Visibility on the boundary interface. Unset infers it from `[Endpoint]`; `false` exports an endpoint-less operation; `true` hides one that has an endpoint |
| `[Mutation(Internal = …)]` | class | The same three states, on a mutation |
| `[CommitStrategy(CommitMode)]` | class | `Once` or `PerStep`. On a `[Boundary]` it is the policy every composing action of that boundary inherits |
| `[UndoWith<TCompensator>]` | class | Best-effort, in-request undo run when a later step of the same request fails |
| `[CompensableStep]` | method | Emitted on a facade method whose action declares an undo; PRAG0424 stops asking its callers |
| `[AcceptsPartialWrites(reason)]` | class | Records that the leftovers of a cross-boundary failure are harmless, and why |
| `[BelongsTo<TBoundary>]` | class | Chooses the boundary only: the group still comes from the namespace, and an operation outside the boundary's namespace has no group |
| `[SubBoundary(Name, Description)]` | the operation class | Names the group this operation belongs to, instead of the namespace inferring it. `Name` wins over the inference and is not reported as inferred (PRAG0413); empty, or the boundary's own name, is **PRAG0416** and the operation stays where it would have been. `Description` becomes the generated group interface's summary. ⚠️ On the **operation**, not on a marker class of its own |

---

# Endpoints

## `[Endpoint]`

```csharp
public sealed class EndpointAttribute(HttpVerb method, string route) : Attribute
{
    public string? Name { get; set; }      // default = class name; duplicates → PRAG0505
}
```

To join a group, put `[EndpointGroup<TGroup>]` beside `[Endpoint]` (see below). There is no
`Group = typeof(…)`.

- **Target**: `Endpoint<T>`, `VoidEndpoint`, `DomainAction<T>`, `VoidDomainAction`, `Mutation<T>`. The class must be `partial` (PRAG0500), with a route (PRAG0502).
- `HttpVerb`: `Get | Post | Put | Patch | Delete | Head | Options`.
- Route placeholders `{id}` bind by PascalCase name; missing property → PRAG0504.
- **Generates**: `{Type}.Endpoint.g.cs` with `MapEndpoint(IEndpointRouteBuilder)`; the infrastructure generates `MapPragmaticEndpoints()`.

## Property binding

| Attribute | Source |
|---|---|
| `[FromRoute]` | Route parameter |
| `[FromQuery]` | Query string |
| `[FromBody]` | Request body |
| `[FromForm]` | Form data |
| `[FromHeader(Name?)]` | HTTP header |
| `[FromClaim("type", IsRequired = true)]` | Claim from `HttpContext.User`; 401 if missing and required |

Properties without an attribute → implicit bind to body (PRAG0512 info). `[FromClaim]` supported types: `string`, `Guid`, `int`, `long`, `bool`, `DateTimeOffset`.

`[FromCurrentUser]` (`Pragmatic.Identity`, on a `[Query]`, action or mutation property) is **not** in this table because it
is not binding: the endpoint never reads it (not from the query string, the route or the body), and it
is absent from OpenAPI and from the boundary member's arguments. The operation's invoker writes it from
the caller, on every door; `[FromClock]` is the same, from `IClock`. `[FromClaim]` stays a public input an in-process caller can set; a
`[FromCurrentUser]` property is `{ get; private set; }` (PRAG0730). Details:
`api-surface/persistence.md` → `[FromCurrentUser]`.

## `[EndpointGroup]`

```csharp
[EndpointGroup("api/admin", Tag = "Admin")]          // declares the group, on a sealed marker class
public sealed class AdminGroup;

[Endpoint(HttpVerb.Get, "users")]                    // → GET api/admin/users
[EndpointGroup<AdminGroup>]                          // joins it
public partial class ListAdminUsers : Endpoint<…> { … }
```

`EndpointGroupAttribute(string routePrefix)` has `Tag` and `Version`. A nested group carries both
forms: its own `[EndpointGroup("…")]` and `[EndpointGroup<TParent>]`.

## `[ExposeEndpoint<TAction>]`

- **Target**: `[Module]` class (not the action). `AllowMultiple`.
- `ExposeEndpointAttribute<TAction>(HttpVerb method, string route)`; `Name`, `AdditionalPermissions`, `AllowAnonymous`.
- Exposes over HTTP the actions of a package integrated via `[UsePackage<T>]`. Route is relative to the package `RoutePrefix`.
- `ExposeEndpointAttribute<TAction, TGroup>` puts the route inside `TGroup` instead (its prefix and its
  `ConfigureGroup` options), exactly as a member `[Endpoint]` is; the group need not be used by any
  `[Endpoint]`. A `TGroup` that is not an `[EndpointGroup("…")]` the host can resolve is **PRAG1682**.

## Processor

```csharp
[PreProcessor<TProcessor>]      // Order; IEndpointPreProcessor → ValueTask<PreProcessorResult>
[PostProcessor<TProcessor>]     // IEndpointPostProcessor: always runs, even on failure
```

`PreProcessorResult`: `Continue()` | `Fail(IError)` | `NotFound(resourceType, id?)`.

## OpenAPI and cross-cutting

| Attribute | Effect |
|---|---|
| `[ApiTags("Orders")]` | OpenAPI tag |
| `[ApiSummary]` / `[ApiDescription]` | OpenAPI documentation |
| `[AllowAnonymous]` | Override group-level auth |
| `[ApiVersion("2.0")]` | Version; `Deprecated`, `SunsetDate` |
| `[SinceVersion("2.0")]` | On property: body versioning, convention `ExecuteV2`/`V3` |
| `[RateLimit(Requests, Window)]` or `Policy` | Throttling; invalid config (no `Policy` nor `Requests`+`Window`) is a runtime no-op |
| `[ResponseCache(Duration, ...)]` | Response caching |
| `[AllowedContentTypes(...)]` / `[MaxFileSize(bytes)]` | On `IFormFile`: 415 / 413 |
| `[HttpStatus(code)]` | On an `Error` class: overrides HTTP mapping |

## Error → HTTP mapping

`Result<T, IError>` with concrete `Error` types. Defaults: `ValidationError`→422 (a body that cannot be bound →400, before any rule), `UnauthorizedError`→401, `ForbiddenError`→403, `NotFoundError`→404, `ConflictError`→409, `BusinessRuleError`/`IError`→422. Maximum 6 typed error types (PRAG0503). Per-type override: `[HttpStatus(402)]` on the error.

---

# Key diagnostics

| ID | Sev | Cause | Fix |
|---|---|---|---|
| PRAG0400 | Error | `[DomainAction]` not `partial` (a `[Boundary]` not partial is PRAG0406) | `partial` |
| PRAG0401 | Error | `[DomainAction]` wrong base class | Inherit `DomainAction<T>`/`VoidDomainAction` |
| PRAG0409 | Error | `[Mutation]` does not inherit `Mutation<TEntity>` | Change base class |
| PRAG0410 | Error | `Mode` cannot be determined | `Mode = ...` or rename with prefix |
| PRAG0414 | Warning | Mutation property without `SetXxx()` | Add setter on entity or `[MapIgnore]` |
| PRAG0500 | Error | `[Endpoint]` not `partial` | `partial` |
| PRAG0502 | Error | Missing route | Specify the route |
| PRAG0503 | Error | > 6 error types | Reduce to ≤ 6 |
| PRAG0504 | Warning | Route param without property | Add property |
| PRAG0505 | Error | Duplicate endpoint name | Unique `Name` |
| PRAG0507 | Error | Endpoint group not found | The `TGroup` of `[EndpointGroup<TGroup>]` must carry `[EndpointGroup("…")]` |
| PRAG0512 | Info | Property implicitly bound to body | Add `[FromBody]`/`[FromQuery]`/... |
| PRAG0516 | Error | Invalid `[MaxFileSize]` limit | Use a positive byte limit |

---

# Showcase examples

> ⚠️ Paths into the **upstream Pragmatic.Design repository**, not into your project. If you installed these skills from the marketplace you do not have these files, and you do not need them: everything above is self-contained. Reach for them only with that repository open.

| Pattern | File |
|---|---|
| Mutation Create + Endpoint | `examples/showcase/src/Showcase.Booking/Guests/Mutations/CreateGuestMutation.cs` |
| VoidDomainAction + `[FromClaim]` | `examples/showcase/src/Showcase.Billing/Invoices/Actions/MarkInvoicePaidAction.cs` |
| DomainAction multi-dependency + OpenAPI | `examples/showcase/src/Showcase.Billing/Invoices/Actions/RefundInvoiceAction.cs` |
| Action with async validation + versioning | `examples/showcase/src/Showcase.Booking/Reservations/Actions/CreateReservationAction.cs` |
| Endpoint<T> with `[FromRoute]` + Group | `examples/showcase/src/Showcase.Billing/Invoices/Endpoints/GetInvoiceEndpoint.cs` |
| Single-row read by id, in a Group (`[Query(Single = true)]`) | `examples/showcase/src/Showcase.Booking/Guests/Queries/GetGuestQuery.cs` |
