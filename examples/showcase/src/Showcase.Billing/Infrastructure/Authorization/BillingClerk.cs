namespace Showcase.Billing.Infrastructure.Authorization;

/// <summary>
///     Module-defined permission template for day-to-day billing work: raise an invoice, price it,
///     take a payment, print the document. Not an application role — a host composes one from it.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <c>[PermissionSet]</c> is what makes this list readable from <em>another</em> assembly.
///         A host role whose <c>DefaultPermissions</c> spreads it is compiled elsewhere, and a
///         referenced assembly is metadata: a <c>static</c> list has no constant value there, so
///         without the attribute the role catalogue said the role granted <b>nothing</b> while the
///         runtime granted every entry. Marked, the generator publishes the values as
///         <c>[assembly: PermissionSetValues]</c> and the reading compilation takes them from there.
///     </para>
///     <para>
///         Refunding is deliberately absent: <c>billing.invoice.refund</c> reverses money that has
///         already moved, and it belongs to the finance manager. The list is written out rather than
///         given as <c>BillingPermissions.All</c> for that reason alone — a wildcard here would hand
///         a clerk the refund without anybody choosing to.
///     </para>
/// </remarks>
public sealed class BillingClerk : IRoleDefinition
{
    public static string Name => "billing-clerk-template";

    public static string? Description => "Raise, price and settle invoices — no refunds";

    [PermissionSet]
    public static IReadOnlyList<string> Permissions =>
    [
        BillingPermissions.Invoice.Read,
        BillingPermissions.Invoice.Create,
        BillingPermissions.Invoice.Update,
        BillingPermissions.LineItem.All,
        BillingPermissions.Fee.All,
        BillingPermissions.Payment.All,
        BillingPermissions.Document.Read
    ];
}
