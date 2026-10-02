using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic.Validation.Diagnostics;
using Pragmatic.Validation.Types;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Validation;

/// <summary>
///     Runtime implementation of <see cref="IValidator{T}" /> that combines
///     synchronous validation (from attributes) with asynchronous validation.
/// </summary>
/// <remarks>
///     <para>
///         This validator executes validation in the optimal order:
///         <list type="number">
///             <item>Sync validation first (fast, no I/O) via <see cref="ISyncValidator.Validate()" /></item>
///             <item>If sync fails and <see cref="ValidationOptions.FailFast" /> is enabled, return immediately</item>
///             <item>Async validators filtered by <see cref="IAsyncValidatorBindings{T}" /> trigger bindings</item>
///             <item>Combine errors from both phases</item>
///         </list>
///     </para>
///     <para>
///         When <see cref="IAsyncValidatorBindings{T}" /> is not provided (DTOs, mutations without
///         <c>[AsyncValidate]</c>), all async validators are invoked unconditionally for backward
///         compatibility.
///     </para>
/// </remarks>
/// <typeparam name="T">The type to validate.</typeparam>
internal sealed partial class CompositeValidator<T> : IValidator<T>
{
    // Materialized once in constructor; stores (validator, cachedTypeName) to avoid GetType() on hot path
    private readonly (IAsyncValidator<T> Validator, string Name)[] _asyncValidators;
    private readonly IAsyncValidatorBindings<T>? _bindings;
    private readonly ValidationOptions _options;
    private readonly ILogger<CompositeValidator<T>> _logger;

    // Cached to avoid repeated reflection on hot path
    private readonly string _typeName = typeof(T).Name;

    /// <summary>
    ///     Creates a new CompositeValidator with optional async validators and bindings.
    /// </summary>
    /// <param name="options">Validation options.</param>
    /// <param name="asyncValidators">Async validators resolved from DI.</param>
    /// <param name="bindings">
    ///     Optional bindings metadata. When null, all async validators are invoked
    ///     unconditionally (backward compat for DTOs/mutations).
    /// </param>
    /// <param name="logger">Optional logger. When null, logging is suppressed.</param>
    public CompositeValidator(
        IOptions<ValidationOptions> options,
        IEnumerable<IAsyncValidator<T>> asyncValidators,
        IAsyncValidatorBindings<T>? bindings = null,
        ILogger<CompositeValidator<T>>? logger = null)
    {
        _options = options.Value;
        // Materialise and cache type names once to eliminate GetType() on the hot validation path
        _asyncValidators = asyncValidators
            .Select(v => (v, v.GetType().Name))
            .ToArray();
        _bindings = bindings;
        _logger = logger ?? NullLogger<CompositeValidator<T>>.Instance;
    }

    /// <inheritdoc />
    public Task<ValidationError> ValidateAsync(T instance, CancellationToken ct = default)
        => ValidateAsync(instance, modifiedProperties: null, ct);

    /// <inheritdoc />
    public async Task<ValidationError> ValidateAsync(
        T instance,
        IReadOnlySet<string>? modifiedProperties,
        CancellationToken ct = default)
    {
        using var activity = ValidationDiagnostics.ActivitySource.StartActivity($"Validate.{_typeName}");
        activity?.SetTag(ValidationTags.Type, _typeName);

        ValidationDiagnostics.ValidationExecutions.Add(1,
            new KeyValuePair<string, object?>("type", _typeName));

        var stopwatch = Stopwatch.StartNew();

        LogValidationStarting(_typeName);

        var error = ValidationError.Valid;

        // Step 1: Sync validation (change-aware if ISyncValidator)
        if (instance is ISyncValidator syncValidator)
        {
            error = syncValidator.Validate(modifiedProperties);
            if (error.IsFailure)
            {
                LogSyncValidationFailed(_typeName, error.Count);
                if (_options.FailFast)
                {
                    RecordCompletion(stopwatch, activity, error);
                    return error;
                }
            }
            else
            {
                LogSyncValidationPassed(_typeName);
            }
        }

        // Step 2: Async validation (filtered by bindings if available)
        foreach (var (validator, validatorType) in _asyncValidators)
        {
            ct.ThrowIfCancellationRequested();

            // Unwrap decorator wrappers so SG-generated bindings match the
            // concrete validator they were generated against.
            var bindingType = validator is IValidatorDecorator decorated
                ? decorated.InnerValidatorType
                : validator.GetType();

            // When bindings exist, filter by trigger; when null, invoke all (backward compat)
            if (_bindings is not null &&
                !_bindings.ShouldInvoke(bindingType, modifiedProperties))
            {
                LogAsyncValidatorSkipped(validatorType);
                continue;
            }

            LogAsyncValidatorExecuting(validatorType, _typeName);
            var asyncError = await validator.ValidateAsync(instance, ct).ConfigureAwait(false);
            error = error.Combine(asyncError);

            if (asyncError.IsFailure)
                LogAsyncValidatorFailed(validatorType, _typeName, asyncError.Count);

            if (error.IsFailure && _options.FailFast)
            {
                RecordCompletion(stopwatch, activity, error);
                return error;
            }
        }

        LogValidationCompleted(_typeName, error.Count);
        RecordCompletion(stopwatch, activity, error);
        return error;
    }

    private void RecordCompletion(Stopwatch stopwatch, Activity? activity, ValidationError error)
    {
        stopwatch.Stop();
        var result = error.IsFailure ? "failure" : "success";

        ValidationDiagnostics.ValidationDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("type", _typeName),
            new KeyValuePair<string, object?>("result", result));

        if (error.IsFailure)
        {
            ValidationDiagnostics.ValidationFailures.Add(1,
                new KeyValuePair<string, object?>("type", _typeName));
            activity?.SetTag(ValidationTags.IssueCount, error.Count);
            activity?.SetStatus(ActivityStatusCode.Ok); // Validation failure is not an error — it's expected behavior
        }
        else
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
    }
}
