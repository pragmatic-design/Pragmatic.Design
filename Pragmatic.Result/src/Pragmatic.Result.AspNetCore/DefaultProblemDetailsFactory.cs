using Microsoft.AspNetCore.Mvc;

namespace Pragmatic.Result.AspNetCore;

/// <summary>
///     Default implementation of <see cref="IProblemDetailsFactory" />.
/// </summary>
/// <remarks>
///     <para>
///         Uses <see cref="IErrorMessageResolver" /> to provide localized error messages
///         in the "detail" field of ProblemDetails.
///     </para>
/// </remarks>
internal sealed class DefaultProblemDetailsFactory : IProblemDetailsFactory
{
    private readonly IErrorMessageResolver _messageResolver;

    /// <summary>
    ///     Creates a new instance of DefaultProblemDetailsFactory.
    /// </summary>
    /// <param name="messageResolver">The error message resolver for localization</param>
    public DefaultProblemDetailsFactory(IErrorMessageResolver messageResolver)
    {
        _messageResolver = messageResolver ?? throw new ArgumentNullException(nameof(messageResolver));
    }

    /// <inheritdoc />
    public ProblemDetails Create(IError error, string? instance = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        var problemDetails = new ProblemDetails
        {
            Status = error.StatusCode,
            // Error.Title is non-null (defaults to string.Empty), so treat empty as "no title" and fall
            // back to the status-derived default — matching ProblemDetailsFactory (a plain ?? never fires).
            Title = string.IsNullOrEmpty(error.Title) ? ProblemDetailsFactory.GetDefaultTitle(error.StatusCode) : error.Title,
            Type = ProblemDetailsFactory.GetProblemType(error.StatusCode),
            Instance = instance
        };

        // Try to get localized title
        var localizedTitle = _messageResolver.ResolveTitle(error.Code, error);
        if (localizedTitle is not null)
            problemDetails.Title = localizedTitle;

        // Detail: prefer the resolver's localized message; fall back to error.Description when the
        // resolver yields nothing (the default NullErrorMessageResolver always does). Without this
        // fallback, wiring AddPragmaticResult() would produce a null Detail — strictly worse than the
        // factoryless inline path, which uses error.Description.
        var localizedMessage = _messageResolver.Resolve(error.Code, error);
        problemDetails.Detail = string.IsNullOrEmpty(localizedMessage) ? error.Description : localizedMessage;

        // Add error code as extension
        problemDetails.Extensions["code"] = error.Code;

        // Every IError, not just the ones deriving from Error. An `is Error` narrowing here would
        // silently exclude struct-based errors — ValidationError above all, whose issues carry the
        // field name the client needs. The resolver goes with it: the issues' messages are words too,
        // and need localizing as much as the title and the detail.
        error.WriteExtensions(problemDetails.Extensions, _messageResolver);

        // Set retry header AFTER WriteExtensions so the typed int retryAfter is authoritative and never clobbered
        if (error is Error { IsTransient: true, RetryAfter: not null } transientError)
            problemDetails.Extensions["retryAfter"] = (int)transientError.RetryAfter.Value.TotalSeconds;

        return problemDetails;
    }
}
