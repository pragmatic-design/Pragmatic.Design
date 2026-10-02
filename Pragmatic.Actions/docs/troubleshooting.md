# Troubleshooting

Practical problem/solution guide for Pragmatic.Actions. Each section covers a common issue, the likely causes, and the fix.

---

## Action Not Found at Runtime (DI Resolution Failure)

You have an action class that compiles, but resolving `IDomainActionInvoker<T, TReturn>` or `IMutationInvoker<T, TEntity>` throws `InvalidOperationException` at runtime.

### Checklist

1. **Does the class have `[DomainAction]` or `[Mutation]`?** Without the attribute, the SG does not generate an invoker. The class compiles fine but has no pipeline integration.

2. **Is the class `partial`?** Diagnostic `PRAG0400` fires if this is missing. Without `partial`, the SG cannot generate the nested `Invoker` class.

3. **Does it inherit from the correct base class?** `[DomainAction]` requires `DomainAction<T>` or `VoidDomainAction`. `[Mutation]` requires `Mutation<TEntity>`. Check for `PRAG0401` or `PRAG0409`.

4. **Is the SG analyzer referenced?** In your `.csproj`, the `Pragmatic.SourceGenerator` must be referenced with `OutputItemType="Analyzer"`:

   ```xml
   <ProjectReference Include="..\Pragmatic.SourceGenerator\...\Pragmatic.SourceGenerator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
   ```

5. **Did you register the generated invokers in DI?**

   With Composition (automatic):
   ```csharp
   await PragmaticApp.RunAsync(args);
   ```

   Without Composition (manual):
   ```csharp
   builder.Services.AddPragmaticActions();    // Core pipeline
   builder.Services.AddMyAppActions();        // Generated DomainAction invokers
   builder.Services.AddMyAppMutations();      // Generated Mutation invokers
   ```

6. **Check the SG output.** In Visual Studio, expand **Dependencies > Analyzers > Pragmatic.SourceGenerator** in Solution Explorer. Look for `{Type}.Invoker.g.cs` and `_Infra.Actions.Registration.g.cs`. If these files do not exist, the SG is not processing your class.

---

## Dependencies Are Null Inside Execute()

Private fields are `null` when `Execute()` or `ApplyAsync()` runs, causing `NullReferenceException`.

### Checklist

1. **Are the fields `private`?** The SG only injects private fields. Public properties are treated as inputs, not dependencies.

2. **Did you remove `readonly`?** Fields declared as `private readonly` may not be settable by the generated `SetDependencies()`. Use `private IMyService _service = null!;` without `readonly`.

3. **Are you accessing the field outside `Execute()`?** Dependencies are injected by `SetDependencies()` which runs inside `InvokeAsync()`. Property initializers, computed properties, and constructors all run before injection.

4. **Is the dependency registered in DI?** If the service is not registered, `SetDependencies()` resolves `null` from the container. Check your DI registrations.

5. **Is the action invoked through the pipeline?** If you call `action.Execute(ct)` directly without going through the invoker, `SetDependencies()` never runs. Always invoke via `IDomainActionInvoker<T>` or `IMutationInvoker<T>`.

---

## Mutation Mode Not Determined

The SG emits `PRAG0410` -- "Mutation mode could not be determined."

### Checklist

1. **Does the class name start with a recognized prefix?** The SG infers mode from the name: `Create*` = Create, `Update*` = Update, `Delete*` = Delete, `Restore*` = Restore.

2. **Set the mode explicitly if the name does not match:**

   ```csharp
   [Mutation(Mode = MutationMode.Update)]
   public partial class ConfirmReservation : Mutation<Reservation> { }
   ```

3. **For `CreateOrUpdate` (upsert):** This mode is not inferred from naming. Always set it explicitly:

   ```csharp
   [Mutation(Mode = MutationMode.CreateOrUpdate)]
   public partial class UpsertProduct : Mutation<Product> { }
   ```

---

## Validation Not Running

The action has `[Validate]` or validation attributes on properties, but invalid input is not rejected.

### Checklist

