using Microsoft.Extensions.Logging;

namespace Pragmatic.Validation;

/// <summary>
///     Source-generated structured logging for <see cref="CompositeValidator{T}"/>.
///     Covers sync and async validation phases.
/// </summary>
internal sealed partial class CompositeValidator<T>
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Validation starting for {typeName}")]
    private partial void LogValidationStarting(string typeName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sync validation passed for {typeName}")]
    private partial void LogSyncValidationPassed(string typeName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sync validation failed for {typeName}: {errorCount} error(s)")]
    private partial void LogSyncValidationFailed(string typeName, int errorCount);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Async validator '{validatorType}' skipped (property trigger not matched)")]
    private partial void LogAsyncValidatorSkipped(string validatorType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Async validator '{validatorType}' executing for {typeName}")]
    private partial void LogAsyncValidatorExecuting(string validatorType, string typeName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Async validator '{validatorType}' failed for {typeName}: {errorCount} error(s)")]
    private partial void LogAsyncValidatorFailed(string validatorType, string typeName, int errorCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Validation completed for {typeName}: {errorCount} total error(s)")]
    private partial void LogValidationCompleted(string typeName, int errorCount);
}
