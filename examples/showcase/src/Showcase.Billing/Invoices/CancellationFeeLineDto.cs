namespace Showcase.Billing.Dtos;

/// <summary>
/// A cancellation fee, with the penalty it was calculated from.
/// </summary>
/// <remarks>
/// The second shape <c>FeeLineDto</c> dispatches to. Two of them rather than one is deliberate: with
/// a single derived DTO the dispatch and "the base always maps to the base" are indistinguishable,
/// because any answer other than the base shape would be the right one.
/// </remarks>
[MapFrom<CancellationFee>]
public partial class CancellationFeeLineDto : FeeLineDto
{
    /// <summary>Percentage applied as penalty — 0.25 is 25%.</summary>
    public decimal PenaltyRate { get; init; }

    public decimal OriginalAmount { get; init; }
}
