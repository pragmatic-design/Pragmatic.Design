using Pragmatic.Internationalization.Types;

namespace Invoicing.Billing.Dtos;

/// <summary>A line as it is read back: what was charged, and what it came to.</summary>
[MapFrom<InvoiceLine>]
public partial class InvoiceLineDto
{
    public Guid Id { get; init; }

    public string Description { get; init; } = "";

    public decimal Quantity { get; init; }

    public Money UnitPrice { get; init; }

    public decimal VatRate { get; init; }

    public Money LineNet { get; init; }

    public Money LineVat { get; init; }
}
