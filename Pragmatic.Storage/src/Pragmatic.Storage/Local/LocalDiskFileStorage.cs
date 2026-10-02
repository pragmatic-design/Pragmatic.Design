using System.Buffers;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Storage.Local;

/// <summary>
///     Saves files to the local filesystem under <c>{basePath}/files/{container}/</c>.
/// </summary>
/// <remarks>
///     Intended for development and demo environments only.
///     Each file is stored with a random prefix to avoid name collisions.
///     Serve the base directory as a static files root (e.g. <c>app.UseStaticFiles()</c>).
/// </remarks>
public sealed class LocalDiskFileStorage : IFileStorage, IFileInfoProvider
{
    private readonly string _basePath;
    private readonly ILogger<LocalDiskFileStorage> _logger;
    private readonly long _maxFileSizeBytes;

    /// <param name="basePath">
    ///     Root directory for file storage (e.g. <c>IHostEnvironment.ContentRootPath + "/wwwroot"</c>).
    ///     A <c>files/</c> sub-directory will be created automatically.
    /// </param>
    /// <param name="logger">Logger for storage operations.</param>
    /// <param name="maxFileSizeBytes">
    ///     Maximum accepted size of a single uploaded file, in bytes. <c>0</c> (default) means
    ///     no limit. Set a positive value to reject oversized uploads before they fill the disk.
    /// </param>
    public LocalDiskFileStorage(
        string basePath,
        ILogger<LocalDiskFileStorage> logger,
        long maxFileSizeBytes = 0)
    {
        _basePath = basePath;
        _logger = logger;
        _maxFileSizeBytes = maxFileSizeBytes;
    }

    /// <inheritdoc />
    public async Task<Uri> SaveAsync(
        Stream content,
        string fileName,
        string container,
        CancellationToken ct = default)
    {
        var dir = ResolveContainerDirectory(container);

        // Fast-path size check for seekable streams, before creating the file.
        if (_maxFileSizeBytes > 0 && content.CanSeek && content.Length > _maxFileSizeBytes)
            throw new FileSizeLimitExceededException(
                $"Upload rejected: file size {content.Length} exceeds the limit of {_maxFileSizeBytes} bytes.",
                _maxFileSizeBytes, content.Length);

        Directory.CreateDirectory(dir);

        var ext = Path.GetExtension(fileName);
        var storedName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(dir, storedName);

        // Write to a temp file in the SAME directory, then rename: a concurrent reader can never
        // observe a partially written file at the final path (File.Move is atomic per volume).
        var tempPath = fullPath + ".tmp";

        try
        {
            var fs = File.Create(tempPath);
            await using (fs.ConfigureAwait(false))
                await CopyWithLimitAsync(content, fs, ct).ConfigureAwait(false);

            File.Move(tempPath, fullPath);
        }
        catch
        {
            // Do not leave a partial/oversized temp file behind on failure.
            TryDeleteFile(tempPath);
            throw;
        }

        _logger.LogInformation(
            "Stored {OriginalName} → files/{Container}/{StoredName}",
            fileName, container, storedName);

        return new Uri($"/files/{container}/{storedName}", UriKind.Relative);
    }

