using Pragmatic.Result;

namespace Pragmatic.Actions.Abstractions;

/// <summary>
///     Interface for void actions that can execute and return a VoidResult.
///     All VoidDomainAction base classes implement this interface.
/// </summary>
public interface IVoidExecutable
{
    /// <summary>
    ///     Executes the action and returns a void result (success/failure only).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A VoidResult indicating success or containing an error.</returns>
    Task<VoidResult<IError>> Execute(CancellationToken ct = default);
}
