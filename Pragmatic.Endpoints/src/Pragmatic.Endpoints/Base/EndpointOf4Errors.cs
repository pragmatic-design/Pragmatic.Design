using Pragmatic.Result;

namespace Pragmatic.Endpoints.Base;

/// <summary>
///     Base class for endpoints that return a response with four typed errors.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <typeparam name="TError1">The first error type.</typeparam>
/// <typeparam name="TError2">The second error type.</typeparam>
/// <typeparam name="TError3">The third error type.</typeparam>
/// <typeparam name="TError4">The fourth error type.</typeparam>
public abstract class Endpoint<TResponse, TError1, TError2, TError3, TError4>
    where TError1 : IError
    where TError2 : IError
    where TError3 : IError
    where TError4 : IError
{
    /// <summary>
    ///     Handles the endpoint request.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result containing the response or error.</returns>
    public abstract Task<Result<TResponse, TError1, TError2, TError3, TError4>> HandleAsync(
        CancellationToken ct = default);
}
