using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Filtering;

namespace Pragmatic.Logging.Samples.Samples;

/// <summary>
///     Demonstrates redaction end-to-end: category/provider filters drop the noise,
///     then an explicit <c>EnableRedaction()</c> on the console provider masks
///     sensitive structured properties (email, apiKey) and sensitive fragments in
///     the message itself (email regex). Without <c>EnableRedaction()</c> the sink
///     emits the values in the clear — toggling the call makes the effect visible.
/// </summary>
public static class RedactionSample
{
    public static void Run()
    {
        Console.WriteLine("\n--- Filters & sensitive data handling ---");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticLogging(
            global =>
            {
                // Drop log lines from infrastructure categories we never want in the
                // customer-facing output (health probes, monitoring, etc.).
                global.FilterExpression = f => !f.HealthCheck() && !f.MonitoringTools();
            },
            builder =>
            {
                builder.AddConsole(config =>
                {
                    // The console provider only surfaces Information and above.
                    config.FilterExpression = f => f.Level(LogLevel.Information);

                    // Turn on the redaction pipeline for this sink. The default
                    // SensitivePropertyNames list matches email/key/token/apikey, and
                    // MessageRedactionPatterns masks inline email/SSN/card fragments.
                    // Structured properties are replaced with "[REDACTED]" — see the
                    // output below for the before/after effect on Email/ApiKey/IP.
                    config.EnableRedaction();
                });
            });

        using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<RedactionSampleCategory>>();

        // Sensitive parameters kept as structured properties so the redaction
        // pipeline can recognize them by name (email, apiKey, etc.).
        logger.LogInformation(
            "User sign-in attempt: email={Email}, ip={IpAddress}",
            "alice@example.com",
            "203.0.113.42");

        logger.LogInformation(
            "Outbound API call: endpoint={Endpoint}, apiKey={ApiKey}",
            "https://api.partner.example.com/v1/orders",
            "sk_live_8f3b2c9e4d1a");

        logger.LogInformation(
            "Charged order {OrderId} for customer {CustomerEmail}",
            "ORD-7899",
            "bob@example.com");
    }

    private sealed class RedactionSampleCategory;
}
