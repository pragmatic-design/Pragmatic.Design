using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Aot.SqlEngine;

/// <summary>A fixed rule on the row.</summary>
public sealed class PublishedReviews : Pragmatic.Persistence.Query.Filters.VisibilityRule<Review>
{
    public override System.Linq.Expressions.Expression<Func<Review, bool>> ToExpression() => r => r.Published;
}
