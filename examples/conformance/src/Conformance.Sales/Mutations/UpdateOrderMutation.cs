using Pragmatic.Actions.Mutation;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Entity;
using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Mutations;

/// <summary>
///     The order with its <b>optional</b> lines: the collection that can go unmentioned.
/// </summary>
/// <remarks>
///     <para>
///         The control case of <c>UpdateOrderLinesMutation</c>, where the body <em>is</em> the collection
///         and omitting it is not possible. Here the body is an object with two properties, and
///         <c>Lines</c> can be missing: absence is not a value, and this shape is the only one in which
///         that can be measured for a collection.
///     </para>
///     <para>
///         ⚠️ An absent collection must not <b>empty</b> the collection: a merge call emitted without a
///         guard would let <c>Sync</c> read the null as «the incoming set is empty». The single
///         navigation next to it (<c>RenameOrderMutation.DeliveryAddress</c>) has the same guard.
///         <c>TheOmittedCollection</c> measures both halves.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[AllowAnonymous]
[Endpoint(HttpVerb.Put, "api/orders/{id}")]
[ReturnsDto<OrderDto>]
public partial class UpdateOrderMutation : Mutation<Order>
{
    public required Guid Id { get; init; }

    public string Reference { get; init; } = "";

    /// <summary>The lines, or <c>null</c> to leave them unmentioned. An empty list removes them all.</summary>
    public List<WriteOrderLineMutation>? Lines { get; init; }
}
