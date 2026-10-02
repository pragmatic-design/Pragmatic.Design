using Microsoft.AspNetCore.Http;

namespace Pragmatic.Result.AspNetCore;

/// <summary>
///     Extension methods for converting Result types to HTTP responses (Minimal APIs).
/// </summary>
/// <remarks>
///     <para>
///         These extensions allow seamless integration between <see cref="Result{TValue, TError}" />
///         and ASP.NET Core Minimal APIs by converting results to <see cref="IResult" />.
///     </para>
///     <para>
///         Success results return the value with appropriate status code (200 OK, 201 Created, etc.).
///         Failure results return ProblemDetails with the error's HTTP status code.
///     </para>
///     <para>
///         For localization support, inject <see cref="IProblemDetailsFactory" /> and use the overloads
///         that accept the factory parameter.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Simple usage (no localization)
/// app.MapGet("/users/{id}", async (int id, UserService service) =>
/// {
///     var result = await service.GetUserAsync(id);
///     return result.ToHttpResult();
/// });
/// 
/// // With localization support (inject factory)
/// app.MapGet("/users/{id}", async (int id, IProblemDetailsFactory factory, UserService service) =>
/// {
///     var result = await service.GetUserAsync(id);
///     return result.ToHttpResult(factory);
/// });
/// </code>
/// </example>
public static class ResultHttpExtensions
{
    #region VoidResult<TError> - With Factory (DI)

    /// <summary>
    ///     Converts a VoidResult to an IResult for Minimal APIs with localization support.
    /// </summary>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="result">The result to convert</param>
    /// <param name="factory">The problem details factory</param>
    /// <param name="successStatusCode">HTTP status code for success (defaults to 204)</param>
    /// <returns>An IResult suitable for Minimal API responses</returns>
    public static IResult ToHttpResult<TError>(
        this VoidResult<TError> result,
        IProblemDetailsFactory factory,
        int successStatusCode = StatusCodes.Status204NoContent)
        where TError : IError
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (result.IsSuccess)
            return successStatusCode switch
            {
                StatusCodes.Status200OK => Results.Ok(),
                StatusCodes.Status202Accepted => Results.Accepted(),
                _ => Results.NoContent()
            };

