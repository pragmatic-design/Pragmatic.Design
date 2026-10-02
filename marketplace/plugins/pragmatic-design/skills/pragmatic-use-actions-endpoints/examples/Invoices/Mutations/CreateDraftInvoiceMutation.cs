using Invoicing.Billing.Errors;
using Invoicing.Registry.Contracts;
using Invoicing.Registry.Customers.Queries;

namespace Invoicing.Billing.Invoices.Mutations;

/// <summary>
///     The accountant drafts an invoice: a customer, some lines, and the totals computed for them.
/// </summary>
/// <remarks>
///     The customer is read through Registry's published contract — never through <c>Customer</c>, which
///     belongs to the other module — and one that does not exist in this company answers 404. The read is
///     already tenant-filtered, so another company's customer is simply not there.
/// </remarks>
[Mutation(Mode = MutationMode.Create)]
[RequirePermission(BillingPermissions.Invoice.Create)]
[Endpoint(HttpVerb.Post, "api/invoices")]
[CreatedAt("/api/invoices/{Id}")]
[ReturnsDto<InvoiceDto>]
public partial class CreateDraftInvoiceMutation : Mutation<Invoice>
{
    private IRegistryReads _registry = null!;

    public required Guid CustomerId { get; init; }

    [MaxLength(500)]
    public string? Notes { get; init; }

    /// <summary>The lines the draft starts with. An invoice with none is refused by the invariant.</summary>
    public required List<WriteInvoiceLineMutation> Lines { get; init; }

    public override async Task<Result<Invoice, IError>> ApplyAsync(Invoice entity, CancellationToken ct = default)
    {
        var customer = await _registry
            .GetCustomerBillingDetails(new GetCustomerBillingDetailsQuery { CustomerId = CustomerId }, ct)
            .ConfigureAwait(false);

        if (customer.Count == 0)
            return new CustomerNotFoundError();

        entity.SetCustomerCode(customer[0].Code);
        entity.Recalculate();

        return entity;
    }
}
