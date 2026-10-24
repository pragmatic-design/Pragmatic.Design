using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Repository;

namespace PragmaticBoundaries.Tests;

/// <summary>An application service across the two boundaries, as an action is written: each unit of work by its boundary's key.</summary>
public sealed class ReviewDesk(
    [FromKeyedServices(typeof(CatalogBoundary))] IUnitOfWork catalog,
    [FromKeyedServices(typeof(ReviewsBoundary))] IUnitOfWork reviews,
    IRepository<Product> products,
    IRepository<ProductReview> productReviews)
{
    public async Task<Guid> ListAndReviewAsync(string name, string text, int stars)
    {
        var product = new Product { Name = name };
        products.Add(product);
        await catalog.SaveChangesAsync();
        var review = new ProductReview { Text = text, Stars = stars };
        review.SetProductId(product.Id); // the relation's key, generated with its setter
        productReviews.Add(review);
        await reviews.SaveChangesAsync();
        return product.Id;
    }

    public async Task<(Product? Product, IReadOnlyList<ProductReview> Reviews)> ReadAsync(string name)
    {
        var product = (await products.FindAsync(Specs.Named(name))).SingleOrDefault();
        return (product, product is null ? [] : await productReviews.FindAsync(Specs.Of(product.Id)));
    }
}
