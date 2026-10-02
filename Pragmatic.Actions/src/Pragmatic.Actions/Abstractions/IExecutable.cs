using Pragmatic.Result;

namespace Pragmatic.Actions.Abstractions;

/// <summary>
///     Interface for types that can execute and return a result.
///     All DomainAction base classes implement this interface.
/// </summary>
/// <typeparam name="TReturn">The return type on success.</typeparam>
public interface IExecutable<TReturn>
{
    /// <summary>
    ///     Executes the action and returns a result.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result containing the return value or an error.</returns>
    Task<Result<TReturn, IError>> Execute(CancellationToken ct = default);
}
