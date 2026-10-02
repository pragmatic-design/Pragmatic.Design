using Conformance.Catalog;
using Conformance.Catalog.Entities;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Conformance.Sales.Queries;

/// <summary>
///     A Sales operation that invokes a Catalog one through the boundary's public interface — the shape
///     that interface exists for.
/// </summary>
/// <remarks>
///     <para>
///         The caller is <c>[AllowAnonymous]</c> and carries no permission;
///         <c>GuardCatalogItemMutation</c> requires <c>conformance.catalogitem.guard</c>. The invoked
///         operation's permission must hold: it belongs to another boundary, and crossing the boundary is
///         exactly the case that permission exists to protect.
///     </para>
///     <para>
///         ⚠️ <c>IsInternalCall</c> is read only by the authorization filters, so entering an internal
///         call means «do not ask for permissions». A generated implementation that entered it on
///         <b>every</b> method would let any module obtain another's writes without the permission,
///         merely by injecting its interface. The public interface does not enter it.
///     </para>
/// </remarks>
[DomainAction]
[AllowAnonymous]
[Endpoint(HttpVerb.Post, "api/orders/across-boundary")]
public partial class CallAcrossTheBoundaryAction : VoidDomainAction
{
    private ICatalogActions _catalog = null!;

    public required Guid ItemId { get; init; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        var result = await _catalog
            .GuardCatalogItem(new GuardCatalogItemMutation { Id = ItemId, ListPrice = 9.99m }, ct)
            .ConfigureAwait(false);

        return result.IsFailure ? VoidResult<IError>.Failure(result.Error) : Success;
    }
}
