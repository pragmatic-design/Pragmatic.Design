namespace Showcase.Host.Authorization;

/// <summary>Application role: full billing including refunds (L2 custom).</summary>
public sealed class FinanceManagerRole : IRole
{
    public static string Name => "finance-manager";
    public static string? Description => "Full billing access including refunds";
    public static IReadOnlyList<string> DefaultPermissions => [];
}
