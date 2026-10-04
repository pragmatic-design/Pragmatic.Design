---
title: "Getting Started"
description: "> Build a message handler with resilience from zero in 5 minutes."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Messaging/docs/getting-started.md
sidebar:
  order: 2
---
> Build a message handler with resilience from zero in 5 minutes.

## Prerequisites

- .NET 10 SDK
- A Pragmatic.Design project with `[Module]` and `[Boundary]` configured
- `Pragmatic.SourceGenerator` referenced as Analyzer

## Pragmatic.Events vs Pragmatic.Messaging

Before we start, an important distinction:

| | Pragmatic.Events | Pragmatic.Messaging |
|---|---|---|
| **Scope** | In-process, same boundary | Cross-boundary, cross-service |
| **Dispatch** | Synchronous, same transaction | Async, decoupled, outbox-backed |
| **Handler** | `IDomainEventHandler<T>` | `IMessageHandler<T>` |
| **Use when** | Side effects within same boundary | Communication between boundaries/services |

**The bridge**: The SG generates a `MessageHandlerEventAdapter<T>` that lets domain events (from `Pragmatic.Events`) also reach message handlers. This is optional: you can use messaging standalone.

This tutorial covers **Pragmatic.Messaging**, the cross-boundary, async messaging system.

## Step 1: Define a Message

A message is any record/class. No base class required.

```csharp
// Showcase.Billing/Messages/InvoiceApprovalRequested.cs
namespace Showcase.Billing.Messages;

public sealed record InvoiceApprovalRequested(
    Guid InvoiceId,
    Guid ApproverId,
    decimal Amount,
    string Currency);
```

## Step 2: Create a Message Handler

```csharp
// Showcase.Billing/Handlers/InvoiceApprovalHandler.cs
using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;
using Pragmatic.Resilience.Attributes;

namespace Showcase.Billing.Handlers;

[MessageHandler]
[Retry(MaxAttempts = 3, Strategy = BackoffStrategy.ExponentialWithJitter, BaseDelayMs = 200)]
public sealed partial class InvoiceApprovalHandler(
    IInvoiceService invoiceService,
    ILogger<InvoiceApprovalHandler> logger) : IMessageHandler<InvoiceApprovalRequested>
{
    public async Task HandleAsync(
        InvoiceApprovalRequested message,
        MessageContext context,
        CancellationToken ct = default)
    {
        await invoiceService.ApproveAsync(message.InvoiceId, message.ApproverId, ct);
        logger.LogInformation("Invoice {Id} approved by {Approver}", message.InvoiceId, message.ApproverId);
    }
}
```

Key points:
- `[MessageHandler]`: SG discovers and generates a pipeline wrapper
- `[Retry]`: generates a retry loop with exponential backoff + jitter (inline, no Polly)
- `partial`: required for SG to add `[LoggerMessage]` stubs
- `IMessageHandler<T>`: the contract. `T` is your message type.

## Step 3: Publish a Message

From anywhere in your code, inject `IMessageBus` and publish:

```csharp
public class InvoiceWorkflow(IMessageBus bus)
{
    public async Task RequestApproval(Guid invoiceId, Guid approverId, decimal amount)
    {
        var message = new InvoiceApprovalRequested(invoiceId, approverId, amount, "EUR");
        await bus.PublishAsync(message);
    }
}
```

With context (correlation, tenant):

```csharp
var context = MessageContext.New(
    correlationId: orderId.ToString(),
    tenantId: currentTenant);
await bus.PublishAsync(message, context);
```

## Step 4: Configure the Transport

```csharp
// Program.cs
await PragmaticApp.RunAsync(args, app =>
{
    app.UseMessaging(msg =>
    {
        msg.UseChannels(ch =>
        {
            ch.Capacity = 1000;
            ch.ConsumerCount = 2;
        });
    });
});
```

The SG auto-registers handlers: no manual DI needed.

## Step 5: Build and Verify

```bash
dotnet build
```

Check generated files in `obj/Debug/net10.0/generated/`:

| File | Content |
|------|---------|
| `InvoiceApprovalHandler.Pipeline.g.cs` | Retry loop (3 attempts, exponential+jitter) + telemetry |
| `_Infra.Messaging.Registration.g.cs` | `AddPragmaticMessageHandlers()`: auto-registers all handlers |
| `_Infra.Messaging.TypeRegistry.g.cs` | AOT-safe `switch` for message deserialization |

## What Happens at Runtime

1. `bus.PublishAsync(message)` dispatches to all registered `IMessageHandler<T>`
2. `InvoiceApprovalHandler_Pipeline.ExecuteAsync()` runs:
   - Idempotency check (if `EnableIdempotency()` configured)
   - Retry loop: attempt 1 → on failure, wait ~200ms → attempt 2 → wait ~600ms → attempt 3
   - Telemetry: Activity span, duration metric, handler counter
   - Logging: started/completed/failed via `[LoggerMessage]`
3. On success: metrics recorded, activity closed
4. On final failure: exception propagates, dead letter store captures message

## Domain Events Bridge (Optional)

If you also use `Pragmatic.Events`, the SG generates an adapter so domain events reach your message handlers:

```csharp
// Entity raises a domain event (Pragmatic.Events)
reservation.RaiseDomainEvent(new ReservationConfirmed(...));

// The SG generates MessageHandlerEventAdapter<ReservationConfirmed>
// which bridges IDomainEventHandler<T> → IMessageHandler<T>
// So your [MessageHandler] for ReservationConfirmed also runs.
```

This bridge is automatic when both Events and Messaging are referenced. No config needed.

## Next Steps

- [Concepts](/modules/messaging/concepts/): architecture, pipeline diagram, transport model
- [Common Mistakes](/modules/messaging/common-mistakes/): top 10 pitfalls
- [Troubleshooting](/modules/messaging/troubleshooting/): checklists, diagnostics, FAQ
- **Showcase**: `examples/showcase/src/Showcase.Billing/Events/Handlers/` for real-world patterns
