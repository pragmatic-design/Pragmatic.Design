using System.Net;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Storage.GoogleCloud;

/// <summary>
///     Google Cloud Storage file storage implementation.
///     Stores files as objects in a GCS bucket.
/// </summary>
public sealed partial class GoogleCloudFileStorage(
    StorageClient storageClient,
    GoogleCloudStorageOptions options,
    ILogger<GoogleCloudFileStorage> logger)
    : IFileStorage, IFileInfoProvider, ISignedUrlProvider
{
    /// <inheritdoc />
    /// <remarks>
    ///     The object name is <c>{ObjectPrefix}{container}/{GUID}{extension}</c>. The returned URI is
    ///     based on <see cref="GoogleCloudStorageOptions.PublicBaseUrl" /> when configured, otherwise
    ///     an internal <c>gs://bucket/object</c> URI. Oversized uploads are rejected with
    ///     <see cref="Pragmatic.Storage.FileSizeLimitExceededException" /> (an
    ///     <see cref="InvalidOperationException" />) when
    ///     <see cref="GoogleCloudStorageOptions.MaxFileSizeBytes" /> is set.
    /// </remarks>
    public async Task<Uri> SaveAsync(Stream content, string fileName, string container, CancellationToken ct = default)
    {
        // Guard oversized uploads before streaming to GCS. Seekable streams are rejected up front
        // via Length; non-seekable streams (HTTP bodies, chunked inputs) have no known length, so
        // we wrap them in a limiting stream that throws once the running byte total exceeds the
        // limit while the SDK consumes the payload — mirroring LocalDiskFileStorage.
        if (options.MaxFileSizeBytes > 0 && content.CanSeek && content.Length > options.MaxFileSizeBytes)
            throw new Pragmatic.Storage.FileSizeLimitExceededException(
                $"Upload rejected: file size {content.Length} exceeds the limit of {options.MaxFileSizeBytes} bytes.",
                options.MaxFileSizeBytes, content.Length);

        var inputStream = options.MaxFileSizeBytes > 0 && !content.CanSeek
            ? new Pragmatic.Storage.LimitedReadStream(content, options.MaxFileSizeBytes)
            : content;

        var ext = Path.GetExtension(fileName);
        var key = $"{options.ObjectPrefix}{container}/{Guid.NewGuid():N}{ext}";

        await storageClient.UploadObjectAsync(
            options.BucketName,
            key,
            Pragmatic.Storage.MimeTypes.GetMimeType(ext),
            inputStream,
            cancellationToken: ct).ConfigureAwait(false);

        LogStored(fileName, options.BucketName, key);

        return options.PublicBaseUrl is not null
            ? new Uri($"{options.PublicBaseUrl.TrimEnd('/')}/{key}", UriKind.Absolute)
            : new Uri($"gs://{options.BucketName}/{key}", UriKind.Absolute);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Downloads the object into an in-memory stream. Accepts only <c>gs://</c> URIs (whose
    ///     bucket must match the configured bucket) or URIs prefixed with the configured
    ///     <see cref="GoogleCloudStorageOptions.PublicBaseUrl" />; anything else is rejected with
    ///     <see cref="ArgumentException" />. Returns <see langword="null" /> when the object does
    ///     not exist (a 404). The returned stream must be disposed by the caller.
    /// </remarks>
    public async Task<Stream?> GetAsync(Uri fileUri, CancellationToken ct = default)
    {
        var key = ResolveKey(fileUri);
        var buffer = new MemoryStream();
        try
        {
            await storageClient.DownloadObjectAsync(options.BucketName, key, buffer, cancellationToken: ct)
                .ConfigureAwait(false);
            buffer.Position = 0;
            return buffer;
        }
        catch (GoogleApiException ex) when (IsNotFound(ex))
        {
            await buffer.DisposeAsync().ConfigureAwait(false);
            return null;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Reads object metadata without downloading the content. Accepts only <c>gs://</c> URIs
    ///     (whose bucket must match the configured bucket) or URIs prefixed with the configured
    ///     <see cref="GoogleCloudStorageOptions.PublicBaseUrl" />; anything else is rejected with
    ///     <see cref="ArgumentException" />.
    /// </remarks>
    public async Task<bool> ExistsAsync(Uri fileUri, CancellationToken ct = default)
    {
        var key = ResolveKey(fileUri);
        try
        {
            await storageClient.GetObjectAsync(options.BucketName, key, cancellationToken: ct).ConfigureAwait(false);
            return true;
        }
        catch (GoogleApiException ex) when (IsNotFound(ex))
        {
            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Idempotent: deleting an object that does not exist is a no-op. Accepts only <c>gs://</c>
    ///     URIs (whose bucket must match the configured bucket) or URIs prefixed with the configured
    ///     <see cref="GoogleCloudStorageOptions.PublicBaseUrl" />; anything else is rejected with
    ///     <see cref="ArgumentException" />.
    /// </remarks>
    public async Task DeleteAsync(Uri fileUri, CancellationToken ct = default)
    {
        var key = ResolveKey(fileUri);
        try
        {
            await storageClient.DeleteObjectAsync(options.BucketName, key, cancellationToken: ct).ConfigureAwait(false);
            LogDeleted(options.BucketName, key);
        }
        catch (GoogleApiException ex) when (IsNotFound(ex))
        {
            // Object does not exist — treat as a no-op, consistent with LocalDiskFileStorage.
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Reads object metadata (a single GET) without downloading the content. Returns
    ///     <see langword="null" /> when the object does not exist (a 404). Accepts only <c>gs://</c>
    ///     URIs (whose bucket must match the configured bucket) or URIs prefixed with the configured
    ///     <see cref="GoogleCloudStorageOptions.PublicBaseUrl" />; anything else is rejected with
    ///     <see cref="ArgumentException" />.
    /// </remarks>
    public async Task<StoredFileInfo?> GetInfoAsync(Uri fileUri, CancellationToken ct = default)
    {
        var key = ResolveKey(fileUri);
        try
        {
            var metadata = await storageClient.GetObjectAsync(options.BucketName, key, cancellationToken: ct)
                .ConfigureAwait(false);
            return new StoredFileInfo
            {
                SizeBytes = (long?)metadata.Size ?? 0,
                ContentType = metadata.ContentType,
                LastModified = metadata.UpdatedDateTimeOffset,
                FileUri = fileUri,
            };
        }
        catch (GoogleApiException ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Generates a read-only signed URL valid for <paramref name="expiry" />. Signing requires a
    ///     service-account credential: supply a <see cref="GoogleCloudStorageOptions.UrlSigner" /> or
    ///     configure <c>GOOGLE_APPLICATION_CREDENTIALS</c>. When neither is available the request
    ///     fails with <see cref="NotSupportedException" />. Accepts only <c>gs://</c> URIs (whose
    ///     bucket must match the configured bucket) or URIs prefixed with the configured
    ///     <see cref="GoogleCloudStorageOptions.PublicBaseUrl" />; anything else is rejected with
    ///     <see cref="ArgumentException" />.
    /// </remarks>
    public async Task<Uri> GetDownloadUrlAsync(Uri fileUri, TimeSpan expiry, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var key = ResolveKey(fileUri);

        var signer = ResolveSigner()
                     ?? throw new NotSupportedException(
                         "Signed URLs require service-account credentials; configure GOOGLE_APPLICATION_CREDENTIALS or pass a signer.");

        var url = await signer.SignAsync(options.BucketName, key, expiry, HttpMethod.Get, cancellationToken: ct)
            .ConfigureAwait(false);

        return new Uri(url, UriKind.Absolute);
    }

    /// <summary>
    ///     Resolves the <see cref="UrlSigner" /> to use: the one supplied on the options, otherwise a
    ///     signer built from application-default credentials when <c>GOOGLE_APPLICATION_CREDENTIALS</c>
    ///     points at a credentials file. Returns <see langword="null" /> when no signer can be built.
    /// </summary>
    private UrlSigner? ResolveSigner()
    {
        if (options.UrlSigner is not null)
            return options.UrlSigner;

        // Only probe application-default credentials when explicitly pointed at a credentials file,
        // so an unconfigured environment fails fast (NotSupportedException) without a metadata probe.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS")))
            return null;

        try
        {
            return UrlSigner.FromCredential(GoogleCredential.GetApplicationDefault());
        }
        catch (Exception ex)
        {
            LogSignerUnavailable(ex);
            return null;
        }
    }

    /// <summary>
    ///     Resolves a URI to a GCS object name.
    ///     Accepts <c>gs://bucket/object</c> URIs (bucket must match the configured bucket) and
    ///     public base-URL-prefixed URIs only. Throws <see cref="ArgumentException" /> for any other
    ///     URI to prevent arbitrary object-name injection.
    /// </summary>
    private string ResolveKey(Uri fileUri)
    {
        // gs://bucket/object → object
        if (fileUri.Scheme == "gs")
        {
            if (!string.Equals(fileUri.Host, options.BucketName, StringComparison.Ordinal))
                throw new ArgumentException(
                    $"Cannot resolve '{fileUri}': its bucket '{fileUri.Host}' does not match the configured bucket '{options.BucketName}'.",
                    nameof(fileUri));

            return fileUri.AbsolutePath.TrimStart('/');
        }

        // Public URL → strip base URL prefix
        if (options.PublicBaseUrl is not null)
        {
            var path = fileUri.ToString();
            var prefix = options.PublicBaseUrl.TrimEnd('/') + "/";
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return path[prefix.Length..];
        }

        // Reject everything else — never fall through to raw ToString() which allows
        // arbitrary object-name injection from caller-supplied URIs.
        throw new ArgumentException(
            $"Cannot resolve '{fileUri}' to a GCS object name: URI must use the 'gs://' scheme or be prefixed with the configured PublicBaseUrl.",
            nameof(fileUri));
    }

    private static bool IsNotFound(GoogleApiException ex)
        => ex.HttpStatusCode == HttpStatusCode.NotFound || ex.Error?.Code == 404;

    [LoggerMessage(Level = LogLevel.Information, Message = "GCS: stored {OriginalName} → gs://{Bucket}/{Key}")]
    private partial void LogStored(string originalName, string bucket, string key);

    [LoggerMessage(Level = LogLevel.Information, Message = "GCS: deleted gs://{Bucket}/{Key}")]
    private partial void LogDeleted(string bucket, string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "GCS: could not build a URL signer from application-default credentials")]
    private partial void LogSignerUnavailable(Exception exception);
}
