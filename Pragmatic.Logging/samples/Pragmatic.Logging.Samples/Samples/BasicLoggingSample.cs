using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Extensions;

namespace Pragmatic.Logging.Samples.Samples;

/// <summary>
///     Minimum viable Pragmatic.Logging setup: IServiceCollection → AddPragmaticLogging
///     with global + provider callbacks → resolve ILogger → emit. Every other sample
///     in the portfolio is a progressive addition on top of this pattern.
/// </summary>
public static class BasicLoggingSample
{
    public static void Run()
    {
        Console.WriteLine("\n--- Basic logging ---");

        var services = new ServiceCollection();
        services.AddLogging();  // registers the base ILoggerFactory / ILogger<T>
        services.AddPragmaticLogging(
            global => { /* no global filter for the basic sample */ },
            builder => builder.AddConsole());

        using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<BasicLoggingUser>>();

        logger.LogDebug("Debug diagnostic emitted");
        logger.LogInformation("Basic info line");
        logger.LogWarning("Something looks off, but we continue");
        logger.LogError("Something broke — but no exception was thrown");

        try
        {
            throw new InvalidOperationException("Simulated failure");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Caught an exception during the happy-path demo");
        }
    }

    /// <summary>Placeholder type used as the ILogger&lt;T&gt; category.</summary>
    private sealed class BasicLoggingUser;
}
