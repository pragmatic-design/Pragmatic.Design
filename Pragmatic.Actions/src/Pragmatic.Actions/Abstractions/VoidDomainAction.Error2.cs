using Pragmatic.Result;

namespace Pragmatic.Actions.Abstractions;

/// <summary>
///     Base class for void actions that can produce two specific error types.
/// </summary>
public abstract class VoidDomainAction<TError1, TError2>
    : VoidDomainAction, IProducesError<TError1, TError2>
    where TError1 : IError
    where TError2 : IError
{
}
