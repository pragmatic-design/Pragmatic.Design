using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Pragmatic.Storage.Sftp;

/// <summary>
///     SFTP (SSH File Transfer Protocol) file storage implementation. Stores files on a remote SSH
///     server under <c>{BasePath}/{container}/{GUID}{extension}</c>.
/// </summary>
/// <remarks>
///     <para>
///         <strong>Connection model.</strong> SSH.NET's client is connection-based and not
///         thread-safe, whereas this provider is a shared singleton used concurrently. Each operation
///         therefore opens its own client (via <see cref="ISftpClientFactory" />), connects, runs, then
///         disconnects and disposes. This is simple and safe; it trades a connection setup per call for
///         zero shared mutable state. For high-throughput workloads a caller can supply a pooling
///         <see cref="ISftpClientFactory" /> without changing this class.
///     </para>
///     <para>
///         SFTP exposes no signed/public URLs, so this provider implements <see cref="IFileStorage" />
///         and <see cref="IFileInfoProvider" /> but not <c>ISignedUrlProvider</c>. The URI returned by
///         <see cref="SaveAsync" /> is an opaque <c>sftp://{host}/{container}/{storedName}</c>
///         identifier that this same provider re-resolves back to a remote path.
///     </para>
/// </remarks>
public sealed partial class SftpFileStorage : IFileStorage, IFileInfoProvider
{
    private readonly SftpStorageOptions _options;
    private readonly ISftpClientFactory _clientFactory;
    private readonly ILogger<SftpFileStorage> _logger;
    private readonly long _maxFileSizeBytes;

    /// <summary>The remote base directory with any trailing slash trimmed (e.g. <c>""</c> for <c>"/"</c>).</summary>
    private readonly string _basePath;

    /// <summary>
    ///     Creates a provider that opens SFTP connections from <paramref name="options" /> using the
    ///     default <see cref="ISftpClientFactory" />.
    /// </summary>
    /// <param name="options">SFTP connection and storage options.</param>
    /// <param name="logger">Logger for storage operations.</param>
    public SftpFileStorage(SftpStorageOptions options, ILogger<SftpFileStorage> logger)
        : this(options, new SftpClientFactory(options), logger)
    {
    }

