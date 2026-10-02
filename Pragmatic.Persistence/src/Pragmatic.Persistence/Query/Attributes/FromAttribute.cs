namespace Pragmatic.Persistence.Query.Attributes;

/// <summary>
///     Maps a view property from an entity property.
///     Use in <see cref="QueryViewAttribute{TRoot}"/> classes for non-aggregated fields.
/// </summary>
/// <typeparam name="TEntity">The source entity type.</typeparam>
/// <remarks>
///     <para>
///         If <see cref="Property"/> is not specified, the view property name is used.
///         The entity must be either the root entity or a joined entity.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [QueryView&lt;Order&gt;]
/// public partial class OrderSummaryView
/// {
///     // Same name as entity property
///     [From&lt;Order&gt;]
///     public Guid CustomerId { get; init; }
///
///     // Different name - explicit mapping
///     [From&lt;Order&gt;(Property = "OrderNumber")]
///     public string OrderRef { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FromAttribute<TEntity> : Attribute where TEntity : class
{
    /// <summary>
    ///     The source property name on the entity.
    ///     If not specified, uses the view property name.
    /// </summary>
    public string? Property { get; init; }
}
