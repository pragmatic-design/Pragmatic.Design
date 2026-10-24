using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Aot.SqlEngine;

/// <summary>A declared query: a filter, a sort with its default, a page.</summary>
[Pragmatic.Persistence.Query.Attributes.Query<Review>]
public partial class SmokeReviewsQuery
{
    [Pragmatic.Persistence.Query.Attributes.Filter]
    public string? Text { get; init; }

    [Pragmatic.Persistence.Query.Attributes.Sort(DefaultDirection = Pragmatic.Persistence.Query.SortDirection.Ascending)]
    public Pragmatic.Persistence.Query.SortDirection? TextSort { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 10;
}
