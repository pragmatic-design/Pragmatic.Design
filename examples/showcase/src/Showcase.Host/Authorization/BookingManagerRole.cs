namespace Showcase.Host.Authorization;

/// <summary>
///     Application role: booking manager — full booking ops + catalog read.
///     Composed from module definitions.
/// </summary>
public sealed class BookingManagerRole : IRole
{
    public static string Name => "booking-manager";
    public static string? Description => "Full booking operations with catalog context";
    public static IReadOnlyList<string> DefaultPermissions => [];
    // Permissions are composed via IncludeDefinition in Program.cs
}
