using Pragmatic.Authoring;
using Pragmatic.Persistence.Lifecycle;
using Pragmatic.Comments;
using Pragmatic.Attachments;
using Pragmatic.Tags;
using Showcase.Catalog.Entities;

namespace Showcase.Booking.Entities;

/// <summary>
/// A guest reservation at a hotel property.
/// Demonstrates: 3 FK relationships (GuestId, PropertyId, RoomTypeId),
/// cross-boundary navigation references, enum status, state machine, full infrastructure mix.
/// Read-only: create/update managed by domain actions (CreateReservationAction, mutations).
/// </summary>
[Entity]
[Auditable]
[SoftDelete]
[HasOwner]
[ConcurrencyAware]
[StateMachine<ReservationStatus>]
[Raises<ReservationCreated>]
// Read-only on purpose, and now said rather than implied: Capabilities defaults to None, so
// this scaffolded nothing at all until PRAG2612 pointed it out. Writes stay with
// CreateReservationAction and the mutations, which is what the summary above describes.
[Resource("reservations", Capabilities = ResourceCapabilities.Read | ResourceCapabilities.List)]
[HasComments]
[HasTags]
// The thumbnail bounds are what make this the showcase of the derived-file half: an attached photo
// of the room gets a preview at upload, and the retention job has two files to reclaim rather than
// one. Both halves are E2E here because both are invisible when they break — a missing thumbnail
// looks like an unsupported format, and a leaked one looks like nothing at all.
[HasAttachments(
    AllowedExtensions = ".pdf,.jpg,.png,.docx",
    PurgeDeletedAfterDays = 30,
    ThumbnailMaxWidth = 200,
    ThumbnailMaxHeight = 200)]
[HasPresets]
[PresetProvider<Infrastructure.Lifecycle.ReservationPresetProvider>]
[Relation.ManyToOne<Property>]
[Relation.ManyToOne<RoomType>]
[Relation.OneToMany<RoomAssignment>]
[Relation.ManyToOne<Guest>]
public partial class Reservation : DomainEventSource, IEntity
{
    /// <summary>Human-readable reservation number, e.g. RES-202601-00001. The {SEQ:5} token is backed by
    /// a real database sequence; [GeneratedValue] wires the generated generator itself.</summary>
    [GeneratedValue("RES-{YYYY}{MM}-{SEQ:5}")]
    public string ReservationNumber { get; private set; } = "";


    [FutureDate]
    public DateTimeOffset CheckIn { get; private set; }

    [GreaterThanProperty(nameof(CheckIn))]
    public DateTimeOffset CheckOut { get; private set; }

    /// <summary>Number of nights for this reservation. SQL-translatable via Reservation.Expr.NightsCount.</summary>
    [Projectable]
    public int NightsCount => (CheckOut - CheckIn).Days;

    public int NumberOfGuests { get; private set; } = 1;

    public decimal TotalAmount { get; private set; }

    public string Currency { get; private set; } = "EUR";

    public ReservationStatus Status { get; private set; } = ReservationStatus.Pending;

    public string? SpecialRequests { get; private set; }

    /// <summary>User ID or "system" — set when the reservation is checked in.</summary>
    public string? CheckedInBy { get; private set; }

    /// <summary>
    /// When the guest said they would arrive. Stored in UTC like every other instant here; what the
    /// guest actually typed was a wall clock on their own device, and
    /// <c>[FromClientTimezone]</c> on CreateReservationRequest.ExpectedArrival is what turned it into
    /// an instant.
    /// </summary>
    public DateTimeOffset? ExpectedArrival { get; private set; }

    /// <summary>
    /// When the guest actually walked in, as the front desk typed it — on the hotel's clock, which is
    /// the business timezone and not the clerk's browser. See CheckInGuestMutation.ActualArrival.
    /// </summary>
    public DateTimeOffset? ActualArrival { get; private set; }

    // Anemic model: no behavior methods. State changes are domain operations (the Reservation mutations)
    // that call the SG-generated TransitionTo directly; the event on entering a state is declared by
    // [RaisesEvent<T>] on the target ReservationStatus member, and ReservationCancelled (which carries the
    // caller's reason) is raised by [Raises<ReservationCancelled>] on CancelReservationMutation.

    public static Reservation Create(
        Guid guestId,
        Guid propertyId,
        Guid roomTypeId,
        DateTimeOffset checkIn,
        DateTimeOffset checkOut,
        int numberOfGuests,
        decimal totalAmount,
        string currency,
        string? specialRequests = null)
    {
        var reservation = new Reservation
        {
            GuestId = guestId,
            PropertyId = propertyId,
            RoomTypeId = roomTypeId,
            CheckIn = checkIn,
            CheckOut = checkOut,
            NumberOfGuests = numberOfGuests,
            TotalAmount = totalAmount,
            Currency = currency,
            SpecialRequests = specialRequests,
            Status = ReservationStatus.Pending
        };

        // ReservationCreated is raised on persist by [Raises<ReservationCreated>] (lifecycle) — not here.
        return reservation;
    }
}
