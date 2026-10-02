using Pragmatic.Actions.Mutation;
using Pragmatic.Authorization;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;

namespace Conformance.Catalog.Entities;

/// <summary>
///     An operation under the posture that <b>refuses</b> the name the posture would give it.
/// </summary>
/// <remarks>
///     <para>
///         The derivation looks at the type's name: <c>Reprice</c> is not one of the verbs it
///         recognizes, so <c>RepriceCatalogItemMutation</c> would become <c>catalog.reprice-catalog-item</c>
///         — a flat permission, outside the entity's family, that no existing role grants. But changing
///         the list price <em>is</em> modifying the item, and whoever can modify it must be able to do so
///         without anyone having to notice a new permission.
///     </para>
///     <para>
///         <c>[ExplicitPermission]</c> is that choice: the name is the entity's generated constant, not a
///         string — so it survives a rename of the type, which is exactly what moves derived values and
///         produces a 403 nobody decided.
///     </para>
///     <para>
///         ⚠️ It is not <c>[RequirePermission]</c> with the same value: that declares a requirement where
///         there was none, this <b>replaces</b> the one the posture would have imposed. The difference
///         shows only where the posture is on, and it is the cell
///         <c>TheExplicitNameBeatsTheDerivedOne</c> measures — whoever has the derived name is refused.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[ExplicitPermission(CatalogPermissions.CatalogItem.Update)]
[Endpoint(HttpVerb.Put, "api/catalog-items/{id}/price")]
public partial class RepriceCatalogItemMutation : Mutation<CatalogItem>
{
    public required Guid Id { get; init; }

    public decimal ListPrice { get; init; }
}
