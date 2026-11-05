using Pragmatic.Mapping.Attributes;

namespace PragmaticDocuments.Tests;

/// <summary>
///     A stay's label (#537): its arrival formatted (<c>[MapProperty(Format)]</c>) and its length converted (<c>[MapConverter]</c>), as
///     Showcase.Billing's <c>InvoiceSummaryDto</c> shows its issue date and its total.
/// </summary>
[MapFrom<Stay>]
public sealed partial class StayLabel
{
    public string Code { get; init; } = "";

    [MapProperty(nameof(Stay.Arrival), Format = "dd/MM/yyyy")]
    public string ArrivesOn { get; init; } = "";

    [MapProperty(nameof(Stay.Length))]
    [MapConverter<HoursConverter>]
    public string Lasts { get; init; } = "";
}
