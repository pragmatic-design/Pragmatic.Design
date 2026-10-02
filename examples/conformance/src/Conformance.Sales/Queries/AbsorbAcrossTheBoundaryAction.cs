using Conformance.Catalog;
using Conformance.Catalog.Entities;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Conformance.Sales.Queries;

/// <summary>
///     The twin of <see cref="CallAcrossTheBoundaryAction" />: same call, same caller without
///     permissions, the only difference <c>[AbsorbsChildPermissions]</c>.
/// </summary>
/// <remarks>
///     <para>
///         It is the half that makes the other measurable: without it, «the permission across the
///         boundary holds» would also be satisfied by a check that always refuses. And it is the shape in
///         which an operation declares that it answers for what it invokes — same attribute and same
///         meaning it has on a composite and on a mutation with children.
///     </para>
///     <para>
///         ⚠️ Declaring it is a domain decision: whoever can call this operation writes to the catalog
///         without holding its permission. Silence must not be able to make that decision in the
///         author's place, which is why the default is the strict rule.
///     </para>
/// </remarks>
[DomainAction]
[AllowAnonymous]
[AbsorbsChildPermissions]
[Endpoint(HttpVerb.Post, "api/orders/across-boundary-absorbed")]
public partial class AbsorbAcrossTheBoundaryAction : VoidDomainAction
{
    private ICatalogActions _catalog = null!;

    public required Guid ItemId { get; init; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        var result = await _catalog
            .GuardCatalogItem(new GuardCatalogItemMutation { Id = ItemId, ListPrice = 19.99m }, ct)
            .ConfigureAwait(false);

        return result.IsFailure ? VoidResult<IError>.Failure(result.Error) : Success;
    }
}
