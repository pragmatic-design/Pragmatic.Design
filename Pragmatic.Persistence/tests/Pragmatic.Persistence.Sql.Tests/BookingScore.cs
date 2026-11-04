using Pragmatic.Mapping.Attributes;

namespace PragmaticEntities.Tests;

/// <summary>A booking's phone and score: <c>Score</c> is the entity's <c>[Projectable]</c> member, no column (#506).</summary>
[MapFrom<Booking>]
public sealed class BookingScore
{
    public string? Phone { get; init; }

    public int Score { get; init; }
}
