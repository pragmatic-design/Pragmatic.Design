# Common Mistakes

These are the most common issues developers encounter when using Pragmatic.Actions. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Forgetting `partial` on the Action Class

**Wrong:**

```csharp
[DomainAction]
public class GetInvoiceById : DomainAction<InvoiceDto>
{
    private IReadRepository<Invoice> _invoices = null!;
    public required Guid Id { get; init; }

    public override async Task<Result<InvoiceDto, IError>> Execute(CancellationToken ct)
    {
        // ...
    }
}
```

**Compile result:** `PRAG0400` error -- "Action class 'GetInvoiceById' must be declared as partial".

**Right:**

```csharp
[DomainAction]
public partial class GetInvoiceById : DomainAction<InvoiceDto>
{
    private IReadRepository<Invoice> _invoices = null!;
    public required Guid Id { get; init; }

    public override async Task<Result<InvoiceDto, IError>> Execute(CancellationToken ct)
    {
        // ...
    }
}
```

**Why:** The source generator emits `SetDependencies()` and the nested `Invoker` class into a partial class. Without `partial`, the compiler cannot merge the generated code with your class. This applies to all action types: `[DomainAction]`, `[Mutation]`, and `[Boundary]` classes.

---

## 2. Missing `[DomainAction]` or `[Mutation]` Attribute

**Wrong:**

```csharp
// No attribute -- the SG does not process this class
public partial class CreateReservation : DomainAction<Guid>
{
    private IRepository<Reservation> _reservations = null!;

    public override async Task<Result<Guid, IError>> Execute(CancellationToken ct)
    {
        // ...
    }
}
```

**Compile result:** No errors, no warnings. The class compiles fine, but no `Invoker` is generated and no DI registration exists.

**Runtime result:** Attempting to resolve `IDomainActionInvoker<CreateReservation, Guid>` from DI throws `InvalidOperationException: No service for type 'IDomainActionInvoker<CreateReservation, Guid>'`.

**Right:**

```csharp
[DomainAction]
public partial class CreateReservation : DomainAction<Guid>
{
    // ...
}
```

**Why:** The SG only processes classes decorated with `[DomainAction]` or `[Mutation]`. Without the attribute, the class is just a plain C# class -- no invoker, no pipeline, no DI registration. This is the most common cause of "service not registered" errors at runtime.

---

## 3. Wrong Base Class for the Attribute

**Wrong:**

```csharp
[Mutation(Mode = MutationMode.Create)]
public partial class CreateAmenity : DomainAction<Guid>  // Should be Mutation<Amenity>
{
    public required string Name { get; init; }
}
```

**Compile result:** `PRAG0409` error -- "Mutation class 'CreateAmenity' must inherit from Mutation<TEntity>".

**Right:**

```csharp
[Mutation(Mode = MutationMode.Create)]
public partial class CreateAmenityMutation : Mutation<Amenity>
{
    public required string Name { get; init; }
}
```

**Why:** `[Mutation]` requires the class to inherit from `Mutation<TEntity>`. The SG needs the entity type parameter to generate `ApplyToEntity()`, `LoadEntityAsync()`, and the mutation pipeline. Similarly, `[DomainAction]` requires `DomainAction<T>` or `VoidDomainAction`.

---

## 4. Using Constructor Injection Instead of Field Injection

**Wrong:**

```csharp
[DomainAction]
public partial class GetInvoice : DomainAction<InvoiceDto>
{
    private readonly IReadRepository<Invoice> _invoices;

    // Constructor injection -- the SG does not use this
    public GetInvoice(IReadRepository<Invoice> invoices)
    {
        _invoices = invoices;
    }

    public override async Task<Result<InvoiceDto, IError>> Execute(CancellationToken ct)
    {
        var invoice = await _invoices.GetByIdAsync(Id, ct);
        // ...
    }
}
```

**Compile result:** Compiles, but the SG also generates `SetDependencies()` which sets `_invoices`. You now have two injection paths.

**Runtime result:** The `Invoker` creates the action instance without calling your constructor (it uses a parameterless path). Your constructor never runs. `_invoices` is set only via `SetDependencies()`, which works -- but your constructor is dead code.

**Right:**

```csharp
[DomainAction]
public partial class GetInvoice : DomainAction<InvoiceDto>
{
    private IReadRepository<Invoice> _invoices = null!;  // SG injects via SetDependencies

    public required Guid Id { get; init; }

    public override async Task<Result<InvoiceDto, IError>> Execute(CancellationToken ct)
    {
        var invoice = await _invoices.GetByIdAsync(Id, ct);
        // ...
    }
}
```

