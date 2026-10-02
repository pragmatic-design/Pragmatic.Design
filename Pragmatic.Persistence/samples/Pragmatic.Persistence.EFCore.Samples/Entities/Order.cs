using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Samples.Entities;

/// <summary>
///     Order entity demonstrating:
///     - [Entity] with Guid identifier
///     - IEntity interface implementation
///     - IAuditable for tracking
///     - [LogicKey] for order number uniqueness
///     - Navigation properties
/// </summary>
[Entity]
[Auditable]
[Relation.ManyToOne<Customer>.WithNavigation("Customer")]
[Relation.ManyToOne<Address>.WithNavigation("ShippingAddress", Required = false)]
[Relation.OneToMany<OrderLine>.WithNavigation("Lines")]
public partial class Order : IEntity, IAuditable
{
    /// <summary>
    ///     Entity persistence identifier.
    /// </summary>
    public Guid PersistenceId { get; set; } = Guid.NewGuid();

    /// <summary>
    ///     Alias for PersistenceId for convenience.
    /// </summary>
    public Guid Id => PersistenceId;

    /// <summary>
    ///     Order number (business key).
    /// </summary>
    [LogicKey]
    public string OrderNumber { get; set; } = "";

    /// <summary>
    ///     Order status.
    /// </summary>
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    /// <summary>
    ///     Order total amount.
    /// </summary>
    public decimal Total { get; set; }

    /// <summary>
    ///     Order notes.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    ///     Order date.
    /// </summary>
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;

    // IAuditable implementation
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>
///     Order status enumeration.
/// </summary>
public enum OrderStatus
{
    Pending,
    Processing,
    Shipped,
    Delivered,
    Cancelled
}
