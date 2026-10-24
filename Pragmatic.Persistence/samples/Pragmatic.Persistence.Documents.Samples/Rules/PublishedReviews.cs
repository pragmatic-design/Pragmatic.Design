using System.Linq.Expressions;
using Pragmatic.Persistence.Query.Filters;
using PragmaticDocumentSamples.Entities;

namespace PragmaticDocumentSamples.Rules;

/// <summary>The reviews anyone may read.</summary>
public sealed class PublishedReviews : VisibilityRule<Review>
{
    public override Expression<Func<Review, bool>> ToExpression() => r => r.Published;
}
