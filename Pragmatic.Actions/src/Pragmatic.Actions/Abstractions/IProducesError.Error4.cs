using Pragmatic.Result;

namespace Pragmatic.Actions.Abstractions;

/// <summary>
///     Marker interface indicating that an action can produce four specific error types.
/// </summary>
public interface IProducesError<TError1, TError2, TError3, TError4>
    where TError1 : IError
    where TError2 : IError
    where TError3 : IError
    where TError4 : IError
{
}
