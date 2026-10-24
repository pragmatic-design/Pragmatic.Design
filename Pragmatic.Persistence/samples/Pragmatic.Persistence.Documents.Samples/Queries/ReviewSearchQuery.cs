using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;
using PragmaticDocumentSamples.Entities;

namespace PragmaticDocumentSamples.Queries;

/// <summary>A paged search over reviews, declared as Pragmatic declares queries: a text filter, a sort with its default, a projection.</summary>
[Query<Review, ReviewLine>]
public partial class ReviewSearchQuery
{
    [Filter]
    public string? Text { get; init; }

    [Sort(DefaultDirection = SortDirection.Descending)]
    public SortDirection? TextSort { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 2;
}
