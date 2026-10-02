namespace Showcase.Booking.Guests.Mutations;

/// <summary>
/// Updates a guest.
/// Demonstrates: Mutation&lt;T&gt; + [Mutation] + [Endpoint] — all nullable for partial update.
/// </summary>
[Mutation(Mode = MutationMode.Update)]
[RequirePermission(BookingPermissions.Guest.Update)]
[Endpoint(HttpVerb.Put, "api/guests/{id}")]
public partial class UpdateGuestMutation : Mutation<Guest>
{
    public required Guid Id { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Nationality { get; init; }
    public string? PreferredLanguage { get; init; }
}
