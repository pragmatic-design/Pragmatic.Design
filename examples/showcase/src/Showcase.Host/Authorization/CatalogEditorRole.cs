namespace Showcase.Host.Authorization;

/// <summary>Application role: full catalog CRUD.</summary>
public sealed class CatalogEditorRole : IRole
{
    public static string Name => "catalog-editor";
    public static string? Description => "Creates and manages catalog entities";
    public static IReadOnlyList<string> DefaultPermissions => [];
}
