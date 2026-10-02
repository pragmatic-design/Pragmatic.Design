namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     Specifies where a query filter should be applied.
/// </summary>
/// <remarks>
///     <para>
///         Combine multiple scopes using bitwise OR to apply filters
///         in multiple contexts.
///     </para>
///     <para>
///         A collection navigation is filtered according to <b>where the query reads it</b>, and the
///         position is decided by the operator of the query that reaches it: an <c>Include</c> is
///         <see cref="Collections" />, a <c>Select</c> (and <c>SelectMany</c>, <c>GroupBy</c>, a join's
///         result) is <see cref="Projections" />, and every other operator — <c>Where</c>,
///         <c>OrderBy</c>, <c>Any</c>… — is <see cref="Subqueries" />. What is nested inside that
///         operator's lambda takes the same position.
///     </para>
///     <para>
///         ⚠️ Optional and required references are not visited yet: <see cref="OptionalReferences" />
///         and <see cref="RequiredReferences" /> change nothing today.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Apply to root and collections
/// public FilterScope Scope => FilterScope.Root | FilterScope.Collections;
///
/// // Apply everywhere except projections
/// public FilterScope Scope => FilterScope.All &amp; ~FilterScope.Projections;
/// </code>
/// </example>
[Flags]
public enum FilterScope
{
    /// <summary>
    ///     Filter is disabled.
    /// </summary>
    None = 0,

    /// <summary>
    ///     Apply to the root query (top-level Where clause).
    /// </summary>
    Root = 1,

    /// <summary>
    ///     Apply to a collection navigation loaded with its entity: <c>Include</c> and
    ///     <c>ThenInclude</c>.
    /// </summary>
    Collections = 2,

    /// <summary>
    ///     Apply to optional reference navigations (e.g., Order.Customer when nullable). Not visited yet.
    /// </summary>
    OptionalReferences = 4,

    /// <summary>
    ///     Apply to required reference navigations (e.g., Order.Customer when required). Not visited yet.
    /// </summary>
    RequiredReferences = 8,

    /// <summary>
    ///     Apply to a collection navigation read to decide which rows the query returns or in what
    ///     order: inside <c>Where</c>, <c>OrderBy</c>, <c>Any</c>, <c>All</c>, <c>Count</c> and every
    ///     other operator that is not a projection.
    /// </summary>
    Subqueries = 16,

    /// <summary>
    ///     Apply to a collection navigation read to shape the result: inside <c>Select</c>,
    ///     <c>SelectMany</c>, <c>GroupBy</c>, and the result of a join — aggregates included.
    /// </summary>
    Projections = 32,

    /// <summary>
    ///     Apply to the set a declared join reads (<c>[Join&lt;T&gt;]</c>): the target is filtered before
    ///     it is joined.
    /// </summary>
    Joins = 64,

    /// <summary>
    ///     Every navigation, wherever the query reads it.
    /// </summary>
    AllNavigations = Collections | OptionalReferences | RequiredReferences | Subqueries | Projections,

    /// <summary>
    ///     Default scope: the root, a collection wherever it is read, the sets a join reads, and optional
    ///     references. Not required references, to avoid breaking joins.
    /// </summary>
    /// <remarks>
    ///     A filter guards rows, and a row read through a predicate, a projection or a join is the same
    ///     row: leaving any of them out by default would hand back what the filter exists to withhold.
    /// </remarks>
    Default = Root | Collections | Subqueries | Projections | Joins | OptionalReferences,

    /// <summary>
    ///     Apply everywhere.
    /// </summary>
    All = Root | AllNavigations | Joins
}