    /// <summary>
    ///     Test/extension seam: creates a provider with a caller-supplied
    ///     <see cref="ISftpClientFactory" /> (e.g. a fake client, or a pooling factory).
    /// </summary>
    internal SftpFileStorage(
        SftpStorageOptions options,
        ISftpClientFactory clientFactory,
        ILogger<SftpFileStorage> logger)
    {
        _options = options;
        _clientFactory = clientFactory;
        _logger = logger;
        _maxFileSizeBytes = options.MaxFileSizeBytes;
        _basePath = options.BasePath.TrimEnd('/');
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Enforces <see cref="SftpStorageOptions.MaxFileSizeBytes" /> before connecting (seekable
    ///     fast-path) or mid-upload for non-seekable streams via <see cref="LimitedReadStream" />.
    ///     Creates the remote container directories as needed, then uploads to
    ///     <c>{BasePath}/{container}/{GUID}{extension}</c> using POSIX ('/') separators.
    /// </remarks>
    public async Task<Uri> SaveAsync(Stream content, string fileName, string container, CancellationToken ct = default)
    {
        // Validate the container and the size limit BEFORE opening a connection, so an invalid
        // request never touches the network.
        EnsureSafeRelative(container, nameof(container));

        if (_maxFileSizeBytes > 0 && content.CanSeek && content.Length > _maxFileSizeBytes)
            throw new FileSizeLimitExceededException(
                $"Upload rejected: file size {content.Length} exceeds the limit of {_maxFileSizeBytes} bytes.",
                _maxFileSizeBytes, content.Length);

        var inputStream = _maxFileSizeBytes > 0 && !content.CanSeek
            ? new LimitedReadStream(content, _maxFileSizeBytes)
            : content;

        var ext = Path.GetExtension(fileName);
        var storedName = $"{Guid.NewGuid():N}{ext}";
        var remoteDir = BuildRemotePath(container);
        var remotePath = $"{remoteDir}/{storedName}";

        await WithClientAsync(async (client, token) =>
        {
            await EnsureDirectoryAsync(client, remoteDir, token).ConfigureAwait(false);
            await client.UploadFileAsync(inputStream, remotePath, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);

        LogStored(fileName, remotePath);
        return new Uri($"sftp://{_options.Host}/{container}/{storedName}", UriKind.Absolute);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The remote file is buffered into an in-memory stream before the connection is closed, so
    ///     the returned stream stays readable after this method returns. Returns <see langword="null" />
    ///     when the file does not exist. The caller owns the returned stream and must dispose it.
    /// </remarks>
    public async Task<Stream?> GetAsync(Uri fileUri, CancellationToken ct = default)
    {
        var remotePath = ResolveRemotePath(fileUri);

        return await WithClientAsync<Stream?>(async (client, token) =>
        {
            if (!await client.ExistsAsync(remotePath, token).ConfigureAwait(false))
                return null;

            // Connection-per-operation: buffer the remote content locally before disconnecting,
            // otherwise a lazily-read SFTP stream would die once the connection is closed.
            var buffer = new MemoryStream();
            await client.DownloadFileAsync(remotePath, buffer, token).ConfigureAwait(false);
            buffer.Position = 0;
            return buffer;
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(Uri fileUri, CancellationToken ct = default)
    {
        var remotePath = ResolveRemotePath(fileUri);
        return WithClientAsync((client, token) => client.ExistsAsync(remotePath, token), ct);
    }

    /// <inheritdoc />
    /// <remarks>Idempotent: deleting a file that does not exist is a no-op.</remarks>
    public Task DeleteAsync(Uri fileUri, CancellationToken ct = default)
    {
        var remotePath = ResolveRemotePath(fileUri);
        return WithClientAsync(async (client, token) =>
        {
            if (!await client.ExistsAsync(remotePath, token).ConfigureAwait(false))
                return;

            await client.DeleteFileAsync(remotePath, token).ConfigureAwait(false);
            LogDeleted(remotePath);
        }, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Reads attributes (size, last-write time) via a single SFTP <c>stat</c> and derives the
    ///     content type from the file extension via <see cref="MimeTypes" />. Returns
    ///     <see langword="null" /> when the file does not exist.
    /// </remarks>
    public Task<StoredFileInfo?> GetInfoAsync(Uri fileUri, CancellationToken ct = default)
    {
        var remotePath = ResolveRemotePath(fileUri);
        return WithClientAsync<StoredFileInfo?>(async (client, token) =>
        {
            try
            {
                var file = await client.GetAsync(remotePath, token).ConfigureAwait(false);
                return new StoredFileInfo
                {
                    SizeBytes = file.Length,
                    ContentType = MimeTypes.GetMimeType(Path.GetExtension(remotePath)),
                    LastModified = new DateTimeOffset(DateTime.SpecifyKind(file.LastWriteTimeUtc, DateTimeKind.Utc)),
                    FileUri = fileUri,
                };
            }
            catch (SftpPathNotFoundException)
            {
                return null;
            }
        }, ct);
    }

    /// <summary>
    ///     Opens a fresh client, connects, runs <paramref name="operation" />, then always
    ///     disconnects and disposes the client.
    /// </summary>
    private async Task<T> WithClientAsync<T>(Func<ISftpClient, CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        var client = _clientFactory.Create();
        try
        {
            await client.ConnectAsync(ct).ConfigureAwait(false);
            return await operation(client, ct).ConfigureAwait(false);
        }
        finally
        {
            if (client.IsConnected)
                client.Disconnect();
            client.Dispose();
        }
    }

    /// <summary>Overload for operations that produce no result.</summary>
    private Task WithClientAsync(Func<ISftpClient, CancellationToken, Task> operation, CancellationToken ct)
        => WithClientAsync<object?>(async (client, token) =>
        {
            await operation(client, token).ConfigureAwait(false);
            return null;
        }, ct);

    /// <summary>
    ///     Ensures every directory segment of <paramref name="remoteDir" /> exists, creating any that
    ///     are missing. Walks the path top-down so intermediate directories are created in order.
    /// </summary>
    private static async Task EnsureDirectoryAsync(ISftpClient client, string remoteDir, CancellationToken ct)
    {
        var current = string.Empty;
        foreach (var segment in remoteDir.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = $"{current}/{segment}";
            if (!await client.ExistsAsync(current, ct).ConfigureAwait(false))
                await client.CreateDirectoryAsync(current, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Combines <see cref="_basePath" /> with a validated relative POSIX path.
    /// </summary>
    private string BuildRemotePath(string relative) => $"{_basePath}/{relative}";

    /// <summary>
    ///     Resolves an <c>sftp://</c> URI produced by <see cref="SaveAsync" /> back to a remote path.
    ///     Rejects any other scheme, and re-validates the path against traversal, to prevent a
    ///     caller-supplied URI from reaching outside the storage root.
    /// </summary>
    /// <exception cref="ArgumentException">The URI is not a valid <c>sftp://</c> storage URI.</exception>
    private string ResolveRemotePath(Uri fileUri)
    {
        if (!fileUri.IsAbsoluteUri || fileUri.Scheme != "sftp")
            throw new ArgumentException(
                $"Cannot resolve '{fileUri}' to an SFTP path: URI must use the 'sftp://' scheme.",
                nameof(fileUri));

        var relative = Uri.UnescapeDataString(fileUri.AbsolutePath).TrimStart('/');
        EnsureSafeRelative(relative, nameof(fileUri));
        return BuildRemotePath(relative);
    }

    /// <summary>
    ///     Validates a caller-supplied relative POSIX path: not empty, not rooted, no backslashes and
    ///     no <c>..</c> traversal segments — mirroring the path-traversal guard of
    ///     <c>LocalDiskFileStorage</c> for POSIX paths.
    /// </summary>
    /// <exception cref="ArgumentException">The path is empty, rooted, or escapes the storage root.</exception>
    private static void EnsureSafeRelative(string relative, string paramName)
    {
        if (string.IsNullOrWhiteSpace(relative))
            throw new ArgumentException("Path must not be empty.", paramName);

        if (relative.Contains('\\'))
            throw new ArgumentException($"'{relative}' must use POSIX '/' separators, not '\\'.", paramName);

        if (relative.StartsWith('/'))
            throw new ArgumentException($"'{relative}' must be relative, not rooted.", paramName);

        foreach (var segment in relative.Split('/'))
        {
            if (segment == "..")
                throw new ArgumentException($"'{relative}' must not contain '..' traversal segments.", paramName);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "SFTP: stored {OriginalName} → {RemotePath}")]
    private partial void LogStored(string originalName, string remotePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "SFTP: deleted {RemotePath}")]
    private partial void LogDeleted(string remotePath);
}
