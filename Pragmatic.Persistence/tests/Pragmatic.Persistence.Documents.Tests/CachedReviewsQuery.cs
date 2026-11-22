using Pragmatic.Caching.Attributes;
using Pragmatic.Persistence.Query.Attributes;

namespace PragmaticDocuments.Tests;

/// <summary>
///     The reviews the caller may see, through its cache: a <c>[Cacheable]</c> query over an owned entity, whose answer is the caller's own
///     (#882). The Sql engine's declaration, on the documents engine's entities.
/// </summary>
[Query<Review>]
[Cacheable(Duration = "10m")]
public partial class CachedReviewsQuery
{
    /// <summary>Optional, so a query with none set reads every review the caller may see.</summary>
    public bool? Published { get; init; }
}
