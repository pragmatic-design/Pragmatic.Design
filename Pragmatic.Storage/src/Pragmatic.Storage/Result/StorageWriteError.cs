using Pragmatic.Result;

// ReSharper disable once CheckNamespace — errors live in the Pragmatic.Storage namespace so they
// surface alongside IFileStorage without an extra using; the Result/ folder only groups them.
namespace Pragmatic.Storage;

/// <summary>
///     Catch-all for a storage I/O failure (a provider write/read/delete error, an invalid
///     container or key). Maps to HTTP 500 Internal Server Error.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Reason" /> carries the originating exception message only — never the stack
///         trace — so it is safe to surface at the API boundary.
///     </para>
///     <para>
///         <see cref="Error.IsTransient" /> reflects <see cref="Transient" />: <see cref="From" />
///         marks transport-level faults (<see cref="IOException" />, <see cref="TimeoutException" />)
///         as transient so a retry policy can act on them.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// var result = await storage.SaveAsResultAsync(content, fileName, "photos", ct);
/// return result.Match(
///     uri => Ok(uri),
///     error => error.IsTransient ? StatusCode(503, "Retry") : StatusCode(500, error.Title));
///     </code>
/// </example>
public sealed record StorageWriteError : Error
{
    /// <inheritdoc />
    public override string Code => "STORAGE_WRITE_ERROR";

    /// <inheritdoc />
    public override int StatusCode => 500;

    /// <inheritdoc />
    public override string Title => "Storage Write Error";

    /// <summary>Gets the originating failure reason (an exception message), if any.</summary>
    public string? Reason { get; init; }

    /// <summary>
    ///     Gets whether the failure looks transient and may succeed on retry.
    /// </summary>
    /// <remarks>Surfaced through the base <see cref="Error.IsTransient" />.</remarks>
    public bool Transient { get; init; }

    /// <inheritdoc />
    public override bool IsTransient => Transient;

    /// <summary>Creates a <see cref="StorageWriteError" /> from an explicit reason.</summary>
    /// <param name="reason">A human-readable failure reason.</param>
    /// <param name="transient">Whether the failure may succeed on retry.</param>
    public static StorageWriteError Create(string? reason, bool transient = false)
        => new() { Reason = reason, Transient = transient };

    /// <summary>
    ///     Creates a <see cref="StorageWriteError" /> from an exception, copying only its message
    ///     into <see cref="Reason" /> (never the stack trace) and marking transport-level faults
    ///     (<see cref="IOException" />, <see cref="TimeoutException" />) as transient.
    /// </summary>
    /// <param name="exception">The originating exception.</param>
    public static StorageWriteError From(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new StorageWriteError
        {
            Reason = exception.Message,
            Transient = exception is IOException or TimeoutException,
        };
    }

    /// <inheritdoc />
    public override void WriteExtensions(IDictionary<string, object?> extensions)
    {
        if (Reason is not null) extensions["reason"] = Reason;
    }
}
