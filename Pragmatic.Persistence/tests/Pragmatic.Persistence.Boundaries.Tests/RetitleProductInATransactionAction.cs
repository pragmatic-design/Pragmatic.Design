using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Invoker;
using Pragmatic.Persistence.Entity;
using Pragmatic.Result;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     Renames a product through <see cref="RetitleProductMutation" /> in a transaction: the mutation saves inside it, so the action's own save
///     has nothing left to write, and the mutation's declared event is all the outbox has to take (#803).
/// </summary>
[DomainAction]
[BelongsTo<CatalogBoundary>]
[Transactional]
public partial class RetitleProductInATransactionAction : VoidDomainAction
{
    private IMutationInvoker<RetitleProductMutation, Product> _retitle = null!;

    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        var retitled = await _retitle.InvokeAsync(new RetitleProductMutation { Id = Id, Name = Name }, ct).ConfigureAwait(false);
        return retitled.IsFailure ? VoidResult<IError>.Failure(retitled.Error) : Success;
    }
}
