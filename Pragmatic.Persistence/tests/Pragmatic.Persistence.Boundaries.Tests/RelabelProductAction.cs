using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Commit;
using Pragmatic.Actions.Invoker;
using Pragmatic.Persistence.Entity;
using Pragmatic.Result;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     Renames a product of the catalog, whose boundary has an event outbox, through <see cref="RetitleProductMutation" />: the action owns
///     the commit, holding the mutation's save back, and the mutation's declared event waits in its batch (#800).
/// </summary>
[DomainAction]
[BelongsTo<CatalogBoundary>]
[CommitStrategy(CommitMode.Once)]
public partial class RelabelProductAction : VoidDomainAction
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
