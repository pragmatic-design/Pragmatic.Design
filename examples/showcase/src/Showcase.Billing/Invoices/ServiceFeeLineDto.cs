namespace Showcase.Billing.Dtos;

/// <summary>
/// A service fee, with the service that caused it.
/// </summary>
/// <remarks>
/// The shape <c>FeeLineDto</c> dispatches to for a <c>ServiceFee</c> row. It inherits the base DTO
/// because <c>[MapDerived]</c> requires it — <c>PRAG0330</c> refuses a derived DTO that does not —
/// and that inheritance is legal because the four generated statics say they hide the base's; a
/// <c>[MapFrom]</c> DTO inheriting another would otherwise be <c>CS0108</c>, an error under
/// <c>--warnaserror</c>.
/// </remarks>
[MapFrom<ServiceFee>]
public partial class ServiceFeeLineDto : FeeLineDto
{
    /// <summary>What the guest was charged for, which is the whole point of the derived shape.</summary>
    public string ServiceName { get; init; } = "";

    public DateTimeOffset ServiceDate { get; init; }
}
