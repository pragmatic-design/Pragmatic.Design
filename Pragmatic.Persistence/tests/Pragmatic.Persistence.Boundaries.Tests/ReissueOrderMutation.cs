using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Mutation;
using Pragmatic.Authoring;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     Renumbers an order and declares <see cref="OrderRenumbered" />: invoked by <see cref="ReplaceOrderNumberAction" />, on the boundary that
///     publishes through the Messaging outbox (#800).
/// </summary>
[Mutation(Mode = MutationMode.Update)]
[Raises<OrderRenumbered>]
public partial class ReissueOrderMutation : Mutation<Order>
{
    public required Guid Id { get; init; }

    public required string Number { get; init; }
}
