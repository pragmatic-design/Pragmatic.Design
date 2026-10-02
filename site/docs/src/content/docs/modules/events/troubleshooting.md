---
title: "Troubleshooting"
description: "Practical problem/solution guide for Pragmatic.Events. Each section covers a common issue, the likely causes, and the fix."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Events/docs/troubleshooting.md
sidebar:
  order: 5
---
Practical problem/solution guide for Pragmatic.Events. Each section covers a common issue, the likely causes, and the fix.

---

## Events Not Dispatching After SaveChanges

Entities raise events, `SaveChangesAsync()` succeeds, but handlers never fire.

### Checklist

1. **Did you register the dispatcher?**

   ```csharp
   builder.Services.AddInMemoryDomainEvents();
   ```

   `AddPragmaticEventHandlers()` already does this (via `TryAdd`), so this step only applies when you register handlers by hand. Without a dispatcher the events are **left on the entity** rather than taken and dropped — so `entity.DomainEvents` growing across saves is the symptom that says this is your problem.

2. **Did you save through the unit of work?** Dispatch belongs to whoever performed the write: the generated invoker of a mutation or action, the batch of a composition, or `EfCoreUnitOfWork`. A call straight to `dbContext.SaveChangesAsync()` bypasses all three — the rows are written and nothing is dispatched.

3. **Did you add the interceptor to your DbContext?** Only matters for `[Raises<T>]`, which is what raises them:

   ```csharp
   services.AddDbContext<AppDbContext>(options =>
   {
       options.UseNpgsql(connectionString);
       options.UseDomainEvents();  // LifecycleEventsInterceptor — raising only
   });
   ```

   Events raised by hand with `RaiseEvent(...)` do not need it; declared ones need nothing else.

4. **Does your entity implement `IHasDomainEvents`?** The hand-over scans `ChangeTracker.Entries<IHasDomainEvents>()`. If your entity inherits `DomainEventSource`, this is handled. If you implement events manually, verify the entity implements `IHasDomainEvents`.

5. **Are events actually being raised?** Set a breakpoint in `RaiseEvent()` or check `entity.DomainEvents.Count` before `SaveChangesAsync()`. If the list is empty, the entity is not raising events in its domain operation.

6. **Is the entity tracked by the ChangeTracker?** Only tracked entities are scanned. An entity loaded `AsNoTracking()` or detached takes its events with it.

7. **Did the save succeed?** Events are taken only after a successful save. A refused write keeps its events on the entity on purpose: announcing a write that did not happen is worse than not announcing it.

---

## Handlers Not Executing

The dispatcher fires, but a specific handler never runs.

### Checklist

1. **Is the handler registered in DI?** Check one of these registration methods:

   ```csharp
   // SG-generated (requires the [EventHandler] attribute)
   services.AddPragmaticEventHandlers();

   // Or individually
   services.AddDomainEventHandler<MyHandler, MyEvent>();
   ```

2. **Does the handler implement `IDomainEventHandler<T>` for the correct event type?** A handler for `ReservationConfirmed` will not fire for `ReservationCreated`. Verify the generic type parameter matches the event being raised.

3. **Is the handler's assembly referenced by the host?** `AddPragmaticEventHandlers()` is generated per assembly. A handler in a module the host does not reference is never registered by anyone.

4. **Check the handler's `Order` property.** If a preceding handler throws an `OperationCanceledException`, the entire dispatch chain stops. Non-`OperationCanceledException` exceptions do not block subsequent handlers, but cancellation does.

5. **Is the event type correct at dispatch time?** The untyped batch dispatch (`DispatchAsync(IEnumerable<IDomainEvent>)`) resolves handlers based on the runtime type of each event. If you upcast the event to `IDomainEvent` before raising, the runtime type is preserved and dispatch works correctly.

---

## Handler Failures Silently Swallowed

A handler throws, but the caller of `SaveChangesAsync()` does not see the exception.

### This Is By Design

The `InMemoryEventDispatcher` uses a continue-on-failure strategy. Handler exceptions (except `OperationCanceledException`) are:

1. Logged at `Error` level with the handler name and event name.
2. Counted in the `pragmatic.events.handler_failures` metric.
3. Recorded as an exception on the current OTel `Activity`.
4. **Not propagated** to the caller.

### How to Detect Failures

- **Logs:** Look for `Error`-level messages: `"Handler {HandlerName} failed for event {EventName}"`.
- **Metrics:** Monitor `pragmatic.events.handler_failures` counter, tagged by `event.name` and `handler.name`.
- **Traces:** In your distributed tracing UI, look for exceptions recorded on `Event.{EventName}` activities.

### If You Need Fail-Fast

The continue-on-failure policy is intentional for independent side effects. If you need a side effect that must succeed or fail atomically with the domain operation, do not use an event handler. Instead:

- Include the logic directly in the domain action or service method.
- Use a single handler that combines the critical and non-critical side effects with its own error handling.

---

## InvalidOperationException: IDomainEventDispatcher Not Registered

```
System.InvalidOperationException: No service for type 'Pragmatic.Events.IDomainEventDispatcher' has been registered.
```

### Cause

`AddInMemoryDomainEvents()` was not called before the service provider was built.

### Fix

```csharp
builder.Services.AddInMemoryDomainEvents();
```

This must appear before `builder.Build()`. If using `Pragmatic.Composition` with `PragmaticApp.RunAsync()`, the SG-generated host handles this automatically when the Events module is detected.

---

## Events Dispatched Twice on Retry

After a transient database failure, the retry succeeds but event handlers fire twice for the same events.

### Cause

You are dispatching events manually **before** clearing them from the entity, and the retry re-collects them.

### Fix

The framework handles this correctly by design: taking the events off the entity is what clears them, so there is no window in which the same event is both pending and already handed over. If you are dispatching by hand, follow the same pattern:

