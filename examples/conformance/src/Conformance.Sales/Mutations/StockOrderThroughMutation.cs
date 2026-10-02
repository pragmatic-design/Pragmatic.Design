using Conformance.Catalog;
using Conformance.Catalog.Entities;
using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;
using Conformance.Sales.Errors;
using Pragmatic.Actions.Mutation;
using Pragmatic.Authorization;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.Result;

namespace Conformance.Sales.Mutations;

/// <summary>
///     Writes in its own boundary, creates an item in the catalog's, and can fail afterwards.
/// </summary>
/// <remarks>
///     <para>
///         The caller that gives <c>[UndoWith&lt;T&gt;]</c> its meaning: the catalog commits first,
///         this mutation fails afterwards, and the undo registered by the inner invoker runs in reverse
///         order before the response goes back. Without the declaration the item would stay written, and
///         that is the row the case goes looking for.
///     </para>
///     <para>
///         <c>Fail</c> is in the body on purpose, instead of two twin operations: the <b>control</b> is
///         the very same call with the flag off, where the item stays. «The item is not there» alone
///         would also be satisfied by a step that never wrote it.
///     </para>
///     <para>
///         ⚠️ No <c>[AcceptsPartialWrites]</c>, unlike the two siblings next to it: there the choice is
///         to accept the leftovers, here it is to undo them, and they are the two different answers
///         <c>PRAG0424</c> admits. The generator recognizes it by reading the <c>[CompensableStep]</c> it
///         wrote itself on the catalog's facade — not this declaration, which cannot be seen from there.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[AllowAnonymous]
[Endpoint(HttpVerb.Put, "api/orders/{id}/stocked-classification")]
[ReturnsDto<OrderDto>]
public partial class StockOrderThroughMutation : Mutation<Order>
{
    private ICatalogActions _catalog = null!;

    public required Guid Id { get; init; }

    /// <summary>The name of the item to create across the boundary.</summary>
    [MapIgnore]
    public required string ItemName { get; init; }

    /// <summary>Whether to fail <b>after</b> the catalog committed: the case's only lever.</summary>
    [MapIgnore]
    public bool Fail { get; init; }

    public override async Task<Result<Order, IError>> ApplyAsync(Order entity, CancellationToken ct = default)
    {
        var added = await _catalog
            .AddCatalogItemUndoably(
                new AddCatalogItemUndoablyMutation { Name = ItemName, ListPrice = 7.70m }, ct)
            .ConfigureAwait(false);

        if (added.IsFailure)
            return Result<Order, IError>.Failure(added.Error);

        if (Fail)
            return Result<Order, IError>.Failure(new StockingRefusedError());

        entity.SetNotes("stocked");
        return entity;
    }
}
