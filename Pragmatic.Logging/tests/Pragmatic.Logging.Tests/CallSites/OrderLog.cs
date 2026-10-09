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

    public const string FailedTemplate = "Payment for {OrderId} failed";

    [LoggerMessage(EventId = 1006, Level = LogLevel.Error, Message = FailedTemplate)]
    public static partial void Failed(ILogger logger, Exception exception, int orderId);

    public const string StockLowTemplate = "Stock low for {Sku}";

    // EventId = 0 on purpose: without it the generator derives one from the name, and a line with no event is
    // the case this call site is for.
    [LoggerMessage(EventId = 0, Level = LogLevel.Warning, Message = StockLowTemplate)]
    public static partial void StockLow(ILogger logger, string sku);

    // A number with a format, whose message bytes are not its JSON; a boolean; a nullable number.
    public const string BatchTemplate = "Batch {Count:D4} done {Ok} retries {Retries} total {Total}";

    [LoggerMessage(EventId = 1008, Level = LogLevel.Information, Message = BatchTemplate)]
    public static partial void Batch(ILogger logger, int count, bool ok, int? retries, decimal total);

    // Quotes in the template, none in the value: a message to escape, a property that is not.
    public const string SaidTemplate = "Said \"{Word}\"";

    [LoggerMessage(EventId = 1009, Level = LogLevel.Information, Message = SaidTemplate)]
    public static partial void Said(ILogger logger, string word);

    public const string QuotedTemplate = "Café \"{Name}\" <ok> & ünïcode";

    [LoggerMessage(EventId = 1007, Level = LogLevel.Information, Message = QuotedTemplate)]
    public static partial void Quoted(ILogger logger, string name);
}
