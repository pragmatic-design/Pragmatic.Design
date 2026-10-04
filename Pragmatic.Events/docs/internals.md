# Pragmatic.Events Internals

Deep dive into dispatcher mechanics, handler ordering, error handling, the EF Core interceptor, and observability.

## InMemoryEventDispatcher

### Architecture

`InMemoryEventDispatcher` implements `IDomainEventDispatcher` and is registered as **scoped** by `AddInMemoryDomainEvents()`. It resolves handlers from `IServiceProvider` at dispatch time.

Two dispatch paths exist:

| Method | Use case |
|--------|----------|
| `DispatchAsync<TEvent>(event)` | Typed dispatch -- resolves `IDomainEventHandler<TEvent>` directly |
| `DispatchAsync(IEnumerable<IDomainEvent>)` | Untyped batch -- used by the EF Core interceptor when event types are mixed |

### Typed Dispatch Flow

```
DispatchAsync<TEvent>(@event)
  1. Start Activity "Event.{EventName}" on Pragmatic.Events ActivitySource
  2. Increment pragmatic.events.dispatched counter
  3. Resolve all IDomainEventHandler<TEvent> from IServiceProvider
  4. Sort handlers by Order (ascending)
  5. For each handler:
     a. Resolve ICallContext (optional, from Pragmatic.Abstractions)
     b. Enter internal call scope (skips auth filters)
     c. handler.HandleAsync(@event, ct)
     d. Dispose internal call scope
     e. On exception (except OperationCanceledException):
        - Log at Error level
        - Increment pragmatic.events.handler_failures counter
        - Record exception on Activity
        - Continue to next handler
  6. Record dispatch duration in pragmatic.events.dispatch_duration histogram
  7. If failures > 0, log Warning with failure count
```

### Untyped Batch Dispatch: SG typed dispatch tables

When `DispatchAsync(IEnumerable<IDomainEvent>)` is called (typically by the EF Core interceptor), the dispatcher iterates events and bridges each to the typed path **without reflection**, using source-generated dispatch tables.

For every assembly that has `[EventHandler]` handlers, the Composition generator emits a
`GeneratedEventDispatchTable : ITypedEventDispatchTable`, an event→`DispatchAsync<TEvent>` switch built with compile-time pattern matching:

```csharp
// Generated (per module): _Infra.Events.DispatchTable.g.cs
internal sealed class GeneratedEventDispatchTable : ITypedEventDispatchTable
{
    public Task? TryDispatch(IDomainEventDispatcher dispatcher, IDomainEvent @event, CancellationToken ct)
        => @event switch
        {
            ReservationConfirmed typed => dispatcher.DispatchAsync(typed, ct),
            InvoicePaid typed          => dispatcher.DispatchAsync(typed, ct),
            _ => null   // not covered by this table
        };
}
```

Each table is registered additively by that module's generated `AddPragmaticEventHandlers()`. The dispatcher receives all of them (`IEnumerable<ITypedEventDispatchTable>`, one per composed module) and probes them in order:

```
DispatchAsync(events)
  for each event:
    1. Check CancellationToken
    2. Probe each registered ITypedEventDispatchTable in order
    3. First table whose switch matches the concrete type wins -> typed DispatchAsync<TEvent>
    4. If no table covers the type -> dynamic fallback: DispatchAsync((dynamic)@event, ct)
```

This is the **AOT-safe path**: no `MakeGenericMethod`, no reflection. The `dynamic` fallback is reached only when no module referenced the source generator, or when an event type has no registered handler; it routes through the DLR (not reflection) and is annotated `[RequiresDynamicCode]`/`[RequiresUnreferencedCode]` (suppressed). For guaranteed AOT, ensure handlers are `[EventHandler]`-registered so a table covers every dispatched event, or call the typed `DispatchAsync<TEvent>` directly.

## Handler Ordering

Handlers are sorted by the `Order` property (default `0`, lower runs first):

```csharp
var handlers = _serviceProvider.GetServices<IDomainEventHandler<TEvent>>()
    .OrderBy(h => h.Order)
    .ToList();
```

Within the same `Order` value, execution follows DI registration order. `AddPragmaticEventHandlers()` registers in a deterministic order (sorted by full type name), so the sequence is stable across builds, but it is invisible at the handler. Give handlers distinct `Order` values whenever the sequence actually matters.

## Error Handling

### Continue-on-Failure Strategy

The dispatcher catches all exceptions except `OperationCanceledException`:

