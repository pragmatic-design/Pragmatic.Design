# Cookbook: Modelling a domain

Boundaries, entities, relations and what the generator adds for you. Read this **before** writing the
first entity: the sections are ordered by what costs the most build cycles.

> The single most expensive thing is not a concept: it is **not knowing which namespace an attribute
> lives in**. Section 1 exists for that reason and nothing else.

## 1. Namespaces: the table to copy

```csharp
using Pragmatic.Persistence.Entity;            // [Entity], [Auditable], [SoftDelete], [LogicKey],
                                               // [GeneratedValue], [Relation.*], [BelongsTo<T>], IEntity
using Pragmatic.Persistence.Query.Attributes;  // [Projectable], [Filter], [Sort], [Query<,>]
using Pragmatic.Persistence.StateMachine;      // [StateMachine], [TransitionFrom]
using Pragmatic.Actions.Attributes;            // [Boundary], [DomainAction]
using Pragmatic.Actions.Mutation;              // [Mutation], Mutation<T>, MutationMode
using Pragmatic.Composition.Attributes;        // [Module], [Include<,>], [NeedsStep<T>], [StartupStep]
using Pragmatic.Composition.Abstractions;      // IStartupStep
using Pragmatic.Endpoints;                     // HttpVerb
using Pragmatic.Endpoints.Base;                // Endpoint<T>, VoidEndpoint
using Pragmatic.Endpoints.Attributes;          // [Endpoint], [FromRoute], [FromQuery], [Autocomplete]
using Pragmatic.Authorization;                 // [RequirePermission], IPermissionChecker
using Pragmatic.Result;                        // Result, Error, IError
using Pragmatic.Result.Http;                   // NotFoundError, BusinessRuleError, ConflictError
```

**`[BelongsTo<T>]` comes from `Pragmatic.Persistence.Entity`**, for entities *and* for domain
actions, which both use it to override the namespace-derived boundary. There is one of it, so
importing both that namespace and `Pragmatic.Actions.Attributes` (ordinary on an entity) needs no
qualification.

> Likewise for eager loading: the mutation attribute is `[EagerLoad]`, and
> `[Include<TModule, TDatabase>]` is unambiguously the host topology one.

Every namespace in the block above was checked against the source, not remembered.

## 2. Boundary, module, and where entities must live

```csharp
[Boundary] public partial class SalesBoundary;                    // Pragmatic.Actions.Attributes
[Module(Name = "MyApp.Sales")] public sealed class SalesModule;   // Pragmatic.Composition.Attributes
```

⚠ **Entities go in the `{Boundary}.Entities` namespace**, whatever folder they sit in. The generator
derives the host's database namespace from it; a feature-folder namespace makes the generated schema
types land elsewhere and the host stops compiling.

Sub-folders under the boundary (`Orders/`, `Customers/`) become **sub-boundaries** inferred from the
namespace, and produce separate action interfaces. That is the only thing folders decide.

### Where each kind of file goes, and which namespaces are flat

The namespace follows the folder, with one exception: entities, DTOs and errors declare a **flat**
namespace whatever folder they sit in (`MyApp.Sales.Dtos`, never `MyApp.Sales.Orders.Dtos`):

| what | folder | namespace |
|---|---|---|
| entity | `{Feature}/Order.cs` | `{Boundary}.Entities` |
| action · mutation · query · endpoint | `{Feature}/Actions/` … | `{Boundary}.{Feature}.Actions` … |
| DTO | `{Feature}/Dtos/` | `{Boundary}.Dtos` |
| error | `{Feature}/Errors/` | `{Boundary}.Errors` |
| enum | `Enums/` | `{Boundary}.Enums` |
| domain service | `Infrastructure/Services/` | `{Boundary}.Infrastructure.Services` |

**Operations carry the feature; entities, DTOs and errors are flat.** That is not decoration: the
operation namespace is what the generator reads to infer the sub-boundary, and a DTO or an error that
carried the feature would put two spellings of the same concept in the API surface for no gain.
`Infrastructure` stays in the namespace like any folder, and the inference stops at it: nothing
under it is a group.

## 3. The entity, and what you must write yourself

```csharp
namespace MyApp.Sales.Entities;

[Entity]
[Auditable]
[SoftDelete]
[BelongsTo<SalesBoundary>]
public partial class Order : IEntity          // ⚠ declare the interface
{
    [LogicKey] public string OrderNumber { get; private set; } = "";
    public Guid CustomerId { get; private set; }
    public decimal Total { get; private set; }
}
```

