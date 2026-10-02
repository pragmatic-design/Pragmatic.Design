namespace Showcase.Billing.Entities;

/// <summary>
/// A fee charged when a reservation is cancelled after the free cancellation window.
/// Derived type in TPH hierarchy under <see cref="Fee"/>.
/// </summary>
/// <remarks>
///     Carries <c>[Entity]</c>, like <see cref="ServiceFee"/>: see the remarks there for why the form
///     is the one worth having.
/// </remarks>
[Entity]
public partial class CancellationFee : Fee
{
    /// <summary>Percentage of the reservation total applied as penalty (e.g. 0.25 = 25%).</summary>
    public decimal PenaltyRate { get; private set; }

    /// <summary>The original reservation amount the penalty was calculated from.</summary>
    public decimal OriginalAmount { get; private set; }
}
