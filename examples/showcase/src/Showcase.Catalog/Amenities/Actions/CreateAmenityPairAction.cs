using Showcase.Catalog.Amenities.Mutations;

namespace Showcase.Catalog.Amenities.Actions;

/// <summary>
/// Declarative composite action: creates two amenities atomically via mutation-typed step properties.
/// Demonstrates the [CompositeAction] mutation-step pattern — the generated CompositeInvoker runs each
/// step without saving, commits once, and flushes deferred events/cache after the single transaction.
/// If any step fails (e.g. validation), nothing is persisted.
///
/// It also demonstrates the round over HTTP: a mutation-typed property is an ordinary body property,
/// so the generated request body nests one JSON object per step and the handler hands them to the
/// composite invoker. The permission is declared here: the composite is the door to this route.
/// The steps declare none of their own, so nothing else is asked; a step that did declare one
/// would be asked for it as it runs, unless the composite declared [AbsorbsChildPermissions].
/// </summary>
[DomainAction]
[CompositeAction]
[BelongsTo<CatalogBoundary>]
[Endpoint(HttpVerb.Post, "api/amenities/pairs")]
[RequirePermission(CatalogPermissions.Amenity.Create)]
public partial class CreateAmenityPairAction : VoidDomainAction
{
    /// <summary>First amenity to create.</summary>
    public required CreateAmenityMutation First { get; init; }

    /// <summary>Second amenity to create (in the same transaction as the first).</summary>
    public required CreateAmenityMutation Second { get; init; }
}