⚠️ **`: IEntity` is not optional.** Without it the generated code fails with `CS0311` ("no
implicit reference conversion"), pointing at generated files rather than at your class. The attribute
takes no type argument (the key is always a `Guid`); the interface is what the generated
infrastructure binds to.

⚠️ **`ITenantEntity` also obliges the host**: without `app.UseMultiTenancy(…)` the host runs the
single-tenant default: every row gets tenant `default`, so nothing is isolated. Choose where the
tenant comes from. See `pragmatic-use-multitenancy`.

⚠️ Implementing a marker interface means implementing **its members too**: `ITenantEntity` requires
`public string TenantId { get; set; }` written by hand, with a **public setter**; the interceptor
assigns it. "Managed for you" describes the value, not the property (`CS0535` otherwise).

## 4. What the generator adds: do not write these

This is where a newcomer loses the most build cycles: rewriting a member that already exists.

| you get | shape | rewriting it gives |
|---|---|---|
| factory | `Order.Create(orderNumber, customerId, total)` | `CS1729` |
| setters | `SetOrderNumber(...)`, one per `private set` property | `CS0111` |
| identity | `Id`, `PersistenceId` (equality stays reference-based) | `CS0111` |
| permissions | `SalesPermissions.Order.Read/Create/Update/Delete` | `CS0102` |
| repository | `IRepository<Order>`; `GetByOrderNumberAsync` from `[LogicKey]` on the concrete `Order.Repository` | n/a |
| audit / soft delete | `CreatedAt`, `UpdatedBy`, `IsDeleted`… | `CS0111` |

Two rules that surprise people:

- **`Create(...)` takes fewer parameters than you expect.** A property becomes one only when it is
  **non-nullable, has no initializer, and is not a foreign key**, and never for `Id`/`PersistenceId`
  or the audit, soft-delete and concurrency members. So `public string Notes { get; private set; } = "";`
  is out (initializer), `public string? Note` is out (nullable), and `WorksiteId` is out (foreign key).
  Set those afterwards with the generated `SetNotes(...)`. Guessing the arity gives `CS1729`.
- **In hand-written LINQ use `PersistenceId`, not `Id`.** `Id` is a generated convenience alias with no
  column behind it, and EF fails at runtime with *"Translation of member 'Id' … failed"*: a 500, not
  a compile error. In `[Filter]` say `MapTo = "PersistenceId"`.

Inspect what was generated for a type with `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>`;
the files land under `obj/…/generated/`.

## 5. Relations

Declare them on the entity that owns the reference. The generator creates the navigation and the
foreign key; you consume the navigation in queries and projections.

The foreign key is an ordinary property, so a DTO may carry `CustomerId` and a mutation may set it;
you do not have to reach through the navigation, and you must **not** declare the property yourself
(the generator already writes it, and a second declaration collides).

```csharp
[Entity]
[Relation.ManyToOne<Customer>]                                   // Order.Customer + CustomerId
[Relation.OneToMany<OrderLine>]                                  // Order.OrderLines
[Relation.ManyToMany<Tag>.WithNavigation("Tags", Inverse = "Orders")]
public partial class Order : IEntity { ... }
```

What it buys you (this is the part worth the attribute):

```csharp
[Filter(MapTo = "Customer.Phone")] public string? Phone { get; init; }   // filters through the nav,
                                                                          // one SQL query
```

Two relations to the same type? `WithNavigation("UniqueName")` tells them apart.

**Many-to-many with data on the relation is not `[Relation.ManyToMany<>]`**: it is an entity of its
own. If the link carries an amount, a date, or who decided it, model it:

```csharp
[Entity]
[Relation.ManyToOne<Worksite>]
[Relation.ManyToOne<Supplier>]
public partial class Assignment : IEntity       // the link IS the domain object
{
    public decimal Amount { get; private set; }
    public DateOnly AgreedOn { get; private set; }
}
```

Use `[Relation.ManyToMany<>]` only for a bare association: "this supplier is accredited on this
site", nothing more to say about it.

### Across boundaries: foreign key, and a contract

A relation declared across boundaries generates the foreign key only: the two entities live in
different `DbContext`s, so there is no navigation to include. (A boundary that declares
`[ReadAccess<T>]` on the target reads it in its own context, and then the navigation is generated.)

**To *do* something in the other boundary, use the interface the generator already emits**:
`I{Boundary}Actions`, registered for you, with a `Local` implementation now and a `Remote` one if the
boundary is ever split out. Hand-writing a service for that duplicates it and loses the invoker
(permission check, validation, unit of work, audit). See `pragmatic-use-actions-endpoints`.

**To *read* data it owns, the owning boundary publishes a query.** `[Published]` beside `[Query<,>]`
makes the generator write `I{Module}Reads` (the contract, its implementation and its registration),
and the other boundary injects that contract, never the entities:

```csharp
// in MyApp.Suppliers: the owner declares what others may read
[Query<Supplier, SupplierContactDto>]
[Published]                                          // → ISuppliersReads.GetSupplierContacts(...)
public partial class GetSupplierContactsQuery
{
    [Filter(Operator = FilterOperator.In, MapTo = "PersistenceId")]
    public required List<Guid> SupplierIds { get; init; }   // the whole set at once: a list screen
                                                            // looping one id at a time is an N+1
}

// in MyApp.Worksites: depends on the contract, never on the other boundary's entities
public partial class Assignment : IEntity
{
    public Guid SupplierId { get; private set; }     // no navigation, and that is correct
}
```

The contract lands in `{App}.{Module}.Contracts` and is named from the namespace's second segment
(`MyApp.Suppliers` → `ISuppliersReads`) and the query minus `Query`; `ContractName` and `MethodName`
on `[Published]` rename them.

⚠️ Flattening through a cross-boundary relation (`[MapProperty("Supplier.Name")]`) is **PRAG0334**:
the navigation does not exist (unless `[ReadAccess<T>]`). And do not generalise the foreign-key style back into the same
boundary: inside one boundary the navigation is what makes a nested read a single query.

## 6. Domain errors: a type, not a string

`BusinessRuleError.Create("some-rule", "…")` is available for one-offs, but a rule that matters is a
type. `Error` is a public abstract record:

```csharp
public sealed record ExpiredQualificationError(string Company, string WorkType, DateOnly ExpiredOn) : Error
{
    public override string Code => "EXPIRED_QUALIFICATION";
    public override int StatusCode => 422;
    public override string Title => "Qualification expired";
    public override string MessageKey => "worksites.assignment.expired-qualification";
    public override IReadOnlyDictionary<string, object>? Parameters =>
        new Dictionary<string, object>
        {
            ["company"] = Company, ["workType"] = WorkType, ["expiredOn"] = ExpiredOn,
        };
}
```

The type is the rule's identity, the constructor carries the facts, and `Parameters` is what makes
the message translatable. **Interpolating the text into `Details` hardcodes one language** and
bypasses the resolver, which reads `MessageKey` verbatim, with no `error.` prefix added.

**Then declare it on the action**, or nothing downstream knows it exists:

```csharp
public partial class CreateAssignmentAction
    : DomainAction<Guid, ExpiredQualificationError, WorksiteClosedError>
```

### A business rule that repeats is a type

`BusinessRuleError.Create("worksite-closed", …)` raised from three places writes the string three
times and still produces one type, so a caller cannot tell it from any other 422 and the endpoint
contract cannot name it. Derive instead: the family, the code, the 422 and the `rule` field on the
wire all come with it:

```csharp
public sealed record WorksiteClosedError : BusinessRuleError
{
    public WorksiteClosedError(string address, DateOnly closedOn) : base("worksite-closed")
    {
        Parameters = new Dictionary<string, object> { ["address"] = address, ["closedOn"] = closedOn };
    }
}
```

The rule is a constructor argument, not a property you may forget, and it appears **once**. `is
BusinessRuleError` still matches, and **PRAG1804** checks that `worksite-closed` exists in the
translations.

`Create(...)` stays for the genuinely one-off case: a rule raised in a single place that nobody
outside has to name.

## 7. Checklist before the first build

- Entities in `{Boundary}.Entities`, each with `[Entity]` **and** `: IEntity`.
- `[BelongsTo<T>]` from `Pragmatic.Persistence.Entity`, the only one there is.
- No hand-written `Create`, `SetXxx`, `Id`, or permission constants.
- Relations inside the boundary; across it, a foreign key plus a `[Published]` query of the owner.
- A link that carries data is an entity, not a `ManyToMany`.
- Rules as error types; `MessageKey` matching a real translation key.
