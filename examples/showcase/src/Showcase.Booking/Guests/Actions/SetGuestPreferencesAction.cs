using Microsoft.EntityFrameworkCore;

namespace Showcase.Booking.Guests.Actions;

/// <summary>
/// Creates or updates the preference profile for a guest (upsert pattern).
/// Demonstrates:
/// - DomainAction with IRepository (write)
/// - [LoadEntity&lt;Guest&gt;] pre-load guard: the guest must exist (404 otherwise) — the load runs
///   AFTER authorization, so an unauthorized caller is denied (403) before any DB read
/// - Upsert via LINQ query on IQueryable (not GetByIdAsync — non-PK lookup)
/// - GuestPreferences.GuestId FK used as lookup key
/// Boundary assignment is inferred from namespace (Showcase.Booking.Actions → BookingBoundary).
/// </summary>
[DomainAction]
[RequirePermission(BookingPermissions.GuestPreferences.Update)]
[LoadEntity<Guest>(nameof(GuestId))]
[Endpoint(HttpVerb.Put, "api/guests/{guestId}/preferences")]
[ApiSummary("Set Guest Preferences")]
[ApiDescription("Creates or updates the preference profile for a guest.")]
[ApiTags("Guests")]
public partial class SetGuestPreferencesAction : DomainAction<Guid>
{
    private IRepository<GuestPreferences> _preferences = null!;

    /// <summary>The guest whose preferences are being set.</summary>
    public required Guid GuestId { get; init; }

    /// <summary>Preferred room type name (e.g. "Suite", "Double").</summary>
    public string? PreferredRoomType { get; init; }

    /// <summary>Dietary requirements or restrictions.</summary>
    public string? DietaryRequirements { get; init; }

    /// <summary>Whether the guest prefers a high floor.</summary>
    public bool PrefersHighFloor { get; init; }

    /// <summary>Whether the guest prefers a quiet room.</summary>
    public bool PrefersQuietRoom { get; init; }

    /// <summary>Any additional notes or special requests.</summary>
    public string? AdditionalNotes { get; init; }

    public override async Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
    {
        // Guest existence is guaranteed here: [LoadEntity<Guest>] pre-loaded it (404 otherwise),
        // and it ran only after the [RequirePermission] check passed.

        // Upsert: lookup existing preferences by GuestId FK (non-PK query)
        var existing = await _preferences.Query()
            .FirstOrDefaultAsync(p => p.GuestId == GuestId, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            var prefs = GuestPreferences.Create(
                GuestId, PreferredRoomType, DietaryRequirements,
                PrefersHighFloor, PrefersQuietRoom, AdditionalNotes);

            _preferences.Add(prefs);
            return prefs.Id;
        }

        // Anemic model: the update lives in the operation via the generated setters (UoW persists).
        existing.SetPreferredRoomType(PreferredRoomType);
        existing.SetDietaryRequirements(DietaryRequirements);
        existing.SetPrefersHighFloor(PrefersHighFloor);
        existing.SetPrefersQuietRoom(PrefersQuietRoom);
        existing.SetAdditionalNotes(AdditionalNotes);

        return existing.Id;
    }
}
