using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Invoker;
using Pragmatic.Persistence.Entity;
using Pragmatic.Result;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     Renumbers an order through <see cref="ReissueOrderMutation" /> in a transaction, on the boundary that publishes through the Messaging
///     outbox: the action's own save writes nothing (#803).
/// </summary>
[DomainAction]
[BelongsTo<OrdersBoundary>]
[Transactional]
public partial class ReissueOrderInATransactionAction : VoidDomainAction
{
    private IMutationInvoker<ReissueOrderMutation, Order> _reissue = null!;

    public required Guid Id { get; init; }

    public required string Number { get; init; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        var reissued = await _reissue.InvokeAsync(new ReissueOrderMutation { Id = Id, Number = Number }, ct).ConfigureAwait(false);
        return reissued.IsFailure ? VoidResult<IError>.Failure(reissued.Error) : Success;
    }
}
