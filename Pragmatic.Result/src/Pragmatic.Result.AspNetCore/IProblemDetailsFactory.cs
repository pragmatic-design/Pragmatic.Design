using Microsoft.AspNetCore.Mvc;

namespace Pragmatic.Result.AspNetCore;

/// <summary>
///     Factory for creating ProblemDetails from Result errors.
/// </summary>
/// <remarks>
///     <para>
///         This factory converts <see cref="IError" /> instances
///         to RFC 7807 Problem Details format for HTTP API responses.
///     </para>
///     <para>
///         The default implementation uses <see cref="IErrorMessageResolver" /> to provide
///         localized error messages in the "detail" field.
///     </para>
/// </remarks>
public interface IProblemDetailsFactory
{
    /// <summary>
    ///     Creates a ProblemDetails from an IError.
    /// </summary>
    /// <param name="error">The error (uses error.StatusCode for HTTP status)</param>
    /// <param name="instance">Optional request path for Problem Details instance field</param>
    /// <returns>A ProblemDetails instance</returns>
    ProblemDetails Create(IError error, string? instance = null);
}