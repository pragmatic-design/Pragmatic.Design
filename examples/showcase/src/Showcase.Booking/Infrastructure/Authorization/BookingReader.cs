namespace Showcase.Booking.Infrastructure.Authorization;

/// <summary>
///     Module-defined permission template for read-only booking access.
/// </summary>
/// <remarks>
///     ⚠️ <c>[PermissionSet]</c> on the list is what lets a role in <em>another</em> assembly spread
///     it into its <c>DefaultPermissions</c> and still be catalogued: across the boundary there is no
///     initializer to read, and an unmarked list made the catalogue say the role granted nothing
///     while the runtime granted every entry. The values travel as
///     <c>[assembly: PermissionSetValues]</c>, written by the generator onto this assembly.
/// </remarks>
public sealed class BookingReader : IRoleDefinition
{
    public static string Name => "booking-reader";
    public static string? Description => "Read-only access to booking data";

    [PermissionSet]
    public static IReadOnlyList<string> Permissions =>
    [
        BookingPermissions.Reservation.Read,
        BookingPermissions.Guest.Read,
        BookingPermissions.RoomAssignment.Read,
        BookingPermissions.StaffAssignment.Read
    ];
}
