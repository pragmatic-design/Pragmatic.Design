namespace Showcase.Catalog.Properties.Mutations;

/// <summary>
/// Restores a soft-deleted hotel property.
/// Demonstrates: MutationMode.Restore — resets IsDeleted/DeletedAt/DeletedBy, bypasses query filters to load.
/// </summary>
[Mutation(Mode = MutationMode.Restore)]
[Endpoint(HttpVerb.Post, "api/properties/{id}/restore")]
[RequirePermission(CatalogPermissions.Property.Update)]
public partial class RestorePropertyMutation : Mutation<Property>
{
    public required Guid Id { get; init; }
}
