using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Samples.Entities;

/// <summary>
///     Customer entity demonstrating:
///     - [Entity] attribute with Guid identifier
///     - IEntity interface implementation
///     - IAuditable for automatic timestamp tracking
///     - ISoftDelete for logical deletion
///     - [LogicKey] for business key uniqueness
/// </summary>
[Entity]
[Auditable]
[SoftDelete]
[Relation.OneToMany<Order>.WithNavigation("Orders")]
[Relation.OneToMany<Address>.WithNavigation("Addresses")]
public partial class Customer : IEntity, IAuditable, ISoftDelete
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
    ///     Customer email (business key).
    /// </summary>
    [LogicKey]
    public string Email { get; set; } = "";

    /// <summary>
    ///     Customer name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    ///     Customer phone number.
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    ///     Customer type for classification.
    /// </summary>
    public CustomerType Type { get; set; } = CustomerType.Individual;

    /// <summary>
    ///     Whether the customer is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    // IAuditable implementation
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    // ISoftDelete implementation
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}

/// <summary>
///     Customer type enumeration.
/// </summary>
public enum CustomerType
{
    Individual,
    Business,
    Enterprise
}
