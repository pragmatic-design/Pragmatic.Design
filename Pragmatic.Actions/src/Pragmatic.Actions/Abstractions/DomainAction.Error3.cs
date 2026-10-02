using Pragmatic.Result;

namespace Pragmatic.Actions.Abstractions;

/// <summary>
///     Base class for domain actions that return a value and can produce three specific error types.
/// </summary>
/// <typeparam name="TReturn">The return type on success.</typeparam>
/// <typeparam name="TError1">The first error type this action can produce.</typeparam>
/// <typeparam name="TError2">The second error type this action can produce.</typeparam>
/// <typeparam name="TError3">The third error type this action can produce.</typeparam>
public abstract class DomainAction<TReturn, TError1, TError2, TError3>
    : DomainAction<TReturn>, IProducesError<TError1, TError2, TError3>
    where TError1 : IError
    where TError2 : IError
    where TError3 : IError
{
}
