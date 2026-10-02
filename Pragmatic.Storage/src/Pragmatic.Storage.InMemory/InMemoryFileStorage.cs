using System.Collections.Concurrent;

namespace Pragmatic.Storage.InMemory;

/// <summary>
///     An in-memory <see cref="IFileStorage"/> that keeps every stored file in a
///     <see cref="ConcurrentDictionary{TKey,TValue}"/>. Intended for unit / integration tests and
///     local development where a real disk or cloud backend is unnecessary.
/// </summary>
/// <remarks>
///     <para>
///         Files are addressed by an opaque <c>mem://{container}/{guid}{ext}</c> URI returned from
///         <see cref="SaveAsync"/>. The instance is thread-safe and also implements
///         <see cref="IFileInfoProvider"/>, so metadata (size, content type, last-modified) can be
///         queried without downloading the content.
///     </para>
///     <para>
///         Nothing is persisted: the store lives for the lifetime of the instance. Use
///         <see cref="Clear"/> to reset between tests and <see cref="Count"/> to assert on the number
///         of stored files.
///     </para>
/// </remarks>
public sealed class InMemoryFileStorage : IFileStorage, IFileInfoProvider
{
    private readonly ConcurrentDictionary<string, StoredFile> _files = new(StringComparer.Ordinal);

    /// <summary>Gets the number of files currently held in the store.</summary>
    public int Count => _files.Count;

    /// <inheritdoc />
    public async Task<Uri> SaveAsync(Stream content, string fileName, string container, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct).ConfigureAwait(false);

        var extension = Path.GetExtension(fileName);
        var key = $"{container}/{Guid.NewGuid():N}{extension}";
        _files[key] = new StoredFile(buffer.ToArray(), MimeTypes.GetMimeType(extension), DateTimeOffset.UtcNow);

        return new Uri($"mem://{key}", UriKind.Absolute);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Returns a fresh <see cref="MemoryStream"/> over a snapshot of the stored bytes on every call,
    ///     so the caller may read and dispose it freely without affecting the store or a later
    ///     <see cref="GetAsync"/>.
    /// </remarks>
    public Task<Stream?> GetAsync(Uri fileUri, CancellationToken ct = default)
        => Task.FromResult(_files.TryGetValue(ResolveKey(fileUri), out var file)
            ? (Stream)new MemoryStream(file.Bytes, writable: false)
            : null);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(Uri fileUri, CancellationToken ct = default)
        => Task.FromResult(_files.ContainsKey(ResolveKey(fileUri)));

    /// <inheritdoc />
    public Task DeleteAsync(Uri fileUri, CancellationToken ct = default)
    {
        _files.TryRemove(ResolveKey(fileUri), out _);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<StoredFileInfo?> GetInfoAsync(Uri fileUri, CancellationToken ct = default)
        => Task.FromResult(_files.TryGetValue(ResolveKey(fileUri), out var file)
            ? new StoredFileInfo
            {
                SizeBytes = file.Bytes.LongLength,
                ContentType = file.ContentType,
                LastModified = file.LastModified,
                FileUri = fileUri,
            }
            : null);

    /// <summary>Removes every file from the store. Useful to reset state between tests.</summary>
    public void Clear() => _files.Clear();

    /// <summary>Resolves a URI to the key of a stored file.</summary>
    /// <param name="fileUri">A URI this store returned from <see cref="SaveAsync" />.</param>
    /// <returns>The key the file is held under, whether or not a file is there.</returns>
    /// <exception cref="ArgumentException">
    ///     The URI is not in the <c>mem://</c> shape this store produces.
    /// </exception>
    /// <remarks>
    ///     ⚠️ A URI in another provider's shape is a caller error, not a missing file, and the two
    ///     answers are kept apart on purpose: a key that resolves but holds nothing yields
    ///     <see langword="null" />, and a URI that cannot name anything in here says so at the call
    ///     site. Reading it as "the file is not there" is what let a stale row in a database look like
    ///     an ordinary absence while the application was pointed at the wrong store — the shape this
    ///     seam exists to make visible.
    /// </remarks>
    private static string ResolveKey(Uri fileUri)
    {
        ArgumentNullException.ThrowIfNull(fileUri);

        if (!fileUri.IsAbsoluteUri || !string.Equals(fileUri.Scheme, "mem", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"'{fileUri}' is not a URI this store produced. Expected the 'mem://' shape returned by "
                + $"{nameof(SaveAsync)}; ask the provider that wrote it.",
                nameof(fileUri));

        return $"{fileUri.Host}{fileUri.AbsolutePath}";
    }

    private readonly record struct StoredFile(byte[] Bytes, string ContentType, DateTimeOffset LastModified);
}
