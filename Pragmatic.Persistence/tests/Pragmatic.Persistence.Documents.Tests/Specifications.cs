using Pragmatic.Persistence.Query.Attributes;
using Pragmatic.Specification;

namespace PragmaticDocuments.Tests;

/// <summary>Specifications of reviews as an application writes them: factories capturing their arguments, a fixed one, a list.</summary>
public static partial class ReviewSpecifications
{
    public static Specification<Review> StartingWith(string prefix) => Spec<Review>.Where(r => r.Text.StartsWith(prefix));

    public static Specification<Review> AtLeast(int stars) => Spec<Review>.Where(r => r.Stars >= stars);

    public static Specification<Review> Drafts => Spec<Review>.Where(r => !r.Published);

    public static Specification<Review> AnyOf(string[] texts) => Spec<Review>.Where(r => texts.Contains(r.Text));

    /// <summary>A text match that ignores case (#495), as the Showcase's <c>PropertySpecifications.NameContains</c> writes it.</summary>
    public static Specification<Review> Containing(string text) => Spec<Review>.Where(r => r.Text.Contains(text, System.StringComparison.OrdinalIgnoreCase));

    public static Specification<Review> EndingWith(string text) => Spec<Review>.Where(r => r.Text.EndsWith(text, System.StringComparison.OrdinalIgnoreCase));

    /// <summary>An ordinal comparison named explicitly: case-sensitive, as the match without a StringComparison.</summary>
    public static Specification<Review> StartingOrdinally(string prefix) => Spec<Review>.Where(r => r.Text.StartsWith(prefix, System.StringComparison.Ordinal));

    /// <summary>Every review, as the Showcase's invoice export writes it (#538): the condition is the constant true.</summary>
    public static Specification<Review> Everything => Spec<Review>.Where(r => true);

    /// <summary>No review: the constant false.</summary>
    public static Specification<Review> Nothing => Spec<Review>.Where(r => false);

    /// <summary>Constants inside a condition (#538): neutral (<c>&amp;&amp; true</c>, <c>|| false</c>) or absorbing (<c>|| true</c>, <c>&amp;&amp; false</c>).</summary>
    public static Specification<Review> AtLeastAndTrue(int stars) => Spec<Review>.Where(r => r.Stars >= stars && true);

    public static Specification<Review> AtLeastOrTrue(int stars) => Spec<Review>.Where(r => r.Stars >= stars || true);

    public static Specification<Review> AtLeastOrFalse(int stars) => Spec<Review>.Where(r => r.Stars >= stars || false);

    public static Specification<Review> AtLeastAndFalse(int stars) => Spec<Review>.Where(r => r.Stars >= stars && false);

    public static Specification<Review> NotFalse => Spec<Review>.Where(r => !false);

    /// <summary>
    ///     A rule promoted to a query (#511): Pragmatic derives <c>RatedQuery</c>, whose filter is this rule. Its <c>Stars</c> is the rule's
    ///     input and names a field of the review: read as a filter of its own it would be an equality, and <c>AtLeast</c> would read one rating.
    /// </summary>
    [Query<Review>]
    public static Specification<Review> Rated(int? stars) => stars is null ? Spec<Review>.True : AtLeast(stars.Value);
}
