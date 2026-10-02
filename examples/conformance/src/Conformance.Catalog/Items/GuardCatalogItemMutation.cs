using Pragmatic.Actions.Mutation;
using Pragmatic.Authorization;

namespace Conformance.Catalog.Entities;

/// <summary>
///     A catalog operation that requires a permission, and has no route.
/// </summary>
/// <remarks>
///     <para>
///         It exists for the <b>boundary</b> cell: the only way to reach it from outside is the
///         boundary's public interface, <c>ICatalogActions</c>, the contract through which another module
///         invokes this one's operations. If the permission did not hold there, it would hold nowhere —
///         <c>GuardDeliveryAddressMutation</c> says the same thing one level down, for nested children.
///     </para>
///     <para>
///         ⚠️ No <c>[Endpoint]</c>, on purpose: a route would give a second door, and the case would no
///         longer know which of the two refused. <c>Internal = false</c> follows: without a route an
///         operation ends up on the internal interface, and here it is exactly the public one that is
///         needed — the only one another module can inject.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update, Internal = false)]
[RequirePermission("conformance.catalogitem.guard")]
public partial class GuardCatalogItemMutation : Mutation<CatalogItem>
{
    public required Guid Id { get; init; }

    public decimal ListPrice { get; init; }
}
