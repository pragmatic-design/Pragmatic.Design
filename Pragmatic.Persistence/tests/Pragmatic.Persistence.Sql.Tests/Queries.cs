using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;

namespace PragmaticEntities.Tests;

/// <summary>A paged search over reviews, declared as Pragmatic declares queries: filters, a sort with its default, a page.</summary>
[Query<Review>]
public partial class SearchReviewsQuery
{
    /// <summary>A string under [Filter] without an operator: Contains, Pragmatic's default.</summary>
    [Filter]
    public string? Text { get; init; }

    /// <summary>Nullable, no attribute: an optional filter, Equals.</summary>
    public bool? Published { get; init; }

    /// <summary>A collection: In, on the column it names.</summary>
    [Filter(MapTo = nameof(Review.PropertyId))]
    public List<Guid>? Properties { get; init; }

    [Filter(Operator = FilterOperator.StartsWith, IgnoreCase = true, MapTo = nameof(Review.Text))]
    public string? Prefix { get; init; }

    [Sort(DefaultDirection = SortDirection.Descending)]
    public SortDirection? TextSort { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 2;
}

/// <summary>A review's line: a projection, the DTO's columns only.</summary>
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

/// <summary>Where a review was written, or by whom: a [FilterGroup(FilterLogic.Or)], as the Showcase's city-or-country group.</summary>
[FilterDto<Review>]
public partial class ReviewSourceFilter
{
    [Filter(Operator = FilterOperator.EndsWith, MapTo = nameof(Review.Text))]
    public string? At { get; set; }

    [Filter(Operator = FilterOperator.StartsWith, MapTo = nameof(Review.Text))]
    public string? By { get; set; }
}

/// <summary>A filter object: an OR group, and a member joined to it with AND.</summary>
[FilterDto<Review>]
public partial class ReviewFilter
{
    [FilterGroup(FilterLogic.Or)]
    public ReviewSourceFilter? Source { get; set; }

    [Filter(Operator = FilterOperator.GreaterOrEqual, MapTo = nameof(Review.Stars))]
    public int? MinStars { get; set; }
}

/// <summary>An unpaged query whose filters are one [ComplexFilter], as the Showcase's property search carries its location.</summary>
[Query<Review>]
public partial class FilterReviewsQuery
{
    [ComplexFilter]
    public ReviewFilter? Where { get; init; }

    [Sort(DefaultDirection = SortDirection.Ascending)]
    public SortDirection? TextSort { get; init; }
}