1. **For DomainActions -- is `[Validate]` present?** The `ValidationFilter` only runs async validation when `[Validate]` is on the class. Sync validation (from attributes) runs by default.

   ```csharp
   [DomainAction]
   [Validate]  // Required for async validation
   public partial class CreateReservation : DomainAction<Guid> { }
   ```

2. **Is `Pragmatic.Validation` referenced?** The validation SG generates `ISyncValidator` implementations only when the package is in the project.

3. **For Mutations -- validation is built-in.** L1 validation runs automatically (no `[Validate]` needed). Check that the mutation class implements `ISyncValidator` (generated from validation attributes on properties).

4. **Is the `ValidationFilter` enabled?** Check that `EnableValidationFilter` is not disabled in options:

   ```csharp
   services.AddPragmaticActions(options =>
   {
       options.EnableValidationFilter = true;  // default
   });
   ```

5. **Is `[NoValidation]` present?** This attribute explicitly disables all validation:

   ```csharp
   [DomainAction]
   [NoValidation]  // Skips all validation
   public partial class InternalSync : DomainAction<bool> { }
   ```

---

## Authorization Returns 403 on Internal Calls

An action calls another action within the same boundary, but the inner action fails with `ForbiddenError`.

### Checklist

1. **Are you calling through the internal interface?** Only the generated `I{Boundary}InternalActions` runs the call as an internal one. The public `I{Boundary}Actions` enforces the permission of the operation it invokes — it is the contract another module injects — and so does a direct invoker call:

   ```csharp
   // Correct: I{Boundary}InternalActions (same assembly) — the internal call skips the callee's permission
   await _booking.ConfirmReservation(mutation, ct);        // _booking is IBookingInternalActions

   // Refused with 403 if the caller lacks the callee's permission
   await _bookingActions.ConfirmReservation(mutation, ct); // IBookingActions: the callee's permission is enforced
   await _confirmInvoker.InvokeAsync(mutation, ct);        // direct invoker call: authorization is checked
   ```

   An operation that answers for what it invokes through the public interface declares `[AbsorbsChildPermissions]` instead.

   ⚠️ **If the operation sits in a group, the internal interface is the group's twin.** `I{Boundary}{Group}InternalActions` is what carries it, and `I{Boundary}Actions.{Group}` is the *public* group interface — resolved to the guarded implementation, so calling through it enforces the permission. Either inject the twin, or go `root.{Group}` from `I{Boundary}InternalActions`; both hand back the same unguarded object.

2. **Is `ActionCallContext` registered?** It must be scoped in DI. `AddPragmaticActions()` registers it automatically.

3. **Is the calling action in the same boundary?** Cross-boundary calls are not considered internal. Authorization runs for cross-boundary calls even through boundary interfaces.

---

## Boundary Interface Missing Actions

The generated `I{Boundary}Actions` interface does not include some of your actions.

### Checklist

1. **Is the action in the correct namespace?** Boundaries capture actions by namespace prefix. An action in `MyApp.Catalog.Products.Mutations` is captured by a boundary in `MyApp.Catalog`.

2. **Is the action marked `Internal = true`?** Internal actions are excluded from the boundary interface:

   ```csharp
   [DomainAction(Internal = true)]  // Excluded from boundary interface
   public partial class HelperAction : DomainAction<bool> { }
   ```

3. **Is the action marked `System = true`?** System actions are also excluded:

   ```csharp
   [DomainAction(System = true)]  // Excluded from boundary interface
   public partial class HealthCheck : DomainAction<bool> { }
   ```

4. **Is there a namespace collision?** If a sub-namespace has its own `[Boundary]`, it "subtracts" from the parent. Check that a child boundary is not capturing actions you expect in the parent.

5. **Is the boundary class `partial`?** Diagnostic `PRAG0406` fires if not. The SG cannot generate the interface implementation without `partial`.

---

## Mutation Entity Not Found (404 on Update/Delete)

An update or delete mutation returns `NotFoundError` even though the entity exists in the database.

### Checklist

1. **Is the `Id` property correctly named?** The SG looks for a public property named `Id` on the mutation to use as the entity key. If your property has a different name, ensure the generated `LoadEntityAsync` resolves it.

2. **Are query filters blocking the load?** Soft-delete filters may exclude the entity. For `MutationMode.Restore`, the SG bypasses the soft-delete filter. For `Update` and `Delete`, the entity must not be soft-deleted.

