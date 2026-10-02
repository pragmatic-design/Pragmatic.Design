using Pragmatic.Actions.Mutation;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Entity;
using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Mutations;

/// <summary>
///     Rewrites the set of an order's lines — <b>the shape with mutation children</b>.
/// </summary>
/// <remarks>
///     <para>
///         Demonstrates: the collection is <b>merged by key</b>. A line already present is updated and
///         keeps its identity, one without a match is created, and one that does not arrive is
///         removed. It is the <c>Sync</c> strategy, the default: the set sent is the set that remains.
///     </para>
///     <para>
///         It also demonstrates the loading: the invoker includes <c>Lines</c> before applying,
///         because a merge that does not see the existing lines removes none of them and adds them all.
///     </para>
///     <para>
///         ⚠️ The child is a <b>mutation</b>, constrained twice: it must be a mutation, and it can be
///         nested <b>only along a declared relation</b>. The second constraint keeps nesting inside the
///         aggregate and inside the boundary, with no extra rule. A <c>[MapTo]</c> DTO child — shape
///         without behaviour, no <c>ApplyAsync</c>, no validators, no <c>[RequirePermission]</c> — is
///         refused with <c>PRAG0442</c>.
///     </para>
///     <para>
///         ⚠️ The restriction applies <b>here</b>, not in Mapping. Between POCOs, without EF, a DTO that
///         nests another DTO is the right and final shape: there anything consistent between objects
///         goes. Confusing the two levels makes a correct shape look wrong.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[AllowAnonymous]
[Endpoint(HttpVerb.Put, "api/orders/{id}/lines")]
[ReturnsDto<OrderDto>]
public partial class UpdateOrderLinesMutation : Mutation<Order>
{
    public required Guid Id { get; init; }

    /// <summary>The complete set of lines the order must have after the operation.</summary>
    public required List<WriteOrderLineMutation> Lines { get; init; }
}
