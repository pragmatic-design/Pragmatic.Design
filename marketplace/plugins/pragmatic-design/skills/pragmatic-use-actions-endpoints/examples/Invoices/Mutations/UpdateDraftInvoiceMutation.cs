using Invoicing.Billing.Errors;

namespace Invoicing.Billing.Invoices.Mutations;

/// <summary>
///     The accountant changes a draft: lines added, changed and removed in one call, and the totals follow.
/// </summary>
/// <remarks>
///     <para>
///         The set sent is the set that remains: the merge pairs on each line's id, so a line already
///         there keeps its identity, one without a match is created, and one that does not arrive is
///         removed.
///     </para>
///     <para>
///         "Only a draft may be changed" is a rule about the row this operation <b>loads</b>, so it is
///         <c>ValidateLoaded</c> and not an <c>IAsyncValidator</c>, which would read the invoice a second
///         time. The state machine refuses the moves; this refuses the edit.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[RequirePermission(BillingPermissions.Invoice.Update)]
[Endpoint(HttpVerb.Put, "api/invoices/{id}")]
[ReturnsDto<InvoiceDto>]
public partial class UpdateDraftInvoiceMutation : Mutation<Invoice>
{
    public required Guid Id { get; init; }

    [MaxLength(500)]
    public string? Notes { get; init; }

    public required List<WriteInvoiceLineMutation> Lines { get; init; }

    public override Task<Result<Invoice, IError>> ApplyAsync(Invoice entity, CancellationToken ct = default)
    {
        // ⚠️ The rule reads the row this mutation targets, and `ValidateLoaded()` cannot: the invoker
        // calls it with no arguments, and `Mutation<TEntity>` hands the entity to `ApplyAsync` and
        // nowhere else. On an action, where the loaded rows are fields, it is the right place; here the
        // refusal has to be the first thing the apply does.
        if (entity.Status != InvoiceStatus.Draft)
            return Task.FromResult<Result<Invoice, IError>>(new InvoiceNotDraftError());

        entity.Recalculate();

        return Task.FromResult<Result<Invoice, IError>>(entity);
    }
}
