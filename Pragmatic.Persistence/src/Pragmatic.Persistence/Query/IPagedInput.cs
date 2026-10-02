namespace Pragmatic.Persistence.Query;

/// <summary>
///     Declares that a query is paged, and makes the compiler ask for the two properties that say so.
/// </summary>
/// <remarks>
///     <para>
///         In <c>Pragmatic.Persistence.Query</c> rather than beside <c>IPagedQuery</c> in
///         <c>.Interfaces</c>, and the difference is who writes it. The generator implements
///         <c>IPagedQuery</c>; a person types this one, in a file that already imports this namespace
///         for <c>FilterOperator</c> and <c>SortDirection</c>. A reminder you have to go and find is a
///         reminder that does not work — which is how the first application to use it found out.
///     </para>
///     <para>
///         Paging is a convention: a query with a <c>Page</c> and a <c>PageSize</c> property is paged,
///         and the generator implements <see cref="Interfaces.IPagedQuery{TEntity,TResult}" /> on it. A query
///         without them is not, and nothing says which you meant — the two properties are written by
///         hand every time and misremembering a name gives an unpaged query that compiles.
///     </para>
///     <para>
///         Declaring this interface does not change what is generated. It is a reminder with teeth: the
///         compiler will not accept the class until both properties are there, spelled the way the
///         convention reads them.
///     </para>
///     <example>
///         <code>
/// [Query&lt;Order, OrderDto&gt;]
/// public partial class SearchOrdersQuery : IPagedInput
/// {
///     public string? Code { get; init; }
///     public int Page { get; init; } = 1;
///     public int PageSize { get; init; } = 20;
/// }
/// </code>
///     </example>
///     <para>
///         ⚠️ The names are the convention and are not configurable — the transform matches
///         <c>Page</c> and <c>PageSize</c> case-insensitively and excludes them from the filters. To
///         call them something else <em>on the wire</em>, rename the query parameter rather than the
///         property: <c>[FromQuery(Name = "per_page")]</c>.
///     </para>
/// </remarks>
public interface IPagedInput
{
    /// <summary>The page to return, 1-based.</summary>
    int Page { get; }

    /// <summary>How many items the page holds.</summary>
    int PageSize { get; }
}
