using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Storage.Local;

namespace Pragmatic.Storage.Samples;

/// <summary>
/// Demonstrates path-traversal protection in <see cref="LocalDiskFileStorage"/>. The
/// <c>container</c> argument is validated by <c>ResolveContainerDirectory</c> (rejecting
/// traversal/absolute segments and empty values); read/delete URIs are validated by
/// <c>TryResolveSafePath</c> (resolving outside the root → not found).
/// </summary>
public static class PathTraversalSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("--- Path-traversal rejection (LocalDiskFileStorage) ---");

        var demoRoot = SampleSupport.CreateTempRoot();
        try
        {
            var storage = new LocalDiskFileStorage(demoRoot, NullLogger<LocalDiskFileStorage>.Instance);

            // 1. A normal container is accepted.
            using (var ok = new MemoryStream("safe"u8.ToArray()))
            {
                var uri = await storage.SaveAsync(ok, "2026.txt", "invoices");
                Console.WriteLine($"Accepted container 'invoices' -> {uri}");
            }

            // 2. Containers that escape the storage root (traversal / absolute) are rejected.
            var maliciousContainers = new[]
            {
                @"..\..\secret",
                "../../etc",
                "",            // empty container
                "   ",         // whitespace
            };

            foreach (var container in maliciousContainers)
            {
                var label = string.IsNullOrWhiteSpace(container) ? "<empty/whitespace>" : container;
                try
                {
                    using var input = new MemoryStream("payload"u8.ToArray());
                    await storage.SaveAsync(input, "x.txt", container);
                    Console.WriteLine($"  ACCEPTED (UNEXPECTED): {label}");
                }
                catch (ArgumentException)
                {
                    Console.WriteLine($"  Rejected container: {label}");
                }
            }

            // 3. On read paths, an absolute or traversal URI resolves to "not found" rather than escaping.
            var traversal = new Uri("/../../secret.txt", UriKind.Relative);
            Console.WriteLine($"GetAsync(traversalUri) is null: {(await storage.GetAsync(traversal)) is null}");
            Console.WriteLine($"ExistsAsync(traversalUri): {await storage.ExistsAsync(traversal)}");
        }
        finally
        {
            SampleSupport.Cleanup(demoRoot);
        }

        Console.WriteLine();
    }
}
