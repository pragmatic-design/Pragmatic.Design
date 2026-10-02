namespace Pragmatic.Storage;

/// <summary>
///     Optional capability for a file storage provider that can report a file's metadata
///     (size, content type, last-modified) without downloading its content.
/// </summary>
/// <remarks>
///     <para>
///         This is a <strong>separate, optional</strong> interface: <see cref="IFileStorage" /> stays
///         at four methods and custom providers are not forced to implement it. Implement it on a
///         provider that can answer a metadata query cheaply — a local <c>stat</c>, an Azure Blob
///         <c>GetProperties</c>, an S3 <c>HEAD</c> — instead of streaming the whole file just to
///         learn its size.
///     </para>
///     <para>
///         Consume it by pattern-matching the injected storage:
///         <c>if (storage is IFileInfoProvider p) …</c>, or take the Result-based
///         <see cref="FileInfoResultExtensions.GetInfoAsResultAsync" /> on the typed provider.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // LocalDisk, Azure Blob and S3 all implement this capability.
/// var info = await provider.GetInfoAsync(fileUri, ct);
/// if (info is null) return NotFound();
/// return Ok(new { info.SizeBytes, info.ContentType });
///     </code>
/// </example>
public interface IFileInfoProvider
{
    /// <summary>
    ///     Returns metadata for a stored file, or <see langword="null" /> if the file does not exist.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The <see langword="null" /> semantics match <see cref="IFileStorage.GetAsync" />: a
    ///         missing file yields <see langword="null" /> rather than throwing.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>A URI the provider did not produce is a caller error, not an absent file.</b> The
    ///         URI shape is provider-defined (see <see cref="IFileStorage.SaveAsync" />), so a URI in
    ///         another provider's shape names nothing here and every provider throws
    ///         <see cref="ArgumentException" /> rather than answering. Two questions were being given
    ///         one answer: "I do not have that file" is <see langword="null" />, and "that is not one
    ///         of my addresses" is a wiring mistake that says so at the call site. Ask the provider
    ///         that wrote the URI.
    ///     </para>
    ///     <para>
    ///         The same holds for the four methods of <see cref="IFileStorage" />, which resolve the
    ///         URI the same way — a provider that refused from one and answered from another would be
    ///         the same divergence at a smaller scale.
    ///     </para>
    /// </remarks>
    /// <param name="fileUri">The URI returned by <see cref="IFileStorage.SaveAsync" />.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="StoredFileInfo" />, or <see langword="null" /> when the file is absent.</returns>
    /// <exception cref="ArgumentException">
    ///     The URI is not in the shape this provider produces.
    /// </exception>
    Task<StoredFileInfo?> GetInfoAsync(Uri fileUri, CancellationToken ct = default);
}