**Why:** Action classes are not resolved from DI. They are created by the caller (`new CreateReservation { ... }`) and then the `Invoker` calls `SetDependencies()` to inject the fields. Do not add constructors with dependency parameters -- they are never called by the pipeline. Use `private` fields with `= null!` initializers.

---

## 5. Declaring Dependencies as Public Properties

**Wrong:**

```csharp
[DomainAction]
public partial class CancelOrder : VoidDomainAction
{
    // These look like inputs, not dependencies
    public IRepository<Order> Orders { get; set; } = null!;
    public IClock Clock { get; set; } = null!;

    public required Guid OrderId { get; init; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct)
    {
        var order = await Orders.GetByIdAsync(OrderId, ct);
        // ...
    }
}
```

**Compile result:** it compiles, with no diagnostic. The SG treats public properties as action *inputs*, not as dependencies, so `Orders` and `Clock` are simply never injected.

**Runtime result:** `Orders` and `Clock` are `null` at execution time. `NullReferenceException` in `Execute()`.

**Right:**

```csharp
[DomainAction]
public partial class CancelOrder : VoidDomainAction
{
    private IRepository<Order> _orders = null!;    // dependency
    private IClock _clock = null!;                        // dependency

    public required Guid OrderId { get; init; }           // input

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct)
    {
        var order = await _orders.GetByIdAsync(OrderId, ct);
        // ...
    }
}
```

**Why:** The SG convention is: **private fields** = dependencies (injected via `SetDependencies`), **public properties** = inputs (set by the caller). If you need a service, declare it as a private field. If the caller should provide a value, declare it as a public property.

---

## 6. Mutation Without Mode and No Naming Convention

**Wrong:**

```csharp
[Mutation]
public partial class ProcessAmenity : Mutation<Amenity>
{
    public required string Name { get; init; }
}
```

**Compile result:** `PRAG0410` error -- "Mutation class 'ProcessAmenity': specify [Mutation(Mode = ...)] or use Create/Update prefix in the class name".

**Right (option A -- explicit mode):**

```csharp
[Mutation(Mode = MutationMode.Create)]
public partial class ProcessAmenity : Mutation<Amenity>
{
    public required string Name { get; init; }
}
```

**Right (option B -- naming convention):**

```csharp
[Mutation]
public partial class CreateAmenityMutation : Mutation<Amenity>
{
    public required string Name { get; init; }
}
```

**Why:** The SG must know the operation mode to generate the correct lifecycle (create new entity vs. load existing). It infers from the class name prefix: `Create*` = Create, `Update*` = Update, `Delete*` = Delete, `Restore*` = Restore. If the name does not match, set `Mode` explicitly.

---

## 7. Throwing Exceptions Instead of Returning Errors

**Wrong:**

```csharp
[DomainAction]
public partial class GetInvoice : DomainAction<InvoiceDto>
{
    private IReadRepository<Invoice> _invoices = null!;
    public required Guid Id { get; init; }

    public override async Task<Result<InvoiceDto, IError>> Execute(CancellationToken ct)
    {
        var invoice = await _invoices.GetByIdAsync(Id, ct);
        if (invoice is null)
            throw new KeyNotFoundException($"Invoice {Id} not found");  // BAD

        return invoice.ToDto();
    }
}
```

**Runtime result:** The exception propagates past the filter pipeline. When paired with `[Endpoint]`, it produces a 500 Internal Server Error. The `NotFoundError` that should map to 404 is never used. Telemetry records the invocation as an "exception" rather than a business "failure."

**Right:**

```csharp
[DomainAction]
public partial class GetInvoice : DomainAction<InvoiceDto, NotFoundError>
{
    private IReadRepository<Invoice> _invoices = null!;
    public required Guid Id { get; init; }

    public override async Task<Result<InvoiceDto, IError>> Execute(CancellationToken ct)
    {
        var invoice = await _invoices.GetByIdAsync(Id, ct);
        if (invoice is null)
            return NotFoundError.For<Invoice, Guid>(Id);

        return invoice.ToDto();
    }
}
```

**Why:** The Result pattern is the contract. The pipeline maps typed errors to HTTP status codes (`NotFoundError` = 404, `ConflictError` = 409, etc.) and records them as business failures in telemetry. Exceptions bypass all of this. Reserve exceptions for truly unexpected failures (database connection lost, out of memory), not for expected business conditions.

---

## 8. Forgetting to Register the Invoker in DI

**Wrong:**

```csharp
// Program.cs
builder.Services.AddPragmaticActions();
// Missing: builder.Services.AddMyAppActions();
// Missing: builder.Services.AddMyAppMutations();
```

**Runtime result:** `InvalidOperationException: No service for type 'IDomainActionInvoker<CreateReservation, Guid>'` when the endpoint or boundary tries to resolve the invoker.

