using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;

namespace PragmaticEntities.Tests;

/// <summary>The reviews with their property's name, an inner join by key (#508): a review whose property the caller cannot see is dropped.</summary>
[Query<Review, ReviewPlace>]
[Join<Property>(ForeignKey = "PropertyId", TargetKey = "Id", Type = JoinType.Inner, Alias = "Place")]
public partial class ReviewPlacesInnerQuery
{
    [Sort(DefaultDirection = SortDirection.Ascending)]
    public SortDirection? TextSort { get; init; }
}
