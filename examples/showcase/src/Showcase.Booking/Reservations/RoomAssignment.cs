namespace Showcase.Booking.Entities;

/// <summary>
/// Maps a reservation to a specific room number assigned at check-in.
/// Demonstrates: multiple ManyToOne to same entity (Guest) with disambiguation via WithNavigation,
/// optional FK (RequestedBy → Guid?), and creating related entities inside a Mutation.ApplyAsync.
/// </summary>
[Entity]
[Relation.ManyToOne<Guest>.WithNavigation("AssignedGuest", ForeignKey = "AssignedGuestId")]
[Relation.ManyToOne<Guest>.WithNavigation("RequestedBy", ForeignKey = "RequestedById", Required = false)]
[Relation.ManyToOne<Reservation>]
public partial class RoomAssignment : IEntity
{
    public string RoomNumber { get; private set; } = "";

    public DateTimeOffset AssignedAt { get; private set; }

    /// <summary>Creates a room assignment record linking a reservation to a physical room.</summary>
    public static RoomAssignment Create(Guid reservationId, Guid guestId, string roomNumber, DateTimeOffset assignedAt) =>
        new()
        {
            ReservationId = reservationId,
            AssignedGuestId = guestId,
            RoomNumber = roomNumber,
            AssignedAt = assignedAt
        };
}