**Right (with Composition -- automatic):**

```csharp
// When using PragmaticApp.RunAsync(), registration is automatic
await PragmaticApp.RunAsync(args);
```

**Right (standalone -- manual):**

```csharp
builder.Services.AddPragmaticActions();       // Core pipeline
builder.Services.AddMyAppActions();           // Generated: DomainAction invokers
builder.Services.AddMyAppMutations();         // Generated: Mutation invokers
```

**Why:** `AddPragmaticActions()` registers only the pipeline infrastructure (filters, call context). The generated `AddXxxActions()` and `AddXxxMutations()` methods register the actual invokers. Both are needed. When using `Pragmatic.Composition`, the SG-generated host calls everything automatically.

---

## 9. Accessing Dependencies Before the Pipeline Runs

**Wrong:**

```csharp
[DomainAction]
public partial class ComputePrice : DomainAction<decimal>
{
    private IPricingService _pricing = null!;

    public required Guid ProductId { get; init; }

    // Accessing _pricing in a property initializer -- it's null here!
    public decimal BasePrice => _pricing.GetBasePrice(ProductId);

    public override async Task<Result<decimal, IError>> Execute(CancellationToken ct)
    {
        return BasePrice * 1.1m;
    }
}
```

**Runtime result:** `NullReferenceException` when `BasePrice` is accessed. Dependencies are injected via `SetDependencies()` which runs inside `InvokeAsync()`. Before that, all dependency fields are `null`.

**Right:**

```csharp
[DomainAction]
public partial class ComputePrice : DomainAction<decimal>
{
    private IPricingService _pricing = null!;

    public required Guid ProductId { get; init; }

    public override async Task<Result<decimal, IError>> Execute(CancellationToken ct)
    {
        // Access dependencies only inside Execute()
        var basePrice = _pricing.GetBasePrice(ProductId);
        return basePrice * 1.1m;
    }
}
```

**Why:** The action lifecycle is: (1) caller creates instance and sets input properties, (2) `InvokeAsync` calls `SetDependencies`, (3) pipeline runs, (4) `Execute` is called. Dependencies are `null` until step 2. Only access them inside `Execute()` or `ApplyAsync()`.

---

## 10. Mutation ApplyAsync Returning the Wrong Type

**Wrong:**

```csharp
[Mutation(Mode = MutationMode.Update)]
public partial class ConfirmReservation : Mutation<Reservation, ConflictError>
{
    public required Guid Id { get; init; }

    public override async Task<Result<Reservation, IError>> ApplyAsync(
        Reservation entity, CancellationToken ct)
    {
        entity.Confirm();
        return entity.Id;  // Returns Guid, not Reservation!
    }
}
```

**Compile result:** Compile error -- `Result<Reservation, IError>` expected, but `Guid` is implicitly converted to `Result<Guid, IError>`.

**Right:**

```csharp
[Mutation(Mode = MutationMode.Update)]
public partial class ConfirmReservation : Mutation<Reservation, ConflictError>
{
    public required Guid Id { get; init; }

    public override async Task<Result<Reservation, IError>> ApplyAsync(
        Reservation entity, CancellationToken ct)
    {
        entity.Confirm();
        return entity;  // Return the entity itself
    }
}
```

**Why:** `ApplyAsync` must return `Result<TEntity, IError>` -- the modified entity on success. The pipeline uses this entity for L2 validation, persistence, and event dispatching. The `ReturnType` property on `[Mutation]` controls what the *endpoint* returns to the HTTP caller (Id, LogicalKey, or Entity), but `ApplyAsync` always returns the entity.

---

## 11. Side Effects in Mutation ApplyToEntity

**Wrong:**

```csharp
[Mutation(Mode = MutationMode.Create)]
public partial class CreateInvoice : Mutation<Invoice>
{
    private IEmailService _emailService = null!;  // dependency

    public required decimal Amount { get; init; }
    public required string CustomerEmail { get; init; }

    public override void ApplyToEntity(Invoice entity)
    {
        entity.SetAmount(Amount);

        // Side effect in ApplyToEntity -- runs BEFORE validation!
        _emailService.SendAsync(CustomerEmail, "Invoice created");
    }
}
```

**Runtime result:** The email is sent even if L2 entity validation fails. `ApplyToEntity` runs before entity validation, so side effects here can produce inconsistent state.

**Right:**

```csharp
[Mutation(Mode = MutationMode.Create)]
public partial class CreateInvoice : Mutation<Invoice>
{
    public required decimal Amount { get; init; }
    public required string CustomerEmail { get; init; }

    // Let the SG auto-generate ApplyToEntity for property mapping.
    // Side effects go in domain events, dispatched AFTER persistence:
    // entity.AddDomainEvent(new InvoiceCreatedEvent(...));
}
```

