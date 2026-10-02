using Pragmatic.Result;

namespace Pragmatic.Actions.Abstractions;

/// <summary>
///     Marker interface indicating that an action can produce a specific error type.
///     Used by the source generator to document error types in OpenAPI.
/// </summary>
/// <typeparam name="TError">The error type this action can produce.</typeparam>
public interface IProducesError<TError>
    where TError : IError
{
}
