using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model representing a GridFilter class definition.
///     Used for incremental generator caching.
/// </summary>
internal sealed record GridFilterModel
{
    /// <summary>
    ///     The namespace of the grid filter class.
    /// </summary>
    public string Namespace { get; init; } = "";

    /// <summary>
    ///     The name of the grid filter class.
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    ///     The full type name including namespace.
    /// </summary>
    public string FullTypeName => string.IsNullOrEmpty(Namespace) ? TypeName : $"{Namespace}.{TypeName}";

    /// <summary>
    ///     The accessibility modifier (public, internal, etc.).
    /// </summary>
    public required string Accessibility { get; init; }

    /// <summary>
    ///     The type keyword (class, record).
    /// </summary>
    public required string TypeKind { get; init; }

    /// <summary>
    ///     Whether this is a record type.
    /// </summary>
    public bool IsRecord { get; init; }

    /// <summary>
    ///     The fully qualified name of the entity type (TEntity).
    /// </summary>
    public required string EntityTypeFullName { get; init; }

    /// <summary>
    ///     The simple name of the entity type.
    /// </summary>
    public required string EntityTypeName { get; init; }

    /// <summary>
    ///     All properties in this grid filter class.
    /// </summary>
    public EquatableArray<GridFilterPropertyModel> Properties { get; init; } = EquatableArray<GridFilterPropertyModel>.Empty;

    /// <summary>
    ///     Nested filter groups with OR/AND logic.
    /// </summary>
    public EquatableArray<FilterGroupModel> FilterGroups { get; init; } = EquatableArray<FilterGroupModel>.Empty;

    /// <summary>
    ///     Filterable properties.
    /// </summary>
    public IEnumerable<GridFilterPropertyModel> FilterableProperties => Properties.Where(p => p.IsFilterable);

    /// <summary>
    ///     Sortable properties (ordered by priority).
    /// </summary>
    public IEnumerable<GridFilterPropertyModel> SortableProperties => Properties
        .Where(p => p.IsSortable)
        .OrderBy(p => p.SortPriority);

    /// <summary>
    ///     Whether this filter has paging properties.
    /// </summary>
    public bool HasPaging => Properties.Any(p => p.IsPageProperty) && Properties.Any(p => p.IsPageSizeProperty);

    /// <summary>
    ///     The Page property (if exists).
    /// </summary>
    public GridFilterPropertyModel? PageProperty => Properties.FirstOrDefault(p => p.IsPageProperty);

    /// <summary>
    ///     The PageSize property (if exists).
    /// </summary>
    public GridFilterPropertyModel? PageSizeProperty => Properties.FirstOrDefault(p => p.IsPageSizeProperty);

    /// <summary>
    ///     Whether this filter has any filterable properties.
    /// </summary>
    public bool HasFilterable => Properties.Any(p => p.IsFilterable);

    /// <summary>
    ///     Whether this filter has any sortable properties.
    /// </summary>
    public bool HasSortable => Properties.Any(p => p.IsSortable);

    /// <summary>
    ///     Whether model is valid for code generation.
    /// </summary>
    public bool IsValid => !string.IsNullOrEmpty(TypeName) && !string.IsNullOrEmpty(EntityTypeFullName);
}