3. **Is the correct boundary/DbContext being used?** If the mutation belongs to boundary A but the entity is in boundary B's database, the load will fail. Check `[BelongsTo<T>]` assignment.

4. **Does the entity have `[Include("navigation")]`?** If the mutation needs navigation properties, add `[Include]` to eagerly load them:

   ```csharp
   [Mutation(Mode = MutationMode.Update)]
   [Include("LineItems")]
   public partial class UpdateInvoice : Mutation<Invoice> { }
   ```

---

## Generated Code Not Compiling

The build fails with PRAG04xx diagnostics from the source generator.

### Diagnostics Reference

| ID | Severity | Cause | Fix |
|----|----------|-------|-----|
| PRAG0400 | Error | Class is not `partial` | Add `partial` keyword to the class declaration |
| PRAG0401 | Error | Missing base class | Inherit from `DomainAction<T>` or `VoidDomainAction` |
| PRAG0403 | Error | `ReturnType = LogicalKey` on an entity without a `[LogicKey]`, or with a part only the relation graph can type | Return `Id` or `Entity`, or declare the key's parts on properties the entity has |
| PRAG0404 | Error | `[LoadEntity]` or `[LoadEntities]` key property not found | Verify the property name in `[LoadEntity<T>(nameof(PropertyName))]` |
| PRAG0405 | Error | `[LoadEntity]` or `[LoadEntities]` key type not determined | Ensure the entity has a recognizable key (Id property or `[Key]`) |
| PRAG0411 | Error | `[LoadEntity]` key property is not of the entity's key type, or a `[LoadEntities]` one not a collection of it | Declare it `Guid`, or `Guid?` for a load that happens only when the key is given; for `[LoadEntities]`, `IReadOnlyList<Guid>`, `Guid[]` or `List<Guid>` |
| PRAG0454 | Error | A load's `Specification` names no static `Specification<TEntity>` of the entity | Name one with `nameof(EntitySpecifications.Member)`, or a member of `{Entity}Specifications`, returning a specification of that entity and accessible from the operation |
| PRAG0455 | Error | A parameter of a load's rule binds no property of the operation | Declare a property of the parameter's name (case ignored) whose type converts to it, or give the parameter a default |
| PRAG0456 | Error | A load names both a key and a `Specification`, or neither | Keep one: the key property, or the rule |
| PRAG0457 | Error | `RequireReadPermission` on an entity whose read permission is not known | The entity's permissions are generated with it by the persistence generator — load an `[Entity]` of this module or of a referenced one |
| PRAG0458 | Error | A `[LoadFrom<TQuery>]` property is not of what the query answers, or `TQuery` is no declared `[Query]` of this compilation | Declare the property as the query's answer: the DTO for `Single = true`, `PagedResult<TDto>` for a paged query, `IReadOnlyList<TDto>` otherwise |
| PRAG0459 | Error | A `required` input of a `[LoadFrom]` query binds no property of the operation | Declare a property of the input's name (case ignored) whose type converts to it — a private computed one is fine: `private int Year => From.Year;` |
| PRAG0460 | Error | `[LoadEntity(By = …)]` names no single-part `[LogicKey]` of the entity, or stands beside a `Specification` | Name the entity's logic key member — `By = nameof(Employee.EmployeeNumber)` — beside the key property; a composite key or an entity without one loads by id or by a `Specification` |
| PRAG0461 | Error | The key property of `[LoadEntity(By = …)]` is not of the logic key's type | Declare the property of the logic key's type (`string` for a number or code) |
| PRAG0462 | Warning | `[RequireExists]` beside a `[LoadEntity]` of the same entity and key | Remove the `[RequireExists]`: the load already proves the row exists, and only the load runs |
| PRAG0453 | Error | A `[LoadEntity]` `Include` path, or an `[EagerLoad]` path of a mutation, names no navigation of the entity | Fix the path: each segment a navigation (declared, or generated by a `[Relation]`) of the entity the previous one leads to |
| PRAG0452 | Error | A `ValidateLoaded` / `ValidateLoadedAsync` with a signature the invoker does not call | `ValidationError ValidateLoaded()` or `Task<ValidationError> ValidateLoadedAsync(CancellationToken)`, instance, not generic |
| PRAG0451 | Error | `[LoadCurrentUser]` cannot be generated | The module needs exactly one `[PragmaticUser]` entity, and `Pragmatic.Identity.Persistence` for its resolver |
| PRAG0406 | Error | `[Boundary]` class is not `partial` | Add `partial` to the boundary class |
| PRAG0407 | Error | `[Boundary]` class has no namespace | Move the class into a namespace |
| PRAG0409 | Error | Mutation base class wrong | Inherit from `Mutation<TEntity>`, not `DomainAction<T>` |
| PRAG0410 | Error | Mutation mode not determined | Set `[Mutation(Mode = ...)]` or use Create/Update/Delete prefix |
| PRAG0412 | Warning | SubBoundary nesting > 2 levels | Consider flattening the namespace structure |
| PRAG0413 | Info | SubBoundary inferred from namespace | Informational -- the SG auto-detected a sub-boundary |
| PRAG0414 | Warning | Mutation property has no matching entity setter | Add a `Set{Property}()` on the entity, or mark the mutation property `[MapIgnore]` |
| PRAG0415 | Warning | `[AuthorizationPolicy]` type could not be resolved | Ensure the referenced policy type exists and is accessible |
| PRAG0418 | Warning | `[RequirePermission(constant)]` could not be resolved to a permission value, so it would **not** be enforced | Use a literal permission string, or a generated entity permission |

