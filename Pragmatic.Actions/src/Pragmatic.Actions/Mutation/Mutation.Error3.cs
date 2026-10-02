using Pragmatic.Result;

namespace Pragmatic.Actions.Mutation;

/// <summary>
///     Base class for mutations that can produce three specific error types.
/// </summary>
public abstract class Mutation<TEntity, TError1, TError2, TError3>
    : Mutation<TEntity>
    where TEntity : class
    where TError1 : IError
    where TError2 : IError
    where TError3 : IError;
