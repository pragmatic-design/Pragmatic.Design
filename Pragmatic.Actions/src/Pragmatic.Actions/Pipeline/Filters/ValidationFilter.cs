using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Result;
using Pragmatic.Validation;
using Pragmatic.Validation.Types;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Actions.Pipeline.Filters;

/// <summary>
///     Built-in filter that validates DomainActions.
///     Runs at Order 100 (before all other business logic filters).
/// </summary>
/// <remarks>
///     <para>
///         <b>Default behavior:</b> sync validation (from validation attributes), and async validation
///         when the generator found a <c>[Validator]</c> for the action or a nested property — it writes
///         that decision into <see cref="IActionValidationMetadata" />.
///     </para>
///     <para>
///         <b>Validation modes:</b>
///     </para>
///     <list type="table">
///         <listheader>
///             <term>Attribute</term>
///             <description>Behavior</description>
///         </listheader>
///         <item>
///             <term>(none)</term>
///             <description>Sync (via <see cref="ISyncValidator" />); async when a <c>[Validator]</c> is declared</description>
///         </item>
///         <item>
///             <term>[Validate]</term>
///             <description>Sync + Async validation</description>
///         </item>
///         <item>
///             <term>[Validate(AsyncOnly = true)]</term>
///             <description>Async validation only</description>
///         </item>
///         <item>
///             <term>[NoValidation]</term>
///             <description>No validation</description>
///         </item>
///     </list>
///     <para>
///         <b>Zero reflection:</b> Validation metadata is provided by the source generator
///         via <see cref="IActionValidationMetadata" />. Actions without this interface
///         use the default behavior (sync only, no nested validators).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Default: sync validation, plus the async validators declared for it
/// [DomainAction]
/// public partial class CreateUser : DomainAction&lt;UserId&gt;
/// {
///     [Required, Email]
///     public required string Email { get; init; }
/// }
///
/// // Explicit sync + async
/// [DomainAction]
/// [Validate]
/// public partial class CreateUser : DomainAction&lt;UserId&gt;
/// {
///     [Required, Email]
///     public required string Email { get; init; }
/// }
///
/// // Opt-out of all validation
/// [DomainAction]
/// [NoValidation]
/// public partial class ImportData : DomainAction&lt;ImportResult&gt;
/// {
///     // Validation handled manually in Execute()
/// }
/// </code>
/// </example>
internal sealed partial class ValidationFilter : IActionFilter
{
    private readonly ILogger<ValidationFilter> _logger;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    ///     Creates a new ValidationFilter.
    /// </summary>
    /// <param name="serviceProvider">The service provider for resolving validators.</param>
    /// <param name="logger">The logger instance.</param>
    public ValidationFilter(IServiceProvider serviceProvider, ILogger<ValidationFilter> logger)
    {
        ThrowIfNull(serviceProvider);
        ThrowIfNull(logger);

        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public int Order => FilterOrder.Validation;

    /// <inheritdoc />
    public async Task<VoidResult<IError>> BeforeExecuteAsync<TAction, TReturn>(
        TAction action,
        CancellationToken ct)
        where TAction : DomainAction<TReturn>
    {
        // Use SG-generated metadata when available, otherwise fall back to defaults.
        // Default: sync validation only, no nested validators, no async.
        var metadata = action as IActionValidationMetadata;

        // Check if validation is explicitly disabled via [NoValidation]
        if (metadata is { HasNoValidation: true })
            return VoidResult<IError>.Success();

        // Get validation mode from SG metadata or use defaults
        var runSync = metadata?.RunSyncValidation ?? true;
        var runAsync = metadata?.RunAsyncValidation ?? false;

        var hasSyncValidator = action is ISyncValidator;
        var asyncValidator = runAsync ? _serviceProvider.GetService<IAsyncValidator<TAction>>() : null;
        var hasAsyncValidator = asyncValidator is not null;

        // Determine what to run based on configuration and availability
        var shouldRunSync = runSync && hasSyncValidator;
        var shouldRunAsync = runAsync && hasAsyncValidator;

        // Skip only if validation is completely disabled (not even nested validators possible)
        if (!runSync && !runAsync)
            return VoidResult<IError>.Success();

        var actionName = typeof(TAction).Name;
        LogValidationStarting(actionName);

        ValidationError? syncError = null;
        ValidationError? asyncError = null;

        // Phase 1: Sync validation (ISyncValidator)
        if (shouldRunSync && action is ISyncValidator syncValidator)
        {
            var syncResult = syncValidator.Validate();
            if (syncResult.IsFailure)
            {
                syncError = syncResult;
                LogSyncValidationFailed(actionName, syncError.Value.Count);
            }
        }

        // Phase 1b: Validate ISyncValidator properties on the action (e.g. Request DTOs)
        // Uses SG-generated direct property access — zero reflection.
        if (runSync && metadata is not null)
        {
            var nestedError = metadata.ValidateNestedSync();
            if (nestedError.HasValue)
            {
                syncError = syncError.HasValue
                    ? syncError.Value.Combine(nestedError.Value)
                    : nestedError.Value;
                LogSyncValidationFailed(actionName, nestedError.Value.Count);
            }
        }

        // Phase 2: Async validation (IAsyncValidator<T>)
        if (shouldRunAsync && asyncValidator is not null)
        {
            var asyncResult = await asyncValidator.ValidateAsync(action, ct).ConfigureAwait(false);
            if (asyncResult.IsFailure)
            {
                asyncError = asyncResult;
                LogAsyncValidationFailed(actionName, asyncError.Value.Count);
            }
        }

        // Phase 2b: Validate IAsyncValidator<TProperty> for nested properties (e.g. Request DTOs)
        // Uses SG-generated typed resolution — zero reflection.
        if (runAsync && metadata is not null)
        {
            var nestedAsyncError = await metadata.ValidateNestedAsync(_serviceProvider, ct)
                .ConfigureAwait(false);
            if (nestedAsyncError.HasValue)
            {
                asyncError = asyncError.HasValue
                    ? asyncError.Value.Combine(nestedAsyncError.Value)
                    : nestedAsyncError.Value;
                LogAsyncValidationFailed(actionName, nestedAsyncError.Value.Count);
            }
        }

        // Combine errors if any
        if (syncError.HasValue || asyncError.HasValue)
        {
            var combinedError = CombineErrors(syncError, asyncError);
            LogValidationFailed(actionName, combinedError.Count);
            return VoidResult<IError>.Failure(combinedError);
        }

        LogValidationSucceeded(actionName);
        return VoidResult<IError>.Success();
    }

    /// <inheritdoc />
    public Task AfterExecuteAsync<TAction, TReturn>(
        TAction action,
        Result<TReturn, IError> result,
        CancellationToken ct)
        where TAction : DomainAction<TReturn>
    {
        // Validation filter doesn't need to do anything after execution
        return Task.CompletedTask;
    }

    private static ValidationError CombineErrors(ValidationError? syncError, ValidationError? asyncError)
    {
        if (syncError.HasValue && asyncError.HasValue)
            return syncError.Value.Combine(asyncError.Value);

        return syncError ?? asyncError!.Value;
    }

    // =========================================================================
    // LoggerMessage - Zero Allocation
    // =========================================================================

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Validating action {ActionName}")]
    private partial void LogValidationStarting(string actionName);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Action {ActionName} sync validation failed with {ErrorCount} errors")]
    private partial void LogSyncValidationFailed(string actionName, int errorCount);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Action {ActionName} async validation failed with {ErrorCount} errors")]
    private partial void LogAsyncValidationFailed(string actionName, int errorCount);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Action {ActionName} validation failed with {ErrorCount} total errors")]
    private partial void LogValidationFailed(string actionName, int errorCount);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Action {ActionName} validation succeeded")]
    private partial void LogValidationSucceeded(string actionName);
}
