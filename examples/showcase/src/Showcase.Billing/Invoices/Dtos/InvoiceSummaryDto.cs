using Showcase.Billing.Infrastructure.Converters;

namespace Showcase.Billing.Dtos;

/// <summary>
/// Invoice summary for list views.
/// Demonstrates: MapFrom, MapProperty(Format), MapConverter.
/// </summary>
[MapFrom<Invoice>]
[GenerateProjection]
public partial class InvoiceSummaryDto
{
    public Guid Id { get; init; }
    public string InvoiceNumber { get; init; } = "";
    public Guid ReservationId { get; init; }
    public decimal TotalAmount { get; init; }
    public string Currency { get; init; } = "";
    public InvoiceStatus Status { get; init; }
    public DateTimeOffset IssuedAt { get; init; }

    /// <summary>Formatted issue date (yyyy-MM-dd). Demonstrates MapProperty(Format).</summary>
    [MapProperty(nameof(Invoice.IssuedAt), Format = "yyyy-MM-dd")]
    public string IssuedDate { get; init; } = "";

    /// <summary>Human-readable total. Demonstrates [MapConverter] for custom type conversion.</summary>
    [MapProperty(nameof(Invoice.TotalAmount))]
    [MapConverter<MoneyToStringConverter>]
    public string TotalFormatted { get; init; } = "";

    /// <summary>
    /// Localized status label for the current culture.
    /// Demonstrates: T.xxx embedded translation keys (generated from translations/*.json).
    /// </summary>
    [MapIgnore]
    public string StatusLabel => Status switch
    {
        InvoiceStatus.Draft => T.Invoice.Status.Draft.Value,
        InvoiceStatus.Issued => T.Invoice.Status.Issued.Value,
        InvoiceStatus.Paid => T.Invoice.Status.Paid.Value,
        InvoiceStatus.PartiallyPaid => T.Invoice.Status.PartiallyPaid.Value,
        InvoiceStatus.Overdue => T.Invoice.Status.Overdue.Value,
        InvoiceStatus.Cancelled => T.Invoice.Status.Cancelled.Value,
        InvoiceStatus.Refunded => T.Invoice.Status.Refunded.Value,
        _ => Status.ToString()
    };
}
