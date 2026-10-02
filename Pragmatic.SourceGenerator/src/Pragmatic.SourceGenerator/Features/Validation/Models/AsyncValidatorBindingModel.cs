namespace Pragmatic.SourceGenerator.Features.Validation.Models;

/// <summary>
///     Model representing a single [AsyncValidate&lt;TValidator&gt;] binding.
/// </summary>
internal sealed record AsyncValidatorBindingModel
{
    /// <summary>Fully qualified type name of the async validator.</summary>
    public required string ValidatorFullTypeName { get; init; }

    /// <summary>
    ///     The property name that triggers this validator.
    ///     Null means entity-level binding (fires on any modification).
    /// </summary>
    public string? TriggerPropertyName { get; init; }
}
