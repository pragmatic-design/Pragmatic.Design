using Pragmatic.Persistence.Query.Attributes;

namespace PragmaticEntities.Tests;

/// <summary>
///     The reviews counted by whether they are published (#507), as the Showcase's <c>ReservationsByStatusView</c>: the key both
///     <c>[GroupBy]</c> and <c>[From]</c>, a count, a sum and a maximum.
/// </summary>
[QueryView<Review>]
[GroupBy<Review>(Properties = "Published")]
public partial class ReviewsByPublication
{
    [From<Review>]
    public bool Published { get; init; }

    [Count<Review>]
    public int Reviews { get; init; }

    [Sum<Review>(Expression = "Stars")]
    public int Stars { get; init; }

    [Max<Review>(Expression = "Stars")]
    public int Best { get; init; }
}
