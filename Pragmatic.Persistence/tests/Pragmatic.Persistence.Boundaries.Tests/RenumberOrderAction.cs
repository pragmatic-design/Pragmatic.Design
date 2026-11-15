using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authoring;
using Pragmatic.Persistence.Entity;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     Renumbers an order, whose boundary publishes through the Messaging outbox, and declares <see cref="OrderRenumbered" /> (#736).
/// </summary>
[DomainAction]
[BelongsTo<OrdersBoundary>]
[Raises<OrderRenumbered>]
[LoadEntity<Order>(nameof(Id))]
public partial class RenumberOrderAction : VoidDomainAction<NotFoundError>
{
    public required Guid Id { get; init; }

    public required string Number { get; init; }

    public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        _order.Number = Number;
        return Task.FromResult<VoidResult<IError>>(Success);
    }
}
