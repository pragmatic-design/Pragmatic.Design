namespace Showcase.Booking.Reservations.Sagas;

/// <summary>
///     State enum for the check-in saga.
/// </summary>
public enum CheckInState
{
    /// <summary>Guest has not arrived yet.</summary>
    Pending,

    /// <summary>Guest arrived, identity verified.</summary>
    GuestVerified,

    /// <summary>Room has been assigned.</summary>
    RoomAssigned,

    /// <summary>Check-in completed successfully.</summary>
    Completed,

    /// <summary>Check-in cancelled (no-show or other reason).</summary>
    Cancelled
}
