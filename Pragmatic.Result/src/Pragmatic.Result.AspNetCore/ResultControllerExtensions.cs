using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Pragmatic.Result.AspNetCore;

/// <summary>
///     Extension methods for converting Result types to ActionResult (MVC Controllers).
/// </summary>
/// <remarks>
///     <para>
///         These extensions allow integration between <see cref="Result{TValue, TError}" />
///         and ASP.NET Core MVC Controllers by converting results to <see cref="IActionResult" />.
///     </para>
///     <para>
///         Success results return the value with appropriate status code (200 OK, 201 Created, etc.).
///         Failure results return ProblemDetails with the error's HTTP status code.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [ApiController]
/// public class UsersController(IProblemDetailsFactory problemDetailsFactory) : ControllerBase
/// {
///     [HttpGet("{id}")]
///     public async Task&lt;IActionResult&gt; Get(int id)
///     {
///         var result = await _userService.GetUserAsync(id);
///         return result.ToActionResult(problemDetailsFactory);
///     }
/// }
/// </code>
/// </example>
public static class ResultControllerExtensions
{
    /// <param name="result">The result to convert</param>
    /// <typeparam name="TValue">The success value type</typeparam>
    /// <typeparam name="TError">The error type (must implement <see cref="IError" />)</typeparam>
    extension<TValue, TError>(Result<TValue, TError> result) where TError : IError
    {
        /// <summary>
        ///     Converts a Result to an IActionResult for MVC Controllers.
        /// </summary>
        /// <param name="factory">The problem details factory (inject via DI)</param>
        /// <param name="successStatusCode">HTTP status code for success (defaults to 200)</param>
        /// <returns>An IActionResult suitable for MVC controller responses</returns>
        public IActionResult ToActionResult(IProblemDetailsFactory factory,
            int successStatusCode = StatusCodes.Status200OK)
        {
            ArgumentNullException.ThrowIfNull(factory);

            if (result.IsSuccess)
                return (successStatusCode, value: result.Value) switch
                {
                    (StatusCodes.Status204NoContent, _) => new NoContentResult(),
                    (_, null) => new StatusCodeResult(successStatusCode),
                    (StatusCodes.Status201Created, var value) => new ObjectResult(value)
                        { StatusCode = StatusCodes.Status201Created },
                    (StatusCodes.Status202Accepted, var value) => new AcceptedResult((string?)null, value),
                    (_, var value) => new OkObjectResult(value)
                };

            return ToErrorActionResult(result.Error, factory);
        }

        /// <summary>
        ///     Converts a Result to an IActionResult with 201 Created status on success.
        /// </summary>
        /// <param name="factory">The problem details factory</param>
        /// <param name="location">Optional URI for the created resource</param>
        /// <returns>An IActionResult suitable for MVC controller responses</returns>
        public IActionResult ToCreatedActionResult(IProblemDetailsFactory factory,
            string? location = null)
        {
            ArgumentNullException.ThrowIfNull(factory);

            if (result.IsSuccess)
                return new CreatedResult(location, result.Value);

            return ToErrorActionResult(result.Error, factory);
        }

        /// <summary>
        ///     Converts a Result to an IActionResult with 201 Created and location from route values.
        /// </summary>
        /// <param name="factory">The problem details factory</param>
        /// <param name="actionName">The action name for the location header</param>
        /// <param name="routeValues">Route values for generating the location URI</param>
        /// <returns>An IActionResult suitable for MVC controller responses</returns>
        public IActionResult ToCreatedAtActionResult(IProblemDetailsFactory factory,
            string actionName,
            object? routeValues = null)
        {
            ArgumentNullException.ThrowIfNull(factory);

            if (result.IsSuccess)
                return new CreatedAtActionResult(actionName, null, routeValues, result.Value);

            return ToErrorActionResult(result.Error, factory);
        }

        /// <summary>
        ///     Converts a Result to an IActionResult with 204 No Content on success.
        /// </summary>
        /// <param name="factory">The problem details factory</param>
        /// <returns>An IActionResult suitable for MVC controller responses</returns>
        public IActionResult ToNoContentActionResult(IProblemDetailsFactory factory)
        {
            ArgumentNullException.ThrowIfNull(factory);

            if (result.IsSuccess)
                return new NoContentResult();

            return ToErrorActionResult(result.Error, factory);
        }
    }

    /// <summary>
    ///     Converts a VoidResult to an IActionResult for MVC Controllers.
    /// </summary>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="result">The result to convert</param>
    /// <param name="factory">The problem details factory</param>
    /// <param name="successStatusCode">HTTP status code for success (defaults to 204)</param>
    /// <returns>An IActionResult suitable for MVC controller responses</returns>
    public static IActionResult ToActionResult<TError>(
        this VoidResult<TError> result,
        IProblemDetailsFactory factory,
        int successStatusCode = StatusCodes.Status204NoContent)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (result.IsSuccess)
            return successStatusCode switch
            {
                StatusCodes.Status200OK => new OkResult(),
                StatusCodes.Status202Accepted => new AcceptedResult(),
                _ => new NoContentResult()
            };

        return ToErrorActionResult(result.Error, factory);
    }

    private static IActionResult ToErrorActionResult(IError error, IProblemDetailsFactory factory)
    {
        var problemDetails = factory.Create(error);
        return new ObjectResult(problemDetails)
        {
            StatusCode = problemDetails.Status ?? error.StatusCode
        };
    }
}