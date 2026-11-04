using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;

namespace PragmaticEntities.Tests;

/// <summary>
///     A paged search declared with <c>Paged = true</c> (#504): Page and PageSize are not written here, the query's generated half writes them,
///     with Pragmatic's defaults (page 1, twenty rows), as the Showcase's <c>SearchGuestsQuery</c> does.
/// </summary>
[Query<Review>(Paged = true)]
public partial class PagedReviewsQuery
{
    [Filter]
    public string? Text { get; init; }

    [Sort(DefaultDirection = SortDirection.Ascending)]
    public SortDirection? TextSort { get; init; }
}
