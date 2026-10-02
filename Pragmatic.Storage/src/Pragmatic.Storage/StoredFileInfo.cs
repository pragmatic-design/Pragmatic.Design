namespace Pragmatic.Storage;

/// <summary>
///     Metadata about a stored file, retrieved without downloading its content.
/// </summary>
/// <remarks>
///     Named <see cref="StoredFileInfo" /> (not <c>FileInfo</c>) to avoid a clash with
///     <see cref="System.IO.FileInfo" />. Produced by <see cref="IFileInfoProvider.GetInfoAsync" />
///     for providers that can query metadata cheaply (a local <c>stat</c>, an Azure
///     <c>GetProperties</c>, an S3 <c>HEAD</c>).
/// </remarks>
/// <example>
///     <code>
/// if (storage is IFileInfoProvider provider)
/// {
///     var info = await provider.GetInfoAsync(fileUri, ct);
///     if (info is not null)
///         Console.WriteLine($"{info.SizeBytes} bytes, {info.ContentType}");
/// }
///     </code>
/// </example>
public sealed record StoredFileInfo
{
    /// <summary>Gets the size of the stored file, in bytes.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    ///     Gets the content (MIME) type reported by the provider, or <see langword="null" />
    ///     when the provider does not record one.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    ///     Gets the last-modified timestamp reported by the provider, or <see langword="null" />
    ///     when unknown.
    /// </summary>
    public DateTimeOffset? LastModified { get; init; }

    /// <summary>Gets the URI that identifies the stored file (the value passed to the query).</summary>
    public required Uri FileUri { get; init; }
}
