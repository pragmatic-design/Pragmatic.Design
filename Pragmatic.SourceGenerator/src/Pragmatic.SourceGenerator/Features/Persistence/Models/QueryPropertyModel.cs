using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model for a property in a Query class.
/// </summary>
internal sealed record QueryPropertyModel
{
    /// <summary>
    ///     The property name.
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    ///     The property type (fully qualified).
    /// </summary>
    public required string PropertyType { get; init; }

    /// <summary>The property's type, fully qualified.</summary>
    /// <remarks>
    ///     ⚠️ <see cref="PropertyType" /> is the type as the author wrote it — <c>SortDirection?</c> —
    ///     which resolves in the author's file and nowhere else. The query's own generated members live
    ///     beside it and are fine; the boundary facade does not, and a parameter typed that way does not
    ///     compile there. Carried rather than recomputed: only the transform has the symbol.
    /// </remarks>
    public string? QualifiedPropertyType { get; init; }

    /// <summary>
    ///     Whether this property is required (has 'required' modifier).
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>
    ///     Whether the property is <c>[FromCurrentUser]</c>: filled by the invoker from the caller, so it
    ///     is not an input anyone else supplies — not a request parameter, not a boundary argument.
    /// </summary>
    public bool IsBoundFromTheCaller { get; init; }

    /// <summary>
    ///     Whether the filter is applied whatever the value: a required input, or the caller's own.
    /// </summary>
    /// <remarks>
    ///     A bound value skipped at its default, like an optional filter, would read every row on the one
    ///     path that reaches <c>Apply</c> without the invoker to fill it.
    /// </remarks>
    public bool IsAlwaysApplied => IsRequired || IsBoundFromTheCaller;

    /// <summary>
    ///     Whether this property is nullable.
    /// </summary>
    public bool IsNullable { get; init; }

    /// <summary>
    ///     Whether the ENTITY property this filter reads is itself nullable (as opposed to
    ///     <see cref="IsNullable" />, which describes the query property). A string operator on a
    ///     nullable column has to be null-guarded: <c>e.Phone.Contains(v)</c> is fine once the provider
    ///     translates it to SQL, but throws as soon as the same expression is evaluated over objects,
    ///     and it does not compile clean under a nullable context.
    /// </summary>
    public bool EntityPathIsNullable { get; init; }

    /// <summary>
    ///     The filter operator to use (default is Equals).
    /// </summary>
    public FilterOperatorKind Operator { get; init; } = FilterOperatorKind.Equals;

    /// <summary>
    ///     The target property path on the entity (if different from property name).
    /// </summary>
    public string? MapTo { get; init; }

    /// <summary>
    ///     Compare without case, by lowering both sides. Already reduced to "requested and applicable":
    ///     the transform clears it on anything that is not a string.
    /// </summary>
    public bool IgnoreCase { get; init; }

    /// <summary>
    ///     Whether this property is a filter property (has [Filter] or is required).
    /// </summary>
    public bool IsFilter { get; init; }

    /// <summary>
    ///     Whether this filter property is a collection of scalars (List&lt;string&gt;, string[], …).
    ///     Such filters default to the <c>In</c> operator and are skipped when null OR empty.
    /// </summary>
    public bool IsCollection { get; init; }

    /// <summary>
    ///     Whether this property is a sort property (has [Sort]).
    /// </summary>
    public bool IsSort { get; init; }

    /// <summary>
    ///     For sort properties: the default sort direction.
    /// </summary>
    public SortDirectionKind? DefaultSortDirection { get; init; }

    /// <summary>
    ///     For sort properties: the sort priority (lower = higher priority).
    /// </summary>
    public int SortPriority { get; init; }

    /// <summary>
    ///     Whether this is the Page property (by convention).
    /// </summary>
    /// <summary>
    ///     A settable input that is neither a filter, a sort, nor paging — so nothing is generated for
    ///     it and the caller's value is read and dropped.
    /// </summary>
    /// <remarks>
    ///     A property becomes a filter when it carries <c>[Filter]</c>, is <c>required</c>, or is
    ///     nullable. A plain non-nullable scalar is none of the three, and it is the form anyone writes
    ///     first on a route id, because an id in the route <i>is</i> mandatory. Reported as PRAG0707:
    ///     it produced a <c>Single = true</c> query whose <c>Apply</c> was the identity and which
    ///     answered 200 with whichever row came first.
    /// </remarks>
    public bool IsInertInput { get; init; }

    public bool IsPageProperty { get; init; }

    /// <summary>
    ///     Whether this is the PageSize property (by convention).
    /// </summary>
    public bool IsPageSizeProperty { get; init; }

    /// <summary>
    ///     Gets the effective entity property path.
    /// </summary>
    /// <summary>
    ///     The columns a <c>[SearchAcross]</c> property searches, with <c>||</c>; empty for any other
    ///     property.
    /// </summary>
    /// <remarks>
    ///     The value is matched against these instead of a column named after the property — which the
    ///     entity does not have: <c>Search</c> is the query's input, not a field of the row.
    /// </remarks>
    public EquatableArray<string> SearchAcrossPaths { get; init; } = EquatableArray<string>.Empty;

    /// <summary>Whether the search ignores case (<c>[SearchAcross(IgnoreCase = true)]</c>).</summary>
    public bool SearchIgnoresCase { get; init; }

    /// <summary>
    ///     Whether the property carries <c>[SearchAcross]</c> and is not text, so there is nothing to
    ///     search with.
    /// </summary>
    /// <remarks>Carried so <c>PRAG0703</c> can say so instead of the attribute doing nothing.</remarks>
    public bool HasInertSearchAcross { get; init; }

    /// <summary>
    ///     Whether the property carries <c>[FilterGroup]</c>, which on a query is read by nobody.
    /// </summary>
    /// <remarks>
    ///     The attribute is read inside a <c>[FilterDto&lt;T&gt;]</c> — on a property OF a filter DTO
    ///     whose type is another filter DTO. On a query, <c>[ComplexFilter]</c> is the one that carries
    ///     a whole filter object.
    /// </remarks>
    public bool HasInertFilterGroup { get; init; }

    /// <summary>
    ///     Where the property is declared, for the diagnostics that name it.
    /// </summary>
    /// <remarks>
    ///     Excluded from equality by <see cref="LocationInfo" />, so carrying it does not disturb the
    ///     incremental cache.
    /// </remarks>
    public LocationInfo? Location { get; init; }

    public string EffectivePropertyPath => MapTo ?? DerivePropertyPath();

    private string DerivePropertyPath()
    {
        // Remove "Sort" suffix for sort properties
        if (IsSort && PropertyName.EndsWith("Sort", StringComparison.Ordinal))
            return PropertyName.Substring(0, PropertyName.Length - 4);

        return PropertyName;
    }
}

/// <summary>
///     Filter operator kinds.
/// </summary>
internal enum FilterOperatorKind
{
    Equals,
    NotEquals,
    Contains,
    StartsWith,
    EndsWith,
    GreaterThan,
    GreaterOrEqual,
    LessThan,
    LessOrEqual,
    In,

    /// <summary>
    ///     Declared by <c>FilterOperator</c> and rendered by nothing. It is named here so the value the
    ///     transform casts from the attribute has a member to land on — without it the cast produced an
    ///     enum value outside the type, which every switch treated as its default and turned into
    ///     <c>==</c>. Reported as <c>PRAG0701</c>.
    /// </summary>
    Between
}

/// <summary>
///     Sort direction kinds.
/// </summary>
internal enum SortDirectionKind
{
    Ascending,
    Descending
}
