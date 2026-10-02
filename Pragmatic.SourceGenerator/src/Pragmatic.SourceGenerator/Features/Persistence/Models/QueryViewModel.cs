using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model for a QueryView class (aggregation report).
/// </summary>
internal sealed record QueryViewModel
{
    /// <summary>
    ///     The type name.
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    ///     The namespace.
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    ///     The full type name (including namespace).
    /// </summary>
    public string FullTypeName => string.IsNullOrEmpty(Namespace) ? TypeName : $"{Namespace}.{TypeName}";

    /// <summary>
    ///     The accessibility (public, internal, etc.).
    /// </summary>
    public string Accessibility { get; init; } = "public";

    /// <summary>
    ///     Whether this is a record type.
    /// </summary>
    public bool IsRecord { get; init; }

    /// <summary>
    ///     The root entity type (fully qualified).
    /// </summary>
    public required string RootEntityTypeFullName { get; init; }

    /// <summary>
    ///     The group by properties.
    /// </summary>
    public IReadOnlyList<GroupByModel> GroupByProperties { get; init; } = [];

    /// <summary>
    ///     The aggregate properties.
    /// </summary>
    public IReadOnlyList<AggregatePropertyModel> AggregateProperties { get; init; } = [];

    /// <summary>
    ///     The source properties (from entity).
    /// </summary>
    public IReadOnlyList<SourcePropertyModel> SourceProperties { get; init; } = [];

    /// <summary>
    ///     How many <c>[Join&lt;T&gt;]</c> attributes the view declares — all of them inert.
    /// </summary>
    /// <remarks>
    ///     The transform reads <c>[GroupBy]</c>, <c>[From]</c> and the five aggregates, and nothing
    ///     else. A <c>[Join]</c> here therefore configures nothing; the way to reach another entity is
    ///     <c>[GroupBy&lt;TOther&gt;(Via = "Nav")]</c>. Counted so PRAG0703 can say so.
    /// </remarks>
    public int InertJoinCount { get; init; }

    /// <summary>The keys and paths <c>[GroupBy]</c> names that the entity does not have, for PRAG0732.</summary>
    public EquatableArray<UnresolvedGroupKeyModel> UnresolvedGroupKeys { get; init; } =
        EquatableArray<UnresolvedGroupKeyModel>.Empty;

    /// <summary>
    ///     Whether the model is valid for code generation.
    /// </summary>
    public bool IsValid => !string.IsNullOrEmpty(TypeName) &&
                           !string.IsNullOrEmpty(RootEntityTypeFullName) &&
                           (GroupByProperties.Count > 0 || AggregateProperties.Count > 0);

    /// <summary>
    ///     Whether this view has group by.
    /// </summary>
    public bool HasGroupBy => GroupByProperties.Count > 0;

    /// <summary>
    ///     Whether this view has aggregations.
    /// </summary>
    public bool HasAggregations => AggregateProperties.Count > 0;
}

/// <summary>
///     Model for a group by property.
/// </summary>
internal sealed record GroupByModel
{
    /// <summary>
    ///     The property name in the view.
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    ///     The property type.
    /// </summary>
    public required string PropertyType { get; init; }

    /// <summary>
    ///     The entity type this groups by.
    /// </summary>
    public required string EntityType { get; init; }

    /// <summary>
    ///     The navigation path on the root entity used to reach the grouped entity
    ///     (e.g., "Customer", or a dotted multi-segment path like "Order.Customer").
    ///     Empty when grouping by a property on the root entity.
    /// </summary>
    public string NavigationPath { get; init; } = "";

    /// <summary>
    ///     The entity property to group by (on the entity reached via <see cref="NavigationPath"/>).
    /// </summary>
    public required string EntityProperty { get; init; }

    /// <summary>
    ///     Whether this group key is also projected onto a view property of the same name.
    ///     Property-level (auto-detected) keys are always projected. Class-level
    ///     <c>[GroupBy&lt;T&gt;(Via)]</c> keys are only projected when a matching view property
    ///     exists; otherwise the key participates in the GROUP BY clause but its value is read
    ///     by a <c>[From]</c>/source property instead.
    /// </summary>
    public bool IsProjected { get; init; } = true;

    /// <summary>
    ///     The grouping key access path, relative to the entity parameter <c>e</c>.
    ///     Combines <see cref="NavigationPath"/> and <see cref="EntityProperty"/>.
    /// </summary>
    public string KeyAccessPath =>
        string.IsNullOrEmpty(NavigationPath) ? EntityProperty : $"{NavigationPath}.{EntityProperty}";

    /// <summary>
    ///     For a <c>[Projectable]</c> member, its body written over the entity parameter <c>e</c>; null
    ///     for a column, which is read as <c>e.{KeyAccessPath}</c>.
    /// </summary>
    /// <remarks>
    ///     A computed member's getter is no column, and EF Core cannot translate it inside a
    ///     <c>GroupBy</c>. The body is what the database can compute — what a projection inlines too.
    /// </remarks>
    public string? KeyBody { get; init; }

    /// <summary>The expression the grouping key is built from.</summary>
    public string KeyExpression => KeyBody ?? $"e.{KeyAccessPath}";
}

/// <summary>
///     Model for an aggregate property.
/// </summary>
internal sealed record AggregatePropertyModel
{
    /// <summary>
    ///     The property name in the view.
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    ///     The property type (e.g., decimal, int, double).
    /// </summary>
    public required string PropertyType { get; init; }

    /// <summary>
    ///     The aggregation kind.
    /// </summary>
    public required AggregateKind Kind { get; init; }

    /// <summary>
    ///     The entity type for the aggregation.
    /// </summary>
    public required string EntityType { get; init; }

    /// <summary>
    ///     The expression to aggregate (e.g., "Total", "Quantity * Price").
    /// </summary>
    public required string Expression { get; init; }

    /// <summary>
    ///     <see cref="Expression" /> written over the row <c>x</c> by the transform — every member it
    ///     names read from the row, a <c>[Projectable]</c> one as its body — or null, which reads
    ///     <c>x.{Expression}</c>.
    /// </summary>
    public string? RowBody { get; init; }

    /// <summary>What the aggregate lambda returns for a row.</summary>
    public string RowExpression => RowBody ?? $"x.{Expression}";

    /// <summary>
    ///     Optional where clause for the aggregation — the <b>body of a lambda over the row</b>, so
    ///     <c>x.IsCancelled</c> and not <c>IsCancelled</c>.
    /// </summary>
    /// <remarks>
    ///     Inserted verbatim into <c>g.Count(x =&gt; …)</c>, unlike <see cref="Expression" />, which the
    ///     template qualifies itself. <c>PRAG0722</c> reports a clause that never mentions the row.
    /// </remarks>
    public string? WhereClause { get; init; }

    /// <summary>Where the property was declared, so a diagnostic lands on the author's line.</summary>
    public LocationInfo? Location { get; init; }
}

/// <summary>
///     Model for a source property (direct from entity).
/// </summary>
internal sealed record SourcePropertyModel
{
    /// <summary>
    ///     The property name in the view.
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    ///     The property type.
    /// </summary>
    public required string PropertyType { get; init; }

    /// <summary>
    ///     The entity type this comes from.
    /// </summary>
    public required string EntityType { get; init; }

    /// <summary>
    ///     The entity property path.
    /// </summary>
    public required string EntityPropertyPath { get; init; }
}

/// <summary>
///     Kinds of aggregations.
/// </summary>
internal enum AggregateKind
{
    Sum,
    Count,
    Average,
    Min,
    Max
}
