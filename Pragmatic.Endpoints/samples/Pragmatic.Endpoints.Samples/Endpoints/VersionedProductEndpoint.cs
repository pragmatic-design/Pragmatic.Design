using Pragmatic.Endpoints.Attributes;
using Pragmatic.Endpoints.Base;
using Pragmatic.Result;

namespace Pragmatic.Endpoints.Samples.Endpoints;

/// <summary>
///     Response for the product endpoint — v1 returns basic fields.
/// </summary>
public record ProductResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required decimal Price { get; init; }
}

/// <summary>
///     Extended response — v2 adds rating and availability.
/// </summary>
public record ProductResponseV2 : ProductResponse
{
    public double Rating { get; init; }
    public bool InStock { get; init; }
}

/// <summary>
///     Demonstrates convention-based versioning on a plain Endpoint (not DomainAction).
///     The SG detects HandleAsyncV2 and auto-generates versioned route registrations.
///     No [ApiVersion] attribute needed — versions are inferred from methods.
/// </summary>
/// <remarks>
///     Generated routes (with Asp.Versioning.Http):
///     <list type="bullet">
///         <item>GET /products/{id}?api-version=1.0 → HandleAsync (v1)</item>
///         <item>GET /products/{id}?api-version=2.0 → HandleAsyncV2 (v2)</item>
///     </list>
/// </remarks>
[Endpoint(HttpVerb.Get, "/products/{id}")]
[ApiSummary("Get Product")]
[ApiTags("Products")]
public partial class VersionedProductEndpoint : Endpoint<ProductResponse>
{
    [FromRoute] public Guid Id { get; set; }

    /// <summary>v1 — returns basic product info.</summary>
    public override Task<Result<ProductResponse>> HandleAsync(CancellationToken ct = default)
    {
        var response = new ProductResponse
        {
            Id = Id,
            Name = "Widget",
            Price = 9.99m
        };

        return Task.FromResult(Result<ProductResponse>.Success(response));
    }

    /// <summary>v2 — returns extended product info with rating and stock.</summary>
    public Task<Result<ProductResponse>> HandleAsyncV2(CancellationToken ct = default)
    {
        var response = new ProductResponseV2
        {
            Id = Id,
            Name = "Widget",
            Price = 9.99m,
            Rating = 4.5,
            InStock = true
        };

        return Task.FromResult(Result<ProductResponse>.Success(response));
    }
}
