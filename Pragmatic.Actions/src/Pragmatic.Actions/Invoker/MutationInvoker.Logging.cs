using Microsoft.Extensions.Logging;
using Pragmatic.Actions.Mutation;

namespace Pragmatic.Actions.Invoker;

/// <summary>
///     Source-generated structured logging for <see cref="MutationInvoker{TMutation,TEntity}"/>.
///     Covers all 8 pipeline phases: validation → load/create → apply → L2 validate → persist → events → cache.
/// </summary>
public abstract partial class MutationInvoker<TMutation, TEntity>
    where TMutation : Mutation<TEntity>
    where TEntity : class
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Mutation {mutationType} starting")]
    protected partial void LogStart(string mutationType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mutation {mutationType} L1 sync validation failed: {errorCode}")]
    protected partial void LogSyncValidationFailed(string mutationType, string errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mutation {mutationType} L1 async validation failed: {errorCode}")]
    protected partial void LogAsyncValidationFailed(string mutationType, string errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mutation {mutationType} short-circuited by filter {filterName}")]
    protected partial void LogFilterShortCircuit(string mutationType, string filterName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Mutation {mutationType} mode={mode} loading entity")]
    protected partial void LogLoadingEntity(string mutationType, string mode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mutation {mutationType} entity not found (id={entityId})")]
    protected partial void LogEntityNotFound(string mutationType, string? entityId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Mutation {mutationType} creating new entity")]
    protected partial void LogCreatingEntity(string mutationType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Mutation {mutationType} applying changes")]
    protected partial void LogApplyingMutation(string mutationType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mutation {mutationType} apply failed: {errorCode}")]
    protected partial void LogApplyFailed(string mutationType, string errorCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Mutation {mutationType} deleting entity")]
    protected partial void LogDeletingEntity(string mutationType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Mutation {mutationType} L2 entity validation")]
    protected partial void LogEntityValidation(string mutationType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mutation {mutationType} L2 entity validation failed: {errorCode}")]
    protected partial void LogEntityValidationFailed(string mutationType, string errorCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Mutation {mutationType} persisting entity")]
    protected partial void LogPersistingEntity(string mutationType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Mutation {mutationType} dispatching {eventCount} domain events")]
    protected partial void LogDispatchingEvents(string mutationType, int eventCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Mutation {mutationType} invalidating cache")]
    protected partial void LogInvalidatingCache(string mutationType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Mutation {mutationType} running in batch mode — cache invalidation and event dispatch deferred until batch is flushed")]
    protected partial void LogBatchModeDeferred(string mutationType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mutation {mutationType} succeeded in {elapsedMs}ms")]
    protected partial void LogSuccess(string mutationType, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Mutation {mutationType} threw exception in {elapsedMs}ms")]
    protected partial void LogException(string mutationType, long elapsedMs, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mutation {mutationType} dropped {count} declared [Raises<T>] domain event(s): no IDomainEventDispatcher is registered — is Pragmatic.Events wired?")]
    protected partial void LogRaisedEventsDropped(string mutationType, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mutation {mutationType} is an ICacheInvalidator but no ICacheStack is registered — cache invalidation skipped, stale entries may be served")]
    protected partial void LogCacheInvalidationSkipped(string mutationType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Mutation {mutationType} committed successfully but a post-commit side effect (event dispatch / cache invalidation) threw — the mutation is NOT rolled back and is reported as success; the side effect failed")]
    protected partial void LogPostCommitSideEffectFailed(string mutationType, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Mutation {mutationType} failed and the committed work of {uncompensatedAction} could not be undone ({errorCode}) — the system is inconsistent, and the caller is told so")]
    protected partial void LogMutationCompensationFailed(string mutationType, string uncompensatedAction, string errorCode);
}
