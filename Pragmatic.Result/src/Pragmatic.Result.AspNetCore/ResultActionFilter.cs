using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Result.AspNetCore;

/// <summary>
///     Action filter that automatically converts Result types to ActionResult.
/// </summary>
/// <remarks>
///     <para>
///         This filter intercepts controller action responses and converts <see cref="IResultBase" /> types
///         to appropriate <see cref="IActionResult" /> responses:
///         <list type="bullet">
///             <item>Success with value → 200 OK with JSON body</item>
///             <item>Success without value (VoidResult) → 204 No Content</item>
///             <item>Failure → ProblemDetails with appropriate status code</item>
///         </list>
///     </para>
///     <para>
///         Register globally in Program.cs or apply to specific controllers.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Global registration
/// builder.Services.AddControllers(options =>
/// {
///     options.Filters.Add&lt;ResultActionFilter&gt;();
/// });
/// 
/// // Controller action returns Result directly
/// [HttpGet("{id}")]
/// public async Task&lt;Result&lt;User, NotFoundError&gt;&gt; Get(int id)
///     => await _userService.GetByIdAsync(id);
/// </code>
/// </example>
public sealed partial class ResultActionFilter : IAsyncResultFilter
{
    // Process-lifetime static, intentionally NOT disposed: an ActivitySource must stay available for the
    // entire application lifetime, so there is no meaningful point at which a library could dispose it.
    // Its resources are reclaimed by the runtime at process exit. This is the standard System.Diagnostics
    // pattern, and the reason this type does not implement IDisposable.
    private static readonly ActivitySource ActivitySource = new("Pragmatic.Result", "1.0.0");

    private readonly ILogger<ResultActionFilter>? _logger;

    /// <summary>Creates a new instance of the filter.</summary>
    public ResultActionFilter(ILogger<ResultActionFilter>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        // Check for opt-out attribute
        if (context.ActionDescriptor.EndpointMetadata.Any(m => m is SkipResultHandlingAttribute))
        {
            if (_logger is not null)
                LogHandlingSkipped(_logger);
            await next().ConfigureAwait(false);
            return;
        }

        // Check if result is an ObjectResult containing a Result type
        if (context.Result is ObjectResult { Value: IResultBase resultBase })
        {
            using var activity = ActivitySource.StartActivity("result.conversion");
            activity?.SetTag(ResultTags.IsSuccess, resultBase.IsSuccess);

            var factory = context.HttpContext.RequestServices.GetService<IProblemDetailsFactory>();
            context.Result = ConvertToActionResult(resultBase, factory);

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
        }

        await next().ConfigureAwait(false);
    }

    private static IActionResult ConvertToActionResult(IResultBase result, IProblemDetailsFactory? factory)
    {
        if (result.IsSuccess)
        {
            // VoidResult success → 204 No Content
            if (!result.HasValueType)
                return new NoContentResult();

            // Result<T> success → 200 OK (even if value is null)
            return new OkObjectResult(result.ValueAsObject);
        }

        // Failure - convert to ProblemDetails
        var error = result.ErrorAsObject;
        if (error is null)
            return new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Internal Server Error",
                Detail = "An unknown error occurred."
            })
            { StatusCode = StatusCodes.Status500InternalServerError };

        var problemDetails = factory?.Create(error)
                             ?? ProblemDetailsFactory.Create(error);

        return new ObjectResult(problemDetails)
        {
            StatusCode = problemDetails.Status
        };
    }

    // LoggerMessage source-generated methods (zero allocation)

    [LoggerMessage(Level = LogLevel.Debug, Message = "Result handling skipped due to SkipResultHandlingAttribute")]
    private static partial void LogHandlingSkipped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Result converted to success response")]
    private static partial void LogSuccessResponse(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Result converted to error response: {ErrorCode}")]
    private static partial void LogErrorResponse(ILogger logger, string? errorCode);
}