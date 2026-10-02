using Invoicing.Billing.Errors;

namespace Invoicing.Billing.Invoices.Mutations;

/// <summary>
///     Throws away a draft nobody wants: the invoice and its lines go, and nothing is left behind.
/// </summary>
/// <remarks>
///     <para>
///         A real delete, because nothing points back at a draft: it has no number, no document, and
///         nobody outside has ever seen it. The lines go with it — <c>OnDelete = Cascade</c> on the
///         relation, done by the database and not by a loop here.
///     </para>
///     <para>
///         Only a draft. An issued invoice is refused with the same code a change to one is refused with,
///         and is voided instead. The rule is in <c>ApplyAsync</c>, which the delete pipeline calls
///         <em>before</em> the row is removed.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Delete)]
[RequirePermission(BillingPermissions.Invoice.Delete)]
[Endpoint(HttpVerb.Delete, "api/invoices/{id}")]
public partial class DeleteDraftInvoiceMutation : Mutation<Invoice, InvoiceNotDraftError>
{
    public required Guid Id { get; init; }

    public override Task<Result<Invoice, IError>> ApplyAsync(Invoice entity, CancellationToken ct = default)
    {
        return Task.FromResult(entity.Status == InvoiceStatus.Draft
            ? Result<Invoice, IError>.Success(entity)
            : Result<Invoice, IError>.Failure(new InvoiceNotDraftError()));
    }
}
