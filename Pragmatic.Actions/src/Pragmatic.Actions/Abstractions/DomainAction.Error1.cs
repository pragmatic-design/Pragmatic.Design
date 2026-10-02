using Pragmatic.Result;

namespace Pragmatic.Actions.Abstractions;

/// <summary>
///     Base class for domain actions that return a value and can produce one specific error type.
/// </summary>
/// <typeparam name="TReturn">The return type on success.</typeparam>
/// <typeparam name="TError">The error type this action can produce.</typeparam>
public abstract class DomainAction<TReturn, TError>
    : DomainAction<TReturn>, IProducesError<TError>
    where TError : IError
{
}
