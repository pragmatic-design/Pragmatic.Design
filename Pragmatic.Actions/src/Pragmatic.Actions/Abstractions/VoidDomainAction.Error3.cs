using Pragmatic.Result;

namespace Pragmatic.Actions.Abstractions;

/// <summary>
///     Base class for void actions that can produce three specific error types.
/// </summary>
public abstract class VoidDomainAction<TError1, TError2, TError3>
    : VoidDomainAction, IProducesError<TError1, TError2, TError3>
    where TError1 : IError
    where TError2 : IError
    where TError3 : IError
{
}
