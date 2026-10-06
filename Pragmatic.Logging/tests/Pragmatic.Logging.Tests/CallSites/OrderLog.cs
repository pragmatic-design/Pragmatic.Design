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
}
