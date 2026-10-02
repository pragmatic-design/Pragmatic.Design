using Microsoft.Extensions.Logging;

namespace Pragmatic.Actions.Invoker;

/// <summary>
///     Source-generated structured logging for <see cref="ActionInvokerBase"/>.
///     All messages use the <c>kind</c> parameter ("Action" or "VoidAction") to distinguish invocation type.
/// </summary>
public abstract partial class ActionInvokerBase
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "{kind} {actionName} starting")]
    protected partial void LogStart(string kind, string actionName);

    [LoggerMessage(Level = LogLevel.Trace, Message = "{kind} {actionName} has {filterCount} filters")]
    protected partial void LogFilters(string kind, string actionName, int filterCount);

    [LoggerMessage(Level = LogLevel.Trace, Message = "{kind} {actionName}: Filter {filterName} BeforeExecute")]
    protected partial void LogBeforeFilter(string kind, string actionName, string filterName);

    [LoggerMessage(Level = LogLevel.Trace, Message = "{kind} {actionName}: Filter {filterName} AfterExecute")]
    protected partial void LogAfterFilter(string kind, string actionName, string filterName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{kind} {actionName}: Filter {filterName} short-circuited with error {errorCode}")]
    protected partial void LogShortCircuit(string kind, string actionName, string filterName, string errorCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{kind} {actionName} executing")]
    protected partial void LogExecuting(string kind, string actionName);

    [LoggerMessage(Level = LogLevel.Information, Message = "{kind} {actionName} succeeded in {elapsedMs}ms")]
    protected partial void LogSuccess(string kind, string actionName, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{kind} {actionName} failed with {errorCode} in {elapsedMs}ms")]
    protected partial void LogFailure(string kind, string actionName, string errorCode, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "{kind} {actionName} threw exception in {elapsedMs}ms")]
    protected partial void LogException(string kind, string actionName, long elapsedMs, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{kind} {actionName} preparing (loading entities)")]
    protected partial void LogPreparingAction(string kind, string actionName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{kind} {actionName} preparation failed: {errorCode}")]
    protected partial void LogPreparationFailed(string kind, string actionName, string errorCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{kind} {actionName} saving changes")]
    protected partial void LogSavingChanges(string kind, string actionName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dropped {count} declared [Raises<T>] domain event(s): no IDomainEventDispatcher is registered — is Pragmatic.Events wired?")]
    protected partial void LogRaisedEventsDropped(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{kind} {actionName} declares [InvalidatesCache] but no ICacheStack is registered — cache invalidation skipped, stale entries may be served")]
    protected partial void LogCacheInvalidationSkipped(string kind, string actionName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{kind} {actionName} declares [Cacheable] but no ICacheStack is registered — every invocation runs the body")]
    protected partial void LogCacheReadSkipped(string kind, string actionName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{kind} {actionName} answered from the cache")]
    protected partial void LogServedFromCache(string kind, string actionName);

    [LoggerMessage(Level = LogLevel.Error, Message = "{kind} {actionName} committed successfully but a post-commit side effect (event dispatch / AfterExecute filter) threw — the operation is NOT rolled back and is reported as success; the side effect failed")]
    protected partial void LogPostCommitSideEffectFailed(string kind, string actionName, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "{kind} {actionName} failed and the committed work of {uncompensatedAction} could not be undone ({errorCode}) — the system is inconsistent, and the caller is told so")]
    protected partial void LogCompensationFailed(string kind, string actionName, string uncompensatedAction, string errorCode);
}