```csharp
catch (Exception ex) when (ex is not OperationCanceledException)
{
    handlerFailures++;
    EventsDiagnostics.HandlerFailures.Add(1, ...);
    LogHandlerFailed(eventName, handlerName, ex);
    activity?.RecordException(ex);
}
```

This means:
- A failing handler does **not** block subsequent handlers.
- All handlers are invoked regardless of individual failures.
- The dispatcher itself does **not** throw (except on cancellation).
- Failures are observable via logs, metrics, and traces.

### Why Not Fail-Fast?

In a multi-handler scenario, independent side effects (send email, update read model, emit metric) should not block each other. If Handler B fails, Handler C (which is unrelated) should still execute. This is the standard behavior for in-process event dispatch.

If you need transactional guarantees across multiple handlers, either:
- Combine the logic into a single handler.
- Use the outbox pattern with a message broker.

### OperationCanceledException

`OperationCanceledException` propagates immediately from the batch dispatch loop (before reaching the next event). Within a single event's handlers, it propagates through the catch filter and stops the current event's handler chain.

## Internal Call Context

The dispatcher resolves `ICallContext` (defined in `Pragmatic.Abstractions`, namespace `Pragmatic.Pipeline`) as an **optional** service. If present (i.e., `Pragmatic.Actions` is referenced), each handler runs inside an internal call scope:

```csharp
var callContext = _serviceProvider.GetService<Pragmatic.Pipeline.ICallContext>();

foreach (var handler in handlers)
{
    var scope = callContext?.EnterInternalCall();
    try
    {
        await handler.HandleAsync(@event, ct).ConfigureAwait(false);
    }
    finally
    {
        scope?.Dispose();
    }
}
```

**Effect:** when `IsInternalCall` is true, authorization filters (L1 permission, L2 policy, L3 ABAC) skip their checks. This allows event handlers to invoke domain actions (via boundary interfaces) without being blocked by the HTTP caller's permissions.

Data-level filters (L4 -- tenant isolation, soft delete) remain active. The user context is preserved because the handler runs in the same DI scope as the original request.

If `ICallContext` is not registered (e.g., `Pragmatic.Actions` is not referenced), `GetService` returns `null` and `scope` is `null`. The `?.Dispose()` is a no-op. Handlers execute normally without any call context.

## EF Core Interceptor

### LifecycleEventsInterceptor

The only interceptor this package registers, and it **raises**; it does not dispatch. It hooks `SavingChanges` / `SavingChangesAsync`, while the change-tracking state still says whether a row is being added, updated or deleted, maps that state to an `EntityLifecycle`, and calls `RaiseLifecycleEvents` on every tracked `IRaisesLifecycleEvents`, the interface the generator implements on a `[Raises<T>]` entity.

A soft delete arrives as a `Modified` entry whose `IsDeleted` flag flips to `true`; it maps to `Deleted`, not `Updated`. The **transition** is what counts: a flag already `true` and merely written again is an update, or the `Deleted` event would be re-raised on every save.

It is stateless and holds nothing, which is only possible because dispatch is somebody else's job.

### Why there is no dispatching interceptor

An interceptor that dispatched in `SavedChangesAsync` would need a DI scope of its own, to avoid a captive dependency on the scoped dispatcher. Two things would be wrong with it, and the second is the one that matters:

- **Ordering.** It would run inside `SaveChanges`, which under an explicit transaction is before the commit. A handler could act on a write that then rolled back. The interceptor could only log a warning: `BatchContext` lives in a package it cannot see.
- **Scope.** A fresh scope resolves a fresh tenant context, and nothing resolves a tenant in it. Every fail-closed query filter then hides the rows the handler is called to act on: it runs, finds nothing, writes nothing, and reports success. A dispatch that silently does nothing is worse than one that does not happen.

### Dispatch Flow

Dispatch belongs to whoever performed the write, and all three do it in the scope that asked for it:

```
IUnitOfWork.SaveChangesAsync()
  1. LifecycleEventsInterceptor raises the declared events (SavingChanges)
  2. The database commits: a failed save stops here, nothing is taken
  3. Take the events off every tracked IHasDomainEvents entity (taking clears them)
  4. A batch owns this commit?
       yes -> batch.DeferEvent(each), flushed after the commit and outside the claim
       no  -> IDomainEventDispatcher.DispatchAsync(all, ct), with the commit scope suspended
              (no dispatcher registered -> nothing is taken, and the events stay on the entity)
```