Check the **Error List** window in Visual Studio or the build output for diagnostic details and the affected source location.

---

## Endpoint Returns 500 Instead of Business Error

The action should return a typed error (404, 409, etc.) but the endpoint returns 500 Internal Server Error.

### Common Causes

1. **Exception thrown instead of error returned.** The Result pattern only works when you return error objects. Exceptions bypass the pipeline:

   ```csharp
   // Wrong: throws -- produces 500
   throw new InvalidOperationException("Not found");

   // Right: returns -- produces 404
   return NotFoundError.For<Invoice, Guid>(Id);
   ```

2. **DI resolution failure.** A private dependency field is `null` because the service is not registered. The `NullReferenceException` propagates as 500. Check all DI registrations.

3. **Missing middleware.** Add `app.UseExceptionHandler()` to convert unhandled exceptions into ProblemDetails responses.

---

## Telemetry Not Appearing

Actions run but no metrics or traces appear in your observability platform.

### Checklist

1. **Is the `ActivitySource` subscribed?** `ActionsDiagnostics.ActivitySource` has source name `"Pragmatic.Actions"`. Your OTel configuration must include it:

   ```csharp
   builder.Services.AddOpenTelemetry()
       .WithTracing(tracing => tracing
           .AddSource("Pragmatic.Actions"));
   ```

2. **Is the `Meter` subscribed?** Metrics use meter name `"Pragmatic.Actions"`:

   ```csharp
   builder.Services.AddOpenTelemetry()
       .WithMetrics(metrics => metrics
           .AddMeter("Pragmatic.Actions"));
   ```

3. **Is the `LoggingFilter` enabled?** Structured logging requires the filter to be active (default is `true`).

---

## FAQ

### Can I use DomainAction without an endpoint?

Yes. Actions work independently of endpoints. Invoke them programmatically via `IDomainActionInvoker<T, TReturn>` or through boundary interfaces. The `[Endpoint]` attribute is optional.

### When should I override `ApplyAsync` on a Mutation?

Override `ApplyAsync` for logic that goes beyond property mapping: state machine transitions, business rule validation, child entity creation, or conditional updates. For simple property mapping, let the SG generate `ApplyToEntity` automatically.

### Does `ApplyToEntity` run when I override `ApplyAsync`?

Yes. `ApplyToEntity` always runs before `ApplyAsync`. The pipeline calls `mutation.ApplyToEntity(entity)` first (auto-mapped properties), then `mutation.ApplyAsync(entity, ct)` (your custom logic). You do not need to call `base.ApplyAsync()`.

### How do I skip authorization for an internal helper action?

