---
title: "Pipeline Architecture"
description: "Every action in Pragmatic.Actions executes through a filter pipeline managed by an invoker. This guide covers how the pipeline works, the built-in filters, and "
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Actions/docs/pipeline.md
sidebar:
  order: 5
---
Every action in Pragmatic.Actions executes through a filter pipeline managed by an invoker. This guide covers how the pipeline works, the built-in filters, and how to write custom filters.

## Pipeline Flow

### DomainAction Pipeline

The `DomainActionInvoker<TAction, TReturn>` orchestrates this sequence:

```
1. InjectDependencies(action)
   - Generated SetDependencies() resolves private fields from DI

2. BeforeExecute filters (ascending order by Order)
   - Validation (100), permissions (200), policies (210), resource authorization (250), ...
   - Each filter can short-circuit by returning a failure
   - If any filter fails, execution stops immediately

3. PrepareActionAsync(action, ct)
   - Writes the [FromClock] properties (IClock.UtcToday / UtcNow) and the [FromCurrentUser] ones
     (401 when the caller is not authenticated, 404 when a bound member's user entity is missing)
   - Loads the signed-in user's entity for [LoadCurrentUser] into _current{User} (401 / 404 the same way)
   - Loads entities declared with [LoadEntity<T>]
   - Calls the operation's ValidateLoaded() / ValidateLoadedAsync(ct), if declared: rules on what was
     just loaded; a failure is the ValidationError (422) and Execute does not run
   - Returns `NotFoundError` (Pragmatic.Result.Http, 404) if any pre-loaded entity is missing
   - After the filters on purpose: a caller who may not run the action cannot tell an existing row
     from a missing one by 404-vs-403. A validator therefore never sees the loaded entities
   - Inside the scopes of [WithoutFilter<T>] / [FilterMode], like the body: an entity the action
     reads past a filter is preloaded past it too

4. ExecuteActionAsync(action, ct)
   - Calls action.Execute(ct)
   - Or dispatches to ExecuteV2/V3 for versioned actions

5. AfterExecute filters (descending order by Order)
   - Cannot modify the result
   - Used for logging, metrics, cleanup

6. SaveChangesAsync(ct)
   - Only if the action succeeded
   - Persists via boundary-keyed IUnitOfWork
```

### VoidDomainAction Pipeline

Identical to the DomainAction pipeline, except:
- Calls `BeforeExecuteVoidAsync` / `AfterExecuteVoidAsync` on filters
- Returns `VoidResult<IError>` instead of `Result<TReturn, IError>`
- Filters that don't override the void methods default to no-op

### Mutation Pipeline

The `MutationInvoker<TMutation, TEntity>` follows a different, more structured sequence. See [Mutations Guide](/modules/actions/mutations/) for full details.

---

## Built-in Filters

### ValidationFilter (Order 100)

**Source**: `Pragmatic.Actions.Pipeline.Filters.ValidationFilter`

Validates action inputs before business logic runs. Behavior depends on attributes:

| Configuration | Sync | Async |
|--------------|------|-------|
| No attribute | Yes | No |
| `[Validate]` | Yes | Yes |
| `[Validate(AsyncOnly = true)]` | No | Yes |
| `[Validate(Sync = false)]` | No | Yes |
| `[NoValidation]` | No | No |

**Sync validation**: Checks if the action implements `ISyncValidator` (generated from validation attributes like `[Required]`, `[Email]`). Also validates nested properties that implement `ISyncValidator`.

**Async validation**: Resolves `IAsyncValidator<TAction>` from DI. Also checks nested reference-type properties for registered `IAsyncValidator<TProperty>`.

Both sync and async errors are combined into a single `ValidationError` before short-circuiting.

**Performance note**: Attribute lookups and compiled property accessors are cached per action type using `ConcurrentDictionary`. Reflection occurs only on first access.

### PermissionAuthorizationFilter (Order 200)

**Source**: `Pragmatic.Actions.Pipeline.PermissionAuthorizationFilter`

Enforces `[RequirePermission]` and `[RequireAnyPermission]` attributes.

**Behavior**:
1. Skips if `ActionCallContext.IsInternalCall` is true (intra-boundary calls)
2. Resolves the requirement from cached attribute data
3. If no requirement, passes through
4. If user is not authenticated, returns `UnauthorizedError`
5. For `[RequirePermission]`: checks `ICurrentUser.Authorization.HasAllPermissions()`
6. For `[RequireAnyPermission]`: checks `ICurrentUser.Authorization.HasAnyPermission()`
7. Returns `ForbiddenError` if the check fails

