namespace Showcase.Catalog.Properties.Mutations;

/// <summary>
/// Soft-deletes a hotel property.
/// Demonstrates: Mutation&lt;T&gt; + [Mutation(Mode=Delete)] + [Endpoint] for soft-delete.
/// </summary>
[Mutation(Mode = MutationMode.Delete)]
[Endpoint(HttpVerb.Delete, "api/properties/{id}")]
// A visibility rule hides rows from writes too: an update loads through the same filtered
// query, so without this a deactivated property could never be reached again — not to change
// it, not to reactivate it. Managing the catalogue is what [RequirePermission] below grants.
[WithoutFilter<ActiveOnly>]
[RequirePermission(CatalogPermissions.Property.Delete)]
public partial class DeletePropertyMutation : Mutation<Property>
{
    public required Guid Id { get; init; }
}
