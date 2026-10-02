namespace Showcase.Catalog.Infrastructure.Authorization;

/// <summary>
///     Module-defined permission template for full catalog management.
/// </summary>
public sealed class CatalogEditor : IRoleDefinition
{
    public static string Name => "catalog-editor";
    public static string? Description => "Full CRUD on all catalog entities";
    public static IReadOnlyList<string> Permissions =>
    [
        CatalogPermissions.All   // "catalog.*"
    ];
}
