namespace Invoicing.Billing.Invoices;

/// <summary>
///     Where an invoice's document is kept: one place decides it, and nothing composes it by hand.
/// </summary>
/// <remarks>
///     ⚠️ The container carries the **tenant**. File storage has no tenant filter and never will — a key
///     composed without the tenant is how one company reads another's invoice — so the one rule about it
///     lives here rather than at each call site.
/// </remarks>
public static class InvoiceStorageKey
{
    /// <summary>The container of a company's invoices for the year the invoice was issued in.</summary>
    public static string ContainerFor(string tenantId, int year) => $"invoices/{tenantId}/{year}";

    /// <summary>The file name, from the number the invoice is quoted by.</summary>
    public static string FileNameFor(string number) => $"{number.Replace('/', '-')}.pdf";
}