        return ToErrorResult(result.Error, factory);
    }

    #endregion

    #region VoidResult<TError> - Static (No Localization)

    /// <summary>
    ///     Converts a VoidResult to an IResult for Minimal APIs.
    /// </summary>
    /// <typeparam name="TError">The error type</typeparam>
    /// <param name="result">The result to convert</param>
    /// <param name="successStatusCode">HTTP status code for success (defaults to 204)</param>
    /// <returns>An IResult suitable for Minimal API responses</returns>
    public static IResult ToHttpResult<TError>(
        this VoidResult<TError> result,
        int successStatusCode = StatusCodes.Status204NoContent)
        where TError : IError
    {
        if (result.IsSuccess)
            return successStatusCode switch
            {
                StatusCodes.Status200OK => Results.Ok(),
                StatusCodes.Status202Accepted => Results.Accepted(),
                _ => Results.NoContent()
            };

        return ToErrorResultStatic(result.Error);
    }

    #endregion

    #region Result<TValue, TError> - With Factory (DI)

    /// <param name="result">The result to convert</param>
    /// <typeparam name="TValue">The success value type</typeparam>
    /// <typeparam name="TError">The error type (must implement IError)</typeparam>
    extension<TValue, TError>(Result<TValue, TError> result) where TError : IError
    {
        /// <summary>
        ///     Converts a Result to an IResult for Minimal APIs with localization support.
        /// </summary>
        /// <param name="factory">The problem details factory (inject via DI for localization)</param>
        /// <param name="successStatusCode">HTTP status code for success (defaults to 200)</param>
        /// <returns>An IResult suitable for Minimal API responses</returns>
        public IResult ToHttpResult(IProblemDetailsFactory factory,
            int successStatusCode = StatusCodes.Status200OK)
        {
            ArgumentNullException.ThrowIfNull(factory);

            if (result.IsSuccess)
                return successStatusCode switch
                {
                    StatusCodes.Status201Created => Results.Created((string?)null, result.Value),
                    StatusCodes.Status202Accepted => Results.Accepted(null, result.Value),
                    StatusCodes.Status204NoContent => Results.NoContent(),
                    _ => Results.Ok(result.Value)
                };

            return ToErrorResult(result.Error, factory);
        }

        /// <summary>
        ///     Converts a Result to an IResult with 201 Created status on success.
        /// </summary>
        /// <param name="factory">The problem details factory</param>
        /// <param name="location">Optional URI for the created resource</param>
        /// <returns>An IResult suitable for Minimal API responses</returns>
        public IResult ToCreatedResult(IProblemDetailsFactory factory,
            string? location = null)
        {
            ArgumentNullException.ThrowIfNull(factory);

            if (result.IsSuccess)
                return Results.Created(location, result.Value);

            return ToErrorResult(result.Error, factory);
        }

        /// <summary>
        ///     Converts a Result to an IResult with 204 No Content on success.
        /// </summary>
        /// <param name="factory">The problem details factory</param>
        /// <returns>An IResult suitable for Minimal API responses</returns>
        public IResult ToNoContentResult(IProblemDetailsFactory factory)
        {
            ArgumentNullException.ThrowIfNull(factory);

            if (result.IsSuccess)
                return Results.NoContent();

            return ToErrorResult(result.Error, factory);
        }
    }

    #endregion

    #region Result<TValue, TError> - Static (No Localization)

    /// <param name="result">The result to convert</param>
    /// <typeparam name="TValue">The success value type</typeparam>
    /// <typeparam name="TError">The error type (must implement IError)</typeparam>
    extension<TValue, TError>(Result<TValue, TError> result) where TError : IError
    {
        /// <summary>
        ///     Converts a Result to an IResult for Minimal APIs.
        /// </summary>
        /// <param name="successStatusCode">HTTP status code for success (defaults to 200)</param>
        /// <returns>An IResult suitable for Minimal API responses</returns>
        /// <remarks>
        ///     This overload does not support localization. For localization, inject
        ///     <see cref="IProblemDetailsFactory" /> and use the factory-accepting overload.
        /// </remarks>
        public IResult ToHttpResult(int successStatusCode = StatusCodes.Status200OK)
        {
            if (result.IsSuccess)
                return successStatusCode switch
                {
                    StatusCodes.Status201Created => Results.Created((string?)null, result.Value),
                    StatusCodes.Status202Accepted => Results.Accepted(null, result.Value),
                    StatusCodes.Status204NoContent => Results.NoContent(),
                    _ => Results.Ok(result.Value)
                };

            return ToErrorResultStatic(result.Error);
        }

        /// <summary>
        ///     Converts a Result to an IResult with 201 Created status on success.
        /// </summary>
        /// <param name="location">Optional URI for the created resource</param>
        /// <returns>An IResult suitable for Minimal API responses</returns>
        public IResult ToCreatedResult(string? location = null)
        {
            if (result.IsSuccess)
                return Results.Created(location, result.Value);

            return ToErrorResultStatic(result.Error);
        }

        /// <summary>
        ///     Converts a Result to an IResult with 204 No Content on success.
        /// </summary>
        /// <returns>An IResult suitable for Minimal API responses</returns>
        public IResult ToNoContentResult()
        {
            if (result.IsSuccess)
                return Results.NoContent();

            return ToErrorResultStatic(result.Error);
        }
    }

    #endregion

    #region Private Helpers

    private static IResult ToErrorResult(IError error, IProblemDetailsFactory factory)
    {
        var problemDetails = factory.Create(error);
        return Results.Problem(problemDetails);
    }

    private static IResult ToErrorResultStatic(IError error)
    {
        var problemDetails = ProblemDetailsFactory.Create(error);
        return Results.Problem(problemDetails);
    }

    #endregion
}