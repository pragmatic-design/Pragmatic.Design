namespace Invoicing.Registry.Customers.Mutations;

/// <summary>
///     Removes a customer from the register: soft, so it can come back, and so that everything issued for
///     them still reads.
/// </summary>
/// <remarks>
///     <para>
///         <b>It is not refused when the customer has invoices, and that is the decision — not an
///         oversight.</b> Registry cannot ask Billing whether an invoice references the customer: the
///         contract would have to be published by Billing and consumed by Registry, which reverses the one
///         dependency between the two modules and closes the cycle they exist to avoid.
///     </para>
///     <para>
///         The snapshot is what makes the permissive answer safe. An issued invoice carries its own copy of
///         the customer's name, VAT number, address and language, frozen the day it was issued, so a
///         deleted customer damages nothing already issued — a list of invoices reads exactly as before.
///         Drafting a <em>new</em> invoice for them is refused on its own: the published read no longer
///         returns the row, and the draft answers <c>CUSTOMER_NOT_FOUND</c>.
///     </para>
///     <para>
///         Soft rather than hard for the same reason a customer is restorable at all: the row is referenced
///         by id from the other module's invoices, and a hard delete would leave those ids naming nothing.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Delete)]
[RequirePermission(RegistryPermissions.Customer.Delete)]
[Endpoint(HttpVerb.Delete, "api/customers/{id}")]
public partial class DeleteCustomerMutation : Mutation<Customer>
{
    public required Guid Id { get; init; }
}
