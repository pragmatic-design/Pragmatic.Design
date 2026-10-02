using Pragmatic.Result;

// ReSharper disable once CheckNamespace — errors live in the Pragmatic.Storage namespace so they
// surface alongside IFileStorage without an extra using; the Result/ folder only groups them.
namespace Pragmatic.Storage;

/// <summary>
///     Represents a rejected upload that exceeded the configured maximum file size.
///     Maps to HTTP 413 Payload Too Large.
/// </summary>
/// <remarks>
///     Produced by <see cref="FileStorageResultExtensions" /> when a
///     <see cref="FileSizeLimitExceededException" /> is caught. <see cref="ActualBytes" /> is null
///     when the source stream was not seekable and its length was never known.
/// </remarks>
/// <example>
///     <code>
/// var result = await storage.SaveAsResultAsync(content, fileName, "photos", ct);
/// return result.Match(
///     uri => Ok(uri),
///     error => error is FileTooLargeError tooLarge
///         ? StatusCode(413, $"Max {tooLarge.LimitBytes} bytes")
///         : StatusCode(error.StatusCode, error.Title));
///     </code>
/// </example>
public sealed record FileTooLargeError : Error
{
    /// <inheritdoc />
    public override string Code => "FILE_TOO_LARGE";

    /// <inheritdoc />
    public override int StatusCode => 413;

    /// <inheritdoc />
    public override string Title => "File Too Large";

    /// <summary>Gets the configured maximum size in bytes, if known.</summary>
    public long? LimitBytes { get; init; }

    /// <summary>Gets the actual size of the rejected upload in bytes, if known.</summary>
    public long? ActualBytes { get; init; }

    /// <summary>
    ///     Creates a <see cref="FileTooLargeError" /> with the configured limit and, when known,
    ///     the actual size that triggered the rejection.
    /// </summary>
    /// <param name="limitBytes">The configured maximum size in bytes.</param>
    /// <param name="actualBytes">The actual size in bytes, or null when not known.</param>
    public static FileTooLargeError Create(long? limitBytes, long? actualBytes = null)
        => new() { LimitBytes = limitBytes, ActualBytes = actualBytes };

    /// <inheritdoc />
    public override void WriteExtensions(IDictionary<string, object?> extensions)
    {
        if (LimitBytes is { } limit) extensions["limitBytes"] = limit;
        if (ActualBytes is { } actual) extensions["actualBytes"] = actual;
    }
}
