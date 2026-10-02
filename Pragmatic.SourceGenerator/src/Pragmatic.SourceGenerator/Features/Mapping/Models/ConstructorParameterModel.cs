namespace Pragmatic.SourceGenerator.Features.Mapping.Models;

/// <summary>
///     Model representing a constructor parameter for [MapTo] generation.
/// </summary>
internal sealed record ConstructorParameterModel
{
    /// <summary>
    ///     The parameter name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    ///     The parameter type.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    ///     Whether the parameter is nullable.
    /// </summary>
    public bool IsNullable { get; init; }

    /// <summary>
    ///     The corresponding property name (if any).
    /// </summary>
    public string? MatchingPropertyName { get; init; }

    /// <summary>
    ///     Whether the parameter is optional (declares an explicit default value). An
    ///     unmatched non-optional, non-nullable parameter is a contract gap (PRAG0316), not a
    ///     candidate for a silent <c>default</c> substitution.
    /// </summary>
    public bool IsOptional { get; init; }
}
