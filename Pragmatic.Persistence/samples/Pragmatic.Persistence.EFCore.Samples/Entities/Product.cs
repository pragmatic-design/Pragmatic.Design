using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Samples.Entities;

/// <summary>
///     Product entity demonstrating:
///     - [Entity] with Guid identifier
///     - IEntity interface implementation
///     - ISoftDelete for logical deletion
///     - [LogicKey] for SKU uniqueness
/// </summary>
[Entity]
[SoftDelete]
public partial class Product : IEntity, ISoftDelete
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
    ///     Product SKU (business key).
    /// </summary>
    [LogicKey]
    public string Sku { get; set; } = "";

    /// <summary>
    ///     Product name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    ///     Product description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    ///     Product price.
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    ///     Stock quantity.
    /// </summary>
    public int StockQuantity { get; set; }

    /// <summary>
    ///     Whether product is available for sale.
    /// </summary>
    public bool IsAvailable { get; set; } = true;

    // ISoftDelete implementation
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
