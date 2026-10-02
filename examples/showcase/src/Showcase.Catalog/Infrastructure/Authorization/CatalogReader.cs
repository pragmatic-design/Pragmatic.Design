namespace Showcase.Catalog.Infrastructure.Authorization;

/// <summary>
///     Module-defined permission template for read-only catalog access.
/// </summary>
public sealed class CatalogReader : IRoleDefinition
{
    public static string Name => "catalog-reader";
    public static string? Description => "Read-only access to all catalog entities";
    public static IReadOnlyList<string> Permissions =>
    [
        CatalogPermissions.Amenity.Read,
        CatalogPermissions.Property.Read,
        CatalogPermissions.RoomType.Read,
        CatalogPermissions.CancellationPolicy.Read
    ];
}
