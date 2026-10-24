using Pragmatic.Specification;

namespace PragmaticDocuments.Tests;

/// <summary>Specifications of reviews as an application writes them: factories capturing their arguments, a fixed one, a list.</summary>
public static partial class ReviewSpecifications
{
    public static Specification<Review> StartingWith(string prefix) => Spec<Review>.Where(r => r.Text.StartsWith(prefix));

    public static Specification<Review> AtLeast(int stars) => Spec<Review>.Where(r => r.Stars >= stars);

    public static Specification<Review> Drafts => Spec<Review>.Where(r => !r.Published);

    public static Specification<Review> AnyOf(string[] texts) => Spec<Review>.Where(r => texts.Contains(r.Text));
}
