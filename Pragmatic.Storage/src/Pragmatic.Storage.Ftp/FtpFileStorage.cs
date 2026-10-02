using FluentFTP;
using FluentFTP.Exceptions;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Storage.Ftp;

/// <summary>
///     FTP / FTPS file storage implementation. Stores files on a remote FTP server under
///     <c>{BasePath}/{container}/</c>.
/// </summary>
/// <remarks>
///     <para>
///         An FTP connection is stateful and not thread-safe, whereas this provider is registered as a
///         shared singleton. Each operation therefore opens its own connection via
///         <see cref="IAsyncFtpClientFactory" />, runs, and disposes it (<c>await using</c>). This
///         trades per-operation connection overhead for correctness under concurrency; for high
///         throughput place a connection-pooling factory behind <see cref="IAsyncFtpClientFactory" />.
///     </para>
///     <para>
///         Files are addressed with POSIX (<c>/</c>) paths only — never OS path separators. The URI
///         returned by <see cref="SaveAsync" /> is an absolute <c>ftp://{host}{remotePath}</c> URI;
///         the other operations re-derive the remote path from its
///         <see cref="System.Uri.AbsolutePath" />. There is no signed-URL capability
///         (<c>ISignedUrlProvider</c> is intentionally not implemented) — FTP has no equivalent.
///     </para>
/// </remarks>
public sealed partial class FtpFileStorage : IFileStorage, IFileInfoProvider
{
    private readonly FtpStorageOptions _options;
    private readonly IAsyncFtpClientFactory _factory;
    private readonly ILogger<FtpFileStorage> _logger;

    /// <summary>
    ///     Creates an <see cref="FtpFileStorage" /> that connects using the supplied options.
    /// </summary>
    /// <param name="options">FTP server, credentials, base path and size-limit options.</param>
    /// <param name="logger">Logger for storage operations.</param>
    public FtpFileStorage(FtpStorageOptions options, ILogger<FtpFileStorage> logger)
        : this(options, new DefaultAsyncFtpClientFactory(options), logger)
    {
    }

