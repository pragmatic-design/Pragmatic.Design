using Microsoft.Extensions.Logging;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;
using Warehouse.Orders.Contracts.Events;

namespace Warehouse.Stock.Infrastructure.MessageHandlers;

/// <summary>
///     An order was cancelled: Stock gives back what it held or picked for it.
/// </summary>
/// <remarks>
///     Idempotent per order (<c>RestoreStockAction</c>): a repeat is logged and acknowledged. Through the
///     internal interface, because a message arrives with no principal.
/// </remarks>
[MessageHandler]
internal sealed partial class RestoreTheStockOfACancelledOrder(
    IStockRestorationsInternalActions restorations,
    ILogger<RestoreTheStockOfACancelledOrder> logger) : IMessageHandler<OrderCancelled>
{
    public async Task HandleAsync(OrderCancelled message, MessageContext context, CancellationToken ct = default)
    {
        var restored = await restorations.RestoreStock(orderId: message.OrderId, ct: ct).ConfigureAwait(false);

        if (restored.IsFailure)
            throw new InvalidOperationException(
                $"The stock of cancelled order {message.OrderId} could not be restored: {restored.Error}");

        if (!restored.Value)
            LogAlreadyRestored(message.OrderId);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "The stock of order {OrderId} was already restored: nothing was written.")]
    private partial void LogAlreadyRestored(Guid orderId);
}
