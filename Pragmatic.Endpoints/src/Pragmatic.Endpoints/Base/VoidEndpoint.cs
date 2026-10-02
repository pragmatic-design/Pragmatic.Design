using Pragmatic.Result;

namespace Pragmatic.Endpoints.Base;

/// <summary>
///     Base class for endpoints that don't return a response body (e.g., DELETE, some PUT).
///     Returns 204 No Content on success.
/// </summary>
public abstract class VoidEndpoint
{
    /// <summary>
    ///     Handles the endpoint request.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The void result indicating success or failure.</returns>
    public abstract Task<VoidResult> HandleAsync(CancellationToken ct = default);
}

/// <summary>
///     Base class for void endpoints with one typed error.
///     Returns 204 No Content on success.
/// </summary>
/// <typeparam name="TError1">The first error type.</typeparam>
public abstract class VoidEndpoint<TError1>
    where TError1 : IError
{
    /// <summary>
    ///     Handles the endpoint request.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The void result indicating success or error.</returns>
    public abstract Task<VoidResult<TError1>> HandleAsync(CancellationToken ct = default);
}

/// <summary>
///     Base class for void endpoints with two typed errors.
///     Returns 204 No Content on success.
/// </summary>
/// <typeparam name="TError1">The first error type.</typeparam>
/// <typeparam name="TError2">The second error type.</typeparam>
public abstract class VoidEndpoint<TError1, TError2>
    where TError1 : IError
    where TError2 : IError
{
    /// <summary>
    ///     Handles the endpoint request.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The void result indicating success or error.</returns>
    public abstract Task<VoidResult<TError1, TError2>> HandleAsync(CancellationToken ct = default);
}

/// <summary>
///     Base class for void endpoints with three typed errors.
///     Returns 204 No Content on success.
/// </summary>
/// <typeparam name="TError1">The first error type.</typeparam>
/// <typeparam name="TError2">The second error type.</typeparam>
/// <typeparam name="TError3">The third error type.</typeparam>
public abstract class VoidEndpoint<TError1, TError2, TError3>
    where TError1 : IError
    where TError2 : IError
    where TError3 : IError
{
    /// <summary>
    ///     Handles the endpoint request.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The void result indicating success or error.</returns>
    public abstract Task<VoidResult<TError1, TError2, TError3>> HandleAsync(CancellationToken ct = default);
}

/// <summary>
///     Base class for void endpoints with four typed errors.
///     Returns 204 No Content on success.
/// </summary>
/// <typeparam name="TError1">The first error type.</typeparam>
/// <typeparam name="TError2">The second error type.</typeparam>
/// <typeparam name="TError3">The third error type.</typeparam>
/// <typeparam name="TError4">The fourth error type.</typeparam>
public abstract class VoidEndpoint<TError1, TError2, TError3, TError4>
    where TError1 : IError
    where TError2 : IError
    where TError3 : IError
    where TError4 : IError
{
    /// <summary>
    ///     Handles the endpoint request.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The void result indicating success or error.</returns>
    public abstract Task<VoidResult<TError1, TError2, TError3, TError4>> HandleAsync(CancellationToken ct = default);
}

/// <summary>
///     Base class for void endpoints with five typed errors.
///     Returns 204 No Content on success.
/// </summary>
/// <typeparam name="TError1">The first error type.</typeparam>
/// <typeparam name="TError2">The second error type.</typeparam>
/// <typeparam name="TError3">The third error type.</typeparam>
/// <typeparam name="TError4">The fourth error type.</typeparam>
/// <typeparam name="TError5">The fifth error type.</typeparam>
public abstract class VoidEndpoint<TError1, TError2, TError3, TError4, TError5>
    where TError1 : IError
    where TError2 : IError
    where TError3 : IError
    where TError4 : IError
    where TError5 : IError
{
    /// <summary>
    ///     Handles the endpoint request.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The void result indicating success or error.</returns>
    public abstract Task<VoidResult<TError1, TError2, TError3, TError4, TError5>> HandleAsync(
        CancellationToken ct = default);
}

/// <summary>
///     Base class for void endpoints with six typed errors.
///     Returns 204 No Content on success.
/// </summary>
/// <typeparam name="TError1">The first error type.</typeparam>
/// <typeparam name="TError2">The second error type.</typeparam>
/// <typeparam name="TError3">The third error type.</typeparam>
/// <typeparam name="TError4">The fourth error type.</typeparam>
/// <typeparam name="TError5">The fifth error type.</typeparam>
/// <typeparam name="TError6">The sixth error type.</typeparam>
public abstract class VoidEndpoint<TError1, TError2, TError3, TError4, TError5, TError6>
    where TError1 : IError
    where TError2 : IError
    where TError3 : IError
    where TError4 : IError
    where TError5 : IError
    where TError6 : IError
{
    /// <summary>
    ///     Handles the endpoint request.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The void result indicating success or error.</returns>
    public abstract Task<VoidResult<TError1, TError2, TError3, TError4, TError5, TError6>> HandleAsync(
        CancellationToken ct = default);
}