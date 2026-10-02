namespace Pragmatic.Persistence.Query.Attributes;

/// <summary>
///     Declares a join relationship in a query.
///     Use on query classes to specify additional entities to join.
/// </summary>
/// <typeparam name="TTarget">The entity type to join.</typeparam>
/// <remarks>
///     <para>
///         When used with Domain Entities that have <c>[Relation.*]</c> attributes,
///         the join is inferred automatically from the navigation path.
///     </para>
///     <para>
///         For POCO entities, use <see cref="ForeignKey"/> and <see cref="TargetKey"/>
///         to specify the join condition explicitly.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // With Domain Entity (navigation inferred)
/// [Query&lt;Order, OrderDetailDto&gt;]
/// [Join&lt;Customer&gt;(Via = "Customer")]
/// [Join&lt;OrderLine&gt;(Via = "Lines")]
/// public partial class GetOrderWithDetails { ... }
///
/// // With POCO entity (explicit keys)
/// [Query&lt;Order, OrderDetailDto&gt;]
/// [Join&lt;Customer&gt;(ForeignKey = "CustomerId", TargetKey = "Id")]
/// public partial class GetOrderWithCustomer { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class JoinAttribute<TTarget> : Attribute where TTarget : class
{
    /// <summary>
    ///     The navigation path from the root entity.
    ///     When used with Domain Entities, this is the navigation property name.
    /// </summary>
    /// <example>
    ///     <code>Via = "Customer"</code> for direct navigation,
    ///     <code>Via = "Lines.Product"</code> for nested navigation.
    /// </example>
    public string? Via { get; init; }

    /// <summary>
    ///     The foreign key property name on the source entity.
    ///     Required for POCO entities without navigation properties.
    /// </summary>
    public string? ForeignKey { get; init; }

    /// <summary>
    ///     The key property name on the target entity.
    ///     Defaults to "Id".
    /// </summary>
    public string TargetKey { get; init; } = "Id";

    /// <summary>
    ///     The type of join operation.
    ///     Defaults to Inner join.
    /// </summary>
    public JoinType Type { get; init; } = JoinType.Inner;

    /// <summary>
    ///     An optional alias for referencing this join in filters.
    ///     If not specified, the type name is used as the alias.
    /// </summary>
    public string? Alias { get; init; }
}
