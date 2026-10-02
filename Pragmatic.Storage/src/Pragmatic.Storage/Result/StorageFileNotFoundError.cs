using Pragmatic.Result;

// ReSharper disable once CheckNamespace — errors live in the Pragmatic.Storage namespace so they
// surface alongside IFileStorage without an extra using; the Result/ folder only groups them.
namespace Pragmatic.Storage;

/// <summary>
///     Represents a stored file that could not be found for the requested URI.
///     Maps to HTTP 404 Not Found.
/// </summary>
/// <remarks>
///     Named distinctly from <c>Pragmatic.Result.Http.NotFoundError</c> to avoid a clash and because
///     it carries the storage <see cref="FileUri" /> rather than an entity type/id. Produced by
///     <see cref="FileStorageResultExtensions.GetAsResultAsync" /> when the underlying
///     <see cref="IFileStorage.GetAsync" /> returns <see langword="null" />.
/// </remarks>
/// <example>
///     <code>
/// var result = await storage.GetAsResultAsync(fileUri, ct);
/// return result.Match(
///     stream => File(stream, "application/octet-stream"),
///     error => error is StorageFileNotFoundError ? NotFound() : Problem());
///     </code>
/// </example>
public sealed record StorageFileNotFoundError : Error
{
    /// <inheritdoc />
    public override string Code => "STORAGE_FILE_NOT_FOUND";

    /// <inheritdoc />
    public override int StatusCode => 404;

    /// <inheritdoc />
    public override string Title => "File Not Found";

    /// <summary>Gets the URI of the file that was not found.</summary>
    public Uri? FileUri { get; init; }

    /// <summary>Creates a <see cref="StorageFileNotFoundError" /> for the given file URI.</summary>
    /// <param name="fileUri">The URI that did not resolve to a stored file.</param>
    public static StorageFileNotFoundError For(Uri fileUri)
    {
        ArgumentNullException.ThrowIfNull(fileUri);
        return new StorageFileNotFoundError { FileUri = fileUri };
    }

    /// <inheritdoc />
    public override void WriteExtensions(IDictionary<string, object?> extensions)
    {
        if (FileUri is not null) extensions["fileUri"] = FileUri.ToString();
    }
}
