namespace Pragmatic.Persistence.Query.Attributes;

/// <summary>
///     Marks a class as a grid filter for dynamic UI filtering.
///     The source generator creates an Apply method with source-generated if/switch logic.
/// </summary>
/// <typeparam name="TEntity">The entity type to filter.</typeparam>
/// <remarks>
///     <para>
///         Unlike <see cref="FilterDtoAttribute{TEntity}" />, GridFilter supports:
///         <list type="bullet">
///             <item>Dynamic operator selection via {Property}Operator properties</item>
///             <item>Multiple sort columns</item>
///             <item>Built-in pagination</item>
///             <item>Custom filter handlers for special cases (JSON columns, etc.)</item>
///         </list>
///     </para>
///     <para>
///         Properties must be marked with <see cref="FilterableAttribute" /> or
///         <see cref="SortAttribute" /> to be included.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     [GridFilter&lt;Order&gt;]
///     public partial class OrderGridFilter
///     {
///         [Filterable(Operators = FilterOps.String)]
///         public string? OrderNumber { get; set; }
///
///         [Filterable]
///         public OrderStatus? Status { get; set; }
///
///         [Filterable(MapTo = "Customer.Name")]
///         public string? CustomerName { get; set; }
///
///         [Sort]
///         public SortDirection? OrderNumberSort { get; set; }
///
///         public int Page { get; set; } = 1;
///         public int PageSize { get; set; } = 20;
///     }
///
///     // Usage:
///     var query = db.Orders.AsNoTracking();
///     query = filter.Apply(query);  // Generated method
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GridFilterAttribute<TEntity> : Attribute
    where TEntity : class
{
}
