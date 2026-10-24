using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;

namespace PragmaticDocuments.Tests;

// The Sql engine's test declarations (tests/Pragmatic.Persistence.Sql.Pragmatic.Tests/Queries.cs), on the documents engine's entities.

/// <summary>A paged search over reviews, declared as Pragmatic declares queries: filters, a sort with its default, a page.</summary>
[Query<Review>]
public partial class SearchReviewsQuery
{
    /// <summary>A string under [Filter] without an operator: Contains, Pragmatic's default.</summary>
    [Filter]
    public string? Text { get; init; }

    /// <summary>Nullable, no attribute: an optional filter, Equals.</summary>
    public bool? Published { get; init; }

    /// <summary>A collection: In, on the field it names.</summary>
    [Filter(MapTo = nameof(Review.PropertyId))]
    public List<Guid>? Properties { get; init; }

    [Filter(Operator = FilterOperator.StartsWith, IgnoreCase = true, MapTo = nameof(Review.Text))]
    public string? Prefix { get; init; }

    [Sort(DefaultDirection = SortDirection.Descending)]
    public SortDirection? TextSort { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 2;
}

/// <summary>A review's line: a projection, the DTO's fields only.</summary>
[MapFrom<Review>]
public sealed class ReviewLine
{
    public string Text { get; init; } = "";

    public bool Published { get; init; }
}

/// <summary>A DevExpress grid over reviews: every scalar the class declares, the stars restricted to three operators.</summary>
[GridAdapter<Review>(Framework = GridFramework.DevExpress)]
[GridField("stars", AllowedOperators = [">=", "<=", "equals"])]
public partial class ReviewGrid;

/// <summary>An unpaged query into a DTO, its sort without a default (applied only when asked).</summary>
[Query<Review, ReviewLine>]
public partial class ReviewLinesQuery
{
    [Filter(Operator = FilterOperator.Equals)]
    public string? Text { get; init; }

    [Sort]
    public SortDirection? TextSort { get; init; }
}