    /// <summary>
    ///     Copies <paramref name="source"/> into <paramref name="destination"/>, enforcing
    ///     <see cref="_maxFileSizeBytes"/> even for non-seekable streams (where the length is
    ///     not known up front). Throws once the running total exceeds the limit.
    /// </summary>
    private async Task CopyWithLimitAsync(Stream source, Stream destination, CancellationToken ct)
    {
        if (_maxFileSizeBytes <= 0)
        {
            await source.CopyToAsync(destination, ct).ConfigureAwait(false);
            return;
        }

        // Use ArrayPool to avoid per-call heap allocation of the 80 KB buffer.
        const int bufferSize = 81920;
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, bufferSize), ct).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > _maxFileSizeBytes)
                    throw new FileSizeLimitExceededException(
                        $"Upload rejected: stream exceeds the limit of {_maxFileSizeBytes} bytes.",
                        _maxFileSizeBytes);

                await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The returned <see cref="Stream" /> is a <see cref="FileStream" /> opened for
    ///     reading. <strong>The caller is responsible for disposing it.</strong>
    ///     Use a <c>await using</c> block or explicitly call <c>Dispose()</c> on the stream.
    ///     <para>
    ///         The <paramref name="ct" /> parameter is accepted for interface conformance;
    ///         the underlying <see cref="File.OpenRead" /> call is synchronous and does not
    ///         observe it once the file handle is open.
    ///     </para>
    /// </remarks>
    public Task<Stream?> GetAsync(Uri fileUri, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!TryResolveSafePath(fileUri, out var fullPath) || !File.Exists(fullPath))
            return Task.FromResult<Stream?>(null);

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult<Stream?>(stream);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(Uri fileUri, CancellationToken ct = default)
    {
        if (!TryResolveSafePath(fileUri, out var fullPath))
            return Task.FromResult(false);
        return Task.FromResult(File.Exists(fullPath));
    }

    /// <inheritdoc />
    public Task DeleteAsync(Uri fileUri, CancellationToken ct = default)
    {
        if (!TryResolveSafePath(fileUri, out var fullPath))
            return Task.CompletedTask;

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.LogInformation("Deleted {Path}", fullPath);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Reads size and last-write time from the filesystem (<see cref="FileInfo" />) and derives
    ///     the content type from the file extension via <see cref="MimeTypes" />. Returns
    ///     <see langword="null" /> for a missing file or an unsafe (traversal/rooted) URI, matching
    ///     the null semantics of <see cref="GetAsync" />. An absolute URI belongs to another provider
    ///     and throws <see cref="ArgumentException" />.
    /// </remarks>
    public Task<StoredFileInfo?> GetInfoAsync(Uri fileUri, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!TryResolveSafePath(fileUri, out var fullPath) || !File.Exists(fullPath))
            return Task.FromResult<StoredFileInfo?>(null);

        var fileInfo = new FileInfo(fullPath);
        var info = new StoredFileInfo
        {
            SizeBytes = fileInfo.Length,
            ContentType = MimeTypes.GetMimeType(fileInfo.Extension),
            LastModified = new DateTimeOffset(fileInfo.LastWriteTimeUtc),
            FileUri = fileUri,
        };
        return Task.FromResult<StoredFileInfo?>(info);
    }

    /// <summary>
    ///     Resolves the storage directory for a container, ensuring it stays inside
    ///     <c>{basePath}/files/</c>. Rejects path traversal segments and absolute paths
    ///     so a caller-supplied <paramref name="container"/> cannot escape the storage root.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     The container is empty or resolves outside the storage root.
    /// </exception>
    private string ResolveContainerDirectory(string container)
    {
        if (string.IsNullOrWhiteSpace(container))
            throw new ArgumentException("Container must not be empty.", nameof(container));

        var filesRoot = Path.GetFullPath(Path.Combine(_basePath, "files"));
        var resolved = Path.GetFullPath(Path.Combine(filesRoot, container));

        var rootWithSep = filesRoot.EndsWith(Path.DirectorySeparatorChar)
            ? filesRoot
            : filesRoot + Path.DirectorySeparatorChar;

        if (resolved != filesRoot && !resolved.StartsWith(rootWithSep, StringComparison.Ordinal))
            throw new ArgumentException(
                $"Container '{container}' resolves outside the storage root.", nameof(container));

        return resolved;
    }

    /// <summary>
    ///     Resolves a fileUri against the base path, ensuring the result stays inside the base.
    ///     Returns false for paths containing traversal segments that escape the root, rooted paths,
    ///     or any other attempt to reach outside <c>_basePath</c>. Prevents path traversal
    ///     (e.g. <c>../../etc/passwd</c>) and absolute-path substitution.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     The URI is absolute, and so belongs to another provider's address space.
    /// </exception>
    /// <remarks>
    ///     ⚠️ <b>Two refusals, and they stay apart.</b> An absolute URI — <c>mem://</c>, <c>s3://</c>,
    ///     an <c>https://</c> CDN address — is a URI this provider could not have written, because
    ///     <see cref="SaveAsync" /> returns a relative one so the file is servable as a static asset.
    ///     That is a wiring mistake and throws. A <em>relative</em> path that escapes the root is an
    ///     attack, and keeps answering as a file that is not there: reporting it as "you asked the
    ///     wrong provider" would be the more comfortable reading of the two and the wrong one.
    /// </remarks>
    private bool TryResolveSafePath(Uri fileUri, out string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fileUri);

        fullPath = string.Empty;

        if (fileUri.IsAbsoluteUri)
            throw new ArgumentException(
                $"'{fileUri}' is not a URI this storage produced. Expected the relative path returned by "
                + $"{nameof(SaveAsync)}; ask the provider that wrote it.",
                nameof(fileUri));

        var relativePath = fileUri.ToString().TrimStart('/');

        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        // Reject absolute paths and UNC roots outright — they would bypass Path.Combine semantics.
        // Path.IsPathRooted covers Windows drive letters (C:\...) and UNC paths (\\...) so a
        // separate ':' check is redundant and would give a false sense of security.
        if (Path.IsPathRooted(relativePath))
            return false;

        var combined = Path.Combine(_basePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var resolvedFull = Path.GetFullPath(combined);
        var resolvedRoot = Path.GetFullPath(_basePath);

        var rootWithSep = resolvedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? resolvedRoot
            : resolvedRoot + Path.DirectorySeparatorChar;

        if (!resolvedFull.StartsWith(rootWithSep, StringComparison.Ordinal))
            return false;

        fullPath = resolvedFull;
        return true;
    }
}
