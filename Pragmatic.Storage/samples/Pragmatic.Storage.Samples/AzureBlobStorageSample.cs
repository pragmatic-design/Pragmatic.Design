using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Storage.Azure;

namespace Pragmatic.Storage.Samples;

/// <summary>
/// Setup-only sample for <see cref="AzureBlobFileStorage"/>. It demonstrates real construction and
/// configuration via <see cref="AzureBlobStorageOptions"/>, but does NOT perform network calls
/// (those require a live Azure Storage account / connection string).
/// </summary>
public static class AzureBlobStorageSample
{
    public static void Describe()
    {
        Console.WriteLine("--- Azure Blob Storage (setup-only, no network) ---");

        // 1. Options. ContainerPrefix is prepended to the logical container name
        //    (e.g. prefix "myapp-" + container "photos" -> blob container "myapp-photos").
        var options = new AzureBlobStorageOptions
        {
            ContainerPrefix = "myapp-",
        };
        Console.WriteLine($"ContainerPrefix: \"{options.ContainerPrefix}\"");

        // 2. Construct the storage. The Azure SDK client is built from a connection string;
        //    "UseDevelopmentStorage=true" points at Azurite for local development.
        const string connectionString = "UseDevelopmentStorage=true";
        var blobService = new BlobServiceClient(connectionString);
        var storage = new AzureBlobFileStorage(blobService, options, NullLogger<AzureBlobFileStorage>.Instance);
        Console.WriteLine($"Constructed: {storage.GetType().Name} (account host: {blobService.Uri.Host})");

        // 3. Real usage (commented — needs a live account or a running Azurite emulator):
        //    using var input = new MemoryStream("hello azure"u8.ToArray());
        //    var uri = await storage.SaveAsync(input, "greeting.txt", "photos");
        //    await using var stream = await storage.GetAsync(uri); // caller disposes the stream
        //    var present = await storage.ExistsAsync(uri);
        //    await storage.DeleteAsync(uri);
        //
        //    Note: Get/Exists/Delete validate that the URI host matches the configured account,
        //    so you must pass back the URI returned by SaveAsync.

        Console.WriteLine("(Skipped live calls: requires Azure Storage credentials / Azurite.)");
        Console.WriteLine();
    }
}
