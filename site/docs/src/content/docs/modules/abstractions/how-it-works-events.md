---
title: "Events: what happened, said once"
description: "> **Scope**: `src/Pragmatic.Abstractions/Events/`, 10 files."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Abstractions/docs/how-it-works/events.md
sidebar:
  order: 12
---
> **Scope**: `src/Pragmatic.Abstractions/Events/`, 10 files.
> `IDomainEvent` · `IIntegrationEvent` · `IDomainEventHandler<TEvent>` · `IDomainEventDispatcher` ·
> `IHasDomainEvents` · `IRaisesLifecycleEvents` · `EntityLifecycle` · `EntityPropertyChanged<TEntity>` ·
> `PublicEventAttribute` · `ObsoleteEventAttribute`
>
> **Not covered here**: `[Raises<TEvent>]` is the attribute that drives `EntityLifecycle` and
> `IRaisesLifecycleEvents`, but it lives in `Authoring/` with the rest of the authoring surface;
> see [authoring](/modules/abstractions/how-it-works-authoring/). Its effect is described below, because without it two of the types
> in this folder have no way of being produced. Dispatchers, interceptors and handlers outside
> Abstractions are named where they matter, not opened.

For the member-by-member catalogue, see [interfaces](/modules/abstractions/interfaces/#6-events). This document is
about how the pieces fit together, and why they are shaped the way they are.

## The contract

`IDomainEvent` is a marker with two members: `OccurredAt` and `EventId`. `EventId` arrived after the
interface was already implemented across the framework, which is why it is a default interface
member returning `Guid.Empty`: adding a required member would have broken every existing event.
Events that derive from `DomainEvent` in `Pragmatic.Events` get a fresh GUID instead; the default is
a floor, not a recommendation.

`IDomainEventHandler<in TEvent>` is contravariant and carries an `Order`, defaulting to 0. Handlers
for one event run in ascending `Order`.

Two rules of the in-memory dispatcher are part of the contract, not an implementation detail:

- **A throwing handler does not stop the others.** The dispatcher logs the failure and continues
  down the list. One subscriber failing to react must not prevent the rest from reacting.
- **`OperationCanceledException` is the exception to that.** It propagates and aborts the chain,
  because cancellation is not a handler failing: it is the caller withdrawing.

`IDomainEventDispatcher` asks implementations not to enumerate the `events` sequence more than once,
and callers to pass a materialised collection rather than a deferred one.

## How events leave an entity

`IHasDomainEvents` is the entity-side contract: an entity accumulates events, something drains them.
The draining is done by the persistence interceptors in `Pragmatic.Events.EFCore`, which collect
events during `SaveChanges` and dispatch them from `SavedChangesAsync`, once the write has
succeeded, so a handler never sees a change that then failed to persist. Under EF Core's implicit
transaction that moment is after the commit; inside an explicit transaction it comes before
`CommitAsync`, and a handler reaching outside the process there can act on a change the commit never
makes durable. That case is logged as a warning when it happens, because whether a transaction is
open is a run-time fact and nothing at the call site shows it, and the outbox, not this path, is the
mechanism for effects that must wait for the commit. Only the asynchronous path dispatches:
synchronous `SaveChanges()` is deliberately not intercepted, because driving the async dispatch from
it can deadlock.

There are two ways an entity comes to implement it, and they have different requirements.

**Generated cascade setters implement it directly.** `[CascadeSource]` marks a *property*, not the
entity, and within the same assembly a `[CascadeOn]` pointing at a property infers the same thing.
When an entity has one, the generator writes the `_domainEvents` list, the `DomainEvents` property
and `ClearDomainEvents` into the entity itself, and the setter of each marked property appends an
`EntityPropertyChanged<TEntity>.Create(Id, nameof(Property), oldValue, newValue)`; the properties
that carry no such mark stay silent. The entity needs no base class.

**`[Raises<TEvent>]` requires `DomainEventSource`.** The lifecycle template emits a call to
`RaiseEvent`, which is a protected member of the `DomainEventSource` base class in
`Pragmatic.Events`. The generator walks the entity's base chain looking for a base type named
`DomainEventSource` and, when it finds none, reports **PRAG2750** as an error and emits nothing: a
compile-time answer rather than a runtime surprise. **PRAG2751** is the softer neighbour: a warning
when a constructor parameter of the raised event cannot be matched to anything available and is
passed `default`. **PRAG2753** is the third: the attribute on an entity's **method** generates nothing
(only the class-level declaration is wired, and a generator cannot add a raise to a body you wrote),
so the build refuses it and names `[RaisesEvent<T>]` on the state machine's target member and
`RaiseEvent(...)` in the body as the shapes that work.

## Lifecycle events

`[Raises<TEvent>]` takes the lifecycle stage as a positional argument, defaulting to `Created`:

```csharp
[Raises<ReservationCreated>]
[Raises<DrugDeleted>(EntityLifecycle.Deleted)]
public sealed class Reservation : DomainEventSource { … }
```

The generator implements `IRaisesLifecycleEvents.RaiseLifecycleEvents(EntityLifecycle)` on the
entity, switching on the stage and constructing the declared event. `LifecycleEventsInterceptor` in
`Pragmatic.Events.EFCore` calls it while EF Core is saving, for every tracked entity that implements
the interface; the existing dispatch path then publishes what was raised. The interceptor is
registered by `UseDomainEvents` on the `DbContextOptionsBuilder`, and the wiring the generator emits
for a boundary's DbContext adds it on its own. It raises and stops there: what dispatches, after the
commit and in the scope that asked for the write, is the invoker, the batch, or `EfCoreUnitOfWork`.

One mapping is worth stating explicitly: **a soft delete raises `Deleted`, not `Updated`**. To EF
Core the entity is `Modified`; the interceptor recognises an `ISoftDelete` whose `IsDeleted` is
marked modified and now true, and reports the stage the domain means, not the one the database
performed.

## `EntityPropertyChanged<TEntity>`

The event the generated setters raise, and the event the generated cascade handlers consume:
a cascade handler is an `IDomainEventHandler<EntityPropertyChanged<TSource>>`, registered by
generated code, so a property change on one entity propagates to its dependents without anyone
writing a subscription.

`OldValue` and `NewValue` are boxed `object?`. They are whatever the property held, which means an
address, a phone number or a token flows through this event exactly as it was stored. Anything that
logs, serialises or forwards these payloads is handling the raw property value, and should be
written on that assumption.

## Public events, and what marking one produces

`IIntegrationEvent` and `[PublicEvent]` both mean the same thing: this event is part of the contract
other systems may rely on, not an internal ripple. The generator treats them as alternatives: an
event qualifies by implementing the interface, or by carrying an attribute whose simple name is
`PublicEventAttribute`, matched by that name alone and not by namespace.

What they produce is the generated AsyncAPI document. `AsyncApiFeature` collects the concrete
`IDomainEvent` classes and records in the compilation (abstract types and non-`record` structs are
left out) and emits `PragmaticAsyncApi.Json`, an AsyncAPI 3.0 document as a `const string` in
`_Infra.AsyncApi.Generated.g.cs`, with one channel and one message per event. Events are keyed by
their simple name, so two events in the same assembly sharing a name yield a single entry. Every
event carries `x-pragmatic-public`, true or false; only an event carrying `[ObsoleteEvent]` also
carries `x-pragmatic-obsolete`. Message properties are named after the CLR properties, without
applying `[JsonPropertyName]` or the shared camelCase naming policy. The document is the
deliverable: a contract description that ships with the assembly and cannot drift from the events it
describes, because it is regenerated from them on every build.

## Dispatch without reflection

The dispatcher receives events typed as `IDomainEvent` and has to reach handlers typed by the
concrete event. The generator closes that gap: it emits an implementation of
`ITypedEventDispatchTable` into `_Infra.Events.DispatchTable.g.cs`, a compile-time map from event
type to typed invocation. That table is what makes the dispatch path trim- and AOT-safe for the
events it covers, and it covers the event types for which a handler was discovered. Anything the
tables do not recognise (an event with no handler, or every event when no table is present) falls
back to a `dynamic` invocation, which trimming and AOT cannot see through.

A companion analyzer, **PRAG0822**, reports cycles between handlers and the events they raise.

## External references

Named here, described where they live:

- **`Pragmatic.Events` → `DomainEventSource`**: the base class that provides `RaiseEvent` and the
  event list; required by `[Raises<TEvent>]`.
- **`Pragmatic.Events` → `InMemoryEventDispatcher`, `ITypedEventDispatchTable`**: the in-process
  dispatcher and the generated table it dispatches through.
- **`Pragmatic.Events.EFCore` → `LifecycleEventsInterceptor`, `EventOutboxInterceptor`**: the two
  save-time interceptors: one calls `RaiseLifecycleEvents`, one writes to the transactional outbox.
- **`Pragmatic.Persistence.EFCore` → `EfCoreUnitOfWork`**: takes the events off the tracked entities
  after a successful save, and hands them to the batch or dispatches them.
- **`Pragmatic.Actions` → `MutationInvoker`, `ActionInvokerBase`**: dispatch domain events raised
  during an action, as part of the invocation pipeline.
- **`Pragmatic.Messaging` → `MessageHandlerEventAdapter`**: lets a message handler be reached by a
  domain event.
- **`Pragmatic.Persistence` → `[CascadeSource]`**: the attribute behind the generated setters and
  cascade handlers built on `EntityPropertyChanged<TEntity>`.
