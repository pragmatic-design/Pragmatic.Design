using Pragmatic.Actions.Mutation;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Entity;
using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Mutations;

/// <summary>
///     The rows chosen by <b>key</b>, without loading them — `[LinkIds]`.
/// </summary>
/// <remarks>
///     <para>
///         An order's set of labels is a choice, not a modification: the rows already exist and nobody is
///         writing them. Without <c>[LinkIds]</c> the only way to express it would be to send back the
///         whole shape of each label, that is to load them in order to link them.
///     </para>
///     <para>
///         ⚠️ A many-to-many that is declared and never written (<c>RoomType.Amenities</c> in the
///         Showcase) shows the need: the mechanism is what makes the occasion usable.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[AllowAnonymous]
[Endpoint(HttpVerb.Put, "api/orders/{id}/labels")]
[ReturnsDto<OrderDto>]
public partial class SetOrderLabelsMutation : Mutation<Order>
{
    public required Guid Id { get; init; }

    public string Reference { get; init; } = "";

    /// <summary>The labels the order must have after the operation, by key.</summary>
    [LinkIds(nameof(Order.Labels))]
    public List<Guid> LabelIds { get; init; } = [];
}