| Path | Who dispatches |
|---|---|
| Mutation / domain action | the generated invoker, in `RunPostCommitSideEffectsAsync` |
| Composition, `[Transactional]`, hand-opened batch | `DeferredEventFlush`, after the batch commits |
| A repository write, or any plain `IUnitOfWork.SaveChangesAsync` | `EfCoreUnitOfWork` itself |

The generated repository's `SaveChangesAsync` goes through the boundary's `IUnitOfWork` (the same instance an invoker holds, resolved from the same keyed registration), so a write made by hand behaves like the same write made by a mutation.

### Why take after the save, and only on success?

Taking is what clears them, so there is no window where an event is both pending on the entity and already handed over, which is what keeps a retried `SaveChangesAsync` from dispatching twice.

**After** the save, because the lifecycle interceptor raises during `SavingChanges`: collecting first would find an empty list every time. **Only on success**, because a save that failed wrote nothing, and announcing a write that did not happen is worse than not announcing one that did. The refused entity keeps its events, so a retry can still announce the write if it lands.

### Registration

```csharp
public static DbContextOptionsBuilder UseDomainEvents(this DbContextOptionsBuilder optionsBuilder)
{
    ArgumentNullException.ThrowIfNull(optionsBuilder);
    optionsBuilder.AddInterceptors(new LifecycleEventsInterceptor());
    return optionsBuilder;
}
```

No service provider parameter: the interceptor has no dependencies. On a source-generated boundary `DbContext` this is wired for you, and the generated registration hands `EfCoreUnitOfWork` an optional `IDomainEventDispatcher` (`GetService`, not `GetRequiredService`: an application with no domain events registers none, and that must not stop a unit of work from being built).

**Order matters where the outbox is involved.** The lifecycle interceptor must be registered *before* the outbox capture interceptors: they read the entity's events during `SavingChanges` too, and anything raised after them never reaches the outbox. EF Core invokes interceptors in registration order.

## Transactional Outbox

The transactional outbox lives in `Pragmatic.Events.EFCore` (namespace `Pragmatic.Events.EFCore.Outbox`); it is **not** part of `Pragmatic.Messaging`. It has two moving parts.

### EventOutboxInterceptor (capture)

A `SaveChangesInterceptor` that hooks `SavingChanges` / `SavingChangesAsync`, i.e. it runs **before** the commit. For every tracked `IHasDomainEvents` entity it serializes each domain event into an `EventOutboxEntry` and `context.Add`s it, so the outbox rows enlist in the **same transaction** as the entity change. It then calls `ClearDomainEvents()` on the entity, so the post-commit hand-over finds nothing left to take (the two combine safely: the outbox wins, and the event is delivered once). It captures the ambient `Activity.Current?.Id` (`TraceParent`) and the ambient tenant (`TenantId`) onto each row at write time.

