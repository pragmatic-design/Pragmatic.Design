namespace Showcase.Host.Authorization;

/// <summary>Application role: front desk — check-in/out, guest lookup, NO delete.</summary>
public sealed class ReceptionistRole : IRole
{
    public static string Name => "receptionist";
    public static string? Description => "Front desk operations";
    public static IReadOnlyList<string> DefaultPermissions => [];
}
