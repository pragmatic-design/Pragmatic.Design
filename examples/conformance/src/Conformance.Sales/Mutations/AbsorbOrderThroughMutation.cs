using Conformance.Catalog;
using Conformance.Catalog.Entities;
using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;
using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Mutation;
using Pragmatic.Authorization;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.Result;

namespace Conformance.Sales.Mutations;

/// <summary>
///     The same mutation, declaring that it answers for what it invokes.
/// </summary>
/// <remarks>
///     <para>
///         The only difference from its twin: <c>[AbsorbsChildPermissions]</c>. On a mutation the
///         attribute covers both its nested children and what its body invokes — including a call that
///         crosses the boundary — so one attribute has one meaning on actions and mutations alike.
///     </para>
///     <para>
///         ⚠️ The mutation's own permission is untouched: that check runs <b>before</b>
///         <c>ApplyAsync</c>, and absorbing applies only to what the body calls.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[AllowAnonymous]
[AbsorbsChildPermissions]
// PRAG0424: a mutation that writes in its own boundary and calls another commits twice, the inner one
// first. Declared rather than silenced — like the twin next to it: what is measured is absorbing the
// permission, and the two writes have no invariant between them.
[AcceptsPartialWrites("like the twin next to it: what is measured is absorbing the permission, and the two writes have no invariant between them")]
[Endpoint(HttpVerb.Put, "api/orders/{id}/absorbed-classification")]
[ReturnsDto<OrderDto>]
public partial class AbsorbOrderThroughMutation : Mutation<Order>
{
    private ICatalogActions _catalog = null!;

    public required Guid Id { get; init; }

    /// <summary>The catalog item the guarded operation touches.</summary>
    [MapIgnore]
    public required Guid ItemId { get; init; }

    public override async Task<Result<Order, IError>> ApplyAsync(Order entity, CancellationToken ct = default)
    {
        var result = await _catalog
            .GuardCatalogItem(new GuardCatalogItemMutation { Id = ItemId, ListPrice = 3.30m }, ct)
            .ConfigureAwait(false);

        if (result.IsFailure)
            return Result<Order, IError>.Failure(result.Error);

        entity.SetNotes("absorbed");
        return entity;
    }
}
