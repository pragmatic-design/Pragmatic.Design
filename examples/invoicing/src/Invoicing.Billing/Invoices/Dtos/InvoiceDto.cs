using Pragmatic.Internationalization.Types;

namespace Invoicing.Billing.Dtos;

/// <summary>An invoice with its lines and its three totals.</summary>
/// <remarks>
///     The answer of the mutations and of the two payment operations, which build it with
///     <c>FromEntity</c>, and of <c>GetInvoiceQuery</c>, which projects it — hence the
///     <c>[GenerateProjection]</c>, added with that read.
///     <para>
///         ⚠️ A projection over it carries the <c>Money</c> of its nested lines: a <c>Money</c> is
///         projected whole exactly like a <c>[ValueObject]</c>, on a flat DTO and a nested one alike.
///     </para>
/// </remarks>
[MapFrom<Invoice>]
[GenerateProjection]
public partial class InvoiceDto
{
    public Guid Id { get; init; }

    public Guid CustomerId { get; init; }

    public string CustomerCode { get; init; } = "";

    public InvoiceStatus Status { get; init; }

    public string? Number { get; init; }

    public Money NetTotal { get; init; }

    public Money VatTotal { get; init; }

    public Money GrossTotal { get; init; }

    public Money AmountPaid { get; init; }

    /// <summary>
    ///     What is still owed. Computed here and not carried on the entity: it is a subtraction of two
    ///     columns, and a third column for it would be a third thing to keep in agreement with the other
    ///     two. <c>[MapIgnore]</c> because the mapper looks for it on the entity otherwise: a property the
    ///     DTO computes from its own is still a property it has to be told not to map (PRAG0303).
    /// </summary>
    [MapIgnore]
    public Money AmountDue => GrossTotal - AmountPaid;

    public string? Notes { get; init; }

    public DateOnly? IssuedOn { get; init; }

    public DateOnly? DueOn { get; init; }

    /// <summary>Why this invoice is void, and when. Null on every invoice that is not.</summary>
    public string? VoidReason { get; init; }

    public DateOnly? VoidedOn { get; init; }

    // The customer as the invoice froze them. Read back from the invoice and never from the customer:
    // that is the whole point of copying them.
    public string BilledToName { get; init; } = "";

    public string BilledToVatNumber { get; init; } = "";

    public string BilledToEmail { get; init; } = "";

    public string BilledToCulture { get; init; } = "";

    /// <summary>How long the stored document is, and what it hashes to: a client can check what it got.</summary>
    public long? PdfByteLength { get; init; }

    public string? PdfSha256 { get; init; }

    public List<InvoiceLineDto> Lines { get; init; } = [];
}
