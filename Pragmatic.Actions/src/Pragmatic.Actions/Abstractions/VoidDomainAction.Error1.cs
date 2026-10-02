using Pragmatic.Result;

namespace Pragmatic.Actions.Abstractions;

/// <summary>
///     Base class for void actions that can produce one specific error type.
/// </summary>
/// <typeparam name="TError">The error type this action can produce.</typeparam>
public abstract class VoidDomainAction<TError>
    : VoidDomainAction, IProducesError<TError>
    where TError : IError
{
}
