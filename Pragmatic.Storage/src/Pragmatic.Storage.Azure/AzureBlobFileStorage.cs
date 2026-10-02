using System.Collections.Concurrent;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Storage.Azure;

/// <summary>
///     Azure Blob Storage file storage implementation.
///     Each container maps to a blob container in the storage account.
/// </summary>
public sealed partial class AzureBlobFileStorage(
    BlobServiceClient blobService,
    AzureBlobStorageOptions options,
    ILogger<AzureBlobFileStorage> logger)
    : IFileStorage, IFileInfoProvider, ISignedUrlProvider
{
    // Tracks containers that have already been created so we skip the network round-trip
    // on subsequent SaveAsync calls for the same container.
    private readonly ConcurrentDictionary<string, bool> _createdContainers = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    /// <remarks>
    ///     The blob container is <c>{ContainerPrefix}{container}</c> (created on first use), the
    ///     blob name is a random GUID plus the original extension. The returned URI is absolute
    ///     but private: generate a SAS to serve it publicly. Oversized uploads are rejected with
    ///     <see cref="FileSizeLimitExceededException" /> (an <see cref="InvalidOperationException" />)
    ///     when <see cref="AzureBlobStorageOptions.MaxFileSizeBytes" /> is set.
    /// </remarks>
    public async Task<Uri> SaveAsync(Stream content, string fileName, string container, CancellationToken ct = default)
    {
        // Guard oversized uploads before any network call. Seekable streams are rejected up front
        // via Length; non-seekable streams (HTTP bodies, chunked inputs) have no known length, so
        // we wrap them in a limiting stream that throws once the running byte total exceeds the
        // limit while the SDK consumes the payload — mirroring S3FileStorage.
        if (options.MaxFileSizeBytes > 0 && content.CanSeek && content.Length > options.MaxFileSizeBytes)
            throw new FileSizeLimitExceededException(
                $"Upload rejected: file size {content.Length} exceeds the limit of {options.MaxFileSizeBytes} bytes.",
                options.MaxFileSizeBytes, content.Length);

        var inputStream = options.MaxFileSizeBytes > 0 && !content.CanSeek
            ? new LimitedReadStream(content, options.MaxFileSizeBytes)
            : content;

        var containerName = options.ContainerPrefix + container;
        var containerClient = blobService.GetBlobContainerClient(containerName);

        // Skip the CreateIfNotExists round-trip once the container is known to exist. The name is
        // cached only AFTER the create succeeds, so a failed create is never cached as success;
        // concurrent first calls may both invoke CreateIfNotExistsAsync — idempotent and benign.
        if (!_createdContainers.ContainsKey(containerName))
        {
            await containerClient.CreateIfNotExistsAsync(cancellationToken: ct).ConfigureAwait(false);
            _createdContainers.TryAdd(containerName, true);
        }

        var ext = Path.GetExtension(fileName);
        var blobName = $"{Guid.NewGuid():N}{ext}";
        var blobClient = containerClient.GetBlobClient(blobName);

        await blobClient.UploadAsync(inputStream, new BlobHttpHeaders
        {
            ContentType = Pragmatic.Storage.MimeTypes.GetMimeType(ext),
        }, cancellationToken: ct).ConfigureAwait(false);

        LogStored(fileName, container, blobName);

        return blobClient.Uri;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Rejects URIs that do not belong to the configured storage account with
    ///     <see cref="ArgumentException" />. The returned stream must be disposed by the caller;
    ///     disposing it releases the underlying HTTP connection.
    /// </remarks>
    public async Task<Stream?> GetAsync(Uri fileUri, CancellationToken ct = default)
    {
        ValidateUriHost(fileUri);
        var blobClient = new BlobClient(fileUri);
        if (!await blobClient.ExistsAsync(ct).ConfigureAwait(false))
            return null;

        // DownloadStreamingAsync returns a Response<BlobDownloadStreamingResult> whose Value
        // wraps both the HTTP response and the content stream. We return a wrapper that disposes
        // the result (releasing the HTTP connection) when the caller disposes the stream.
        var response = await blobClient.DownloadStreamingAsync(cancellationToken: ct).ConfigureAwait(false);
        return new BlobDownloadStream(response.Value);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Rejects URIs that do not belong to the configured storage account with
    ///     <see cref="ArgumentException" />.
    /// </remarks>
    public async Task<bool> ExistsAsync(Uri fileUri, CancellationToken ct = default)
    {
        ValidateUriHost(fileUri);
        var blobClient = new BlobClient(fileUri);
        var response = await blobClient.ExistsAsync(ct).ConfigureAwait(false);
        return response.Value;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Idempotent: deleting a blob that does not exist is a no-op. Rejects URIs that do not
    ///     belong to the configured storage account with <see cref="ArgumentException" />.
    /// </remarks>
    public async Task DeleteAsync(Uri fileUri, CancellationToken ct = default)
    {
        ValidateUriHost(fileUri);
        var blobClient = new BlobClient(fileUri);
        await blobClient.DeleteIfExistsAsync(cancellationToken: ct).ConfigureAwait(false);
        LogDeleted(fileUri);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Reads blob properties (<c>GetProperties</c>) without downloading the content. Returns
    ///     <see langword="null" /> when the blob does not exist (a 404). Rejects URIs that do not
    ///     belong to the configured storage account with <see cref="ArgumentException" />.
    /// </remarks>
    public async Task<StoredFileInfo?> GetInfoAsync(Uri fileUri, CancellationToken ct = default)
    {
        var blobClient = GetBlobClientFor(fileUri);
        try
        {
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: ct).ConfigureAwait(false);
            var value = properties.Value;
            return new StoredFileInfo
            {
                SizeBytes = value.ContentLength,
                ContentType = value.ContentType,
                LastModified = value.LastModified,
                FileUri = fileUri,
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Generates a read-only SAS URI valid for <paramref name="expiry" />. Requires the
    ///     <see cref="BlobServiceClient" /> to have been created with a shared key credential
    ///     (<c>CanGenerateSasUri</c>); otherwise a <see cref="NotSupportedException" />
    ///     is thrown. Rejects URIs that do not belong to the configured storage account with
    ///     <see cref="ArgumentException" />.
    /// </remarks>
    public Task<Uri> GetDownloadUrlAsync(Uri fileUri, TimeSpan expiry, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var blobClient = GetBlobClientFor(fileUri);

        if (!blobClient.CanGenerateSasUri)
            throw new NotSupportedException(
                "The BlobServiceClient must be created with a shared key credential to generate SAS URLs; " +
                "use a user-delegation SAS or serve via a proxy endpoint.");

        var sasUri = blobClient.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow + expiry);
        return Task.FromResult(sasUri);
    }

    /// <summary>
    ///     Resolves <paramref name="fileUri" /> to a <see cref="BlobClient" /> obtained from the
    ///     configured <see cref="BlobServiceClient" /> (so it inherits the account credential),
    ///     after validating the URI host. Unlike <c>new BlobClient(uri)</c>, the returned client can
    ///     read private blobs and sign SAS URIs.
    /// </summary>
    private BlobClient GetBlobClientFor(Uri fileUri)
    {
        ValidateUriHost(fileUri);
        var builder = new BlobUriBuilder(fileUri);
        return blobService
            .GetBlobContainerClient(builder.BlobContainerName)
            .GetBlobClient(builder.BlobName);
    }

    /// <summary>
    ///     Validates that <paramref name="fileUri" /> belongs to the storage account configured
    ///     via <see cref="BlobServiceClient.AccountName" />.  Throws <see cref="ArgumentException" />
    ///     if the URI host does not match, preventing callers from reading/deleting blobs in
    ///     arbitrary accounts.
    /// </summary>
    private void ValidateUriHost(Uri fileUri)
    {
        var configuredHost = blobService.Uri.Host;
        if (!string.Equals(fileUri.Host, configuredHost, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"The supplied URI host '{fileUri.Host}' does not match the configured storage account '{configuredHost}'.",
                nameof(fileUri));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Azure: stored {OriginalName} → {Container}/{BlobName}")]
    private partial void LogStored(string originalName, string container, string blobName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Azure: deleted {BlobUri}")]
    private partial void LogDeleted(Uri blobUri);

    /// <summary>
    ///     Wraps a <see cref="BlobDownloadStreamingResult" /> so that disposing the stream
    ///     also disposes the Azure SDK result, releasing the underlying HTTP connection.
    /// </summary>
    private sealed class BlobDownloadStream(BlobDownloadStreamingResult result) : Stream
    {
        private readonly Stream _inner = result.Content;

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
                result.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
