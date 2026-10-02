using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model representing a nested filter group with OR/AND logic.
///     <para>
///         When <see cref="Logic" /> is OR, the properties within the group
///         are combined with OR. The group itself is combined with AND to the parent filters.
///     </para>
/// </summary>
internal sealed record FilterGroupModel
{
    /// <summary>
    ///     The name of the property on the parent DTO that holds this group.
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    ///     The logic for combining filters within this group (And/Or).
    /// </summary>
    public required string Logic { get; init; }

    /// <summary>
    ///     The filter properties within this group.
    /// </summary>
    public EquatableArray<GridFilterPropertyModel> Properties { get; init; } = EquatableArray<GridFilterPropertyModel>.Empty;
}
