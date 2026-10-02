namespace Showcase.Booking.Entities;

/// <summary>
/// Guest preference settings. One-to-one with Guest (dependent side — holds the FK).
/// Demonstrates [Relation.OneToOne] relationship and upsert pattern in SetGuestPreferencesAction.
/// </summary>
[Entity]
[Relation.OneToOne<Guest>.WithNavigation("Guest", Inverse = "Preferences")]
public partial class GuestPreferences : IEntity
{
    /// <summary>Preferred room type name (e.g. "Suite", "Double").</summary>
    public string? PreferredRoomType { get; private set; }

    /// <summary>Dietary requirements or restrictions.</summary>
    public string? DietaryRequirements { get; private set; }

    /// <summary>Whether the guest prefers a high floor.</summary>
    public bool PrefersHighFloor { get; private set; }

    /// <summary>Whether the guest prefers a quiet room.</summary>
    public bool PrefersQuietRoom { get; private set; }

    /// <summary>Any additional notes or special requests.</summary>
    public string? AdditionalNotes { get; private set; }

    /// <summary>Creates a new preferences record for the given guest.</summary>
    public static GuestPreferences Create(
        Guid guestId,
        string? preferredRoomType,
        string? dietaryRequirements,
        bool prefersHighFloor,
        bool prefersQuietRoom,
        string? additionalNotes) =>
        new()
        {
            GuestId = guestId,
            PreferredRoomType = preferredRoomType,
            DietaryRequirements = dietaryRequirements,
            PrefersHighFloor = prefersHighFloor,
            PrefersQuietRoom = prefersQuietRoom,
            AdditionalNotes = additionalNotes
        };
}
