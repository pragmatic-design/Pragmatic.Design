using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;

namespace PragmaticEntities.Tests;

/// <summary>The reviews with their property's name, a left join by key (#508): a review whose property the caller cannot see is kept.</summary>
[Query<Review, ReviewPlace>]
[Join<Property>(ForeignKey = "PropertyId", TargetKey = "Id", Type = JoinType.Left, Alias = "Place")]
public partial class ReviewPlacesQuery
{
    [Filter]
    public string? Text { get; init; }

    [Sort(DefaultDirection = SortDirection.Ascending)]
    public SortDirection? TextSort { get; init; }
}
