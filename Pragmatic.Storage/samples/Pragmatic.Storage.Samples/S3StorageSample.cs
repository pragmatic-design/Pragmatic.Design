using Amazon;
using Amazon.S3;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Storage.S3;

namespace Pragmatic.Storage.Samples;

/// <summary>
/// Setup-only sample for <see cref="S3FileStorage"/>. It demonstrates real construction and
/// configuration via <see cref="S3StorageOptions"/>, but does NOT perform network calls
/// (those require live AWS credentials and a real bucket). Works with any S3-compatible
/// endpoint (AWS, MinIO, Cloudflare R2, etc.).
/// </summary>
public static class S3StorageSample
{
    public static void Describe()
    {
        Console.WriteLine("--- AWS S3 Storage (setup-only, no network) ---");

        // 1. Options. PublicBaseUrl (optional) shapes the returned URI and is also the only
        //    accepted prefix when resolving a public URI back to a key; otherwise s3:// URIs are used.
        //    MaxFileSizeBytes (optional) rejects oversized seekable uploads before streaming to S3.
        var options = new S3StorageOptions
        {
            BucketName = "my-app-uploads",
            KeyPrefix = "uploads/",
            PublicBaseUrl = "https://cdn.example.com/uploads",
            MaxFileSizeBytes = 10 * 1024 * 1024,
        };
        Console.WriteLine($"BucketName:       {options.BucketName}");
        Console.WriteLine($"KeyPrefix:        {options.KeyPrefix}");
        Console.WriteLine($"PublicBaseUrl:    {options.PublicBaseUrl}");
        Console.WriteLine($"MaxFileSizeBytes: {options.MaxFileSizeBytes}");

        // 2. Construct the storage. The AWS SDK client carries region + credentials.
        var s3Client = new AmazonS3Client(new AmazonS3Config { RegionEndpoint = RegionEndpoint.EUWest1 });
        var storage = new S3FileStorage(s3Client, options, NullLogger<S3FileStorage>.Instance);
        Console.WriteLine($"Constructed: {storage.GetType().Name}");

        // 3. Real usage (commented — needs live AWS credentials / bucket):
        //    using var input = new MemoryStream("hello s3"u8.ToArray());
        //    var uri = await storage.SaveAsync(input, "greeting.txt", "photos");
        //    await using var stream = await storage.GetAsync(uri); // caller disposes the stream
        //    var present = await storage.ExistsAsync(uri);
        //    await storage.DeleteAsync(uri);
        //
        //    Note: Get/Exists/Delete resolve the key from the URI; only s3:// URIs or
        //    PublicBaseUrl-prefixed URIs are accepted (arbitrary URIs are rejected).

        Console.WriteLine("(Skipped live calls: requires AWS credentials.)");
        Console.WriteLine();
    }
}
