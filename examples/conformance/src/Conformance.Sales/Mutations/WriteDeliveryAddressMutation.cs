using Pragmatic.Actions.Mutation;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Mutations;

/// <summary>
///     The child that is <b>one</b>, not many.
/// </summary>
/// <remarks>
///     No <c>[Endpoint]</c>: it is a child, not an operation. It serves the single-navigation case,
///     where a reference has strategies of its own just as a collection does.
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
public partial class WriteDeliveryAddressMutation : Mutation<DeliveryAddress>
{
    public Guid Id { get; init; }

    public string Street { get; init; } = "";

    public string City { get; init; } = "";
}
