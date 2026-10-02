namespace Showcase.Booking.Guests.Mutations;

/// <summary>
/// Creates a new guest.
/// Demonstrates: Mutation&lt;T&gt; + [Mutation] + [Endpoint] + [CreatedAt] + [ReturnsDto&lt;T&gt;] —
/// answers 201 with the guest as a client sees it, and a real Location header pointing at it.
/// </summary>
/// <remarks>
/// Without [ReturnsDto], a create answers with the new id alone. The tracked entity is what the
/// in-process caller gets; on the wire it would carry OwnerId, CreatedBy and every other column the
/// server owns.
/// </remarks>
[Mutation(Mode = MutationMode.Create)]
[RequirePermission(BookingPermissions.Guest.Create)]
[Endpoint(HttpVerb.Post, "api/guests")]
[CreatedAt("/api/guests/{Id}")]
[ReturnsDto<GuestDto>]
public partial class CreateGuestMutation : Mutation<Guest>
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public string? Phone { get; init; }
    public string? Nationality { get; init; }
    public string? PreferredLanguage { get; init; }
}
