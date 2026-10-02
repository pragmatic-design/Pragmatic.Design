using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Samples.Entities;

/// <summary>
///     Address entity demonstrating:
///     - [Entity] with Guid identifier
///     - IEntity interface implementation
///     - Multiple relationships to same entity
/// </summary>
[Entity]
[Relation.ManyToOne<Customer>.WithNavigation("Customer")]
public partial class Address : IEntity
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
    ///     Address type.
    /// </summary>
    public AddressType Type { get; set; } = AddressType.Shipping;

    /// <summary>
    ///     Street line 1.
    /// </summary>
    public string Street1 { get; set; } = "";

    /// <summary>
    ///     Street line 2.
    /// </summary>
    public string? Street2 { get; set; }

    /// <summary>
    ///     City.
    /// </summary>
    public string City { get; set; } = "";

    /// <summary>
    ///     State or province.
    /// </summary>
    public string State { get; set; } = "";

    /// <summary>
    ///     Postal code.
    /// </summary>
    public string PostalCode { get; set; } = "";

    /// <summary>
    ///     Country.
    /// </summary>
    public string Country { get; set; } = "";

    /// <summary>
    ///     Whether this is the default address.
    /// </summary>
    public bool IsDefault { get; set; }
}

/// <summary>
///     Address type enumeration.
/// </summary>
public enum AddressType
{
    Billing,
    Shipping
}
