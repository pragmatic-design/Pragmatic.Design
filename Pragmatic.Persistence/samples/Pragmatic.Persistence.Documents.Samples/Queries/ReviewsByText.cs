using Pragmatic.Specification;
using PragmaticDocumentSamples.Entities;

namespace PragmaticDocumentSamples.Queries;

/// <summary>
///     Specifications of reviews as an application writes them: a factory, its argument captured. Not <c>ReviewSpecifications</c>: the generator
///     emits that one beside the entity.
/// </summary>
public static class ReviewsByText
{
    public static Specification<Review> StartingWith(string prefix) => Spec<Review>.Where(r => r.Text.StartsWith(prefix));
}
