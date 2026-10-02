namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model for a [FilterGroup] property in a FilterDto class.
///     References a nested FilterDto whose filters are combined with the specified logic.
/// </summary>
internal sealed record FilterDtoGroupModel
{
    /// <summary>
    ///     The property name on the parent DTO.
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    ///     The fully qualified type name of the nested FilterDto.
    /// </summary>
    public required string GroupTypeFullName { get; init; }

    /// <summary>
    ///     The simple type name of the nested FilterDto.
    /// </summary>
    public required string GroupTypeName { get; init; }

    /// <summary>
    ///     Whether to combine filters within the group using OR (true) or AND (false).
    /// </summary>
    public bool UseOrLogic { get; init; }

    /// <summary>
    ///     Whether the property is nullable.
    /// </summary>
    public bool IsNullable { get; init; }
}
