namespace Showcase.Booking.Entities;

/// <summary>The rules that select a guest's preferences.</summary>
/// <remarks>
///     A specification rather than a LINQ chain on <c>Query()</c>: every persistence engine reads a specification, and only EF Core serves an
///     <c>IQueryable</c>.
/// </remarks>
public static partial class GuestPreferencesSpecifications
{
    /// <summary>The preferences of one guest: at most one row, the relation is one-to-one.</summary>
    public static Specification<GuestPreferences> OfGuest(Guid guestId)
        => Spec<GuestPreferences>.Where(p => p.GuestId == guestId);
}
