using Pragmatic;

namespace Invoicing.Billing.Enums;

/// <summary>
///     How the money arrived.
/// </summary>
/// <remarks>
///     It records what happened and decides nothing: no rule of this example reads it. It is here because
///     an accountant reconciling a bank statement needs it, and because a string would let every caller
///     spell "bank transfer" their own way.
/// </remarks>
[FastEnum]
public enum PaymentMethod
{
    BankTransfer,
    Card,
    Cash,
    Other
}
