using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model for a property in a GridFilter class.
/// </summary>
internal sealed record GridFilterPropertyModel
{
    /// <summary>
    ///     The property name.
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    ///     The property type (fully qualified).
    /// </summary>
    public required string PropertyType { get; init; }

    /// <summary>
    ///     Whether this property is nullable.
    /// </summary>
    public bool IsNullable { get; init; }

    /// <summary>
    ///     The target property path on the entity.
    /// </summary>
    public string? MapTo { get; init; }

    /// <summary>
    ///     Whether this is a filterable property.
    /// </summary>
    public bool IsFilterable { get; init; }

    /// <summary>
    ///     Whether this is a sortable property.
    /// </summary>
    public bool IsSortable { get; init; }

    /// <summary>
    ///     The allowed filter operators (flags).
    /// </summary>
    public FilterOpsKind AllowedOperators { get; init; } = FilterOpsKind.All;

    /// <summary>
    ///     The sort priority (lower = higher priority).
    /// </summary>
    public int SortPriority { get; init; }

    /// <summary>
    ///     Whether this is the Page property (by convention).
    /// </summary>
    public bool IsPageProperty { get; init; }

    /// <summary>
    ///     Whether this is the PageSize property (by convention).
    /// </summary>
    public bool IsPageSizeProperty { get; init; }

    /// <summary>
    ///     The companion operator property name (e.g., "NameOperator" for "Name").
    /// </summary>
    public string? OperatorPropertyName { get; init; }

    /// <summary>
    ///     Whether a companion operator property exists.
    /// </summary>
    public bool HasOperatorProperty { get; init; }

    /// <summary>
    ///     Custom filter handler type name.
    /// </summary>
    public string? HandlerTypeName { get; init; }

    /// <summary>
    ///     Arguments for the custom handler.
    /// </summary>
    public string? HandlerArgs { get; init; }

    /// <summary>
    ///     Whether this is a cross-property search field ([SearchAcross]).
    /// </summary>
    public bool IsSearchAcross { get; init; }

    /// <summary>
    ///     Entity property paths to search across (OR logic).
    /// </summary>
    public EquatableArray<string> SearchAcrossPaths { get; init; } = EquatableArray<string>.Empty;

    /// <summary>Whether the search ignores case (<c>[SearchAcross(IgnoreCase = true)]</c>).</summary>
    public bool SearchIgnoresCase { get; init; }

    /// <summary>
    ///     Gets the effective entity property path.
    /// </summary>
    public string EffectivePropertyPath => MapTo ?? DerivePropertyPath();

    private string DerivePropertyPath()
    {
        // Remove "Sort" suffix for sortable properties
        if (IsSortable && PropertyName.EndsWith("Sort", StringComparison.Ordinal))
            return PropertyName.Substring(0, PropertyName.Length - 4);

        return PropertyName;
    }
}

/// <summary>
///     Filter operator flags (mirrors FilterOps enum).
/// </summary>
[Flags]
internal enum FilterOpsKind
{
    None = 0,
    Equality = 1,
    String = 2,
    Compare = 4,
    Range = 8,
    All = Equality | String | Compare | Range
}
