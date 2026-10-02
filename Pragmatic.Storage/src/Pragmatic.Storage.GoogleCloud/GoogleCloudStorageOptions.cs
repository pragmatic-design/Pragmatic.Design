using Google.Cloud.Storage.V1;

namespace Pragmatic.Storage.GoogleCloud;

/// <summary>
///     Configuration for Google Cloud Storage.
/// </summary>
public sealed class GoogleCloudStorageOptions
{
    /// <summary>Google Cloud Storage bucket name.</summary>
    public required string BucketName { get; set; }

    /// <summary>Optional object name prefix (e.g. "uploads/").</summary>
    public string ObjectPrefix { get; set; } = "";

    /// <summary>
    ///     Public base URL for direct access (e.g. a CDN URL fronting the bucket).
    ///     Null = returns <c>gs://bucket/object</c> URIs instead.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    ///     Maximum accepted size of a single uploaded file, in bytes.
    ///     <c>0</c> (default) means no limit. Set a positive value to reject oversized
    ///     uploads before streaming to the bucket, preventing unexpected memory use and
    ///     transfer costs.
    /// </summary>
    public long MaxFileSizeBytes { get; set; }

    /// <summary>
    ///     Optional pre-built <see cref="Google.Cloud.Storage.V1.UrlSigner" /> used by
    ///     <see cref="ISignedUrlProvider.GetDownloadUrlAsync" />. Signing a URL requires a
    ///     service-account credential (a private key), which the plain
    ///     <see cref="StorageClient" /> does not carry, so it must be supplied here or via the
    ///     <c>GOOGLE_APPLICATION_CREDENTIALS</c> environment variable. When null and no
    ///     credentials file is configured, signed-URL requests fail with
    ///     <see cref="NotSupportedException" />.
    /// </summary>
    public UrlSigner? UrlSigner { get; set; }
}
