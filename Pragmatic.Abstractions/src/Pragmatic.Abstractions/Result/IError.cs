namespace Pragmatic.Result;

/// <summary>
///     Interface for all error types in the Result pattern.
/// </summary>
/// <remarks>
///     <para>
///         All errors implement this interface, providing common functionality for:
///         <list type="bullet">
///             <item>HTTP status code mapping</item>
///             <item>Problem Details (RFC 7807) support</item>
///             <item>Human-readable title and description</item>
///         </list>
///     </para>
///     <para>
///         For most error types, extend the <c>Error</c> abstract record in the Result package.
///         Use this interface directly only for struct-based errors like ValidationError.
///     </para>
///     <para>
///         Title and Description use <c>string</c> for zero-dependency contracts. Localization is
///         resolved from the error's <see cref="Code" /> (the message-key convention) via
///         <see cref="IErrorMessageResolver" /> at the serialization boundary — no localization
///         members live on this interface.
///     </para>
/// </remarks>
public interface IError
{
    /// <summary>
    ///     Semantic error code used for identification and localization lookup.
    /// </summary>
    /// <remarks>
    ///     Convention: UPPER_SNAKE_CASE (e.g., "NOT_FOUND", "VALIDATION_ERROR").
    /// </remarks>
    string Code { get; }

    /// <summary>
    ///     HTTP status code for this error.
    /// </summary>
    int StatusCode { get; }

    /// <summary>
    ///     Title for Problem Details response (RFC 7807).
    /// </summary>
    string Title => string.Empty;

    /// <summary>
    ///     Description providing additional context about the error.
    /// </summary>
    string? Description => null;

    /// <summary>
    ///     Writes this error's own properties into the RFC 7807 extensions of the response.
    /// </summary>
    /// <remarks>
    ///     It lives on the interface, not on the <c>Error</c> record, because the serialization
    ///     boundary must be able to call it on <b>any</b> <see cref="IError" />. Reaching it
    ///     through <c>if (error is Error record)</c> would silently exclude every struct-based error —
    ///     including <c>ValidationError</c>, whose issues carry the one thing a client needs: which
    ///     field is wrong. A validation failure would reach the wire as a bare
    ///     <c>VALIDATION_ERROR</c> naming nothing.
    /// </remarks>
    /// <param name="extensions">The extensions dictionary to populate.</param>
    void WriteExtensions(IDictionary<string, object?> extensions) { }

    /// <summary>
    ///     Writes this error's own properties into the extensions, with the words of any it carries
    ///     resolved in the caller's language.
    /// </summary>
    /// <param name="extensions">The extensions dictionary to populate.</param>
    /// <param name="resolver">The request's resolver; null when there is none.</param>
    /// <remarks>
    ///     What the serialization boundary calls. An error whose extensions carry no words — every one
    ///     but <c>ValidationError</c> today — has nothing to resolve, and writes what it always wrote.
    /// </remarks>
    void WriteExtensions(IDictionary<string, object?> extensions, IErrorMessageResolver? resolver)
        => WriteExtensions(extensions);
}