**DI resolution**: Resolves `ICurrentUser` lazily from `IServiceProvider`. Falls back to `AnonymousUser.Instance` if not registered.

### PolicyEvaluationFilter (Order 210)

**Source**: `Pragmatic.Actions.Pipeline.PolicyEvaluationFilter`

Evaluates `[RequirePolicy<T>]` attributes where `T` extends `ResourcePolicy`.

**Behavior**:
1. Skips if `ActionCallContext.IsInternalCall` is true
2. Looks up the policy in the source-generated `IPolicyRegistry` (zero reflection — no attribute scan at runtime)
3. Instantiates it through the generated factory delegate (`static () => new TPolicy()`, no `Activator.CreateInstance`)
4. Calls `policy.Evaluate(currentUser)`
5. Returns `ForbiddenError` if evaluation fails

### ResourceAuthorizationFilter (Order 250)

**Source**: `Pragmatic.Actions.Pipeline.ResourceAuthorizationFilter`

Enforces instance-level authorization via `IResourceAuthorizer<TAction>`.

**Behavior**:
1. Resolves `IResourceAuthorizer<TAction>` from DI
2. If no authorizer is registered, passes through (cached for fast path)
3. Calls `authorizer.CanAccessAsync(currentUser, action, actionName, ct)`
4. Returns `ForbiddenError` if access is denied

This filter handles cases where authorization depends on the specific data being accessed -- e.g., "can this user edit THIS invoice?"

### LoggingFilter (Order 1000)

**Source**: `Pragmatic.Actions.Pipeline.Filters.LoggingFilter`

Provides structured logging with zero allocation via `[LoggerMessage]`:

- **BeforeExecute**: Logs action name at Debug level
- **AfterExecute**: Logs success with result type (Information) or failure with error code (Warning)

For security, this filter does NOT log action parameters or result values.

---

## Filter Ordering

Filters execute in two phases:

1. **BeforeExecute**: ascending order (100, 200, 210, 250, 1000)
2. **AfterExecute**: descending order (1000, 250, 210, 200, 100)

The `FilterOrder` static class provides named constants:

```csharp
public static class FilterOrder
{
    public const int Validation    = 100;
    public const int Authorization = 200;
    public const int Transaction   = 300;
    public const int Caching       = 400;
    public const int Logging       = 1000;
}
```

Gaps between values are intentional -- place custom filters at intermediate values (e.g., 150, 250, 500).

---

## Custom Filters

### Global Filter (All Actions)

Implement `IActionFilter` to create a filter that runs for every action:

```csharp
public class AuditFilter(IAuditLog auditLog) : IActionFilter
{
    public int Order => 900; // After business logic, before logging

    public Task<VoidResult<IError>> BeforeExecuteAsync<TAction, TReturn>(
        TAction action, CancellationToken ct)
        where TAction : DomainAction<TReturn>
    {
        // Pre-execution logic
        return Task.FromResult(VoidResult<IError>.Success());
    }

    public async Task AfterExecuteAsync<TAction, TReturn>(
        TAction action, Result<TReturn, IError> result, CancellationToken ct)
        where TAction : DomainAction<TReturn>
    {
        await auditLog.RecordAsync(typeof(TAction).Name, result.IsSuccess, ct);
    }
}
```

Register:

```csharp
services.AddActionFilter<AuditFilter>();
```

### Action-Specific Filter

Implement `IActionFilter<TAction>` to target a specific action regardless of its return type. Works with both `DomainAction<TReturn>` and `VoidDomainAction`:

```csharp
public class ReservationBusinessRule : IActionFilter<CreateReservation>
{
    public int Order => 250; // After authorization

    public async Task<VoidResult<IError>> BeforeExecuteAsync(
        CreateReservation action, CancellationToken ct)
    {
        // Check business invariants specific to CreateReservation
        if (action.Request.CheckIn < DateOnly.FromDateTime(DateTime.Today))
            return VoidResult<IError>.Failure(
                ValidationError.For("CheckIn", "validation.future_date"));

        return VoidResult<IError>.Success();
    }
}
```

Register:

```csharp
services.AddActionFilter<ReservationBusinessRule, CreateReservation>();
```

### Typed Filter (With Result Access)

Implement `IActionFilter<TAction, TReturn>` when you need access to the typed result in `AfterExecute`:

```csharp
public class InvoiceResultLogger : IActionFilter<GetInvoice, InvoiceDto>
{
    public int Order => 500;

    public Task<VoidResult<IError>> BeforeExecuteAsync(
        GetInvoice action, CancellationToken ct)
        => Task.FromResult(VoidResult<IError>.Success());

    public Task AfterExecuteAsync(
        GetInvoice action, Result<InvoiceDto, IError> result, CancellationToken ct)
    {
        if (result.IsSuccess)
            Console.WriteLine($"Invoice {result.Value.Id} fetched");
        return Task.CompletedTask;
    }
}
```

