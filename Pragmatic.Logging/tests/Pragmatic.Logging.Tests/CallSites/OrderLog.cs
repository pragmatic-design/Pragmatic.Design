using Microsoft.Extensions.Logging;
using Pragmatic.Privacy;

namespace Pragmatic.Logging.Tests.CallSites;

/// <summary>Call sites the generator writes, as an application declares them.</summary>
internal static partial class OrderLog
{
    public const string PlacedTemplate = "Order {OrderId} placed by {Customer} for {Amount} at {PlacedAt}";
    public const string ReceiptTemplate = "Receipt for {OrderId} sent to {Email}";

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = PlacedTemplate)]
    public static partial void Placed(ILogger logger, int orderId, string customer, decimal amount, DateTime placedAt);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = ReceiptTemplate)]
    public static partial void ReceiptSent(ILogger logger, int orderId, [PersonalData(DataCategory.Contact)] string email);

    public const string RegisteredTemplate = "Registered {Customer}";

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = RegisteredTemplate)]
    public static partial void Registered(ILogger logger, Customer customer);

    public const string ShippedTemplate = "Shipped {Shipment}";

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = ShippedTemplate)]
    public static partial void Shipped(ILogger logger, Shipment shipment);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information, Message = "Booked {Ledger}")]
    public static partial void Booked(ILogger logger, Ledger ledger);
}
