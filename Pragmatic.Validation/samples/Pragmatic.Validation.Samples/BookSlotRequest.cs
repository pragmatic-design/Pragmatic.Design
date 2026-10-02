using Pragmatic.Validation.Attributes;

namespace Pragmatic.Validation.Samples;

/// <summary>
///     DTO whose <see cref="SlotStart" /> must be in the future relative to
///     <see cref="ValidationTimeProvider.Current" />. Used by the TimeProvider sample
///     to show deterministic [FutureDate] outcomes under a pinned clock.
/// </summary>
public partial record BookSlotRequest
{
    // A non-nullable DateTime is inherently required; [FutureDate] enforces the
    // value is after ValidationTimeProvider.Current. (Adding [Required] on a
    // non-nullable value type currently trips an SG bug — see BUGS FOUND.)
    [FutureDate]
    public DateTime SlotStart { get; init; }
}