Register:

```csharp
services.AddActionFilter<InvoiceResultLogger, GetInvoice, InvoiceDto>();
```

---

## Short-Circuiting

Any `BeforeExecute` filter can short-circuit the pipeline by returning a failure:

```csharp
public Task<VoidResult<IError>> BeforeExecuteAsync<TAction, TReturn>(
    TAction action, CancellationToken ct)
    where TAction : DomainAction<TReturn>
{
    if (/* condition */)
        return Task.FromResult(VoidResult<IError>.Failure(new ForbiddenError { /* ... */ }));

    return Task.FromResult(VoidResult<IError>.Success());
}
```

When a filter short-circuits:
- The action's `Execute()` is NOT called
- Remaining `BeforeExecute` filters are NOT called
- `AfterExecute` filters are NOT called
- The failure result is returned to the caller
- A `filter_short_circuits` metric is recorded
- An Activity tag is set for distributed tracing

---

## ActionCallContext

The `ActionCallContext` is a scoped service that tracks whether the current invocation is an internal (intra-boundary) call.

```csharp
public sealed class ActionCallContext : ICallContext
{
    public bool IsInternalCall => _depth > 0;
    public InternalCallScope EnterInternalCall();
}
```

When one action calls another within the same boundary via the generated boundary interface, it wraps the call with `EnterInternalCall()`:

```csharp
using var scope = callContext.EnterInternalCall();
await childInvoker.InvokeAsync(childAction, ct);
// scope.Dispose() restores the previous depth
```

Nesting is supported -- the depth counter increments/decrements correctly for multi-level calls.

The `PermissionAuthorizationFilter` and `PolicyEvaluationFilter` both check `IsInternalCall` and skip authorization when true. This prevents redundant permission checks during action orchestration.

The `ICallContext` interface (in `Pragmatic.Pipeline`) allows infrastructure components like `InMemoryEventDispatcher` to enter internal call mode without depending on `Pragmatic.Actions`.

---

## Telemetry Integration

Every action invocation creates an `Activity` from `ActionsDiagnostics.ActivitySource` with the name `Action.{ActionName}` or `Mutation.{MutationType}`.

Activity tags follow OpenTelemetry semantic conventions:

| Tag | Description |
|-----|-------------|
| `action.name` | Full type name |
| `action.kind` | `"action"`, `"void_action"`, or `"mutation"` |
| `action.result` | `"success"`, `"failure"`, or `"short-circuited"` |
| `action.filter_count` | Number of active filters |
| `error.code` | Error code on failure |

Metrics are recorded via the `ActionsDiagnostics.Meter`:
- `pragmatic.actions.invocations` (counter)
- `pragmatic.actions.duration` (histogram, ms)
- `pragmatic.actions.failures` (counter)
- `pragmatic.actions.filter_short_circuits` (counter)

---

## Disabling Built-in Filters

All built-in filters can be disabled via `PragmaticActionsOptions`:

```csharp
services.AddPragmaticActions(options =>
{
    options.EnableValidationFilter = false;           // disable ValidationFilter
    options.EnableLoggingFilter = false;              // disable LoggingFilter
    options.EnablePermissionFilter = false;           // disable PermissionAuthorizationFilter
    options.EnableResourceAuthorizationFilter = false; // disable ResourceAuthorizationFilter
    options.EnablePolicyFilter = false;               // disable PolicyEvaluationFilter
});
```

This is useful for testing or when you want to replace built-in filters with custom implementations.

---

## Filter Execution with Mutation Pipeline

The mutation pipeline (`MutationInvoker`) does NOT use the global `IActionFilter` pipeline directly. Instead, it has its own structured pipeline with explicit phases (see [Mutations Guide](/modules/actions/mutations/)).

However, mutations DO support `IActionFilter<TMutation>` -- typed action filters that run after L1 validation and before entity loading. This is the extension point for mutation-specific business policies:

```csharp
public class InvoiceCreationPolicy : IActionFilter<CreateDraftInvoiceMutation>
{
    public int Order => 200;

    public async Task<VoidResult<IError>> BeforeExecuteAsync(
        CreateDraftInvoiceMutation mutation, CancellationToken ct)
    {
        // Business rule: reject invoices over limit
        if (mutation.TotalAmount > 100_000m)
            return VoidResult<IError>.Failure(new BusinessRuleError("Invoice exceeds limit"));

        return VoidResult<IError>.Success();
    }
}
```
