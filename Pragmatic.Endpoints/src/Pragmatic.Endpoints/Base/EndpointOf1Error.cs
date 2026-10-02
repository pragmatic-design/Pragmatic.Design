using Pragmatic.Result;

namespace Pragmatic.Endpoints.Base;

/// <summary>
///     Base class for endpoints that return a response with one typed error.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <typeparam name="TError1">The first error type.</typeparam>
public abstract class Endpoint<TResponse, TError1>
    where TError1 : IError
{
    /// <summary>
    ///     Handles the endpoint request.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result containing the response or error.</returns>
    public abstract Task<Result<TResponse, TError1>> HandleAsync(CancellationToken ct = default);
}
