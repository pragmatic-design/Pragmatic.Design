using Pragmatic.Actions.Mutation;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Entity;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Mutations;

/// <summary>
///     The same child entity, with a door of its own — the «full case».
/// </summary>
/// <remarks>
///     <para>
///         <c>DeliveryAddress</c> is <c>[PartOf&lt;Order&gt;]</c>, so the order writes it nested; and it
///         is reachable from here on its own. <c>Exclusive = false</c> is what makes the two compatible:
///         without it <c>PRAG0438</c> would refuse the exposed mutation, leaving the choice between a
///         parent that cannot write the child and a child nobody can reach.
///     </para>
///     <para>
///         ⚠️ It is not an extra permission: both doors are operations, with their own permissions, their
///         own validation and their own events. What changes is how many addresses the row has.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[AllowAnonymous]
[Endpoint(HttpVerb.Put, "api/delivery-addresses/{id}")]
public partial class RenameDeliveryAddressMutation : Mutation<DeliveryAddress>
{
    public required Guid Id { get; init; }

    public string Street { get; init; } = "";

    public string City { get; init; } = "";
}
