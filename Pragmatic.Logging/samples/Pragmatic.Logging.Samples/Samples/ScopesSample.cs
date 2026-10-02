using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Extensions;

namespace Pragmatic.Logging.Samples.Samples;

/// <summary>
///     BeginScope adds ambient properties (correlation id, tenant, request) that every
///     log line inside the scope inherits automatically.
/// </summary>
public static class ScopesSample
{
    public static void Run()
    {
        Console.WriteLine("\n--- Scopes and ambient context ---");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticLogging(
            global => { },
            builder => builder.AddConsole());

        using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<ScopesSampleCategory>>();

        // Outer scope: request-level context.
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = Guid.NewGuid().ToString("N")[..8],
            ["TenantId"] = "acme",
        }))
        {
            logger.LogInformation("Request received");

            // Inner scope: unit of work. Properties merge with the outer scope.
            using (logger.BeginScope(new Dictionary<string, object>
            {
                ["Operation"] = "CreateInvoice",
                ["InvoiceId"] = "INV-9001",
            }))
            {
                logger.LogInformation("Starting invoice creation");
                logger.LogInformation("Invoice created successfully");
            }

            logger.LogInformation("Request completed");
        }
    }

    private sealed class ScopesSampleCategory;
}
