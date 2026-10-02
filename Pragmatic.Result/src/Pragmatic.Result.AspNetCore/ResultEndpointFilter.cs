using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Result.AspNetCore;

/// <summary>
///     Endpoint filter that automatically converts Result types to HTTP responses.
/// </summary>
/// <remarks>
///     <para>
///         This filter intercepts endpoint responses and converts <see cref="IResultBase" /> types
///         to appropriate <see cref="IResult" /> responses:
///         <list type="bullet">
///             <item>Success with value → 200 OK with JSON body</item>
///             <item>Success without value (VoidResult) → 204 No Content</item>
///             <item>Failure → ProblemDetails with appropriate status code</item>
///         </list>
///     </para>
///     <para>
///         Use <c>WithResultHandling()</c> extension to register this filter.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Register for all endpoints in a group
/// app.MapGroup("").WithResultHandling()
///    .MapGet("/users/{id}", async (int id, UserService svc)
///        => await svc.GetUserAsync(id));  // Returns Result&lt;User, NotFoundError&gt;
/// </code>
/// </example>
public sealed partial class ResultEndpointFilter : IEndpointFilter
{
    private static readonly ActivitySource ActivitySource = new("Pragmatic.Result", "1.0.0");

    private readonly ILogger<ResultEndpointFilter>? _logger;

    /// <summary>Creates a new instance of the filter.</summary>
    public ResultEndpointFilter(ILogger<ResultEndpointFilter>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var result = await next(context).ConfigureAwait(false);

        // If result is not a Result type, pass through unchanged
        if (result is not IResultBase resultBase)
            return result;

        // Check for opt-out attribute
        var endpoint = context.HttpContext.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<SkipResultHandlingAttribute>() is not null)
        {
            if (_logger is not null)
                LogHandlingSkipped(_logger);
            return result;
        }

        using var activity = ActivitySource.StartActivity("result.conversion");
        activity?.SetTag(ResultTags.IsSuccess, resultBase.IsSuccess);

        // Resolve the factory from DI
        var factory = context.HttpContext.RequestServices.GetService(typeof(IProblemDetailsFactory))
            as IProblemDetailsFactory;

        var httpResult = ConvertToHttpResult(resultBase, factory);

        if (resultBase.IsSuccess)
        {
            if (_logger is not null)
                LogSuccessResponse(_logger);
        }
        else
        {
            var error = resultBase.ErrorAsObject;
            activity?.SetTag(ResultTags.ErrorCode, error?.Code);
            if (_logger is not null)
                LogErrorResponse(_logger, error?.Code);
        }

        return httpResult;
    }

    private static IResult ConvertToHttpResult(IResultBase result, IProblemDetailsFactory? factory)
    {
        if (result.IsSuccess)
        {
            // Use HasValueType to distinguish VoidResult from Result<T> where T happens to be null
            if (!result.HasValueType)
                return Results.NoContent(); // VoidResult success

            var value = result.ValueAsObject;
            return Results.Ok(value); // Result<T> success (null is a valid value)
        }

        // Failure - convert to ProblemDetails
        var error = result.ErrorAsObject;
        if (error is null)
            return Results.Problem("An unknown error occurred.", statusCode: 500);

        // If no factory is registered, build ProblemDetails directly to avoid dereferencing a null factory.
        var problemDetails = factory is not null
            ? factory.Create(error)
            : new ProblemDetails
            {
                Status = error.StatusCode,
                Title = string.IsNullOrEmpty(error.Title) ? null : error.Title,
                Detail = error.Description
            };
        return Results.Problem(problemDetails);
    }

    // LoggerMessage source-generated methods (zero allocation)

    [LoggerMessage(Level = LogLevel.Debug, Message = "Result handling skipped due to SkipResultHandlingAttribute")]
    private static partial void LogHandlingSkipped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Result converted to success response")]
    private static partial void LogSuccessResponse(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Result converted to error response: {ErrorCode}")]
    private static partial void LogErrorResponse(ILogger logger, string? errorCode);
}