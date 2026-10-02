namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     Provides context about where a filter is being applied.
///     Used by <see cref="IQueryFilter{T}"/> to decide whether to apply.
/// </summary>
/// <remarks>
///     <para>
///         This context is passed to <see cref="IQueryFilter.ShouldApplyTo"/>
///         to allow fine-grained control over filter application.
///     </para>
/// </remarks>
public sealed record NavigationContext
{
    /// <summary>
    ///     The type of the navigation property being accessed.
    ///     For collections, this is the collection type (e.g., ICollection&lt;T&gt;).
    /// </summary>
    public required Type NavigationType { get; init; }

    /// <summary>
    ///     The entity type being filtered.
    ///     For collections, this is the element type.
    /// </summary>
    public required Type TargetEntityType { get; init; }

    /// <summary>
    ///     The name of the navigation property.
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    ///     Whether this is a collection navigation.
    /// </summary>
    public required bool IsCollection { get; init; }

    /// <summary>
    ///     Whether this navigation is required (not nullable).
    /// </summary>
    public required bool IsRequired { get; init; }

    /// <summary>
    ///     The type of relationship.
    /// </summary>
    public required RelationType RelationType { get; init; }

    /// <summary>
    ///     The depth in the navigation tree (0 = root).
    /// </summary>
    public required int Depth { get; init; }

    /// <summary>
    ///     The parent entity type.
    /// </summary>
    public required Type ParentEntityType { get; init; }

    /// <summary>
    ///     The full navigation path from root (e.g., "Order.Lines.Product").
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    ///     Where the query reads what this context describes: <see cref="FilterScope.Collections" />
    ///     for an <c>Include</c>, <see cref="FilterScope.Subqueries" /> for a predicate or an ordering,
    ///     <see cref="FilterScope.Projections" /> for a projection, <see cref="FilterScope.Joins" /> for a
    ///     set a declared join reads.
    /// </summary>
    /// <remarks>
    ///     One flag, and the filter's <see cref="IQueryFilter.Scope" /> has to hold it. Meaningful for a
    ///     collection and for a join; the root is decided by <see cref="Depth" />.
    /// </remarks>
    public FilterScope Position { get; init; } = FilterScope.Collections;

    /// <summary>
    ///     Creates a root context (depth 0, no navigation).
    /// </summary>
    public static NavigationContext Root<TEntity>() where TEntity : class => new()
    {
        NavigationType = typeof(TEntity),
        TargetEntityType = typeof(TEntity),
        PropertyName = string.Empty,
        IsCollection = false,
        IsRequired = true,
        RelationType = RelationType.None,
        Depth = 0,
        ParentEntityType = typeof(TEntity),
        Path = null
    };

    /// <summary>
    ///     Creates a context for a collection navigation onto <paramref name="targetEntityType" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Untyped because the navigation map is built per entity <see cref="Type" /> at a point
    ///         where no generic parameter is in hand.
    ///     </para>
    ///     <para>
    ///         A collection and not a reference, because a collection is the only navigation the map is
    ///         ever applied to: <c>PragmaticQueryFilterVisitor</c> rewrites collection navigations and
    ///         leaves optional and required references untouched. Saying so here is what lets
    ///         <c>FilterScope</c> mean something — a filter that excludes
    ///         <see cref="FilterScope.Collections" /> now stays out of the map instead of reaching every
    ///         navigation regardless of what it asked for.
    ///     </para>
    /// </remarks>
    public static NavigationContext ForCollection(Type targetEntityType)
        => ForCollection(targetEntityType, FilterScope.Collections);

    /// <summary>
    ///     A collection navigation onto <paramref name="targetEntityType" />, read in
    ///     <paramref name="position" />: <see cref="FilterScope.Collections" />,
    ///     <see cref="FilterScope.Subqueries" /> or <see cref="FilterScope.Projections" />.
    /// </summary>
    public static NavigationContext ForCollection(Type targetEntityType, FilterScope position) => new()
    {
        NavigationType = targetEntityType,
        TargetEntityType = targetEntityType,
        PropertyName = string.Empty,
        IsCollection = true,
        IsRequired = false,
        RelationType = RelationType.OneToMany,
        Depth = 1,
        ParentEntityType = targetEntityType,
        Path = null,
        Position = position
    };

    /// <summary>
    ///     The set of <paramref name="targetEntityType" /> a declared join reads.
    /// </summary>
    /// <remarks>
    ///     Not a navigation: the joined entity is reached by key, from a set of its own. Depth 1 so it is
    ///     not taken for the root, and <see cref="FilterScope.Joins" /> as the position the filter's scope
    ///     is asked about.
    /// </remarks>
    public static NavigationContext ForJoin(Type targetEntityType) => new()
    {
        NavigationType = targetEntityType,
        TargetEntityType = targetEntityType,
        PropertyName = string.Empty,
        IsCollection = false,
        IsRequired = false,
        RelationType = RelationType.None,
        Depth = 1,
        ParentEntityType = targetEntityType,
        Path = null,
        Position = FilterScope.Joins
    };
}