**Why:** `ApplyToEntity` is a pure property mapping step. It runs before L2 entity validation and before persistence. Side effects (emails, external calls, cache writes) should happen after persistence, either via domain events (`IHasDomainEvents`) or in a post-persistence step. Do not override `ApplyToEntity` for side effects.

---

## 12. Using `typeof()` Instead of Generic Attributes

**Wrong:**

```csharp
[BelongsTo(typeof(BookingBoundary))]        // Non-generic version
[LoadEntity(typeof(Reservation), "Id")]     // Non-generic version
public partial class CancelReservation : DomainAction<bool> { }
```

**Compile result:** These non-generic attribute overloads do not exist. Compile error.

**Right:**

```csharp
[BelongsTo<BookingBoundary>]
[LoadEntity<Reservation>(nameof(Id))]
public partial class CancelReservation : DomainAction<bool> { }
```

**Why:** Pragmatic attributes are always generic. `[BelongsTo<T>]`, `[LoadEntity<T>]`, `[RequirePolicy<T>]` all use generic type parameters. This provides compile-time type safety: the compiler verifies the type exists and satisfies constraints. The `typeof()` pattern is not supported.

---

## 13. Exposing a `[CompositeAction]` Without a Permission

**Wrong:**

```csharp
[DomainAction]
[CompositeAction]
[Endpoint(HttpVerb.Post, "api/amenities/pairs")]   // no [RequirePermission]
public partial class CreateAmenityPairAction : VoidDomainAction
{
    // each step declares [RequirePermission(catalog.amenity.create)] of its own
    public required CreateAmenityMutation First { get; init; }
    public required CreateAmenityMutation Second { get; init; }
}
```

**Runtime result:** `204 No Content`, both rows created, for an authenticated caller holding **no
permissions at all**. Each step's own endpoint would have answered `403` to the same caller.

**Right:**

```csharp
[RequirePermission(CatalogPermissions.Amenity.Create)]
public partial class CreateAmenityPairAction : VoidDomainAction { /* ... */ }
```

**Diagnostic:** `PRAG0440`, an error. It fires only when a step actually requires a permission: a composite of steps that require none has nothing to suppress.

**Why:** the composite is the authorization boundary. Its steps run as internal calls, and their own
`[RequirePermission]` is deliberately not re-checked; otherwise every composite would demand the union
of its steps' permissions, which is not what "this operation is allowed" means. The consequence is that
the composite must state the permission itself. `RequireAuthorizationByDefault` still requires the caller to be
*authenticated*, which is why the result was `204` and not `401` before `PRAG0440` refused the build.

---

## 14. A Collection of Mutations as Composite Steps

**Wrong:**

```csharp
[CompositeAction]
public partial class ImportAmenitiesAction : VoidDomainAction
{
    public required List<CreateAmenityMutation> Items { get; init; }
}
```

**Compile result:** builds clean. **Runtime result:** nothing is written, and no diagnostic said so.

**Why:** steps are read one property at a time, and a property whose type is not itself a mutation or
an action is skipped. `List<CreateAmenityMutation>` is a `List<T>`, so it is not a step. `PRAG0427`
(composite with no steps, a warning) fires only when there are *zero* steps overall, so a composite that mixes one
recognised step with a collection is silent about the half it dropped.

**Right:** `[CompositeAction]` composes a fixed, named set of writes. For a variable number of children
arriving in one nested DTO, use `[PartOf<TParent>]` with a `CollectionStrategy` on the parent's mutation.

---

## Quick Reference

| Mistake | Diagnostic / Symptom |
|---------|---------------------|
| Missing `partial` | `PRAG0400` compile error |
| Missing `[DomainAction]` / `[Mutation]` | No invoker generated, DI resolution fails at runtime |
| Wrong base class for attribute | `PRAG0401` / `PRAG0409` compile error |
| Constructor injection | Dead code, fields injected via SetDependencies instead |
| Public property as dependency | Silent: treated as an input, `NullReferenceException` at runtime |
| Mutation mode not determinable | `PRAG0410` compile error |
| Throwing exceptions | 500 instead of Result-mapped status, wrong telemetry |
| Missing invoker DI registration | `InvalidOperationException` at runtime |
| Accessing deps before pipeline | `NullReferenceException` |
| Wrong return type in ApplyAsync | Compile error |
| Side effects in ApplyToEntity | Runs before validation, inconsistent state |
| `typeof()` in attributes | Compile error -- use generic form |
| `[CompositeAction]` exposed with no permission | `PRAG0440` compile error |
| Collection property as composite steps | Silent: the collection is skipped, nothing is written |
