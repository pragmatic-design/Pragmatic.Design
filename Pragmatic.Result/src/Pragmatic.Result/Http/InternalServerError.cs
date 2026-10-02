namespace Pragmatic.Result.Http;

/// <summary>
///     Represents an internal server error from an unhandled exception.
///     Maps to HTTP 500 Internal Server Error.
/// </summary>
/// <remarks>
///     <para>
///         This error is typically created from caught exceptions.
///         In production, sensitive details (stack trace, inner exception) should be omitted.
///     </para>
///     <para>
///         <b>Usage:</b>
///         <code>
/// try { ... }
/// catch (Exception ex)
/// {
///     return InternalServerError.From(ex, includeDetails: env.IsDevelopment());
/// }
/// </code>
///     </para>
/// </remarks>
public sealed record InternalServerError : Error
{
    /// <inheritdoc />
    public override string Code => "INTERNAL_ERROR";

    /// <inheritdoc />
    public override int StatusCode => 500;

    /// <inheritdoc />
    public override string Title => "Internal Server Error";

    /// <summary>
    ///     Gets the exception type name (e.g., "NullReferenceException").
    /// </summary>
    /// <remarks>Only populated when includeDetails is true.</remarks>
    public string? ExceptionType { get; init; }

    /// <summary>
    ///     Gets the exception message.
    /// </summary>
    /// <remarks>Only populated when includeDetails is true.</remarks>
    public string? Message { get; init; }

    /// <summary>
    ///     Gets the stack trace.
    /// </summary>
    /// <remarks>Only populated when includeDetails is true.</remarks>
    public string? StackTrace { get; init; }

    /// <summary>
    ///     Gets the inner exception type (if any).
    /// </summary>
    public string? InnerExceptionType { get; init; }

    /// <summary>
    ///     Gets the inner exception message (if any).
    /// </summary>
    public string? InnerMessage { get; init; }

    /// <inheritdoc />
    public override void WriteExtensions(IDictionary<string, object?> extensions)
    {
        if (ExceptionType is not null) extensions["exceptionType"] = ExceptionType;
        if (Message is not null) extensions["message"] = Message;
        if (StackTrace is not null) extensions["stackTrace"] = StackTrace;
        if (InnerExceptionType is not null) extensions["innerExceptionType"] = InnerExceptionType;
        if (InnerMessage is not null) extensions["innerMessage"] = InnerMessage;
    }

    /// <summary>
    ///     Creates an InternalServerError from an exception.
    /// </summary>
    /// <param name="ex">The exception to wrap</param>
    /// <param name="includeDetails">
    ///     When <c>false</c> (the default, and the correct setting for production) only the generic
    ///     "An unexpected error occurred" message is exposed. When <c>true</c> the result carries the
    ///     exception type, message, <b>full stack trace</b>, and inner-exception details — sensitive
    ///     internals that must ONLY be surfaced in trusted/development contexts. Never pass <c>true</c>
    ///     on a response that can reach an untrusted client.
    /// </param>
    public static InternalServerError From(Exception ex, bool includeDetails = false)
    {
        ArgumentNullException.ThrowIfNull(ex);

        if (!includeDetails)
            return new InternalServerError
            {
                Message = "An unexpected error occurred"
            };

        return new InternalServerError
        {
            ExceptionType = ex.GetType().FullName,
            Message = ex.Message,
            StackTrace = ex.StackTrace,
            InnerExceptionType = ex.InnerException?.GetType().FullName,
            InnerMessage = ex.InnerException?.Message
        };
    }

    /// <summary>
    ///     Creates an InternalServerError with a message (for non-exception errors).
    /// </summary>
    public static InternalServerError Create(string message)
    {
        return new InternalServerError { Message = message };
    }
}