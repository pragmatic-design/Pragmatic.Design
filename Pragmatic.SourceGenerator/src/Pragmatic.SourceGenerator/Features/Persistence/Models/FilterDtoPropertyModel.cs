namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model for a [Filter] property in a FilterDto class.
/// </summary>
internal sealed record FilterDtoPropertyModel
{
    /// <summary>
    ///     The property name on the DTO.
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    ///     The property type (fully qualified).
    /// </summary>
    public required string PropertyType { get; init; }

    /// <summary>
    ///     The entity property path (MapTo or same name).
    /// </summary>
    public required string EntityPropertyPath { get; init; }

    /// <summary>
    ///     The filter operator (Equals, Contains, etc.).
    /// </summary>
    public required string Operator { get; init; }

    /// <summary>
    ///     Whether to use case-insensitive comparison.
    /// </summary>
    public bool IgnoreCase { get; init; }

    /// <summary>
    ///     Whether the property is nullable.
    /// </summary>
    public bool IsNullable { get; init; }

    /// <summary>
    ///     Whether the underlying type is string.
    /// </summary>
    public bool IsString { get; init; }

    /// <summary>
    ///     Whether this is a collection type (for In operator).
    /// </summary>
    public bool IsCollection { get; init; }
}
