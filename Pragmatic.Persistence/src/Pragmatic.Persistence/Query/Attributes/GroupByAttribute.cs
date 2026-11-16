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

    /// <summary>
    ///     Answer a row for every value of the key, beside the groups the rows form: a value no row has gets its row, with zero (#766).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Without it a value no row has is absent: the view is a <c>GROUP BY</c>, and a group of no rows is none of its groups.
    ///     </para>
    ///     <para>
    ///         Only on a single key whose type is an enum, the key projected onto the view, so the values are the enum's members, known at
    ///         compile time. An empty group's <c>[Count]</c> and <c>[Sum]</c> are 0. <c>[Min]</c>, <c>[Max]</c>, <c>[Avg]</c> and a
    ///         <c>[From]</c> other than the key have no value, so their property must be nullable, and answers null. Each other case is a
    ///         compile error: PRAG0748 (a composite key), PRAG0749 (a key that is not a projected enum), PRAG0750 (a member with no value
    ///         for an empty group that is not nullable).
    ///     </para>
    ///     <para>
    ///         The empty groups follow the ones the rows form, in the enum's declaration order. A paged read pages over all of them, and its
    ///         total counts them.
    ///     </para>
    /// </remarks>
    public bool IncludeEmpty { get; init; }
}
