namespace Pragmatic.Persistence.Query.Attributes;

/// <summary>
///     Marks a computed property for source-generated expression projection.
///     The generator produces a static <c>Expression&lt;Func&lt;TEntity, TResult&gt;&gt;</c>
///     that EF Core can translate to SQL instead of evaluating client-side.
/// </summary>
/// <remarks>
///     <para>
///         Only expression-bodied properties are supported:
///         <c>public decimal Total =&gt; SubTotal + Tax;</c>
///     </para>
///     <para>
///         The generated expression is placed in a nested <c>Expr</c> class:
///         <c>Order.Expr.Total</c>
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial class Order
/// {
///     public decimal SubTotal { get; set; }
///     public decimal Tax { get; set; }
///
///     [Projectable]
///     public decimal Total =&gt; SubTotal + Tax;
/// }
///
/// // Generated: Order.Expr.Total is Expression&lt;Func&lt;Order, decimal&gt;&gt;
/// // Usage:
/// var totals = await db.Orders.Select(Order.Expr.Total).ToListAsync();
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ProjectableAttribute : Attribute
{
}
