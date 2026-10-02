using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Storage.Local;

namespace Pragmatic.Storage.Samples;

/// <summary>
/// Demonstrates the basic <c>SaveAsync</c> / <c>DeleteAsync</c> lifecycle on local-disk storage.
/// </summary>
public static class SaveAndDeleteSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("--- Save & Delete (LocalDiskFileStorage) ---");

        var demoRoot = SampleSupport.CreateTempRoot();
        try
        {
            var storage = new LocalDiskFileStorage(demoRoot, NullLogger<LocalDiskFileStorage>.Instance);

            // SaveAsync(content, fileName, container) returns a relative URI such as
            // "/files/uploads/{guid}.txt". The file is stored with a random name to avoid collisions.
            var content = "Hello, Pragmatic.Storage!"u8.ToArray();
            using var stream = new MemoryStream(content);
            var fileUri = await storage.SaveAsync(stream, "greeting.txt", "uploads");
            Console.WriteLine($"Saved file, URI: {fileUri}");

            // Verify it landed on disk under {root}/files/{container}/.
            var physicalPath = Path.Combine(demoRoot, fileUri.ToString().TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            Console.WriteLine($"Exists on disk: {File.Exists(physicalPath)}");

            // DeleteAsync takes the same URI returned by SaveAsync.
            await storage.DeleteAsync(fileUri);
            Console.WriteLine($"Deleted. Exists now: {File.Exists(physicalPath)}");
        }
        finally
        {
            SampleSupport.Cleanup(demoRoot);
        }

        Console.WriteLine();
    }
}
