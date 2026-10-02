namespace Pragmatic.Storage;

/// <summary>
///     Thrown when an upload is rejected because it exceeds the configured maximum file size.
/// </summary>
/// <remarks>
///     <para>
///         Derives from <see cref="InvalidOperationException" /> so existing callers that catch the
///         broader exception type keep working; the typed subclass exists so the size limit can be
///         distinguished from other <see cref="InvalidOperationException" /> causes without parsing
///         the message. The Result-based surface
///         (<see cref="FileStorageResultExtensions" />) uses it to produce a
///         <see cref="FileTooLargeError" /> carrying the exact limit and (when known) actual size.
///     </para>
///     <para>
///         <see cref="ActualBytes" /> is nullable because the actual size is not known for a
///         non-seekable stream: the limit is enforced mid-copy, so only the limit is available.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// try
/// {
///     await storage.SaveAsync(content, fileName, "photos", ct);
/// }
/// catch (FileSizeLimitExceededException ex)
/// {
///     logger.LogWarning("Upload over {Limit} bytes rejected", ex.LimitBytes);
/// }
///     </code>
/// </example>
public sealed class FileSizeLimitExceededException : InvalidOperationException
{
    /// <summary>Creates a <see cref="FileSizeLimitExceededException" />.</summary>
    /// <param name="message">A human-readable description of the rejection.</param>
    /// <param name="limitBytes">The configured limit in bytes, if known.</param>
    /// <param name="actualBytes">
    ///     The actual size in bytes, if known. Null for non-seekable streams where the limit is
    ///     enforced while the payload is consumed.
    /// </param>
    public FileSizeLimitExceededException(string message, long? limitBytes = null, long? actualBytes = null)
        : base(message)
    {
        LimitBytes = limitBytes;
        ActualBytes = actualBytes;
    }

    /// <summary>Gets the configured maximum size in bytes, if known.</summary>
    public long? LimitBytes { get; }

    /// <summary>Gets the actual size of the rejected upload in bytes, if known.</summary>
    public long? ActualBytes { get; }
}