The table is `__EventOutbox`, mapped by `EventOutboxEntryConfiguration` (applied via `modelBuilder.AddEventOutbox()`). Timestamps are stored as UTC ticks (`long`) so ordering/eligibility predicates translate on every provider including SQLite; the payload column is unbounded (mapped to each provider's large-string type: PostgreSQL `text`, SQL Server `nvarchar(max)`, SQLite `TEXT`).

### EventOutboxDeliveryService&lt;TContext&gt; (delivery)

A `BackgroundService` that polls on `PollingInterval`. Each pass:

1. Selects up to `BatchSize` **eligible** candidate ids: `ProcessedAt == null`, `Attempts < MaxAttempts`, and either unclaimed or with an **expired** claim (`ClaimedUntil < now`).
2. **Atomically claims** them with a compare-and-swap `ExecuteUpdate` that stamps a unique per-poll token into `ClaimedBy` and a lease into `ClaimedUntil`. Only rows this worker won are then loaded and processed: this is what stops multiple replicas from each delivering the same entry. A claim lease (5 minutes) means a crashed worker's rows become re-grabbable rather than stuck.
3. For each claimed row: resolves the CLR type via the **fail-closed** `IEventOutboxTypeResolver`, deserializes the JSON payload, restores the originating trace (`StartActivity` parented to `TraceParent`) and tenant (`SetTenant(entry.TenantId)`), then dispatches through `IDomainEventDispatcher`. On success it sets `ProcessedAt`; on exception it increments `Attempts`, records `LastError`, and releases the claim so the row is retried on a later poll (within `MaxAttempts`).

### Guarantees and edges

- **At-least-once.** A crash between dispatch and the `ProcessedAt` write re-delivers the event on the next poll. Handlers must be idempotent (`IDomainEvent.EventId` is the natural dedup key).
- **Poison messages.** After `MaxAttempts`, a row is no longer eligible: it stays un-processed with its `LastError` populated. There is no separate dead-letter table.
- **Fail-closed type resolution.** `EventOutboxTypeResolver` is built from an allowlist derived from the registered `IDomainEventHandler<T>` service descriptors (`AddEventOutbox` scans the `IServiceCollection`). A stored `EventType` string that is not in the allowlist resolves to `null` and the row is rejected (attempt counted); there is no `Type.GetType` on an arbitrary DB string, so the payload cannot be used to load an unexpected type (no deserialization gadget surface).
- **`AddEventOutbox<TContext>()` is idempotent.** The delivery `IHostedService` is registered via `TryAddEnumerable`, so calling it twice does not spin up duplicate delivery loops.

### EventOutboxOptions

| Option | Default | Validation (on set) |
|--------|---------|---------------------|
| `BatchSize` | 100 | `>= 1`, else `ArgumentOutOfRangeException` |
| `PollingInterval` | 5 s | `> TimeSpan.Zero`, else `ArgumentOutOfRangeException` |
| `MaxAttempts` | 5 | `>= 1`, else `ArgumentOutOfRangeException` |

## Observability

### EventsDiagnostics

All diagnostics are centralized in `EventsDiagnostics` (namespace `Pragmatic.Events.Diagnostics`):

```csharp
public static class EventsDiagnostics
{
    public const string SourceName = "Pragmatic.Events";
    public static readonly ActivitySource ActivitySource;
    public static readonly Meter Meter;

    // Instruments:
    public static readonly Histogram<double> DispatchDuration;    // ms
    public static readonly Counter<long> EventsDispatched;        // total events
    public static readonly Counter<long> HandlerFailures;         // total failures
}
```

### Activity Tags

Tags follow the naming conventions in `EventTags` (from `Pragmatic.Abstractions`):

| Tag constant | Tag name | Description |
|-------------|----------|-------------|
| `EventTags.Name` | `pragmatic.event.name` | Event type name |
| `EventTags.Handler` | `pragmatic.event.handler` | Handler type name (available for per-handler spans) |
| `EventTags.HandlerCount` | `pragmatic.event.handler_count` | Number of handlers resolved |

The dispatcher also sets `event.handler_failures` (not in `EventTags`) when failures occur.

### Metric Dimensions

Both `EventsDispatched` and `HandlerFailures` are tagged with `event.name`. `HandlerFailures` also includes `handler.name`. This allows dashboards to slice by event type and failing handler.

## EntityPropertyChanged\<TEntity\>

A built-in generic event for cascade property propagation:

```csharp
public sealed record EntityPropertyChanged<TEntity> : IDomainEvent where TEntity : class
{
    public required object EntityId { get; init; }
    public required string PropertyName { get; init; }
    public object? NewValue { get; init; }
    public object? OldValue { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
```

Used when a parent entity's property changes and dependent entities need to react. For example, when `RoomType.BaseRate` changes, a handler can update all associated reservations:

```csharp
[EventHandler]
public sealed class RoomTypeRateChangedHandler
    : IDomainEventHandler<EntityPropertyChanged<RoomType>>
{
    public async Task HandleAsync(
        EntityPropertyChanged<RoomType> @event, CancellationToken ct = default)
    {
        if (@event.PropertyName == nameof(RoomType.BaseRate))
        {
            // Update dependent reservations with new rate
        }
    }
}
```

## DI Lifetimes

| Type | Lifetime | Registration |
|------|----------|-------------|
| `IDomainEventDispatcher` / `InMemoryEventDispatcher` | Scoped | `AddInMemoryDomainEvents()` via `TryAddScoped` |
| `IDomainEventHandler<T>` | Scoped | `AddDomainEventHandler<THandler, TEvent>()` |
| `LifecycleEventsInterceptor` | Singleton (implicit) | Created once per `DbContextOptions` by `UseDomainEvents()` |

The dispatcher is scoped because it resolves handlers from the current scope, and dispatch happens in the scope that asked for the write, which is what gives a handler the tenant and the user of the request. The interceptor is effectively singleton because it is attached to `DbContextOptions`; it holds nothing, which it can afford to do only because it never dispatches.

## Thread Safety

- `InMemoryEventDispatcher`: stateless per-call (all per-dispatch state lives in local variables). The injected `ITypedEventDispatchTable[]` is read-only after construction and safe for concurrent probing.
- `LifecycleEventsInterceptor`: stateless -- holds nothing at all.
- `DomainEventSource`: not thread-safe. Entity instances are expected to be used within a single scope (one DbContext, one request).
