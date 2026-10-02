using Pragmatic.Result;

namespace Pragmatic.Actions.Abstractions;

/// <summary>
///     Marker interface indicating that an action can produce three specific error types.
/// </summary>
public interface IProducesError<TError1, TError2, TError3>
    where TError1 : IError
    where TError2 : IError
    where TError3 : IError
{
}
