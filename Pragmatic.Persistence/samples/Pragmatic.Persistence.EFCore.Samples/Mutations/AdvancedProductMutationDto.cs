using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.EFCore.Samples.Entities;
using Pragmatic.Persistence.Patch;

namespace Pragmatic.Persistence.EFCore.Samples.Mutations;

/// <summary>
///     Advanced patch DTO demonstrating mapping attributes:
///     - [MapProperty] for property name remapping
///     - [MapIgnore] for excluding properties
/// </summary>
[Patch<Product>]
public partial class AdvancedProductMutationDto
{
    /// <summary>
    ///     Product name - standard mapping.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    ///     Maps to Description property on the entity.
    ///     Demonstrates [MapProperty] for renaming.
    /// </summary>
    [MapProperty("Description")]
    public string? ProductDescription { get; init; }

    /// <summary>
    ///     Updated price.
    /// </summary>
    public decimal? Price { get; init; }

    /// <summary>
    ///     Maps to StockQuantity on the entity.
    ///     Shows how to map differently named properties.
    /// </summary>
    [MapProperty("StockQuantity")]
    public int? AvailableStock { get; init; }

    /// <summary>
    ///     Maps to IsAvailable on the entity.
    /// </summary>
    [MapProperty("IsAvailable")]
    public bool? InStock { get; init; }

    /// <summary>
    ///     This property is ignored during patching.
    ///     Useful for DTO-only computed fields.
    /// </summary>
    [MapIgnore]
    public string? InternalNotes { get; init; }

    /// <summary>
    ///     Another ignored property - validation status from client.
    /// </summary>
    [MapIgnore]
    public bool IsValidated { get; init; }
}
