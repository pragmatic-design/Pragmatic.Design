namespace PragmaticSagas.Tests;

/// <summary>
///     The confirmation step's compensation, seeded from the saga's state: <see cref="StayId" /> from the saga's own, <see cref="Reason" />
///     required here and absent from the saga, so it is left at its default rather than refusing the compensation (#532).
/// </summary>
public sealed record CancelStay(Guid StayId)
{
    public required string Reason { get; init; }
}
