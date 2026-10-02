namespace Showcase.Host.Authorization;

/// <summary>Application role: read-only catalog access.</summary>
public sealed class CatalogViewerRole : IRole
{
    public static string Name => "catalog-viewer";
    public static string? Description => "Read-only access to catalog data";
    public static IReadOnlyList<string> DefaultPermissions => [];
}
