using Pragmatic.Persistence.Query.Interfaces;

namespace Pragmatic.Persistence.Query.Attributes;

/// <summary>
///     Marks a class as a declarative query object.
///     The source generator auto-generates the Apply method from properties.
/// </summary>
/// <typeparam name="TEntity">The source entity type to query.</typeparam>
/// <typeparam name="TResult">The projected result type.</typeparam>
/// <remarks>
///     <para>
///         The generator analyzes properties and generates:
///         <list type="bullet">
///             <item><b>Required properties</b> → Always applied as filters</item>
///             <item><b>[Filter] properties</b> → Conditionally applied (if not null)</item>
///             <item><b>[Sort] properties</b> → Sorting logic</item>
///             <item><b>Page/PageSize</b> → Paging logic (by convention)</item>
///         </list>
///     </para>
///     <para>
///         The class must be partial. The generator implements <see cref="IQuery{TEntity,TResult}" />
///         or <see cref="IPagedQuery{TEntity,TResult}" /> automatically.
///     </para>
///     <para>
///         Query classes support inheritance for composing common filters.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     // Declarative query - generator creates Apply method automatically
///     [Query&lt;Order, OrderDto&gt;]
///     public partial class GetOrdersForCustomer
///     {
///         // Required → always applied as filter
///         public required Guid CustomerId { get; init; }
///
///         // Optional filter → applied only if not null
///         [Filter(Operator = FilterOperator.Contains, MapTo = "OrderNumber")]
///         public string? SearchTerm { get; init; }
///
///         [Filter]
///         public OrderStatus? Status { get; init; }
///
///         // Sorting with default
///         [Sort(Default = SortDirection.Descending, MapTo = "CreatedAt")]
///         public SortDirection? DateSort { get; init; }
///
///         // Paging (by convention)
///         public int Page { get; init; } = 1;
///         public int PageSize { get; init; } = 20;
///     }
///
///     // Usage:
///     var query = new GetOrdersForCustomer
///     {
///         CustomerId = customerId,
///         SearchTerm = "ORD-2024",
///         Status = OrderStatus.Placed
///     };
///     var result = await executor.ExecuteAsync(query);
///     </code>
/// </example>
// Method as well as Class: a specification is a static factory in practice — ten of the twelve in the
// consumer that prompted this — and an attribute that only took classes would have covered one case in
// twelve. On a static method returning Specification<TEntity> the query type is derived from it, and
// the method's parameters become the query's inputs.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class QueryAttribute<TEntity, TResult> : Attribute
    where TEntity : class
{
    /// <summary>
    ///     Declares that this query yields <b>at most one</b> row: the generated endpoint calls
    ///     <c>ExecuteSingleAsync</c> and answers 404 when nothing matches.
    /// </summary>
    /// <remarks>
    ///     Reading one record is the most common thing an application does, and it was the only
    ///     read shape without a declarative form: paging came from Page/PageSize by convention,
    ///     everything else meant a list, and a single row meant leaving the query pipeline for a
    ///     repository call inside a hand-written endpoint. It is opt-in rather than inferred from
    ///     the properties, because "this filter happens to match one row" is not something the
    ///     generator can know, and guessing it would silently turn a list endpoint into a 404.
    /// </remarks>
    public bool Single { get; set; }

    /// <summary>
    ///     Generates the paging surface — <c>IPagedInput</c> and the two properties with their defaults
    ///     — instead of asking the author to write it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Four lines that every paged query repeats: the interface, <c>Page = 1</c>,
    ///         <c>PageSize = 20</c>. On a specification promoted to a query there is nowhere to write
    ///         them at all — the specification is a predicate, and <c>Confirmed() &amp; OfKind(x)</c> has
    ///         no answer for which page it is on.
    ///     </para>
    ///     <para>
    ///         Opt-in: a query that says nothing answers every matching row, which is what it did before
    ///         this option existed.
    ///     </para>
    /// </remarks>
    public bool Paged { get; set; }

    /// <summary>
    ///     The result type is mapped <b>in memory</b> instead of projected into SQL.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A result type carrying <c>[GenerateProjection]</c> answers with an
    ///         <c>Expression</c> the database evaluates. A type carrying only <c>[MapFrom&lt;T&gt;]</c>
    ///         answers with a <c>Func</c> that runs after the rows arrive — which is what a grid row
    ///         is in every line-of-business screen: a label joining two columns, a converter, a
    ///         formatted date. This says the query answers the second kind.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>Declared, never inferred.</b> The absence of <c>[GenerateProjection]</c> reads as
    ///         an omission rather than a decision, and a materialisation nobody asked for is the
    ///         performance trap the projection exists to avoid.
    ///     </para>
    ///     <para>
    ///         Filtering, sorting and paging stay <b>server-side</b>: only the projection moves. The
    ///         executor materialises the page it was going to return anyway, and maps that.
    ///     </para>
    /// </remarks>
    public bool MapInMemory { get; set; }
}

