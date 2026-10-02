namespace Invoicing.Billing.Entities;

/// <summary>The rules an invoice is read by, beyond its id.</summary>
public static partial class InvoiceSpecifications
{
    /// <summary>
    ///     How long a customer is left alone after being chased about the same invoice.
    /// </summary>
    /// <remarks>
    ///     One place, with a name: the sweep runs every morning, and this is the only thing that keeps a
    ///     daily job from being a daily e-mail.
    /// </remarks>
    public const int DaysBetweenReminders = 7;

    /// <summary>
    ///     The invoices to chase today: issued, past due, not settled, and not chased in the last
    ///     <see cref="DaysBetweenReminders" /> days.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The company is not in the rule. The tenant filter is, and the sweep runs inside a
    ///         <c>TenantScope</c> — which is what keeps one company's sweep off another's invoices without
    ///         anybody writing the condition, and what makes forgetting the scope read <b>zero</b> rows
    ///         rather than everyone's.
    ///     </para>
    ///     <para>
    ///         ⚠️ The cutoff is computed here and captured as a value: <c>today.AddDays(-7)</c> inside the
    ///         expression would be a method call for the provider to translate, and the answer to "what is
    ///         a week before today" is not the database's to give.
    ///     </para>
    ///     <para>
    ///         The amounts are compared through <c>.Amount</c> rather than with <c>Money</c>'s own
    ///         operator: a <c>Money</c> is two columns, and only the decimal one can be a comparison in
    ///         SQL. Both invoices are in the same currency by construction — a payment in another one is
    ///         refused when it is recorded.
    ///     </para>
    /// </remarks>
    public static Specification<Invoice> ToChaseOn(DateOnly today)
    {
        var chasedBefore = today.AddDays(-DaysBetweenReminders);

        // "Overdue" is the entity's own rule, named and not repeated: the list filters by the same
        // specification, and this adds the only thing the sweep knows that a list does not — how long ago
        // the customer was last written to.
        return InvoiceComputedFilters.IsOverdueOnSpec(today)
               & Spec<Invoice>.Where(invoice =>
                   invoice.LastRemindedOn == null || invoice.LastRemindedOn < chasedBefore);
    }

    /// <summary>
    ///     Issued and not settled, whether or not it is due yet.
    /// </summary>
    /// <remarks>
    ///     A different question from <see cref="Invoice.IsOverdueOn" />, not a copy of it: this is what a
    ///     customer <em>owes</em>, which includes an invoice with a week left to run;
    ///     <c>IsOverdueOn</c> is what is <em>late</em>. The two overlap on purpose, and
    ///     <c>OutstandingByCustomerLine</c> aggregates over this one.
    /// </remarks>
    public static Specification<Invoice> Unpaid()
        => Spec<Invoice>.Where(invoice =>
            invoice.Status == InvoiceStatus.Issued
            && invoice.AmountPaid.Amount < invoice.GrossTotal.Amount);
}
