using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Extensions;

namespace Pragmatic.Logging.Samples.Samples;

/// <summary>
///     Route the same log stream to multiple sinks simultaneously. In this sample
///     we keep it to two console providers configured with different minimum levels,
///     so the scenario is self-contained (no file system side effects to clean up).
///     The same shape applies to file/JSON providers once you add them.
/// </summary>
public static class MultipleProvidersSample
{
    public static void Run()
    {
        Console.WriteLine("\n--- Multiple providers (two consoles, different levels) ---");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticLogging(
            global => { },
            builder =>
            {
                // "High signal" console — only warnings and above.
                builder.AddConsole(config =>
                {
                    config.FilterExpression = f => f.Level(LogLevel.Warning);
                });
                // "Verbose" console — every Information line.
                // In a real app you'd swap this for AddFile / AddJson / AddEnhancedJson
                // to route to a persistent sink.
                builder.AddConsole(config =>
                {
                    config.FilterExpression = f => f.Level(LogLevel.Information);
                });
            });

        using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<MultipleProvidersSampleCategory>>();

        logger.LogInformation("System boot — tenant provisioning starting");
        logger.LogInformation(
            "Tenant {TenantId} provisioned in {ElapsedMs}ms", "acme", 142);
        logger.LogWarning(
            "Quota near limit for tenant {TenantId}: {UsedPercent}%", "acme", 87);
        logger.LogError(
            "Tenant {TenantId} exceeded hard cap", "acme");
    }

    private sealed class MultipleProvidersSampleCategory;
}
