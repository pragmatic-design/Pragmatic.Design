using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Samples.Services;
using Pragmatic.Result;

namespace Pragmatic.Actions.Samples.Actions;

/// <summary>
///     Sends an order confirmation email. Demonstrates: VoidDomainAction
///     with a single dependency (no return value).
/// </summary>
[DomainAction]
public partial class SendOrderConfirmationAction : VoidDomainAction
{
    private IEmailService _emailService = null!;

    /// <summary>
    ///     The email address to send confirmation to.
    /// </summary>
    public required string RecipientEmail { get; init; }

    /// <summary>
    ///     The order ID for the confirmation.
    /// </summary>
    public required Guid OrderId { get; init; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        await _emailService.SendAsync(
            RecipientEmail,
            "Order Confirmation",
            $"Your order {OrderId} has been confirmed.",
            ct);

        return Success;
    }
}
