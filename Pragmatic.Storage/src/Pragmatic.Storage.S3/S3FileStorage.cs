using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Storage.S3;

/// <summary>
///     Amazon S3 / Cloudflare R2 file storage implementation.
///     Stores files as objects in an S3-compatible bucket.
/// </summary>
public sealed partial class S3FileStorage(IAmazonS3 s3, S3StorageOptions options, ILogger<S3FileStorage> logger)
    : IFileStorage, IFileInfoProvider, ISignedUrlProvider
{
    /// <inheritdoc />
    /// <remarks>
    ///     The object key is <c>{KeyPrefix}{container}/{GUID}{extension}</c>. The returned URI is
    ///     based on <see cref="S3StorageOptions.PublicBaseUrl" /> when configured, otherwise an
    ///     internal <c>s3://bucket/key</c> URI. Oversized uploads are rejected with
    ///     <see cref="Pragmatic.Storage.FileSizeLimitExceededException" /> (an
    ///     <see cref="InvalidOperationException" />) when
    ///     <see cref="S3StorageOptions.MaxFileSizeBytes" /> is set.
    /// </remarks>
    public async Task<Uri> SaveAsync(Stream content, string fileName, string container, CancellationToken ct = default)
    {
        // Guard oversized uploads before streaming to S3. Seekable streams are rejected up front
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
        var key = $"{options.KeyPrefix}{container}/{Guid.NewGuid():N}{ext}";

        var request = new PutObjectRequest
        {
            BucketName = options.BucketName,
            Key = key,
            InputStream = inputStream,
            ContentType = Pragmatic.Storage.MimeTypes.GetMimeType(ext),
        };

        await s3.PutObjectAsync(request, ct).ConfigureAwait(false);
        LogStored(fileName, options.BucketName, key);

        return options.PublicBaseUrl is not null
            ? new Uri($"{options.PublicBaseUrl.TrimEnd('/')}/{key}", UriKind.Absolute)
            : new Uri($"s3://{options.BucketName}/{key}", UriKind.Absolute);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Accepts only <c>s3://</c> URIs or URIs prefixed with the configured
    ///     <see cref="S3StorageOptions.PublicBaseUrl" />; anything else is rejected with
    ///     <see cref="ArgumentException" />. The returned stream must be disposed by the caller;
    ///     disposing it releases the underlying HTTP connection.
    /// </remarks>
    public async Task<Stream?> GetAsync(Uri fileUri, CancellationToken ct = default)
    {
        var key = ResolveKey(fileUri);
        try
        {
            // GetObjectResponse wraps the HTTP response body. We return a wrapper stream that
            // disposes the response (releasing the HTTP connection) when the caller disposes the stream.
            var response = await s3.GetObjectAsync(options.BucketName, key, ct).ConfigureAwait(false);
            return new S3ResponseStream(response);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Accepts only <c>s3://</c> URIs or URIs prefixed with the configured
    ///     <see cref="S3StorageOptions.PublicBaseUrl" />; anything else is rejected with
    ///     <see cref="ArgumentException" />.
    /// </remarks>
    public async Task<bool> ExistsAsync(Uri fileUri, CancellationToken ct = default)
    {
        var key = ResolveKey(fileUri);
        try
        {
            await s3.GetObjectMetadataAsync(options.BucketName, key, ct).ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Idempotent: deleting an object that does not exist is a no-op. Accepts only <c>s3://</c>
    ///     URIs or URIs prefixed with the configured <see cref="S3StorageOptions.PublicBaseUrl" />;
    ///     anything else is rejected with <see cref="ArgumentException" />.
    /// </remarks>
    public async Task DeleteAsync(Uri fileUri, CancellationToken ct = default)
    {
        var key = ResolveKey(fileUri);
        try
        {
            await s3.DeleteObjectAsync(options.BucketName, key, ct).ConfigureAwait(false);
            LogDeleted(options.BucketName, key);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound ||
                                            ex.ErrorCode == "NoSuchKey")
        {
            // Object does not exist — treat as a no-op, consistent with LocalDiskFileStorage.
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Reads object metadata (a <c>HEAD</c> request) without downloading the content. Returns
    ///     <see langword="null" /> when the object does not exist (a 404). Accepts only <c>s3://</c>
    ///     URIs or URIs prefixed with the configured <see cref="S3StorageOptions.PublicBaseUrl" />;
    ///     anything else is rejected with <see cref="ArgumentException" />.
    /// </remarks>
    public async Task<StoredFileInfo?> GetInfoAsync(Uri fileUri, CancellationToken ct = default)
    {
        var key = ResolveKey(fileUri);
        try
        {
            var metadata = await s3.GetObjectMetadataAsync(options.BucketName, key, ct).ConfigureAwait(false);
            return new StoredFileInfo
            {
                SizeBytes = metadata.ContentLength,
                ContentType = metadata.Headers.ContentType,
                LastModified = metadata.LastModified is { } lastModified
                    ? new DateTimeOffset(DateTime.SpecifyKind(lastModified, DateTimeKind.Utc))
                    : null,
                FileUri = fileUri,
            };
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Generates a read-only pre-signed URL valid for <paramref name="expiry" /> via the S3
    ///     signing endpoint. Accepts only <c>s3://</c> URIs or URIs prefixed with the configured
    ///     <see cref="S3StorageOptions.PublicBaseUrl" />; anything else is rejected with
    ///     <see cref="ArgumentException" />.
    /// </remarks>
    public async Task<Uri> GetDownloadUrlAsync(Uri fileUri, TimeSpan expiry, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var key = ResolveKey(fileUri);

        var url = await s3.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = options.BucketName,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow + expiry,
        }).ConfigureAwait(false);

        return new Uri(url, UriKind.Absolute);
    }

    /// <summary>
    ///     Resolves a URI to an S3 object key.
    ///     Accepts <c>s3://bucket/key</c> URIs and public base-URL-prefixed URIs only.
    ///     Throws <see cref="ArgumentException" /> for any other URI to prevent arbitrary key injection.
    /// </summary>
    private string ResolveKey(Uri fileUri)
    {
        // s3://bucket/key → key
        if (fileUri.Scheme == "s3")
            return fileUri.AbsolutePath.TrimStart('/');

        // Public URL → strip base URL prefix
        if (options.PublicBaseUrl is not null)
        {
            var path = fileUri.ToString();
            var prefix = options.PublicBaseUrl.TrimEnd('/') + "/";
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return path[prefix.Length..];
        }

        // Reject everything else — never fall through to raw ToString() which allows
        // arbitrary bucket key injection from caller-supplied URIs.
        throw new ArgumentException(
            $"Cannot resolve '{fileUri}' to an S3 key: URI must use the 's3://' scheme or be prefixed with the configured PublicBaseUrl.",
            nameof(fileUri));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "S3: stored {OriginalName} → s3://{Bucket}/{Key}")]
    private partial void LogStored(string originalName, string bucket, string key);

    [LoggerMessage(Level = LogLevel.Information, Message = "S3: deleted s3://{Bucket}/{Key}")]
    private partial void LogDeleted(string bucket, string key);

    /// <summary>
    ///     Wraps a <see cref="GetObjectResponse" /> so that disposing the stream also disposes
    ///     the AWS SDK response, releasing the underlying HTTP connection back to the pool.
    /// </summary>
    private sealed class S3ResponseStream(GetObjectResponse response) : Stream
    {
        private readonly Stream _inner = response.ResponseStream;

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                response.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
