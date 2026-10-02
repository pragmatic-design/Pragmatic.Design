using Conformance.Catalog;
using Conformance.Catalog.Entities;
using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;
using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Mutation;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.Result;

namespace Conformance.Sales.Mutations;

/// <summary>
///     A <b>mutation</b> that invokes one of another boundary, declaring nothing.
/// </summary>
/// <remarks>
///     <para>
///         The control of the pair: it makes exactly the same call as the twin next to it and does not
///         carry <c>[AbsorbsChildPermissions]</c>, so the invoked operation's permission holds. Without
///         it, «absorbing works» would also be satisfied by absorbing that always applies.
///     </para>
///     <para>
///         ⚠️ The pair exists for a mutation and not only for an action because on a mutation the
///         attribute must cover what the body invokes, not only the nested children.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[AllowAnonymous]
// PRAG0424: a mutation that writes in its own boundary and calls another commits twice, the inner one
// first. Declared rather than silenced — the case measures the permission across the boundary, not the
// transaction: the order and the catalog item have no invariant between them.
[AcceptsPartialWrites("the case measures the permission across the boundary, not the transaction: the order and the catalog item have no invariant between them")]
[Endpoint(HttpVerb.Put, "api/orders/{id}/guarded-classification")]
[ReturnsDto<OrderDto>]
public partial class ClassifyOrderThroughMutation : Mutation<Order>
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

        entity.SetNotes("classified");
        return entity;
    }
}
