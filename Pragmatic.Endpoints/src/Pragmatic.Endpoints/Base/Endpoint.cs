using Pragmatic.Result;

namespace Pragmatic.Endpoints.Base;

/// <summary>
///     Base class for endpoints that return a response without typed errors.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public abstract class Endpoint<TResponse>
{
    /// <summary>
    ///     Handles the endpoint request.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result containing the response.</returns>
    public abstract Task<Result<TResponse>> HandleAsync(CancellationToken ct = default);
}
