using Pragmatic.Validation.Types;

namespace Pragmatic.Actions.Pipeline;

/// <summary>
///     SG-generated validation metadata for a DomainAction.
///     Replaces runtime reflection in <see cref="Filters.ValidationFilter" />.
/// </summary>
/// <remarks>
///     <para>
///         The source generator implements this interface on partial action classes
///         when non-default validation configuration is detected (e.g., [Validate],
///         [NoValidation], nested ISyncValidator properties).
///     </para>
///     <para>
///         When an action does not implement this interface, the
///         <see cref="Filters.ValidationFilter" /> uses the default behavior:
///         sync validation only, no nested validators.
///     </para>
/// </remarks>
public interface IActionValidationMetadata
{
    /// <summary>
    ///     Whether all validation is disabled ([NoValidation] attribute).
    /// </summary>
    bool HasNoValidation { get; }

    /// <summary>
    ///     Whether sync validation (ISyncValidator) should run.
    /// </summary>
    bool RunSyncValidation { get; }

    /// <summary>
    ///     Whether async validation (IAsyncValidator) should run.
    /// </summary>
    bool RunAsyncValidation { get; }

    /// <summary>
    ///     Validates nested properties implementing ISyncValidator, without reflection.
    ///     Returns null if there are no nested sync validators or all pass.
    /// </summary>
    ValidationError? ValidateNestedSync();

    /// <summary>
    ///     Validates nested properties that have a registered IAsyncValidator,
    ///     resolved from the given <paramref name="serviceProvider" />, without reflection.
    ///     Returns null if there are no nested async validators or all pass.
    /// </summary>
    /// <param name="serviceProvider">The service provider for resolving async validators.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ValidationError?> ValidateNestedAsync(IServiceProvider serviceProvider, CancellationToken ct);
}
