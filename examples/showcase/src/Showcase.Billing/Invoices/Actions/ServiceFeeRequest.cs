using Pragmatic.Validation.Attributes;

namespace Showcase.Billing.Actions;

/// <summary>
/// Request model for adding a service fee to an invoice.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ The rules are here and not on the action, because the action holds a <b>list</b> of these:
/// what has to be true is true of each one, and <c>[ValidateElements]</c> on the list is what runs
/// them. Until then a fee with no name and a negative amount was accepted and <em>lowered</em> the
/// invoice's subtotal — the element type carried no validation and nothing looked inside the list.
/// </para>
/// <para>
/// <c>partial</c> because that is what makes the generated <c>Validate()</c> possible: the element
/// type has to be an <c>ISyncValidator</c> for the list's declaration to have anything to call.
/// </para>
/// </remarks>
public sealed partial record ServiceFeeRequest
{
    /// <summary>What the guest is being charged for, as it appears on the invoice.</summary>
    [Required]
    [MaxLength(100)]
    public required string ServiceName { get; init; }

    /// <summary>What it costs. A fee is a charge: zero is not one, and a negative is a refund.</summary>
    [Range(0.01, 100_000)]
    public required decimal Amount { get; init; }

    /// <summary>When the service was given, when that is not the day the invoice is raised.</summary>
    public DateTimeOffset? ServiceDate { get; init; }
}
