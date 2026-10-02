using Pragmatic.Result;

namespace Pragmatic.Actions.Abstractions;

/// <summary>
///     Base class for void actions that can produce six specific error types.
///     This is the maximum number of error types supported.
/// </summary>
public abstract class VoidDomainAction<TError1, TError2, TError3, TError4, TError5, TError6>
    : VoidDomainAction, IProducesError<TError1, TError2, TError3, TError4, TError5, TError6>
    where TError1 : IError
    where TError2 : IError
    where TError3 : IError
    where TError4 : IError
    where TError5 : IError
    where TError6 : IError
{
}
