namespace Pragmatic.Persistence.Query.Attributes;

/// <summary>
///     Calculates the sum of an expression over the entity.
///     Use in <see cref="QueryViewAttribute{TRoot}"/> classes.
/// </summary>
/// <typeparam name="TEntity">The entity type to aggregate.</typeparam>
/// <example>
///     <code>
/// [Sum&lt;OrderLine&gt;(Expression = "Quantity * UnitPrice")]
/// public decimal TotalAmount { get; init; }
///
/// [Sum&lt;OrderLine&gt;(Expression = "Quantity")]
/// public int TotalQuantity { get; init; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SumAttribute<TEntity> : Attribute where TEntity : class
{
    /// <summary>
    ///     The expression to sum (e.g., "Quantity" or "Quantity * UnitPrice").
    ///     Supports simple arithmetic expressions with entity properties.
    /// </summary>
    public required string Expression { get; init; }
}

/// <summary>
///     Counts entities matching an optional condition.
///     Use in <see cref="QueryViewAttribute{TRoot}"/> classes.
/// </summary>
/// <typeparam name="TEntity">The entity type to count.</typeparam>
/// <example>
///     <code>
/// // Count all orders
/// [Count&lt;Order&gt;]
/// public int OrderCount { get; init; }
///
/// // Count with condition — the clause is the BODY OF A LAMBDA over the row, so it names x
/// [Count&lt;Order&gt;(Where = "x.Status == OrderStatus.Completed")]
/// public int CompletedOrderCount { get; init; }
/// </code>
/// </example>
/// <remarks>
///     ⚠️ <c>Where</c> is not shaped like <c>Expression</c> on the siblings of this family.
///     <c>Sum</c>, <c>Avg</c>, <c>Min</c> and <c>Max</c> take a bare member path and the generator
///     writes <c>x.{Expression}</c>; this one is inserted verbatim into <c>g.Count(x =&gt; …)</c>. A
///     clause of bare names compiles to <c>CS0103</c> inside the generated query view — an error on a
///     line the author never wrote. <c>PRAG0722</c> reports it at the declaration instead.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CountAttribute<TEntity> : Attribute where TEntity : class
{
    /// <summary>
    ///     Optional filter condition for the count, written as the <b>body of a lambda over the row</b>:
    ///     <c>"x.Status == OrderStatus.Completed"</c>. If not specified, counts all entities.
    /// </summary>
    public string? Where { get; init; }
}

/// <summary>
///     Calculates the average of an expression over the entity.
///     Use in <see cref="QueryViewAttribute{TRoot}"/> classes.
/// </summary>
/// <typeparam name="TEntity">The entity type to aggregate.</typeparam>
/// <example>
///     <code>
/// [Avg&lt;OrderLine&gt;(Expression = "UnitPrice")]
/// public decimal AveragePrice { get; init; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AvgAttribute<TEntity> : Attribute where TEntity : class
{
    /// <summary>
    ///     The expression to average.
    /// </summary>
    public required string Expression { get; init; }
}

/// <summary>
///     Calculates the minimum value of an expression over the entity.
///     Use in <see cref="QueryViewAttribute{TRoot}"/> classes.
/// </summary>
/// <typeparam name="TEntity">The entity type to aggregate.</typeparam>
/// <example>
///     <code>
/// [Min&lt;OrderLine&gt;(Expression = "UnitPrice")]
/// public decimal MinPrice { get; init; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MinAttribute<TEntity> : Attribute where TEntity : class
{
    /// <summary>
    ///     The expression to find the minimum of.
    /// </summary>
    public required string Expression { get; init; }
}

/// <summary>
///     Calculates the maximum value of an expression over the entity.
///     Use in <see cref="QueryViewAttribute{TRoot}"/> classes.
/// </summary>
/// <typeparam name="TEntity">The entity type to aggregate.</typeparam>
/// <example>
///     <code>
/// [Max&lt;OrderLine&gt;(Expression = "UnitPrice")]
/// public decimal MaxPrice { get; init; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MaxAttribute<TEntity> : Attribute where TEntity : class
{
    /// <summary>
    ///     The expression to find the maximum of.
    /// </summary>
    public required string Expression { get; init; }
}
