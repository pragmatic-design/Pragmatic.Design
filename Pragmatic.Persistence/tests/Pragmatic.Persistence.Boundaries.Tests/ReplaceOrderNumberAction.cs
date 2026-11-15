using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Commit;
using Pragmatic.Actions.Invoker;
using Pragmatic.Persistence.Entity;
using Pragmatic.Result;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     Renumbers an order through <see cref="ReissueOrderMutation" />, owning the commit the mutation is nested in and holding its save back
///     (#800).
/// </summary>
[DomainAction]
[BelongsTo<OrdersBoundary>]
[CommitStrategy(CommitMode.Once)]
public partial class ReplaceOrderNumberAction : VoidDomainAction
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
