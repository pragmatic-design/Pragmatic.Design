namespace Showcase.Host.Authorization;

/// <summary>
///     System admin — full access to all permissions.
/// </summary>
public sealed class ShowcaseAdmin : IRole
{
    public static string Name => "admin";
    public static string? Description => "Full system access";
    public static IReadOnlyList<string> DefaultPermissions => ["*"];
}
