using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;

namespace PragmaticDocuments.Tests;

/// <summary>One batch's stay labels: a declared projection whose members are formatted and converted after the read (#537).</summary>
[Query<Stay, StayLabel>]
public partial class StayLabelsQuery
{
    [Filter(Operator = FilterOperator.Equals)]
    public string? Batch { get; init; }
}
