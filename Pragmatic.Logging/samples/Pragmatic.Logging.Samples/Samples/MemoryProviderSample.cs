using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Samples.Samples;

/// <summary>
///     The in-memory provider captures every <see cref="LogEntry"/> for inspection — ideal for
///     tests, diagnostics, and demos. Here we wire a <see cref="PragmaticMemoryProvider"/> into a
///     real logging pipeline, emit a few lines, then query the captured entries by level,
///     category, and message text and print the results.
/// </summary>
public static class MemoryProviderSample
{
    public static void Run()
    {
        Console.WriteLine("\n--- In-memory capturing sink ---");

        // Construct the memory provider up front so we can inspect it after logging.
        var memory = new PragmaticMemoryProvider("Memory", PragmaticMemoryConfiguration.ForMemory());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticLogging(
            global => { },
            builder => builder.AddProvider(_ => memory));

        using (var provider = services.BuildServiceProvider())
        {
            var logger = provider.GetRequiredService<ILogger<MemoryProviderSampleCategory>>();

            logger.LogInformation("Cache warmed with {Count} entries", 128);
            logger.LogWarning("Retry {Attempt} of {Max} for {Operation}", 2, 3, "FetchRates");
            logger.LogError("Operation {Operation} failed permanently", "FetchRates");
        }

        // Everything emitted above is now queryable in memory.
        Console.WriteLine($"Captured {memory.Count} entries total.");

        var errors = memory.GetLogEntries(LogLevel.Error);
        Console.WriteLine($"Errors: {errors.Count}");
        foreach (var entry in errors)
            Console.WriteLine($"  [{entry.GetShortLogLevelString()}] {entry.Message}");

        var retries = memory.GetLogEntriesContaining("Retry");
        Console.WriteLine($"Entries mentioning 'Retry': {retries.Count}");

        Console.WriteLine($"Has any warning? {memory.HasLogEntry(LogLevel.Warning)}");
    }

    private sealed class MemoryProviderSampleCategory;
}
