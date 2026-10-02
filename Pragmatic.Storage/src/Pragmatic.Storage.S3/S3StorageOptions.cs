namespace Pragmatic.Storage.S3;

/// <summary>
///     Configuration for S3-compatible storage.
/// </summary>
public sealed class S3StorageOptions
{
    /// <summary>S3 bucket name.</summary>
    public required string BucketName { get; set; }

    /// <summary>Optional key prefix (e.g. "uploads/").</summary>
    public string KeyPrefix { get; set; } = "";

    /// <summary>
    ///     Public base URL for direct access (e.g. CDN URL or S3 website endpoint).
    ///     Null = returns s3:// URIs instead.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    ///     Maximum accepted size of a single uploaded file, in bytes.
    ///     <c>0</c> (default) means no limit. Set a positive value to reject oversized
    ///     uploads before streaming to S3, preventing unexpected memory use and transfer costs.
    /// </summary>
    public long MaxFileSizeBytes { get; set; }
}
