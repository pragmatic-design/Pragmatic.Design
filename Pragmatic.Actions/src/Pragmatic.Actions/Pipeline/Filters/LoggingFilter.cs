using Microsoft.Extensions.Logging;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Result;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Actions.Pipeline.Filters;

/// <summary>
///     Built-in filter that logs action execution details.
///     Runs at Order 1000 (after business logic filters).
/// </summary>
/// <remarks>
///     <para>
///         This filter provides structured logging of action execution with zero allocation
///         using LoggerMessage source generator. It logs:
///     </para>
///     <list type="bullet">
///         <item>Action type and execution start (Debug)</item>
///         <item>Action success with result type (Information)</item>
///         <item>Action failure with error details (Warning)</item>
///     </list>
///     <para>
///         For security, this filter does NOT log action parameters or result values.
///         Use custom filters if you need to log specific data with proper masking.
///     </para>
/// </remarks>
internal sealed partial class LoggingFilter : IActionFilter
{
    private readonly ILogger<LoggingFilter> _logger;

    /// <summary>
    ///     Creates a new LoggingFilter.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public LoggingFilter(ILogger<LoggingFilter> logger)
    {
        ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public int Order => FilterOrder.Logging;

    /// <inheritdoc />
    public Task<VoidResult<IError>> BeforeExecuteAsync<TAction, TReturn>(
        TAction action,
        CancellationToken ct)
        where TAction : DomainAction<TReturn>
    {
        var actionType = typeof(TAction);
        LogActionExecuting(actionType.Name, actionType.FullName ?? actionType.Name);

        return Task.FromResult(VoidResult<IError>.Success());
    }

    /// <inheritdoc />
    public Task AfterExecuteAsync<TAction, TReturn>(
        TAction action,
        Result<TReturn, IError> result,
        CancellationToken ct)
        where TAction : DomainAction<TReturn>
    {
        var actionName = typeof(TAction).Name;

        if (result.IsSuccess)
        {
            var resultType = typeof(TReturn).Name;
            LogActionSucceeded(actionName, resultType);
        }
        else
        {
            LogActionFailed(actionName, result.Error.Code);
        }

        return Task.CompletedTask;
    }

    // =========================================================================
    // LoggerMessage - Zero Allocation
    // =========================================================================

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Executing action {ActionName} ({ActionFullName})")]
    private partial void LogActionExecuting(string actionName, string actionFullName);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Action {ActionName} completed successfully with result type {ResultType}")]
    private partial void LogActionSucceeded(string actionName, string resultType);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Action {ActionName} failed with error {ErrorCode}")]
    private partial void LogActionFailed(string actionName, string errorCode);
}
