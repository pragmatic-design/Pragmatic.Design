namespace Pragmatic.Persistence.Query.Attributes;

/// <summary>
///     Specifies grouping for a <see cref="QueryViewAttribute{TRoot}"/> aggregation view.
/// </summary>
/// <typeparam name="TEntity">The entity type to group by.</typeparam>
/// <remarks>
///     <para>
///         Use on the class level to define which properties to group by.
///         Multiple GroupBy attributes can be used for different entities.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [QueryView&lt;Order&gt;]
/// [Join&lt;Customer&gt;(Via = "Customer")]
/// [GroupBy&lt;Order&gt;(Properties = "Status")]
/// [GroupBy&lt;Customer&gt;(Properties = "Country", Via = "Customer")]
/// public partial class OrdersByStatusAndCountry
/// {
///     [From&lt;Order&gt;]
///     public OrderStatus Status { get; init; }
///
///     [From&lt;Customer&gt;(Property = "Country")]
///     public string CustomerCountry { get; init; }
///
///     [Count&lt;Order&gt;]
///     public int OrderCount { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class GroupByAttribute<TEntity> : Attribute where TEntity : class
{
    /// <summary>
    ///     Comma-separated property names to group by.
    /// </summary>
    public string Properties { get; init; } = "";

    /// <summary>
    ///     The navigation path if grouping by a joined entity.
    ///     Not needed for the root entity.
    /// </summary>
    public string? Via { get; init; }
}
