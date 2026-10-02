namespace Pragmatic.Storage;

/// <summary>
///     Optional capability for a file storage provider that can mint a short-lived,
///     pre-authenticated URL for reading a stored file directly (e.g. from a browser).
/// </summary>
/// <remarks>
///     <para>
///         This is a <strong>separate, optional</strong> interface: <see cref="IFileStorage" /> stays
///         at four methods. Implemented by cloud providers that support signed URLs — Azure Blob
///         (a SAS URI) and S3/R2 (a pre-signed URL). A signed URL lets the client download the file
///         straight from the storage backend, so the application never proxies the bytes.
///     </para>
///     <para>
///         <see cref="Local.LocalDiskFileStorage" /> does <strong>not</strong> implement this: local
///         files are served as static files behind the application, not via a signed URL.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// if (storage is ISignedUrlProvider signer)
/// {
///     var url = await signer.GetDownloadUrlAsync(fileUri, TimeSpan.FromMinutes(15), ct);
///     return Redirect(url.ToString());
/// }
///     </code>
/// </example>
public interface ISignedUrlProvider
{
    /// <summary>
    ///     Generates a temporary, read-only URL that grants direct download access to a stored file
    ///     until it expires.
    /// </summary>
    /// <param name="fileUri">The URI returned by <see cref="IFileStorage.SaveAsync" />.</param>
    /// <param name="expiry">How long the generated URL remains valid, measured from now.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An absolute URL that reads the file directly, valid until <paramref name="expiry" /> elapses.</returns>
    /// <exception cref="NotSupportedException">
    ///     The provider is configured in a way that cannot sign URLs (for example an Azure client
    ///     created without a shared key credential).
    /// </exception>
    Task<Uri> GetDownloadUrlAsync(Uri fileUri, TimeSpan expiry, CancellationToken ct = default);
}
