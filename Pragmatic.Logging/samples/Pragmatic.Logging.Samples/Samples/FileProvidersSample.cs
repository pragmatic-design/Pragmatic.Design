using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Extensions;

namespace Pragmatic.Logging.Samples.Samples;

/// <summary>
///     Persistent sinks: a plain rolling <c>AddFile</c> and a structured <c>AddJson</c> provider.
///     Each is written to a temp file; the service provider is disposed to flush the buffers,
///     then the files are read back and printed so the persisted output is visible. Temp files
///     are cleaned up. (The async NDJSON provider, <c>AddNdjsonAsync</c>, exists for high-throughput
///     log shipping but is omitted here to keep the sample's output deterministic.)
/// </summary>
public static class FileProvidersSample
{
    public static void Run()
    {
        Console.WriteLine("\n--- File / JSON / NDJSON providers ---");

        var dir = Path.Combine(Path.GetTempPath(), "pragmatic-logging-samples", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var textPath = Path.Combine(dir, "app.log");
        var jsonPath = Path.Combine(dir, "app.json");

        try
        {
            // Build a pipeline writing to both persistent sinks at once.
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddPragmaticLogging(
                global => { },
                builder => builder
                    .AddFile(textPath)
                    .AddJson(jsonPath));

            // Dispose flushes batched/buffered providers to disk.
            using (var provider = services.BuildServiceProvider())
            {
                var logger = provider.GetRequiredService<ILogger<FileProvidersSampleCategory>>();
                logger.LogInformation("Service started on port {Port}", 8080);
                logger.LogWarning("Slow query took {ElapsedMs}ms", 1342);
                logger.LogError("Upstream {Service} returned {StatusCode}", "billing", 503);
            }

            // Buffers flush on dispose; give the writers a brief moment to settle.
            Thread.Sleep(200);

            PrintFile("Plain text  (app.log)", textPath);
            PrintFile("Structured  (app.json)", jsonPath);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    private static void PrintFile(string label, string path)
    {
        Console.WriteLine($"\n{label} → {path}");
        if (!File.Exists(path))
        {
            Console.WriteLine("  (no file written)");
            return;
        }

        var lines = File.ReadAllLines(path);
        if (lines.Length == 0)
        {
            Console.WriteLine("  (empty — buffered output may still be flushing)");
            return;
        }

        foreach (var line in lines.Take(5))
        {
            Console.WriteLine($"  {line}");
        }
        if (lines.Length > 5)
            Console.WriteLine($"  ... ({lines.Length} lines total)");
    }

    private sealed class FileProvidersSampleCategory;
}
