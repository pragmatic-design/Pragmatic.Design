using Pragmatic.Result;

namespace Pragmatic.Actions.Mutation;

/// <summary>
///     Base class for mutations that can produce one specific error type.
/// </summary>
/// <typeparam name="TEntity">The entity type this mutation targets.</typeparam>
/// <typeparam name="TError">The error type this mutation can produce.</typeparam>
public abstract class Mutation<TEntity, TError>
    : Mutation<TEntity>
    where TEntity : class
    where TError : IError;
