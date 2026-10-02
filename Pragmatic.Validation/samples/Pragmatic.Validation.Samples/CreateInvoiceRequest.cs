using Pragmatic.Validation.Attributes;

namespace Pragmatic.Validation.Samples;

/// <summary>
///     Deep nested validation: Invoice → Lines (collection) → each line has nested Address.
///     Also demonstrates custom MessageKey overrides.
/// </summary>
public partial record CreateInvoiceRequest
{
    [Required(MessageKey = "invoice.customer_required")]
    public required string CustomerId { get; init; }

    [Required, MinLength(5, MessageKey = "invoice.ref_too_short"), MaxLength(20)]
    public required string InvoiceRef { get; init; }

    // ⚠️ The one legitimate spelling of the attribute, and why it is here rather than on AddTeamRequest:
    // up to a hundred lines, each with a nested address, and a caller pasting a batch in wants the
    // first bad line rather than four hundred errors. StopOnFirstError is the only thing the attribute
    // configures — written bare it is PRAG0223, because the walk happens without it.
    [Required, MinCount(1), MaxCount(100)]
    [ValidateElements(StopOnFirstError = true)]
    public required List<InvoiceLineRequest> Lines { get; init; }

    // Nested object (not collection)
    [Required]
    public required InvoiceAddressRequest BillingAddress { get; init; }
}

/// <summary>
///     Line item within an invoice.
/// </summary>
public partial record InvoiceLineRequest
{
    [Required, MinLength(1)]
    public required string Description { get; init; }

    [Positive(MessageKey = "line.qty_must_be_positive")]
    public int Quantity { get; init; }

    [Range(0.01, 999_999.99)]
    public decimal UnitPrice { get; init; }
}

/// <summary>
///     Nested address — validated as part of the parent.
/// </summary>
public partial record InvoiceAddressRequest
{
    [Required, MinLength(3)]
    public required string Street { get; init; }

    [Required]
    public required string City { get; init; }

    [Required, Regex(@"^[0-9]{5}$", MessageKey = "address.invalid_postal_code")]
    public required string PostalCode { get; init; }
}
