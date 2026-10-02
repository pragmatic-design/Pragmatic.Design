namespace Pragmatic.Persistence.Query.Attributes;

/// <summary>
///     Marks a class as a query view with aggregation support.
///     Use for reporting scenarios with SUM, COUNT, AVG, etc.
/// </summary>
/// <typeparam name="TRoot">The root entity type for the view.</typeparam>
/// <remarks>
///     <para>
///         QueryView supports:
///         <list type="bullet">
///             <item>Simple field mapping with <see cref="FromAttribute{TEntity}"/></item>
///             <item>Aggregations with <see cref="SumAttribute{TEntity}"/>,
///                 <see cref="CountAttribute{TEntity}"/>, etc.</item>
///             <item>Grouping with <see cref="GroupByAttribute{TEntity}"/></item>
///             <item>Joins with <see cref="JoinAttribute{TTarget}"/></item>
///         </list>
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [QueryView&lt;Order&gt;]
/// [Join&lt;OrderLine&gt;(Via = "Lines")]
/// [GroupBy&lt;Order&gt;(Properties = "CustomerId, Status")]
/// public partial class OrderSummaryView
/// {
///     [From&lt;Order&gt;]
///     public Guid CustomerId { get; init; }
///
///     [From&lt;Order&gt;]
///     public OrderStatus Status { get; init; }
///
///     [Count&lt;Order&gt;]
///     public int OrderCount { get; init; }
///
///     [Sum&lt;OrderLine&gt;(Expression = "Quantity * UnitPrice")]
///     public decimal TotalAmount { get; init; }
///
///     [Avg&lt;OrderLine&gt;(Expression = "UnitPrice")]
///     public decimal AveragePrice { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class QueryViewAttribute<TRoot> : Attribute where TRoot : class
{
}
