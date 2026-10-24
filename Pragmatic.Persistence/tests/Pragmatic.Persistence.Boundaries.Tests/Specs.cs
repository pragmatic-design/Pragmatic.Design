using Pragmatic.Specification;

namespace PragmaticBoundaries.Tests;

/// <summary>A specification for each boundary's entity: each compiled by the engine its boundary names.</summary>
public static class Specs
{
    public static Specification<Product> Named(string name) => Spec<Product>.Where(p => p.Name == name);

    public static Specification<ProductReview> Of(Guid product) => Spec<ProductReview>.Where(r => r.ProductId == product);
}
