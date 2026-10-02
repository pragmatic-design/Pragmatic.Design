namespace Pragmatic.Storage;

/// <summary>
///     Provider-agnostic abstraction for file storage operations.
/// </summary>
/// <remarks>
///     <para>
///         Inject <see cref="IFileStorage" /> into domain actions or services that need to persist
///         binary files (photos, documents, imports). The action only calls <see cref="SaveAsync" />;
///         the physical backend is decided at composition time.
///     </para>
///     <para>
///         Built-in implementations:
///         <list type="bullet">
///             <item><see cref="Local.LocalDiskFileStorage" /> — saves under <c>wwwroot/files/</c> (dev/demo)</item>
///             <item>Pragmatic.Storage.Azure — Azure Blob Storage (prod)</item>
///             <item>Pragmatic.Storage.S3 — Amazon S3 / Cloudflare R2 (prod)</item>
///         </list>
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // In a DomainAction
/// private IFileStorage _storage = null!;   // injected by Actions SG
///
/// var uri = await _storage.SaveAsync(Photo.OpenReadStream(), Photo.FileName, "photos", ct);
///     </code>
/// </example>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Singleton)]
public interface IFileStorage
{
    /// <summary>
    ///     Saves a file stream and returns the URI that identifies the stored file.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The returned URI is the stable identifier of the file: persist it and pass it back to
    ///         <see cref="GetAsync" />, <see cref="ExistsAsync" /> and <see cref="DeleteAsync" />.
    ///     </para>
    ///     <para>
    ///         <b>Its shape is provider-defined, and deliberately so.</b> LocalDisk returns a
    ///         <em>relative</em> URI, servable as a static file; Azure Blob returns an absolute URI
    ///         that is private without a SAS; S3 returns the configured <c>PublicBaseUrl</c>-based URI
    ///         or an internal <c>s3://</c> one. Flattening them into one shape would remove a real
    ///         difference.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>So do not rebuild one with <c>new Uri(text)</c>.</b> That overload accepts
    ///         absolute URIs only and throws <see cref="UriFormatException" /> on the relative shape —
    ///         on the day the application moves from the in-memory store to the disk, and not before.
    ///         Persist it as a string and read it back with
    ///         <c>new Uri(text, UriKind.RelativeOrAbsolute)</c>, or persist the <see cref="Uri" />
    ///         itself.
    ///     </para>
    /// </remarks>
    /// <param name="content">The file content stream.</param>
    /// <param name="fileName">Original file name (used for extension / content-type detection).</param>
    /// <param name="container">Logical container or folder (e.g. "photos", "imports").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A relative or absolute URI identifying the stored file.</returns>
    Task<Uri> SaveAsync(
        Stream content,
        string fileName,
        string container,
        CancellationToken ct = default);

    /// <summary>
    ///     Opens a read stream to a previously stored file.
    /// </summary>
    /// <param name="fileUri">The URI returned by <see cref="SaveAsync" />.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A readable stream, or null if the file does not exist.</returns>
    Task<Stream?> GetAsync(Uri fileUri, CancellationToken ct = default);

    /// <summary>
    ///     Checks whether a file exists.
    /// </summary>
    Task<bool> ExistsAsync(Uri fileUri, CancellationToken ct = default);

    /// <summary>
    ///     Deletes a previously stored file.
    /// </summary>
    /// <param name="fileUri">The URI returned by <see cref="SaveAsync" />.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(Uri fileUri, CancellationToken ct = default);
}