Mark it with `[DomainAction(Internal = true)]`. This keeps it off the public `I{Boundary}Actions`: it is reachable through `I{Boundary}InternalActions`, from its own assembly. A call through that interface sets the `ActionCallContext.IsInternalCall` flag, and authorization filters skip their checks.

### Can Mutations dispatch domain events?

Yes. If the entity implements `IHasDomainEvents`, the pipeline dispatches all domain events after `SaveChangesAsync`. Add events in `ApplyAsync` or in entity domain methods:

```csharp
entity.Cancel(reason);  // entity.Cancel() adds a ReservationCancelledEvent internally
```

### What is the difference between L1 and L2 validation?

L1 validates the **mutation input** (the properties on the mutation class) before the entity is loaded. L2 validates the **entity state** after `ApplyToEntity` and `ApplyAsync` have run. L1 uses `ISyncValidator` and `IAsyncValidator<TMutation>`. L2 uses `IValidator<TEntity>` with change-aware validation (only modified properties are checked on updates).

### Can I use `[LoadEntity<T>]` on a Mutation?

Yes, for an entity **other than** the mutation's own — which the pipeline already loads by `Id`. A
mutation that needs, say, the manager a team is created for declares
`[LoadEntity<Employee>(nameof(ManagerId), FieldName = "_manager")]` instead of injecting a repository:
the invoker loads it after the authorization checks and before the mutation's row, answers 404 when
the key names nothing, and hands it to the generated field. It is loaded through the same unit of work
the mutation saves, so what `ApplyAsync` changes on it is written with the mutation.

The row comes alone unless the attribute names its navigations: `Include = "Members, Manager"` (dotted
paths for deeper ones). There is no lazy loading, so a navigation not included is empty.

The key is of the entity's key type — `Guid` — or `PRAG0411` says so on the attribute. A **nullable** key
is the optional load: `[LoadEntity<Employee>(nameof(ManagerId))]` on a `Guid? ManagerId` gives a nullable
field, reads nothing when the caller sent no manager, and still answers 404 for one that does not exist
(Time off `UpdateTeamMutation`).

A key the entity has no member for is not an input to map: on a mutation it is not "a property with no
setter" (`PRAG0414`) — the body reads the row it names.

### What do several loads cost?

One query per load, in sequence — except loads of **one entity by key** with the same `Include` paths, which
are **one** query: `[LoadEntity<Employee>(nameof(ManagerId), FieldName = "_manager")]` beside
`[LoadEntity<Employee>(nameof(DeputyId), FieldName = "_deputy")]` is a single `WHERE Id IN (…)`, each field then
taken from it; a key that names no row is still a 404 naming it, and a null optional key is not asked. Two loads
of one entity need distinct `FieldName`s — the field, and the generated local and setter parameter, are named
after it. Loads of different entities stay separate queries: one DbContext runs one query at a time.

### How do I load the rows a list of keys names?

`[LoadEntities<Employee>(nameof(EmployeeIds))]` on an `IReadOnlyList<Guid> EmployeeIds` — or an array, or
a list — gives an `IReadOnlyList<Employee> _employees` (`FieldName` overrides it), read in **one** query
through the same repository and filters as `[LoadEntity]`. The rows come in the order of the keys, a key
given twice once. Every key that names no row is in **one** 404 — `NotFoundError.ForAll`, the keys
comma-separated in `entityId` — not the first alone. An empty list, or a null one, is an empty field and no
query. A property that is not a collection of the entity's key is `PRAG0411`.

### How do I check that a referenced row exists without loading it?

For a key the operation carries but does not read — a foreign key in the body of a create:

```csharp
[Mutation(Mode = MutationMode.Create)]
[RequireExists<Employee>(nameof(EmployeeId))]
[RequireExists<AbsenceKind>(nameof(AbsenceKindId))]
public partial class GrantAllowanceMutation : Mutation<Allowance> { … }
```

Before the body, after authorization, the invoker asks the repository `ExistsAsync` — its filters, an `EXISTS`, no
row materialized — and answers 404 naming the key when it is not there, instead of the database's foreign-key
violation. A nullable key that is null is not checked. The key is of the entity's key type (`PRAG0411`); on a
mutation it is still written to the entity when the entity has a member of its name. Beside a `[LoadEntity]` of
the same entity and key it is `PRAG0462`: a row that was read exists, and only the load runs.