/// <summary>
///     Marks a class as a declarative query that returns the entity itself.
/// </summary>
/// <typeparam name="TEntity">The entity type to query.</typeparam>
// Method as well as Class: a specification is a static factory in practice — ten of the twelve in the
// consumer that prompted this — and an attribute that only took classes would have covered one case in
// twelve. On a static method returning Specification<TEntity> the query type is derived from it, and
// the method's parameters become the query's inputs.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class QueryAttribute<TEntity> : Attribute
    where TEntity : class
{
    /// <summary>
    ///     Declares that this query yields <b>at most one</b> row: the generated endpoint calls
    ///     <c>ExecuteSingleAsync</c> and answers 404 when nothing matches.
    /// </summary>
    /// <remarks>
    ///     Reading one record is the most common thing an application does, and it was the only
    ///     read shape without a declarative form: paging came from Page/PageSize by convention,
    ///     everything else meant a list, and a single row meant leaving the query pipeline for a
    ///     repository call inside a hand-written endpoint. It is opt-in rather than inferred from
    ///     the properties, because "this filter happens to match one row" is not something the
    ///     generator can know, and guessing it would silently turn a list endpoint into a 404.
    /// </remarks>
    public bool Single { get; set; }

    /// <summary>
    ///     Generates the paging surface — <c>IPagedInput</c> and the two properties with their defaults
    ///     — instead of asking the author to write it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Four lines that every paged query repeats: the interface, <c>Page = 1</c>,
    ///         <c>PageSize = 20</c>. On a specification promoted to a query there is nowhere to write
    ///         them at all — the specification is a predicate, and <c>Confirmed() &amp; OfKind(x)</c> has
    ///         no answer for which page it is on.
    ///     </para>
    ///     <para>
    ///         Opt-in: a query that says nothing answers every matching row, which is what it did before
    ///         this option existed.
    ///     </para>
    /// </remarks>
    public bool Paged { get; set; }

    /// <summary>
    ///     The result type is mapped <b>in memory</b> instead of projected into SQL.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A result type carrying <c>[GenerateProjection]</c> answers with an
    ///         <c>Expression</c> the database evaluates. A type carrying only <c>[MapFrom&lt;T&gt;]</c>
    ///         answers with a <c>Func</c> that runs after the rows arrive — which is what a grid row
    ///         is in every line-of-business screen: a label joining two columns, a converter, a
    ///         formatted date. This says the query answers the second kind.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>Declared, never inferred.</b> The absence of <c>[GenerateProjection]</c> reads as
    ///         an omission rather than a decision, and a materialisation nobody asked for is the
    ///         performance trap the projection exists to avoid.
    ///     </para>
    ///     <para>
    ///         Filtering, sorting and paging stay <b>server-side</b>: only the projection moves. The
    ///         executor materialises the page it was going to return anyway, and maps that.
    ///     </para>
    /// </remarks>
    public bool MapInMemory { get; set; }
}
