namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Assigns an entity to a specific boundary for DbContext grouping (type-safe version).
///     The EFCore source generator creates a per-boundary DbContext.
/// </summary>
/// <remarks>
///     <para>
///         The "Boundary" suffix is stripped automatically from the type name
///         (e.g. <c>SalesBoundary</c> becomes <c>Sales</c>).
///     </para>
///     <example>
///         <code>
/// [Entity]
/// [BelongsTo&lt;SalesBoundary&gt;]
/// public partial class Order
/// {
///     public decimal Total { get; set; }
/// }
/// </code>
///     </example>
/// </remarks>
/// <typeparam name="TBoundary">The boundary class this entity belongs to.</typeparam>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BelongsToAttribute<TBoundary> : Attribute
    where TBoundary : class
{
}