    /// <summary>
    ///     Test seam: creates an <see cref="FtpFileStorage" /> with an explicit client factory so the
    ///     network can be substituted.
    /// </summary>
    internal FtpFileStorage(FtpStorageOptions options, IAsyncFtpClientFactory factory, ILogger<FtpFileStorage> logger)
    {
        _options = options;
        _factory = factory;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The remote path is <c>{BasePath}/{container}/{GUID}{extension}</c>; remote directories are
    ///     created as needed. Oversized uploads are rejected with
    ///     <see cref="FileSizeLimitExceededException" /> when
    ///     <see cref="FtpStorageOptions.MaxFileSizeBytes" /> is set — up front for seekable streams,
    ///     or mid-transfer (via <see cref="LimitedReadStream" />) for non-seekable ones.
    /// </remarks>
    public async Task<Uri> SaveAsync(Stream content, string fileName, string container, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ValidateContainer(container);

        // Reject oversized seekable streams before opening a connection. Non-seekable streams have no
        // known length, so wrap them in a limiting stream that throws once the running total exceeds
        // the limit while the server consumes the payload — mirroring the other providers.
        if (_options.MaxFileSizeBytes > 0 && content.CanSeek && content.Length > _options.MaxFileSizeBytes)
            throw new FileSizeLimitExceededException(
                $"Upload rejected: file size {content.Length} exceeds the limit of {_options.MaxFileSizeBytes} bytes.",
                _options.MaxFileSizeBytes, content.Length);

        var inputStream = _options.MaxFileSizeBytes > 0 && !content.CanSeek
            ? new LimitedReadStream(content, _options.MaxFileSizeBytes)
            : content;

        var ext = Path.GetExtension(fileName);
        var storedName = $"{Guid.NewGuid():N}{ext}";
        var remotePath = BuildRemotePath(container, storedName);

        var client = _factory.Create();
        await using (client.ConfigureAwait(false))
        {
            await client.Connect(ct).ConfigureAwait(false);
            var status = await client
                .UploadStream(inputStream, remotePath, FtpRemoteExists.Overwrite, true, null, ct)
                .ConfigureAwait(false);

            if (status == FtpStatus.Failed)
                throw new IOException($"FTP upload of '{remotePath}' failed.");
        }

        LogStored(fileName, remotePath);
        return new Uri($"ftp://{_options.Host}{remotePath}", UriKind.Absolute);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Downloads the file into an in-memory buffer before the connection is closed, then returns
    ///     that buffer as the stream. Returns <see langword="null" /> when the file does not exist.
    ///     <strong>The caller must dispose the returned stream.</strong>
    /// </remarks>
    public async Task<Stream?> GetAsync(Uri fileUri, CancellationToken ct = default)
    {
        var remotePath = ResolveRemotePath(fileUri);

        var client = _factory.Create();
        await using (client.ConfigureAwait(false))
        {
            await client.Connect(ct).ConfigureAwait(false);

            // Buffer the whole file while connected: the returned stream must outlive the connection.
            var buffer = new MemoryStream();
            try
            {
                var ok = await client.DownloadStream(buffer, remotePath, 0, null, ct, 0).ConfigureAwait(false);
                if (!ok)
                {
                    await buffer.DisposeAsync().ConfigureAwait(false);
                    return null;
                }
            }
            catch (FtpException)
            {
                await buffer.DisposeAsync().ConfigureAwait(false);
                return null;
            }

            buffer.Position = 0;
            return buffer;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(Uri fileUri, CancellationToken ct = default)
    {
        var remotePath = ResolveRemotePath(fileUri);

        var client = _factory.Create();
        await using (client.ConfigureAwait(false))
        {
            await client.Connect(ct).ConfigureAwait(false);
            return await client.FileExists(remotePath, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    /// <remarks>Idempotent: deleting a file that does not exist is a no-op.</remarks>
    public async Task DeleteAsync(Uri fileUri, CancellationToken ct = default)
    {
        var remotePath = ResolveRemotePath(fileUri);

        var client = _factory.Create();
        await using (client.ConfigureAwait(false))
        {
            await client.Connect(ct).ConfigureAwait(false);
            if (!await client.FileExists(remotePath, ct).ConfigureAwait(false))
                return;

            await client.DeleteFile(remotePath, ct).ConfigureAwait(false);
            LogDeleted(remotePath);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Reads the remote object listing without downloading the content. Returns
    ///     <see langword="null" /> when the object is absent or is not a file (e.g. a directory).
    ///     The content type is derived from the file extension via <see cref="MimeTypes" />.
    /// </remarks>
    public async Task<StoredFileInfo?> GetInfoAsync(Uri fileUri, CancellationToken ct = default)
    {
        var remotePath = ResolveRemotePath(fileUri);

        var client = _factory.Create();
        await using (client.ConfigureAwait(false))
        {
            await client.Connect(ct).ConfigureAwait(false);

            FtpListItem? item;
            try
            {
                item = await client.GetObjectInfo(remotePath, true, ct).ConfigureAwait(false);
            }
            catch (FtpException)
            {
                return null;
            }

            if (item is null || item.Type != FtpObjectType.File)
                return null;

            return new StoredFileInfo
            {
                SizeBytes = item.Size,
                ContentType = MimeTypes.GetMimeType(Path.GetExtension(remotePath)),
                // FluentFTP reports DateTime.MinValue when the modified time could not be retrieved.
                LastModified = item.Modified == DateTime.MinValue
                    ? null
                    : new DateTimeOffset(DateTime.SpecifyKind(item.Modified, DateTimeKind.Utc)),
                FileUri = fileUri,
            };
        }
    }

    /// <summary>
    ///     Builds the absolute POSIX remote path for a stored file, using only <c>/</c> separators.
    /// </summary>
    private string BuildRemotePath(string container, string storedName)
    {
        var basePath = _options.BasePath.Trim('/');
        return basePath.Length == 0
            ? $"/{container}/{storedName}"
            : $"/{basePath}/{container}/{storedName}";
    }

    /// <summary>
    ///     Validates a caller-supplied container so it cannot escape the storage root. Rejects empty
    ///     values, path-traversal segments (<c>..</c>), rooted paths and OS-specific separators.
    /// </summary>
    /// <exception cref="ArgumentException">The container is empty or is not a safe relative segment.</exception>
    private static void ValidateContainer(string container)
    {
        if (string.IsNullOrWhiteSpace(container))
            throw new ArgumentException("Container must not be empty.", nameof(container));

        if (container.Contains("..", StringComparison.Ordinal)
            || container.StartsWith('/')
            || container.Contains('\\')
            || container.Contains(':'))
            throw new ArgumentException(
                $"Container '{container}' is not a valid relative path segment.", nameof(container));
    }

    /// <summary>
    ///     Resolves the remote path from a URI produced by <see cref="SaveAsync" />. Accepts only
    ///     <c>ftp://</c> URIs; anything else is rejected to prevent arbitrary path injection.
    /// </summary>
    /// <exception cref="ArgumentException">The URI is not an absolute <c>ftp://</c> URI.</exception>
    private static string ResolveRemotePath(Uri fileUri)
    {
        ArgumentNullException.ThrowIfNull(fileUri);

        if (!fileUri.IsAbsoluteUri || !string.Equals(fileUri.Scheme, "ftp", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Cannot resolve '{fileUri}' to an FTP path: the URI must use the 'ftp' scheme.", nameof(fileUri));

        return Uri.UnescapeDataString(fileUri.AbsolutePath);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "FTP: stored {OriginalName} → {RemotePath}")]
    private partial void LogStored(string originalName, string remotePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "FTP: deleted {RemotePath}")]
    private partial void LogDeleted(string remotePath);
}
