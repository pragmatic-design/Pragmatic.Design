using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;

namespace PragmaticDocuments.Tests;

/// <summary>Reviews with a default sort declared before the caller's: the default orders last, whatever the declaration order (#696).</summary>
[Query<Review>]
public partial class ReviewsByStarsQuery
{
    [Sort(MapTo = nameof(Review.Stars), DefaultDirection = SortDirection.Ascending)]
    public SortDirection? StarsSort { get; init; }

    [Sort]
    public SortDirection? TextSort { get; init; }
}
