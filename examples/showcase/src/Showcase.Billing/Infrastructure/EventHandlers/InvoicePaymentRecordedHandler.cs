using Microsoft.Extensions.Logging;
using Showcase.Billing.Events;

namespace Showcase.Billing.Infrastructure.EventHandlers;

/// <summary>
/// Records the payment <see cref="InvoicePaymentRecorded" /> announces: the invoice's number and total, which the event takes from the
/// invoice <c>MarkInvoicePaidAction</c> loaded, and who paid, from the action's input.
/// </summary>
[EventHandler]
public sealed partial class InvoicePaymentRecordedHandler(ILogger<InvoicePaymentRecordedHandler> logger)
    : IDomainEventHandler<InvoicePaymentRecorded>
{
    public Task HandleAsync(InvoicePaymentRecorded @event, CancellationToken ct = default)
    {
        LogPaymentRecorded(@event.InvoiceNumber, @event.TotalAmount, @event.PaidByUserId);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Invoice {InvoiceNumber} paid: {TotalAmount}, by {PaidByUserId}.")]
    private partial void LogPaymentRecorded(string invoiceNumber, decimal totalAmount, Guid paidByUserId);
}
