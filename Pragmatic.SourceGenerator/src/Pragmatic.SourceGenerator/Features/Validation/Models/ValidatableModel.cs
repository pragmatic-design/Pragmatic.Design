using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Validation.Models;

/// <summary>
///     Model representing a type that needs a generated Validate() method.
///     Used for incremental generator caching.
/// </summary>
internal sealed record ValidatableModel : GeneratorModel
{
    public bool IsRecord { get; init; }
    public bool IsValueType { get; init; }
    public bool IsPartial { get; init; }

    /// <summary>
    ///     The types enclosing this one, outermost first. Empty for a top-level type.
    /// </summary>
    /// <remarks>
    ///     The generated file reopens each of them around the validator, so the nesting survives. It
    ///     also disambiguates the hint name: two nested types with the same simple name under different
    ///     containers would otherwise write to the same file, and Roslyn answers a duplicate hint by
    ///     discarding the generator's whole output with a warning.
    /// </remarks>
    public EquatableArray<ContainingTypeModel> ContainingTypes { get; init; } =
        EquatableArray<ContainingTypeModel>.Empty;

    public EquatableArray<PropertyValidationModel> Properties { get; init; } =
        EquatableArray<PropertyValidationModel>.Empty;

    public EquatableArray<string> AllPropertyNames { get; init; } = EquatableArray<string>.Empty;
    public EquatableArray<string> AllPropertyTypes { get; init; } = EquatableArray<string>.Empty;

    /// <summary>Entity types get change-aware Validate(IReadOnlySet&lt;string&gt;?) generated.</summary>
    public bool IsEntity { get; init; }

    /// <summary>
    ///     Cross-property dependency edges for change-tracking-aware validation: each entry's Property triggers
    ///     re-validation of its Dependents. EquatableArray so the model stays value-equatable for caching.
    /// </summary>
    public EquatableArray<PropertyDependencyModel> PropertyDependencies { get; init; }
        = EquatableArray<PropertyDependencyModel>.Empty;

    /// <summary>Whether any validation attribute specifies groups.</summary>
    public bool HasGroups { get; init; }

    public EquatableArray<AsyncValidatorBindingModel> AsyncValidatorBindings { get; init; }
        = EquatableArray<AsyncValidatorBindingModel>.Empty;
}
