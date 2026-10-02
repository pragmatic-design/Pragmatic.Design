using Pragmatic.Result;

namespace Pragmatic.Actions.Abstractions;

/// <summary>
///     Marker interface indicating that an action can produce two specific error types.
/// </summary>
public interface IProducesError<TError1, TError2>
    where TError1 : IError
    where TError2 : IError
{
}
