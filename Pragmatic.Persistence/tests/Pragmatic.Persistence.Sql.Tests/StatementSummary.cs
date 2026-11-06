using Pragmatic.Mapping.Attributes;

namespace PragmaticEntities.Tests;

/// <summary>
///     A statement as Showcase.Billing's <c>InvoiceSummaryDto</c> shows an invoice (#563): its issue day formatted (<c>[MapProperty(Format)]</c>)
///     and its total converted (<c>[MapConverter]</c>).
/// </summary>
[MapFrom<Statement>]
public sealed partial class StatementSummary
{
    public string Code { get; init; } = "";

    [MapProperty(nameof(Statement.IssuedAt), Format = "yyyy-MM-dd")]
    public string IssuedDate { get; init; } = "";

    [MapProperty(nameof(Statement.Total))]
    [MapConverter<EuroConverter>]
    public string TotalFormatted { get; init; } = "";
}