### How do I load a row by its domain key — a number, a code — rather than its id?

`By` names the entity's `[LogicKey]` member; the key property holds its value:

```csharp
[LoadEntity<Employee>(nameof(EmployeeNumber), By = nameof(Employee.EmployeeNumber))]
public partial class GetEmployeeCardAction : DomainAction<EmployeeCardDto>
{
    public required string EmployeeNumber { get; init; }   // "EMP-00001", from the route
    // _employee is loaded; a number no row carries was answered 404 with it
}
```

The row is read through the lookup the generator writes for the logic key — `{Entity}Specifications.GetBy{Key}Async`,
or `By{Key}` on the filtered query when there is an `Include` — with the same filters and tracking as by id. The
member must be the entity's single-part logic key (`PRAG0460` otherwise, also for a composite key or `By` beside a
`Specification`), and the property of its type (`PRAG0461`). Showcase `GetAmenityByNameAction` loads this way.

### How do I load the rows a rule names, not a key?

Name a specification instead of the key:
`[LoadEntity<Employee>(Specification = nameof(EmployeeSpecifications.ActiveWithNumber))]` reads the first row
it matches — none is a 404 — and `[LoadEntities<LeaveRequest>(Specification = nameof(LeaveRequestSpecifications.PendingOf))]`
every one — none is an empty list, or a 404 with `RequireAny = true`. The member is a static method,
property or field returning a `Specification<TEntity>`; a bare string names a member of `{Entity}Specifications`.
Its parameters bind **by name**, case ignored, to the operation's properties — `PendingOf(Guid id)` takes
the operation's `Id` — and an optional parameter with no property keeps its default. The read goes through
the entity's repository with its filters and tracking, as the key form does (Time off `TransferEmployeeAction`
moves the requests it preloads this way).

`RequireReadPermission = true`, on any load, also asks the entity's read permission — the value of its CRUD
`Read` constant — before anything is read: 401 for nobody signed in, 403 without it. An internal call is not
asked, as it is not asked the operation's own permission.

### How do I use what a declared query answers inside an operation?

Put `[LoadFrom<TQuery>]` on a property of the query's answer type:

```csharp
private int Year => From.Year;                          // binds GetMyBalancesQuery.Year by name

[LoadFrom<GetMyBalancesQuery>]
private IReadOnlyList<AllowanceBalanceDto> Balances { get; set; } = [];
```

The invoker builds the query — each input bound by name, case ignored, from the operation's properties —
and runs it through the **query's own invoker** before the body: its validation, its permission, its read.
A query that fails fails the operation with the same error (403 for its permission, 404 for a `Single`
query that finds nothing). The property is not an input: it is not in the body, the OpenAPI document or the
boundary signature. The data is read-only DTOs; rows to change are `[LoadEntity]`/`[LoadEntities]`.

⚠️ Not the boundary's internal facade (`I{Boundary}InternalActions`) for this: it enters an internal call,
and the query's permission is not asked of anyone.

### How do I test an action without the full pipeline?

Call `action.Execute(ct)` directly, setting dependencies manually:

```csharp
var action = new CreateReservation
{
    Request = new CreateReservationRequest { /* ... */ }
};
// Set dependencies directly (bypasses pipeline)
action.SetDependencies(mockRepo, mockClock);
var result = await action.Execute(ct);
```

For integration testing with the full pipeline, resolve the invoker from a test `IServiceProvider`.

---

## Getting Help

- **GitHub Issues**: [github.com/pragmatic-design/Pragmatic.Design/issues](https://github.com/pragmatic-design/Pragmatic.Design/issues)
- **Showcase Examples**: See the `Showcase` project for working action and mutation implementations across all base class types.
- **Pipeline Reference**: See [pipeline.md](pipeline.md) for filter ordering, lifecycle hooks, and custom filters.
- **Mutation Reference**: See [mutations.md](mutations.md) for entity mutations, ApplyAsync, and auto-mapping.
- **Boundary Reference**: See [boundaries.md](boundaries.md) for boundary interfaces, internal calls, and remote.
