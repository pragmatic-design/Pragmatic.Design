using Pragmatic.Mapping.Attributes;

namespace PragmaticDocuments.Tests;

/// <summary>A booking's phone and score: <c>Score</c> is the entity's <c>[Projectable]</c> member, no field of the document (#506).</summary>
[MapFrom<Booking>]
public sealed class BookingScore
{
    public string? Phone { get; init; }

    public int Score { get; init; }
}
