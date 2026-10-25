using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;

namespace PragmaticEntities.Tests;

/// <summary>One review by its text: a query that answers one row, or a 404 when none matches.</summary>
[Query<Review>(Single = true)]
public partial class ReviewByTextQuery
{
    [Filter(Operator = FilterOperator.Equals)]
    public string? Text { get; init; }
}
