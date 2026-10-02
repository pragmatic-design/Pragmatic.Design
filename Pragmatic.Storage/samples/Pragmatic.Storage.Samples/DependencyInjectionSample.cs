using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Storage.Local;

namespace Pragmatic.Storage.Samples;

/// <summary>
/// Demonstrates resolving <see cref="IFileStorage"/> from DI via the generic
/// <c>AddFileStorage&lt;TStorage&gt;()</c> and the <c>AddLocalDiskStorage()</c> helper.
/// </summary>
public static class DependencyInjectionSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("--- Dependency Injection (AddFileStorage<T> / AddLocalDiskStorage) ---");

        var demoRoot = SampleSupport.CreateTempRoot();
        try
        {
            // A) Generic registration: AddFileStorage<TStorage>() registers TStorage as a singleton
            //    IFileStorage and lets the container activate it. The implementation must be
            //    constructible by DI; InMemoryFileStorage (parameterless ctor) qualifies.
            var servicesA = new ServiceCollection();
            servicesA.AddFileStorage<InMemoryFileStorage>();
            await using (var providerA = servicesA.BuildServiceProvider())
            {
                var first = providerA.GetRequiredService<IFileStorage>();
                var second = providerA.GetRequiredService<IFileStorage>();
                Console.WriteLine($"AddFileStorage<InMemoryFileStorage>() resolved: {first.GetType().Name}");
                Console.WriteLine($"  singleton (same instance): {ReferenceEquals(first, second)}");

                using var input = new MemoryStream("via generic DI"u8.ToArray());
                var uri = await first.SaveAsync(input, "note.txt", "uploads");
                Console.WriteLine($"  saved + round-trip exists: {await first.ExistsAsync(uri)}");
            }

            // B) Convenience helper: AddLocalDiskStorage(basePath) registers LocalDiskFileStorage
            //    (resolving its ILogger from the container).
            var servicesB = new ServiceCollection();
            servicesB.AddLogging();
            servicesB.AddLocalDiskStorage(demoRoot);
            await using (var providerB = servicesB.BuildServiceProvider())
            {
                var storage = providerB.GetRequiredService<IFileStorage>();
                Console.WriteLine($"AddLocalDiskStorage(...) resolved: {storage.GetType().Name}");

                using var input = new MemoryStream("via helper"u8.ToArray());
                var uri = await storage.SaveAsync(input, "note.txt", "uploads");
                Console.WriteLine($"  saved: {uri}");
            }
        }
        finally
        {
            SampleSupport.Cleanup(demoRoot);
        }

        Console.WriteLine();
    }
}
