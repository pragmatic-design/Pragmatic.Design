using System.ComponentModel.DataAnnotations;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.Actions.Boundary;
using Pragmatic.Actions.Mutation;
using Pragmatic.Endpoints.Attributes;

namespace Pragmatic.Endpoints.Samples.Endpoints;

/// <summary>
///     Sample product entity with DDD-style private setters.
///     In a real project, <c>[Entity]</c> would generate the <c>SetXxx()</c> methods.
///     Here they are written manually to keep the sample self-contained.
/// </summary>
public class SampleProduct
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public bool IsActive { get; private set; } = true;

    // In production these come from the [Entity] source generator.
    internal void SetName(string value) => Name = value;
    internal void SetCategory(string value) => Category = value;
    internal void SetPrice(decimal value) => Price = value;
    internal void SetIsActive(bool value) => IsActive = value;
}

/// <summary>
///     Placeholder boundary marker for this sample.
/// </summary>
public sealed class CatalogBoundary : IBoundary { }

/// <summary>
///     Creates a new product using the <c>Mutation&lt;T&gt;</c> base class pattern.
///     Demonstrates the unified Mutation + Endpoint pipeline:
///     - <c>[Mutation(Mode = MutationMode.Create)]</c> wires the MutationInvoker
///     - <c>[Endpoint(HttpVerb.Post, ...)]</c> exposes the route
///     - Validation attributes on properties are auto-validated before persistence
///     - The generator produces the body DTO, handler, and OpenAPI metadata
/// </summary>
/// <remarks>
///     Generated pipeline:
///     1. Bind body → <c>CreateProductMutationBody</c>
///     2. Validate via <c>ISyncValidator</c> (generated from attributes)
///     3. Create entity, apply mutation, save via <c>IMutationInvoker</c>
///     4. Return <c>201 Created</c> with the new entity's ID
/// </remarks>
[Endpoint(HttpVerb.Post, "/products")]
[ApiSummary("Create Product")]
[ApiDescription("Creates a new product in the catalog.")]
[ApiTags("Products", "Catalog")]
[HttpStatus(201)]
[Mutation(Mode = MutationMode.Create)]
[BelongsTo<CatalogBoundary>]
public partial class CreateProductMutation : Mutation<SampleProduct>
{
    /// <summary>
    ///     Display name of the product. Required, max 200 characters.
    /// </summary>
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string Name { get; init; }

    /// <summary>
    ///     Product category. Required.
    /// </summary>
    [Required]
    [StringLength(100)]
    public required string Category { get; init; }

    /// <summary>
    ///     Unit price. Must be greater than zero.
    /// </summary>
    [Range(0.01, 1_000_000)]
    public required decimal Price { get; init; }

    /// <summary>
    ///     Whether the product is immediately available in the catalog.
    /// </summary>
    public bool IsActive { get; init; } = true;
}
