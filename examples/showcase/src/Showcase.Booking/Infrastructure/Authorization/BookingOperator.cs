namespace Showcase.Booking.Infrastructure.Authorization;

/// <summary>
///     Module-defined permission template for standard booking operations.
///     NOT an application role — use in host via <c>IncludeDefinition&lt;BookingOperator&gt;()</c>.
/// </summary>
public sealed class BookingOperator : IRoleDefinition
{
    public static string Name => "booking-operator";
    public static string? Description => "Full CRUD on reservations and guests";
    public static IReadOnlyList<string> Permissions =>
    [
        BookingPermissions.Reservation.All,      // "booking.reservation.*"
        BookingPermissions.Guest.All,            // "booking.guest.*"
        BookingPermissions.GuestPreferences.All  // "booking.guest-preferences.*"
    ];
}
