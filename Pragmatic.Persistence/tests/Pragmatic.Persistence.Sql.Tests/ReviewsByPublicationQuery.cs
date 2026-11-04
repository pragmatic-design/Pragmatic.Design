using Pragmatic.Persistence.Query.Attributes;

namespace PragmaticEntities.Tests;

/// <summary>The reviews counted by publication (#507): the query filters, the view groups.</summary>
[Query<Review, ReviewsByPublication>]
public partial class ReviewsByPublicationQuery
{
    [Filter]
    public string? Text { get; init; }
}
