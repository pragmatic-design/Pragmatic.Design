using Pragmatic;

namespace Showcase.Billing.Enums;

[FastEnum]
public enum PaymentMethod
{
    CreditCard,
    DebitCard,
    BankTransfer,
    Cash,
    OnlinePayment
}
