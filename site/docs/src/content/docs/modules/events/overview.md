---
title: "Pragmatic.Events"
description: "Domain events for .NET 10 — raise, dispatch, and handle with zero ceremony, including an EF Core"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Events/README.md
sidebar:
  order: 0
  label: Overview
---
Domain events for .NET 10 — raise, dispatch, and handle with zero ceremony, including an EF Core
interceptor for automatic post-`SaveChanges` dispatch.

## The Problem

As modules grow they need to react to each other. Confirm a reservation and billing must invoice,
notifications must email, audit must log. Direct calls couple the entity to every downstream service,
force you to edit the entity for each new reaction, and let a non-critical failure (email) break the
core operation.

```csharp
public async Task ConfirmAsync()
{
    Status = ReservationStatus.Confirmed;
    await _billing.CreateDraftInvoiceAsync(Id, GuestId, TotalAmount, Currency);
    await _notifications.SendConfirmationEmailAsync(GuestId, Id);   // failure here breaks confirm
}
```

## The Solution

The entity **raises** an event describing what happened; handlers in other boundaries **subscribe** and
react independently — no coupling, no edits when a new consumer appears, and isolated failure handling.

```csharp
public VoidResult<IError> Confirm()
{
    var result = TransitionTo(ReservationStatus.Confirmed);
    if (result.IsSuccess) RaiseEvent(new ReservationConfirmed(Id, GuestId, PropertyId));
    return result;
}

[EventHandler]
public sealed class CreateInvoiceOnConfirm : IDomainEventHandler<ReservationConfirmed>
{
    public Task HandleAsync(ReservationConfirmed e, CancellationToken ct) => /* ... */;
}
```

The generator registers every `[EventHandler]` class (PRAG1670 if it implements no
`IDomainEventHandler<T>`); the EF Core interceptor dispatches **after** a successful
`SaveChanges`. Ordering and continue-on-failure are built in. When you need crash-safe, at-least-once
delivery, put **`[EnableEventOutbox]`** on the boundary: its events are then written to the
transactional outbox in the same transaction as the entity change and delivered asynchronously
(at-least-once — handlers must be idempotent). The outbox ships in `Pragmatic.Events.EFCore`, not
`Pragmatic.Messaging`. Add
[Messaging](/modules/messaging/overview/) only for cross-service delivery over a broker.

## Installation

```bash
dotnet add package Pragmatic.Events
dotnet add package Pragmatic.Events.EFCore   # post-SaveChanges interceptor dispatch
```

## Status

**Functional** within 1.0.0-alpha — raise, dispatch and handle, handler ordering, continue-on-failure,
declarative lifecycle events (`[Raises<T>]`), the EF Core interceptor, and the transactional outbox. See
the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

| [Concepts](/modules/events/concepts/) | Raise/subscribe model, dispatch timing, ordering, transactional outbox |
| [Getting Started](/modules/events/getting-started/) | Raise an event, write a handler, wire the interceptor, lifecycle `[Raises<T>]`, outbox |
| [Internals](/modules/events/internals/) | Interceptor lifecycle, typed dispatch table, outbox mechanics, observability |
| [Common Mistakes](/modules/events/common-mistakes/) | The most frequent event pitfalls |
| [Troubleshooting](/modules/events/troubleshooting/) | Problem/solution guide |

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](/modules/events/overview/) ecosystem — see [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.Events is licensed under the **PolyForm Small Business 1.0.0** license (free for small
businesses; commercial license above the threshold).
