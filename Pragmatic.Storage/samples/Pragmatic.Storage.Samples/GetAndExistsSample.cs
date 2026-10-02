using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Storage.Local;

namespace Pragmatic.Storage.Samples;

/// <summary>
/// Demonstrates <see cref="IFileStorage.GetAsync"/> and <see cref="IFileStorage.ExistsAsync"/>
/// for reading content back and probing for presence. Both take the <see cref="Uri"/> returned
/// by <c>SaveAsync</c>.
/// </summary>
public static class GetAndExistsSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("--- Get & Exists (LocalDiskFileStorage) ---");

        var demoRoot = SampleSupport.CreateTempRoot();
        try
        {
            var storage = new LocalDiskFileStorage(demoRoot, NullLogger<LocalDiskFileStorage>.Instance);

            // Save a file and keep the returned URI — that is the handle for Get/Exists/Delete.
            var payload = """{ "status": "ok", "items": 3 }"""u8.ToArray();
            Uri fileUri;
            using (var input = new MemoryStream(payload))
            {
                fileUri = await storage.SaveAsync(input, "report.json", "reports");
            }
            Console.WriteLine($"Saved URI: {fileUri}");

            // ExistsAsync: probe presence before reading.
            Console.WriteLine($"ExistsAsync(savedUri): {await storage.ExistsAsync(fileUri)}");

            var bogus = new Uri("/files/reports/does-not-exist.json", UriKind.Relative);
            Console.WriteLine($"ExistsAsync(bogusUri): {await storage.ExistsAsync(bogus)}");

            // GetAsync: returns a readable stream, or null if not found.
            // The caller OWNS the returned stream and must dispose it (use await using).
            await using (var streamFound = await storage.GetAsync(fileUri))
            {
                if (streamFound is not null)
                {
                    using var reader = new StreamReader(streamFound);
                    Console.WriteLine($"GetAsync(savedUri) content: {await reader.ReadToEndAsync()}");
                }
            }

            // GetAsync for a missing file returns null.
            var streamMissing = await storage.GetAsync(bogus);
            Console.WriteLine($"GetAsync(bogusUri) is null: {streamMissing is null}");
        }
        finally
        {
            SampleSupport.Cleanup(demoRoot);
        }

        Console.WriteLine();
    }
}
