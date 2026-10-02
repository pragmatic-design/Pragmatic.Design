using Pragmatic.Mapping.Attributes;

namespace Pragmatic.Mapping.Samples.Dtos;

// ═══════════════════════════════════════════════════════════════════════════════
// Bidirectional Mapping - DTO <-> Entity
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
///     Product entity for bidirectional mapping demo.
/// </summary>
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
///     DTO with bidirectional mapping.
///     Uses both [MapFrom] for Entity->DTO and [MapTo] for DTO->Entity.
/// </summary>
[MapFrom<Product>]
[MapTo<Product>]
public partial record ProductDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public decimal Price { get; init; }
    public int StockQuantity { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>
///     Create request DTO that maps to entity (one-way DTO->Entity).
///     Excludes Id and audit fields that should be set by the system.
/// </summary>
[MapTo<Product>]
public partial record CreateProductRequest
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public decimal Price { get; init; }
    public int StockQuantity { get; init; }

    // Id is ignored (not mapped to entity, will be auto-generated)
    // IsActive defaults to true in entity
    // CreatedAt/UpdatedAt are set by the system
}

/// <summary>
///     Update request DTO with selective mapping.
/// </summary>
[MapTo<Product>]
public partial record UpdateProductRequest
{
    // Id is mapped to identify which entity to update
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public decimal Price { get; init; }

    // StockQuantity is managed separately
    [MapIgnore] public int? RequestedBy { get; init; }
}