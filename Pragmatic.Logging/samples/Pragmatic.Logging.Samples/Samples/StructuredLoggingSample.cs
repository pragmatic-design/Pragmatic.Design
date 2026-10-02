using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Extensions;

namespace Pragmatic.Logging.Samples.Samples;

/// <summary>
///     Structured (template-parameter) logging plus the [LoggerMessage] source generator pattern.
///     The SG emits a strongly-typed extension method so the hot path avoids boxing/allocation.
/// </summary>
public static partial class StructuredLoggingSample
{
    public static void Run()
    {
        Console.WriteLine("\n--- Structured logging ---");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticLogging(
            global => { },
            builder => builder.AddConsole());

        using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<StructuredLoggingSampleCategory>>();

        // Template-parameter logging: values stay structured (not interpolated into the message).
        logger.LogInformation(
            "Order {OrderId} placed by customer {CustomerId} for {Amount:C}",
            "ORD-7823",
            "CUST-42",
            149.95m);

        // LoggerMessage source-generator pattern — fastest + AOT-safe, see partial method below.
        OrderAccepted(logger, "ORD-7824", "CUST-18", 32.10m);
        PaymentFailed(logger, "ORD-7825", reason: "insufficient_funds");
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Order {OrderId} accepted for {CustomerId} ({Amount:C})")]
    private static partial void OrderAccepted(
        ILogger logger, string orderId, string customerId, decimal amount);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Payment failed for order {OrderId}: {Reason}")]
    private static partial void PaymentFailed(
        ILogger logger, string orderId, string reason);

    private sealed class StructuredLoggingSampleCategory;
}