```csharp
var events = entity.DomainEvents.ToList();
entity.ClearDomainEvents();  // Clear BEFORE dispatch
await dispatcher.DispatchAsync(events, ct).ConfigureAwait(false);
```

---

## Events Accumulating on Entities Without Dispatch

The entity's `DomainEvents` list keeps growing across multiple operations, but handlers never fire.

### Possible Causes

1. **No interceptor registered.** See "Events Not Dispatching After SaveChanges" above.

2. **Entity not saved to database.** If you modify an entity and raise events but never call `SaveChangesAsync()`, the interceptor never fires. Events only dispatch on successful persistence.

3. **Entity created outside of EF Core tracking.** If you `new` an entity, raise events, but never `Add` it to a DbSet or the ChangeTracker, the interceptor will not see it.

### Fix

Ensure the entity is tracked and persisted:

```csharp
var reservation = Reservation.Create(/* ... */);
dbContext.Reservations.Add(reservation);  // Now tracked
await dbContext.SaveChangesAsync(ct);     // Interceptor fires
```

---

## Handlers Run But Authorization Blocks Downstream Actions

A handler calls a domain action via a boundary interface, but the action returns a `ForbiddenError` or `UnauthorizedError`.

### Cause

The `ICallContext` is not being resolved, so the handler does not run as an internal call. Authorization filters see the HTTP user's permissions (which may not include the permission needed by the handler's action).

### Possible Fixes

1. **Verify `Pragmatic.Actions` is referenced.** `ICallContext` is registered by the Actions module. Without it, the dispatcher's `GetService<ICallContext>()` returns `null` and internal call mode is not activated.

2. **Check that the boundary interface action uses the pipeline.** If the action is invoked through `IDomainActionInvoker<T>` (the normal path), the pipeline respects `IsInternalCall`. Direct method calls bypass the pipeline entirely.

3. **Do not use a custom `IDomainEventDispatcher` that skips internal call context.** The `InMemoryEventDispatcher` calls `callContext?.EnterInternalCall()` for each handler. A custom dispatcher must do the same.

---

## Events Not Dispatching When Using Sync SaveChanges()

You call the synchronous `SaveChanges()`, it commits, but no handlers fire.

### Cause

Handlers are asynchronous, so dispatch is too, and `IUnitOfWork` exposes only `SaveChangesAsync`. A synchronous `SaveChanges()` on the `DbContext` goes around the unit of work entirely: the rows are written, the declared events are still raised by the interceptor, and nobody takes them. There is no `GetAwaiter().GetResult()` bridge on purpose — that pattern can deadlock in ASP.NET Core and other synchronization-context-bearing hosts.

### Fix

Save through the unit of work, always asynchronously:

```csharp
await unitOfWork.SaveChangesAsync(ct);   // dispatches; dbContext.SaveChanges() does not
```

If a code path genuinely cannot go async and still needs events delivered, dispatch manually after the sync save (clear before dispatch — see "Events Dispatched Twice on Retry"), or route the write through an async path.

---

## FAQ

### Can I have multiple handlers for the same event?

Yes. Register as many `IDomainEventHandler<T>` implementations as needed. They all run for every dispatch of that event type, sorted by `Order`.

### What happens if no handlers are registered for an event?

Nothing. The dispatcher logs at `Debug` level: `"No handlers registered for event {EventName}"`, sets the handler count to 0, and returns. No exception is thrown.

### Can handlers raise new events?

Yes, but those events are not automatically dispatched. If a handler modifies an entity tracked by EF Core and raises new events, those events will be dispatched on the next `SaveChangesAsync()` call. If the handler does not save, the new events accumulate on the entity.

### Can I test handlers without EF Core?

Yes. Create the handler directly and call `HandleAsync`:

```csharp
var handler = new ReservationConfirmedHandler(mockBillingActions);
await handler.HandleAsync(new ReservationConfirmed(/* ... */));
```

Or test with the full dispatcher:

```csharp
var services = new ServiceCollection();
services.AddLogging();
services.AddInMemoryDomainEvents();
services.AddSingleton<IDomainEventHandler<ReservationConfirmed>>(handler);
var sp = services.BuildServiceProvider();
var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();
await dispatcher.DispatchAsync(new ReservationConfirmed(/* ... */));
```

### Is InMemoryEventDispatcher suitable for production?

Yes, for in-process side effects. It is the standard dispatcher for monolithic applications and single-process deployments. When you need at-least-once delivery and crash-safety, enable the **transactional outbox** — it ships in `Pragmatic.Events.EFCore` (namespace `Pragmatic.Events.EFCore.Outbox`), not `Pragmatic.Messaging`. See the outbox Quick Start in [Getting Started](/modules/events/getting-started/#transactional-outbox-at-least-once-delivery). For cross-service messaging over a broker, add `Pragmatic.Messaging`.

### Can I replace InMemoryEventDispatcher with a custom implementation?

Yes. Implement `IDomainEventDispatcher` and register your implementation:

```csharp
services.AddScoped<IDomainEventDispatcher, MyCustomDispatcher>();
```

The EF Core interceptor depends on `IDomainEventDispatcher`, not on `InMemoryEventDispatcher` directly.

---

## Getting Help

- **GitHub Issues**: [github.com/pragmatic-design/Pragmatic.Design/issues](https://github.com/pragmatic-design/Pragmatic.Design/issues)
- **Showcase Examples**: See the `Showcase` project for working cross-boundary event implementations with Booking and Billing boundaries.
- **Internals Guide**: See [internals.md](/modules/events/internals/) for dispatcher mechanics, handler ordering, and observability details.
- **Concepts Guide**: See [concepts.md](/modules/events/concepts/) for architecture decisions and design rationale.
