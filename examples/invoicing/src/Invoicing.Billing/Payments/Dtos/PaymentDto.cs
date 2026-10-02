using Pragmatic.Internationalization.Types;

namespace Invoicing.Billing.Dtos;

/// <summary>One payment against an invoice, as the accountant's screen lists it.</summary>
[MapFrom<Payment>]
[GenerateProjection]
public partial class PaymentDto
{
    public Guid Id { get; init; }

    public Guid InvoiceId { get; init; }

    public DateOnly PaidOn { get; init; }

    public Money Amount { get; init; }

    public string? Reference { get; init; }

    public PaymentMethod Method { get; init; }
}
